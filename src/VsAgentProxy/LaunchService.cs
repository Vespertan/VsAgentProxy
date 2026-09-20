using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class LaunchService
{
    private readonly ProjectService projects;
    private readonly IVsSolutionBuildManager2? build;
    private readonly IVsDebugTargetSelectionService? selectionService;
    public LaunchService(ProjectService projects, IVsSolutionBuildManager2? build, IVsDebugTargetSelectionService? selectionService)
    { this.projects = projects; this.build = build; this.selectionService = selectionService; }

    internal IVsProjectCfgDebugTargetSelection Selection(JObject project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (build == null || selectionService == null) throw new ProxyException("unsupported", "Debug target selection services are unavailable.");
        var configurations = new IVsProjectCfg[1];
        ErrorHandler.ThrowOnFailure(build.FindActiveProjectCfg(IntPtr.Zero, IntPtr.Zero, projects.Hierarchy(project), configurations));
        if (configurations[0] is IVsProjectCfgDebugTargetSelection selection) return selection;
        if (configurations[0] is IVsProjectFlavorCfg flavor)
        {
            var iid = typeof(IVsProjectCfgDebugTargetSelection).GUID;
            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (flavor.get_CfgType(ref iid, out pointer) >= 0 && pointer != IntPtr.Zero && Marshal.GetObjectForIUnknown(pointer) is IVsProjectCfgDebugTargetSelection target)
                    return target;
            }
            finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
        }
        if (projects.Hierarchy(project) is IVsProjectCfgDebugTargetSelection hierarchySelection) return hierarchySelection;
        var hierarchy = projects.Hierarchy(project);
        if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_BrowseObject, out var browse) >= 0
            && browse is Microsoft.VisualStudio.ProjectSystem.Properties.IVsBrowseObjectContext context)
        {
            var configured = context.ConfiguredProject ?? context.UnconfiguredProject.Services.ActiveConfiguredProjectProvider?.ActiveConfiguredProject;
            var target = configured?.Services.ExportProvider.GetExports<IVsProjectCfgDebugTargetSelection>(
                "Microsoft.VisualStudio.ProjectSystem.Microsoft.VisualStudio.Shell.Interop.IVsProjectCfgDebugTargetSelection").SingleOrDefault()?.Value;
            if (target != null) return target;
        }
        throw new ProxyException("unsupported", "The active project configuration does not expose IVsProjectCfgDebugTargetSelection.");
    }

    public JObject Read(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var project = projects.Find(ProtocolSupport.String(parameters, "project") ?? throw new ArgumentException("project is required."));
        var result = new JObject { ["project"] = project, ["availability"] = "available", ["source"] = "IVsProjectCfgDebugTargetSelection" };
        try { result.Merge(ReadSelection(Selection(project))); }
        catch (ProxyException exception) when (exception.Code == "unsupported")
        { result["availability"] = "unsupported"; result["unavailableReason"] = exception.Message; result["profiles"] = new JArray(); result["activeProfile"] = null; }
        var directory = Path.GetDirectoryName((string)project["path"]!);
        var files = new[] { Path.Combine(directory!, ".vscode", "launch.json"), Path.Combine(directory!, "Properties", "launchSettings.json") };
        var configured = new JArray();
        var errors = new JArray();
        foreach (var file in files.Where(File.Exists))
        {
            try
            {
                var json = JObject.Parse(File.ReadAllText(file));
                if (json["configurations"] is JArray configs)
                    foreach (var config in configs.OfType<JObject>()) configured.Add(ConfiguredProfile(config, (string?)config["name"], file));
                if (json["profiles"] is JObject profiles)
                    foreach (var property in profiles.Properties())
                        if (property.Value is JObject config) configured.Add(ConfiguredProfile(config, property.Name, file));
            }
            catch (Exception exception) { errors.Add(ProtocolSupport.ReadError(file, exception)); }
        }
        result["configuredProfiles"] = configured;
        result["configurationReadErrors"] = errors;
        result["effectiveCommandAvailability"] = "unavailable";
        result["configurationNote"] = "Disk configuration is not proof of an active IDE profile or resolved command. Environment variables are omitted.";
        result["projectProperties"] = projects.Properties(new JObject { ["project"] = project["id"],
            ["names"] = new JArray("StartupCommand", "BuildCommand", "BuildCommandWorkingDirectory", "LaunchJsonTarget", "ActiveDebugProfile") });
        var properties = (JArray)result["projectProperties"]!["properties"]!;
        result["evaluatedStartupCommand"] = properties.FirstOrDefault(x => (string?)x["name"] == "StartupCommand")?["value"]?.DeepClone();
        result["evaluatedBuildWorkingDirectory"] = properties.FirstOrDefault(x => (string?)x["name"] == "BuildCommandWorkingDirectory")?["value"]?.DeepClone();
        result["effectiveCommandNote"] = "Evaluated project properties are reported separately; debugger-provider substitutions and the final launched process command remain unconfirmed.";
        return result;
    }

    internal JObject ReadSelection(IVsProjectCfgDebugTargetSelection selection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        selection.GetCurrentDebugTarget(out var activeType, out var activeId, out var activeName);
        var profiles = new JArray();
        if (selection.HasDebugTargets(selectionService!, out var types) && types != null)
        {
            foreach (var type in types.Cast<string>().Distinct())
            {
                var split = type.LastIndexOf(':');
                if (split < 0 || !Guid.TryParse(type.Substring(0, split), out var guid) || !uint.TryParse(type.Substring(split + 1), out var id)) continue;
                foreach (var name in selection.GetDebugTargetListOfType(guid, id).Cast<string>())
                    profiles.Add(new JObject { ["name"] = name, ["targetType"] = guid.ToString("D"), ["targetTypeId"] = id,
                        ["active"] = guid == activeType && id == activeId && name == activeName });
            }
        }
        return new JObject { ["profiles"] = profiles, ["activeProfile"] = activeName,
            ["activeTargetType"] = activeType.ToString("D"), ["activeTargetTypeId"] = activeId };
    }

    public JObject Select(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        projects.RequireIdle();
        var project = projects.Find(ProtocolSupport.String(parameters, "project") ?? throw new ArgumentException("project is required."));
        var name = ProtocolSupport.String(parameters, "name") ?? throw new ArgumentException("name is required.");
        var selection = Selection(project);
        var before = ReadSelection(selection);
        var matches = ((JArray)before["profiles"]!).Where(x => (string?)x["name"] == name).ToArray();
        if (matches.Length != 1) throw new ProxyException("profileNotFound", "Profile name must identify exactly one IDE debug target.", before);
        var chosen = matches[0];
        selection.SetCurrentDebugTarget(Guid.Parse((string)chosen["targetType"]!), (uint)chosen["targetTypeId"]!, name);
        var actual = ReadSelection(selection);
        actual["applied"] = (string?)actual["activeProfile"] == name && (string?)actual["activeTargetType"] == (string?)chosen["targetType"] && (uint?)actual["activeTargetTypeId"] == (uint?)chosen["targetTypeId"];
        actual["previous"] = before;
        // Some project systems publish the new snapshot asynchronously. Never claim immediate success from dispatch alone.
        actual["accepted"] = true;
        return actual;
    }

    internal static JObject ConfiguredProfile(JObject config, string? name, string file) => new()
    {
        ["name"] = name, ["source"] = file, ["sourceKind"] = "diskConfiguration", ["active"] = null,
        ["type"] = config["type"]?.DeepClone() ?? config["commandName"]?.DeepClone(),
        ["url"] = config["url"]?.DeepClone() ?? config["applicationUrl"]?.DeepClone(),
        ["workingDirectory"] = config["cwd"]?.DeepClone() ?? config["workingDirectory"]?.DeepClone(),
        ["program"] = config["program"]?.DeepClone() ?? config["executablePath"]?.DeepClone(),
        ["arguments"] = config["args"]?.DeepClone() ?? config["commandLineArgs"]?.DeepClone()
    };

}
