using System;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class LaunchCheckService
{
    private readonly DTE2 dte;
    private readonly ProjectService projects;

    public LaunchCheckService(DTE2 dte, ProjectService projects) { this.dte = dte; this.projects = projects; }

    public JObject Read()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var state = projects.Read();
        var errors = new JArray();
        string? mode = null;
        string? buildState = null;
        string? configuration = null;
        int? lastBuildFailedProjects = null;
        try { mode = dte.Debugger.CurrentMode.ToString(); }
        catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("mode", exception)); }
        try
        {
            buildState = dte.Solution.SolutionBuild.BuildState.ToString();
            configuration = dte.Solution.SolutionBuild.ActiveConfiguration?.Name;
            if (buildState == vsBuildState.vsBuildStateDone.ToString()) lastBuildFailedProjects = dte.Solution.SolutionBuild.LastBuildInfo;
        }
        catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("buildState", exception)); }

        var commands = new JObject();
        foreach (var command in new[] { "Debug.Start", "Debug.StartWithoutDebugging" })
        {
            try { commands[command] = dte.Commands.Item(command).IsAvailable; }
            catch (Exception exception) { commands[command] = null; errors.Add(ProtocolSupport.ReadError(command, exception)); }
        }
        var reasons = Explain(state, mode, buildState);
        if (lastBuildFailedProjects > 0)
            reasons.Add(Reason("previousBuildFailed", "confirmed", "The last completed build reported " + lastBuildFailedProjects + " failed project(s). Inspect diagnostics and Build output; this may be from an earlier configuration."));
        var startAvailable = (bool?)commands["Debug.Start"];
        if (startAvailable == false)
            reasons.Add(Reason("commandUnavailable", "confirmed", "Debug.Start.IsAvailable is false. Visual Studio does not expose its internal reason through this property."));
        return new JObject
        {
            ["canExecuteStartCommand"] = startAvailable.HasValue ? new JValue(startAvailable.Value) : JValue.CreateNull(),
            ["startAction"] = mode == "dbgBreakMode" ? "continue" : mode == "dbgDesignMode" ? "launch" : "unknown",
            ["mode"] = mode, ["buildState"] = buildState, ["configuration"] = configuration,
            ["lastBuildFailedProjects"] = lastBuildFailedProjects.HasValue ? new JValue(lastBuildFailedProjects.Value) : JValue.CreateNull(),
            ["commands"] = commands, ["reasons"] = reasons,
            ["reasonUnknown"] = startAvailable != true,
            ["projectState"] = state, ["readErrors"] = errors,
            ["launchProfilesAvailability"] = "perProject",
            ["launchProfilesSuggestedRead"] = "launchProfiles(project) and solutionLaunchProfiles; the latter reports the live VS 2026 solution profile when the compatibility adapter matches.",
            ["note"] = "Command availability is not confirmation that the application can start. Reasons describe observed conditions, not VS internal command-routing decisions."
        };
    }

    internal static JArray Explain(JObject state, string? mode, string? buildState)
    {
        var result = new JArray();
        if ((bool?)state["isOpen"] == false) result.Add(Reason("noSolution", "confirmed", "No solution is open."));
        if ((bool?)state["isOpen"] == true && (bool?)state["isFullyLoaded"] == false) result.Add(Reason("solutionLoading", "confirmed", "The solution is not fully loaded."));
        if (buildState == vsBuildState.vsBuildStateInProgress.ToString()) result.Add(Reason("buildInProgress", "confirmed", "A solution build is in progress."));
        if (mode == "dbgRunMode") result.Add(Reason("debuggerRunning", "confirmed", "The debugger is already running."));
        if (mode == "dbgBreakMode") result.Add(Reason("debuggerPaused", "confirmed", "Debug.Start would continue the current session."));
        var startup = state["startupProjects"] as JArray ?? new JArray();
        if ((bool?)state["isOpen"] == true && (bool?)state["startupProjectsAvailable"] == true && startup.Count == 0)
            result.Add(Reason("noStartupProjects", "suspected", "DTE reports no startup projects; alternative launch providers may use a different selection."));
        foreach (var project in (state["projects"] as JArray ?? new JArray()).OfType<JObject>().Where(p => (bool?)p["isStartup"] == true))
        {
            var loadState = (string?)project["loadState"];
            if (loadState == "failed" || loadState == "unloaded")
            {
                var reason = Reason("startupProjectNotLoaded", "confirmed", "A selected startup project is " + loadState + ".");
                reason["project"] = project["path"]?.DeepClone();
                reason["loadError"] = project["loadError"]?.DeepClone();
                result.Add(reason);
            }
        }
        return result;
    }

    private static JObject Reason(string code, string confidence, string evidence) => new()
    { ["code"] = code, ["confidence"] = confidence, ["evidence"] = evidence };
}
