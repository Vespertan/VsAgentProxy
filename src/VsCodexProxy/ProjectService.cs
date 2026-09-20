using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

internal sealed class ProjectService
{
    private readonly DTE2 dte;
    private readonly IVsSolution? solution;
    private static readonly Guid SolutionFolderType = new("66A26720-8FB5-11D2-AA7E-00C04F688DDE");

    public ProjectService(DTE2 dte, IVsSolution? solution)
    {
        this.dte = dte;
        this.solution = solution;
    }

    public void RequireIdle()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!dte.Solution.IsOpen) throw new ProxyException("noSolution", "No solution is open.");
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode || dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
            throw new ProxyException("ideBusy", "This operation requires design mode and no build in progress.");
        if ((bool?)Read()["isFullyLoaded"] != true) throw new ProxyException("solutionLoading", "The solution is not confirmed fully loaded.");
    }

    public JObject Find(string selector)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var projects = ((JArray)Read()["projects"]!).OfType<JObject>();
        var path = ResolvePath(selector, Path.GetDirectoryName(dte.Solution.FullName));
        var matches = projects.Where(x => (bool?)x["isSolutionFolder"] == false &&
            (string.Equals((string?)x["path"], path, StringComparison.OrdinalIgnoreCase) ||
             string.Equals((string?)x["id"], selector.Trim('{', '}'), StringComparison.OrdinalIgnoreCase) ||
             string.Equals((string?)x["uniqueName"], selector, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length != 1) throw new ProxyException("projectNotFound", "Expected one project matching path, uniqueName or GUID: " + selector);
        return matches[0];
    }

    public IVsHierarchy Hierarchy(JObject project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var guid = Guid.Parse((string)project["id"]!);
        ErrorHandler.ThrowOnFailure(solution!.GetProjectOfGuid(ref guid, out var hierarchy));
        return hierarchy;
    }

    public JObject Reload(JObject parameters, DocumentService documents)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        RequireIdle();
        var project = Find(ProtocolSupport.String(parameters, "path") ?? throw new ArgumentException("path is required."));
        if (!(solution is IVsSolution4 reload)) throw new ProxyException("unsupported", "IVsSolution4 is unavailable.");
        documents.RequireClean();
        var id = Guid.Parse((string)project["id"]!);
        RequireIdle();
        documents.RequireClean();
        var unloaded = false;
        try
        {
            if ((string?)project["loadState"] == "loaded")
            {
                ErrorHandler.ThrowOnFailure(reload.UnloadProject(ref id, (uint)_VSProjectUnloadStatus.UNLOADSTATUS_UnloadedByUser));
                unloaded = true;
            }
            ErrorHandler.ThrowOnFailure(reload.ReloadProject(ref id));
        }
        catch (Exception exception)
        {
            throw new ProxyException("projectReloadFailed", "Project reload failed; inspect the returned current project state.",
                new JObject { ["unloadedByProxy"] = unloaded, ["projects"] = Read() }, exception);
        }
        var actual = Find(id.ToString("D"));
        return new JObject { ["accepted"] = true, ["unloadedByProxy"] = unloaded,
            ["loaded"] = (string?)actual["loadState"] == "loaded", ["project"] = actual };
    }

    public JObject Startup()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var state = Read();
        return new JObject { ["paths"] = new JArray(((JArray)state["projects"]!).Where(x => (bool?)x["isStartup"] == true).Select(x => x["path"])),
            ["uniqueNames"] = state["startupProjects"], ["available"] = state["startupProjectsAvailable"], ["source"] = "DTE.SolutionBuild.StartupProjects" };
    }

    public JObject SetStartup(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        RequireIdle();
        var requested = ProtocolSupport.Strings(parameters, "paths").Select(Find).ToArray();
        if (requested.Any(x => (string?)x["loadState"] != "loaded")) throw new ProxyException("projectNotLoaded", "Every startup project must be loaded.");
        var uniqueNames = requested.Select(x => (string)x["uniqueName"]!).ToArray();
        if (uniqueNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != uniqueNames.Length) throw new ArgumentException("Startup projects must be distinct.");
        var previous = dte.Solution.SolutionBuild.StartupProjects;
        try
        {
            dte.Solution.SolutionBuild.StartupProjects = uniqueNames;
            var actual = Startup();
            if (!SameSelection((JArray)actual["uniqueNames"]!, uniqueNames)) throw new InvalidOperationException("Visual Studio did not retain the requested startup project selection.");
            actual["applied"] = true;
            return actual;
        }
        catch (Exception exception)
        {
            var details = new JObject();
            try { dte.Solution.SolutionBuild.StartupProjects = previous; details["rollbackDispatched"] = true; }
            catch (Exception rollback) { details["rollbackError"] = ProtocolSupport.ReadError("rollback", rollback); }
            details["actual"] = Startup();
            throw new ProxyException("startupSelectionFailed", "Startup selection failed; rollback was attempted.", details, exception);
        }
    }

    internal static bool SameSelection(JArray actual, string[] expected) => actual.Values<string>().SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);

    public JObject Properties(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var project = Find(ProtocolSupport.String(parameters, "project") ?? throw new ArgumentException("project is required."));
        var names = ProtocolSupport.Strings(parameters, "names");
        var configuration = dte.Solution.SolutionBuild.ActiveConfiguration;
        var configName = configuration.Name;
        if (configuration is SolutionConfiguration2 config2) configName += "|" + config2.PlatformName;
        var hierarchy = Hierarchy(project);
        if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out var automation) >= 0
            && automation is Project dteProject && dteProject.ConfigurationManager?.ActiveConfiguration is Configuration projectConfiguration)
            configName = projectConfiguration.ConfigurationName + "|" + projectConfiguration.PlatformName;
        var properties = new JArray();
        var storage = hierarchy as IVsBuildPropertyStorage;
        foreach (var name in names)
        {
            var item = new JObject { ["name"] = name, ["value"] = null, ["availability"] = "unavailable" };
            if (storage != null)
            {
                try
                {
                    ErrorHandler.ThrowOnFailure(storage.GetPropertyValue(name, configName, (uint)_PersistStorageType.PST_PROJECT_FILE, out var value));
                    item["value"] = value; item["availability"] = "available";
                }
                catch (Exception exception) { item["error"] = ProtocolSupport.ReadError(name, exception); }
            }
            properties.Add(item);
        }
        return new JObject { ["project"] = project, ["configuration"] = configName, ["properties"] = properties, ["source"] = "IVsBuildPropertyStorage" };
    }

    public JObject Configurations()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var configs = new JArray();
        var active = dte.Solution.SolutionBuild.ActiveConfiguration;
        foreach (SolutionConfiguration configuration in dte.Solution.SolutionBuild.SolutionConfigurations)
            configs.Add(new JObject { ["name"] = configuration.Name, ["platform"] = (configuration as SolutionConfiguration2)?.PlatformName,
                ["active"] = configuration.Name == active.Name && (configuration as SolutionConfiguration2)?.PlatformName == (active as SolutionConfiguration2)?.PlatformName });
        return new JObject { ["configurations"] = configs };
    }

    public JObject Read()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (solution == null) throw new ProxyException("serviceUnavailable", "IVsSolution is unavailable.");
        var errors = new JArray();
        var items = new JArray();
        var startup = new List<string>();
        var startupAvailable = true;
        try
        {
            var value = dte.Solution.SolutionBuild.StartupProjects;
            if (value is Array array) startup.AddRange(array.Cast<object>().Select(Convert.ToString).Where(s => !string.IsNullOrWhiteSpace(s))!);
            else if (value is string text && !string.IsNullOrWhiteSpace(text)) startup.Add(text);
        }
        catch (Exception exception) { startupAvailable = false; errors.Add(ProtocolSupport.ReadError("startupProjects", exception)); }
        var solutionFile = dte.Solution.FullName;
        var baseDirectory = string.IsNullOrEmpty(solutionFile) ? null : Path.GetDirectoryName(solutionFile);
        foreach (var group in new[] { (Flags: __VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, State: "loaded"), (Flags: __VSENUMPROJFLAGS.EPF_UNLOADEDINSOLUTION, State: "unloaded") })
        {
            var type = Guid.Empty;
            ErrorHandler.ThrowOnFailure(solution.GetProjectEnum((uint)group.Flags, ref type, out var enumerator));
            var hierarchies = new IVsHierarchy[1];
            while (true)
            {
                var hr = enumerator.Next(1, hierarchies, out var fetched);
                ErrorHandler.ThrowOnFailure(hr);
                if (fetched == 0) break;
                var hierarchy = hierarchies[0];
                var itemErrors = new JArray();
                var projectId = ReadGuid(() => { ThreadHelper.ThrowIfNotOnUIThread(); ErrorHandler.ThrowOnFailure(solution.GetGuidOfProject(hierarchy, out var guid)); return guid; }, "id", itemErrors);
                var hierarchyType = ReadGuid(() => { ThreadHelper.ThrowIfNotOnUIThread(); ErrorHandler.ThrowOnFailure(hierarchy.GetGuidProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_TypeGuid, out var guid)); return guid; }, "hierarchyTypeGuid", itemErrors);
                string? kind = null;
                try
                {
                    if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out var automation) >= 0
                        && automation is Project projectObject && Guid.TryParse(projectObject.Kind, out var projectKind))
                        kind = projectKind.ToString("D");
                }
                catch (Exception exception) { itemErrors.Add(ProtocolSupport.ReadError("typeGuid", exception)); }
                var name = ReadProperty(hierarchy, (int)__VSHPROPID.VSHPROPID_Name, "name", itemErrors);
                string? uniqueName = null;
                try { ErrorHandler.ThrowOnFailure(solution.GetUniqueNameOfProject(hierarchy, out uniqueName)); }
                catch (Exception exception) { itemErrors.Add(ProtocolSupport.ReadError("uniqueName", exception)); }
                var isFolder = string.Equals(kind, SolutionFolderType.ToString("D"), StringComparison.OrdinalIgnoreCase);
                var faultAvailable = false;
                var faulted = false;
                try
                {
                    faultAvailable = hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID5.VSHPROPID_IsFaulted, out var faultValue) >= 0 && faultValue is bool;
                    faulted = faultAvailable && (bool)faultValue;
                }
                catch (Exception exception) { itemErrors.Add(ProtocolSupport.ReadError("isFaulted", exception)); }
                string? faultMessage = null;
                if (faulted)
                    faultMessage = ReadProperty(hierarchy, (int)__VSHPROPID5.VSHPROPID_FaultMessage, "loadError", itemErrors);
                string? path = null;
                if (!isFolder)
                {
                    try
                    {
                        if (hierarchy is IVsProject project)
                            ErrorHandler.ThrowOnFailure(project.GetMkDocument(VSConstants.VSITEMID_ROOT, out path));
                    }
                    catch (Exception exception) { itemErrors.Add(ProtocolSupport.ReadError("projectDocument", exception)); }
                    try
                    {
                        if (string.IsNullOrWhiteSpace(path) && uniqueName?.StartsWith("<", StringComparison.Ordinal) != true) path = uniqueName;
                        path = ResolvePath(path, baseDirectory);
                    }
                    catch (Exception exception) { path = null; itemErrors.Add(ProtocolSupport.ReadError("path", exception)); }
                }
                items.Add(new JObject
                {
                    ["id"] = projectId, ["name"] = name, ["path"] = path, ["uniqueName"] = uniqueName,
                    ["typeGuid"] = kind, ["hierarchyTypeGuid"] = hierarchyType,
                    ["type"] = isFolder ? "solutionFolder" : path == null ? "unknown" : Path.GetExtension(path),
                    ["isSolutionFolder"] = isFolder, ["loadState"] = faulted ? "failed" : group.State,
                    ["isStartup"] = startupAvailable ? new JValue(startup.Any(s => MatchesStartup(s, uniqueName, path, baseDirectory))) : JValue.CreateNull(),
                    ["loadError"] = faultMessage,
                    ["loadErrorAvailability"] = faultAvailable && (!faulted || faultMessage != null) ? "available" : "unavailable",
                    ["loadErrorSource"] = faultMessage == null ? null : "IVsHierarchy.FaultMessage",
                    ["loadErrorUnavailableReason"] = faultAvailable && (!faulted || faultMessage != null) ? null : "The project hierarchy does not expose fault information; unloaded does not imply failed.",
                    ["readErrors"] = itemErrors
                });
            }
        }
        bool? fullyLoaded = null;
        try
        {
            ErrorHandler.ThrowOnFailure(solution.GetProperty((int)__VSPROPID4.VSPROPID_IsSolutionFullyLoaded, out var value));
            if (value is bool boolean) fullyLoaded = boolean;
        }
        catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("isFullyLoaded", exception)); }
        return new JObject
        {
            ["solution"] = solutionFile, ["isOpen"] = dte.Solution.IsOpen,
            ["isFullyLoaded"] = fullyLoaded.HasValue ? new JValue(fullyLoaded.Value) : JValue.CreateNull(),
            ["projects"] = items, ["startupProjects"] = new JArray(startup),
            ["startupProjectsAvailable"] = startupAvailable, ["readErrors"] = errors
        };
    }

    internal static string? ResolvePath(string? path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        return baseDirectory == null ? null : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    internal static JArray FaultDiagnostics(JObject state) => new JArray(
        (state["projects"] as JArray ?? new JArray()).OfType<JObject>()
        .Where(project => (string?)project["loadState"] == "failed")
        .Select(project => new JObject
        {
            ["severity"] = "error", ["origin"] = "project-load",
            ["source"] = "IVsHierarchy", ["provider"] = "Project system", ["sourceType"] = "project-load",
            ["project"] = project["name"]?.DeepClone(), ["projectId"] = project["id"]?.DeepClone(),
            ["file"] = project["path"]?.DeepClone(), ["line"] = null, ["column"] = null, ["code"] = null,
            ["message"] = project["loadError"]?.DeepClone(), ["faulted"] = true
        }));

    internal static bool MatchesStartup(string startup, string? uniqueName, string? path, string? baseDirectory)
    {
        if (string.Equals(startup, uniqueName, StringComparison.OrdinalIgnoreCase)) return true;
        try { return path != null && string.Equals(ResolvePath(startup, baseDirectory), path, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    private static string? ReadGuid(Func<Guid> read, string field, JArray errors)
    {
        try { var guid = read(); return guid == Guid.Empty ? null : guid.ToString("D"); }
        catch (Exception exception) { errors.Add(ProtocolSupport.ReadError(field, exception)); return null; }
    }

    private static string? ReadProperty(IVsHierarchy hierarchy, int property, string field, JArray errors)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            ErrorHandler.ThrowOnFailure(hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, property, out var value));
            return value as string;
        }
        catch (Exception exception) { errors.Add(ProtocolSupport.ReadError(field, exception)); return null; }
    }
}
