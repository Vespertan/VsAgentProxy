using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

internal sealed class SolutionLaunchService
{
    private readonly ProjectService projects;
    private readonly LaunchService launches;
    private readonly DocumentService documents;
    public SolutionLaunchService(ProjectService projects, LaunchService launches, DocumentService documents)
    { this.projects = projects; this.launches = launches; this.documents = documents; }

    public async Task<JObject> ReadAsync(CancellationToken token)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        try
        {
            var native = new NativeSolutionLaunch();
            var profiles = await native.ProfilesAsync(token);
            return Describe(native, profiles);
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            var fallback = DiskProfiles((string?)projects.Read()["solution"]);
            fallback["readError"] = ProtocolSupport.ReadError("nativeSolutionProfiles", e);
            return fallback;
        }
    }

    private JObject Describe(NativeSolutionLaunch native, object[] profiles)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var state = State(native);
        var selected = native.Selected;
        var selectedName = selected == null ? null : (string?)NativeSolutionLaunch.Property(selected, "Name");
        var items = new JArray(profiles.Select(Serialize));
        var matches = items.OfType<JObject>().Where(p => selectedName != null && (string?)p["name"] == selectedName
            && (bool?)p["isShared"] == (selected == null ? null : (bool?)NativeSolutionLaunch.Property(selected, "IsShared"))).ToArray();
        // VS may restore the selected profile as startup settings before its private
        // selectedLaunchProfile field is repopulated. Match the live settings too.
        if (matches.Length == 0)
        {
            var stateCandidates = items.OfType<JObject>().Where(p =>
            {
                try { return Matches(Resolve(p, false), state); }
                catch { return false; }
            }).ToArray();
            if (stateCandidates.Length == 1) matches = stateCandidates;
        }
        var consistent = matches.Length == 1;
        var errors = new JArray();
        if (matches.Length == 1)
        {
            try { consistent = Matches(Resolve(matches[0], false), state); }
            catch (Exception e) { errors.Add(ProtocolSupport.ReadError("selectedProfile", e)); }
        }
        foreach (var item in items.OfType<JObject>()) item["active"] = consistent && ReferenceEquals(item, matches[0]);
        return new JObject { ["profiles"] = items, ["selectedProfileName"] = selectedName,
            ["activeProfile"] = consistent ? (string?)matches[0]["name"] : null, ["selectionMatchesProfile"] = consistent,
            ["activeProfileAvailability"] = "available", ["selectionAvailability"] = "available",
            ["source"] = "VisualStudio.CommonIDE.live", ["sourceKind"] = "nativeIde",
            ["compatibility"] = "internalVs2026", ["nativeVersion"] = native.Version,
            ["compatibilityNote"] = "Uses an internal VS contract; future VS updates may require an adapter update. No .suo files are edited.",
            ["startupState"] = state, ["readErrors"] = errors };
    }

    public async Task<JObject> SelectAsync(JObject parameters, CancellationToken token)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        projects.RequireIdle();
        documents.RequireClean();
        var name = ProtocolSupport.String(parameters, "name") ?? throw new ArgumentException("name is required.");
        var native = new NativeSolutionLaunch();
        var profiles = await native.ProfilesAsync(token);
        var dtos = new JArray(profiles.Select(Serialize));
        var index = SelectIndex(dtos, name, ProtocolSupport.String(parameters, "scope"));
        var requested = Resolve((JObject)dtos[index], true);
        var before = State(native);
        var previousSelected = native.Selected;
        // Snapshot targets before dispatch, including projects whose requested action is None.
        var previousTargets = new Dictionary<string, JObject>();
        foreach (var row in requested.OfType<JObject>().Where(x => x["debugTarget"]?.Type == JTokenType.String))
        {
            var id = (string)row["id"]!;
            previousTargets[id] = launches.ReadSelection(launches.Selection(projects.Find(id)));
        }
        var solution = (string?)projects.Read()["solution"];
        projects.RequireIdle();
        documents.RequireClean();
        try
        {
            await native.ApplyAsync(profiles[index], token);
            if (!string.Equals(solution, (string?)projects.Read()["solution"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The solution changed while applying the profile.");
            // CPS can publish target changes after the native apply task completes.
            JObject? actual = null;
            for (var attempt = 0; attempt < 21; attempt++)
            {
                actual = Describe(native, profiles);
                if ((string?)actual["activeProfile"] == name && Matches(requested, (JObject)actual["startupState"]!))
                { actual["accepted"] = true; actual["applied"] = true; actual["previousStartupState"] = before; return actual; }
                if (attempt < 20) { await Task.Delay(100, token); await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token); }
            }
            throw new ProxyException("profileVerificationFailed", "Visual Studio did not retain all requested actions, order and debug targets.", actual);
        }
        catch (Exception e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var details = new JObject { ["rollbackAttempted"] = false, ["requestedProfile"] = name };
            if (string.Equals(solution, (string?)projects.Read()["solution"], StringComparison.OrdinalIgnoreCase))
            {
                details["rollbackAttempted"] = true;
                var rollbackErrors = new JArray();
                try
                {
                    native.Startup.StartupMode = (int)before["mode"]!;
                    foreach (var row in ((JArray)before["projects"]!).OfType<JObject>())
                        native.Startup.SetProjectStartupSettings(Guid.Parse((string)row["id"]!), (int)row["mode"]!, (ushort)row["order"]!);
                    if (Guid.TryParse((string?)before["defaultProjectId"], out var defaultId)) native.Startup.SetDefaultStartupProject(defaultId);
                    native.Startup.CommitChanges();
                }
                catch (Exception rollback) { rollbackErrors.Add(ProtocolSupport.ReadError("startupState", rollback)); }
                foreach (var pair in previousTargets)
                {
                    try { launches.Selection(projects.Find(pair.Key)).SetCurrentDebugTarget(Guid.Parse((string)pair.Value["activeTargetType"]!),
                        (uint)pair.Value["activeTargetTypeId"]!, (string)pair.Value["activeProfile"]!); }
                    catch (Exception rollback) { rollbackErrors.Add(ProtocolSupport.ReadError(pair.Key, rollback)); }
                }
                try { native.RestoreSelected(previousSelected); }
                catch (Exception rollback) { rollbackErrors.Add(ProtocolSupport.ReadError("selectedProfile", rollback)); }
                details["rollbackErrors"] = rollbackErrors;
            }
            try { details["actual"] = Describe(native, profiles); }
            catch (Exception read) { details["readError"] = ProtocolSupport.ReadError("actual", read); }
            throw new ProxyException("solutionProfileSelectionFailed", "Profile selection failed; inspect actual state and rollback details.", details, e);
        }
    }

    private JArray Resolve(JObject profile, bool validateTargets)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new JArray();
        foreach (var item in ((JArray)profile["projects"]!).OfType<JObject>())
        {
            var project = projects.Find((string)item["Path"]!);
            if ((string?)project["loadState"] != "loaded") throw new ProxyException("projectNotLoaded", "Profile project must be loaded: " + (string?)project["path"]);
            var action = (string?)item["Action"] ?? "None";
            var mode = ActionMode(action);
            var target = (string?)item["DebugTarget"];
            if (validateTargets && !string.IsNullOrEmpty(target))
            {
                var selection = launches.ReadSelection(launches.Selection(project));
                if (((JArray)selection["profiles"]!).Count(x => (string?)x["name"] == target) != 1)
                    throw new ProxyException("profileNotFound", "Debug target must identify one IDE target: " + target);
            }
            result.Add(new JObject { ["id"] = project["id"]!.DeepClone(), ["mode"] = mode, ["order"] = result.Count, ["debugTarget"] = target });
        }
        if (result.Count == 0 || result.Count > 100 || result.Select(x => (string)x["id"]!).Distinct().Count() != result.Count)
            throw new ArgumentException("A solution profile must contain 1..100 distinct projects.");
        return result;
    }

    private JObject State(NativeSolutionLaunch native)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var rows = new JArray();
        var enumerator = native.Startup.AvailableStartupProjects;
        enumerator.Reset();
        while (enumerator.MoveNext())
        {
            var item = enumerator.Current;
            var rawId = NativeSolutionLaunch.Property(item, "ProjectGuid");
            if (!(rawId is Guid id)) throw new ProxyException("unsupported", "VS returned a startup project without ProjectGuid.");
            var row = new JObject { ["id"] = id.ToString("D"), ["name"] = NativeSolutionLaunch.Property(item, "ProjectName")?.ToString(),
                ["mode"] = Convert.ToInt32(NativeSolutionLaunch.Property(item, "StartupMode")), ["order"] = Convert.ToInt32(NativeSolutionLaunch.Property(item, "StartupOrder")), ["debugTarget"] = null };
            try { row["debugTarget"] = launches.ReadSelection(launches.Selection(projects.Find(id.ToString("D"))))["activeProfile"]!.DeepClone(); }
            catch (Exception e) { row["targetReadError"] = ProtocolSupport.ReadError("debugTarget", e); }
            rows.Add(row);
        }
        return new JObject { ["mode"] = native.Startup.StartupMode,
            ["defaultProjectId"] = NativeSolutionLaunch.Property(native.Startup.DefaultStartupProject, "ProjectGuid") is Guid defaultId ? defaultId.ToString("D") : null, ["projects"] = rows };
    }

    internal static bool Matches(JArray requested, JObject actual)
    {
        if ((int?)actual["mode"] != 1) return false;
        var rows = ((JArray)actual["projects"]!).OfType<JObject>().ToArray();
        foreach (var expected in requested.OfType<JObject>())
        {
            var found = rows.SingleOrDefault(x => (string?)x["id"] == (string?)expected["id"]);
            if (found == null || (int?)found["mode"] != (int?)expected["mode"] || (int?)found["order"] != (int?)expected["order"]) return false;
            if (!string.IsNullOrEmpty((string?)expected["debugTarget"]) && (string?)found["debugTarget"] != (string?)expected["debugTarget"]) return false;
        }
        return rows.All(x => (int?)x["mode"] == 0 || requested.Any(r => (string?)r["id"] == (string?)x["id"]));
    }

    internal static int ActionMode(string action) => action switch
    { "None" => 0, "StartWithoutDebugging" => 1, "Start" => 2, _ => throw new ArgumentException("Unknown launch action: " + action) };

    internal static int SelectIndex(JArray profiles, string name, string? scope)
    {
        if (scope != null && scope != "shared" && scope != "user") throw new ArgumentException("scope must be shared or user.");
        var matches = profiles.Select((p, i) => (p, i)).Where(x => (string?)x.p["name"] == name && (scope == null || (string?)x.p["scope"] == scope)).ToArray();
        if (matches.Length != 1) throw new ProxyException(matches.Length == 0 ? "profileNotFound" : "ambiguousProfile", "Expected exactly one solution profile; use scope to distinguish shared and user profiles.");
        return matches[0].i;
    }

    private static JObject Serialize(object profile)
    {
        var shared = (bool)NativeSolutionLaunch.Property(profile, "IsShared")!;
        return new JObject { ["name"] = (string?)NativeSolutionLaunch.Property(profile, "Name"), ["isShared"] = shared,
            ["scope"] = shared ? "shared" : "user", ["active"] = null,
            ["projects"] = new JArray(((IEnumerable)NativeSolutionLaunch.Property(profile, "Projects")!).Cast<object>().Select(p => new JObject {
                ["Path"] = (string?)NativeSolutionLaunch.Property(p, "Path"), ["Action"] = NativeSolutionLaunch.Property(p, "Action")?.ToString(),
                ["DebugTarget"] = (string?)NativeSolutionLaunch.Property(p, "DebugTarget") })) };
    }

    internal static JObject DiskProfiles(string? solution)
    {
        var result = new JObject { ["profiles"] = new JArray(), ["activeProfile"] = null, ["selectedProfileName"] = null,
            ["activeProfileAvailability"] = "unsupported", ["selectionAvailability"] = "unsupported", ["sourceKind"] = "diskConfiguration",
            ["unavailableReason"] = "The native VS 2026 solution profile adapter is unavailable; disk profiles do not prove the live selection." };
        if (string.IsNullOrEmpty(solution)) return result;
        var sharedPath = Path.ChangeExtension(solution, ".slnLaunch");
        foreach (var path in new[] { sharedPath, sharedPath + ".user" }.Where(File.Exists))
            foreach (var p in JArray.Parse(File.ReadAllText(path)).OfType<JObject>())
                ((JArray)result["profiles"]!).Add(new JObject { ["name"] = p["Name"]?.DeepClone(), ["projects"] = p["Projects"]?.DeepClone(),
                    ["scope"] = path == sharedPath ? "shared" : "user", ["source"] = path, ["active"] = null });
        return result;
    }
}
