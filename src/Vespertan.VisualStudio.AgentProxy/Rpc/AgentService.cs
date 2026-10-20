using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Vespertan.VisualStudio.AgentProxy.Contracts;
using Newtonsoft.Json.Linq;
using DiagnosticsProcess = System.Diagnostics.Process;
using DteBreakpoint = EnvDTE.Breakpoint;
using DteExpression = EnvDTE.Expression;
using DteStackFrame = EnvDTE.StackFrame;
using DteStackFrame2 = EnvDTE90a.StackFrame2;
using Vespertan.VisualStudio.AgentProxy.Services;

namespace Vespertan.VisualStudio.AgentProxy.Rpc;

internal sealed class AgentService : IAgentService
{
    private readonly DTE2 _dte;
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly OperationRegistry _operations = new();
    private readonly DocumentService _documents;
    private readonly IdeEvents _ideEvents;
    private readonly LaunchService _launches;
    private readonly SolutionLaunchService _solutionLaunches;
    private readonly DebugInspectionService _inspection;
    private readonly AgentRequestExecutor _requests = new();
    private readonly EngineEvents _engineEvents;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly OutputService _outputService;
    private readonly ProjectService _projectService;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly LaunchCheckService _launchCheckService;

    public AgentService(DTE2 dte, JoinableTaskFactory joinableTaskFactory,
        OutputService outputService, ProjectService projectService, DiagnosticsService diagnosticsService, DocumentService documents,
        Microsoft.VisualStudio.Shell.Interop.IVsSolutionBuildManager2? buildManager,
        Microsoft.VisualStudio.Shell.Interop.IVsDebugTargetSelectionService? targetSelection,
        Microsoft.VisualStudio.Shell.Interop.IVsDebugger? debuggerService)
    {
        this._dte = dte;
        this._joinableTaskFactory = joinableTaskFactory;
        this._outputService = outputService;
        this._projectService = projectService;
        this._diagnosticsService = diagnosticsService;
        this._documents = documents;
        _ideEvents = new IdeEvents(dte, buildManager, _operations);
        _launches = new LaunchService(projectService, buildManager, targetSelection);
        _solutionLaunches = new SolutionLaunchService(projectService, _launches, documents);
        _inspection = new DebugInspectionService(dte, _ideEvents);
        _engineEvents = new EngineEvents(debuggerService, joinableTaskFactory, _operations);
        _launchCheckService = new LaunchCheckService(dte, projectService);
    }

    public string GetStatusText()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var solution = SafeRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return _dte.Solution.FullName; }, "Unavailable");
        var mode = SafeRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return _dte.Debugger.CurrentMode.ToString(); }, "Unavailable");
        var pid = DiagnosticsProcess.GetCurrentProcess().Id;
        var text = new StringBuilder()
            .AppendLine("Version: " + AgentDocumentation.Version)
            .AppendLine("Service: registered with the Visual Studio broker")
            .AppendLine("Pipe: \\\\.\\pipe\\" + Vespertan.VisualStudio.Contracts.HubServices.PipePrefix + pid)
            .AppendLine("Visual Studio PID: " + pid)
            .AppendLine("Started (UTC): " + _startedUtc.ToString("yyyy-MM-dd HH:mm:ss"))
            .AppendLine("Session: " + _operations.SessionId)
            .AppendLine()
            .AppendLine("Solution: " + (string.IsNullOrEmpty(solution) ? "None open" : solution))
            .AppendLine("Debugger: " + mode);
        return text.AppendLine().AppendLine("Check from a terminal:")
            .AppendLine($"vsagent --pid {pid} status")
            .AppendLine().Append("Snapshot at opening. Press Ctrl+C to copy.").ToString();
    }

    public Task<AgentResult<AgentInfo>> PingAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<AgentInfo>("ping", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return new { version = AgentDocumentation.Version, pid = DiagnosticsProcess.GetCurrentProcess().Id, sessionId = _operations.SessionId };
        }, cancellationToken);
    }

    public Task<AgentResult<AgentCapabilities>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<AgentCapabilities>("capabilities", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return AgentDocumentation.GetCapabilities();
        }, cancellationToken);
    }

    public Task<AgentResult<AgentGuide>> GetDocumentationAsync(DocumentationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AgentGuide>("documentation", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return AgentDocumentation.GetDocumentation(parameters.Value<string>("level"));
        }, cancellationToken);
    }

    public Task<AgentResult<DebuggerStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<DebuggerStatus>("status", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetStatus();
        }, cancellationToken);
    }

    public Task<AgentResult<ProjectState>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<ProjectState>("projects", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _projectService.Read();
        }, cancellationToken);
    }

    public Task<AgentResult<DocumentsResult>> GetDocumentsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<DocumentsResult>("documents", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _documents.Read();
        }, cancellationToken);
    }

    public Task<AgentResult<SaveDocumentsResult>> SaveDocumentsAsync(PathsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<SaveDocumentsResult>("saveDocuments", options, true, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _documents.Save(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<ReloadProjectResult>> ReloadProjectAsync(ReloadProjectOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<ReloadProjectResult>("projectReload", options, true, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = _projectService.Reload(parameters, _documents);
            _operations.Emit("projectReload", JObject.FromObject(result));
            return result;
        }, cancellationToken);
    }

    public Task<AgentResult<StartupProjectsResult>> GetStartupProjectsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<StartupProjectsResult>("startupProjects", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _projectService.Startup();
        }, cancellationToken);
    }

    public Task<AgentResult<StartupProjectsResult>> SetStartupProjectsAsync(PathsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<StartupProjectsResult>("setStartupProjects", options, true, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = _projectService.SetStartup(parameters);
            _operations.Emit("startupProjectsChanged", JObject.FromObject(result));
            return result;
        }, cancellationToken);
    }

    public Task<AgentResult<ProjectPropertiesResult>> GetProjectPropertiesAsync(ProjectPropertiesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<ProjectPropertiesResult>("projectProperties", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _projectService.Properties(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<ConfigurationsResult>> GetConfigurationsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<ConfigurationsResult>("configurations", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _projectService.Configurations();
        }, cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> CancelBuildAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AcceptedResult>("cancelBuild", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.CancelBuild();
        }, cancellationToken);
    }

    public Task<AgentResult<AgentOperation>> GetOperationStatusAsync(OperationStatusOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AgentOperation>("operationStatus", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _operations.Status(ProtocolSupport.String(parameters, "id") ?? throw new ArgumentException("id is required."));
        }, cancellationToken);
    }

    public Task<AgentResult<EventsResult>> GetEventsAsync(EventsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<EventsResult>("events", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _operations.Events(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<StopReasonResult>> GetStopReasonAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<StopReasonResult>("stopReason", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.StopReason;
        }, cancellationToken);
    }

    public Task<AgentResult<LaunchProfilesResult>> GetLaunchProfilesAsync(LaunchProfilesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<LaunchProfilesResult>("launchProfiles", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _launches.Read(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<LaunchProfilesResult>> SelectLaunchProfileAsync(SelectLaunchProfileOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<LaunchProfilesResult>("selectLaunchProfile", options, true, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = _launches.Select(parameters);
            _operations.Emit("launchProfileChanged", JObject.FromObject(result));
            return result;
        }, cancellationToken);
    }

    public Task<AgentResult<SolutionLaunchProfilesResult>> GetSolutionLaunchProfilesAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<SolutionLaunchProfilesResult>("solutionLaunchProfiles", null, false, async (parameters, token) =>
        {
            return await _solutionLaunches.ReadAsync(token);
        }, cancellationToken);
    }

    public Task<AgentResult<SolutionLaunchProfilesResult>> SelectSolutionLaunchProfileAsync(SelectSolutionLaunchProfileOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<SolutionLaunchProfilesResult>("selectSolutionLaunchProfile", options, true, async (parameters, token) =>
        {
            var result = await _solutionLaunches.SelectAsync(parameters, token);
            _operations.Emit("solutionLaunchProfileChanged", JObject.FromObject(result));
            return result;
        }, cancellationToken);
    }

    public Task<AgentResult<LanguageServiceStatus>> GetLanguageServiceStatusAsync(DocumentPathOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<LanguageServiceStatus>("languageServiceStatus", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _documents.LanguageStatus(ProtocolSupport.String(parameters, "path") ?? throw new ArgumentException("path is required."));
        }, cancellationToken);
    }

    public Task<AgentResult<TerminalsResult>> GetTerminalsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<TerminalsResult>("terminals", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return TerminalSupport();
        }, cancellationToken);
    }

    public Task<AgentResult<TerminalsResult>> GetTerminalOutputAsync(TerminalOutputOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<TerminalsResult>("terminalOutput", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            throw new ProxyException("unsupported", "Public VS terminal APIs do not expose existing terminals' output history.", TerminalSupport());
        }, cancellationToken);
    }

    public Task<AgentResult<ScopesResult>> GetScopesAsync(ScopesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<ScopesResult>("scopes", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _inspection.Scopes(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<VariablesResult>> GetVariablesAsync(VariablesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<VariablesResult>("variables", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _inspection.Variables(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<ThreadsResult>> GetThreadsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<ThreadsResult>("threads", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _inspection.Threads();
        }, cancellationToken);
    }

    public Task<AgentResult<ProcessesResult>> GetProcessesAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<ProcessesResult>("processes", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _inspection.Processes();
        }, cancellationToken);
    }

    public Task<AgentResult<SelectContextResult>> SelectContextAsync(SelectContextOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<SelectContextResult>("selectContext", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _inspection.SelectContext(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<LaunchCheckResult>> GetLaunchCheckAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<LaunchCheckResult>("launchCheck", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _launchCheckService.Read();
        }, cancellationToken);
    }

    public Task<AgentResult<StackFrameInfo[]>> GetStackTraceAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<StackFrameInfo[]>("stackTrace", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetStackTrace();
        }, cancellationToken);
    }

    public Task<AgentResult<FrameExpressionsResult>> GetLocalsAsync(FrameExpressionsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<FrameExpressionsResult>("locals", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _inspection.RequireBreak();
            return GetFrameExpressions(parameters, arguments: false);
        }, cancellationToken);
    }

    public Task<AgentResult<FrameExpressionsResult>> GetArgumentsAsync(FrameExpressionsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<FrameExpressionsResult>("arguments", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _inspection.RequireBreak();
            return GetFrameExpressions(parameters, arguments: true);
        }, cancellationToken);
    }

    public Task<AgentResult<EvaluateResult>> EvaluateAsync(EvaluateOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<EvaluateResult>("evaluate", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Evaluate(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo[]>> GetBreakpointsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<BreakpointInfo[]>("breakpoints", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetBreakpoints();
        }, cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo[]>> AddBreakpointAsync(AddBreakpointOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<BreakpointInfo[]>("breakpointAdd", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return AddBreakpoint(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<RemoveBreakpointResult>> RemoveBreakpointAsync(BreakpointSelector options, CancellationToken cancellationToken)
    {
        return InvokeAsync<RemoveBreakpointResult>("breakpointRemove", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return RemoveBreakpoint(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo>> SetBreakpointEnabledAsync(SetBreakpointEnabledOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<BreakpointInfo>("breakpointSetEnabled", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return SetBreakpointEnabled(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo[]>> SetBreakpointCriteriaAsync(SetBreakpointCriteriaOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<BreakpointInfo[]>("breakpointSetCriteria", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return SetBreakpointCriteria(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<ActiveDocumentResult>> GetActiveDocumentAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<ActiveDocumentResult>("activeDocument", null, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetActiveDocument();
        }, cancellationToken);
    }

    public Task<AgentResult<OutputResult>> GetOutputAsync(OutputOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<OutputResult>("output", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _outputService.Read(parameters);
        }, cancellationToken);
    }

    public Task<AgentResult<DiagnosticsResult>> GetDiagnosticsAsync(DiagnosticsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<DiagnosticsResult>("diagnostics", options, false, async (parameters, token) =>
        {
            return await ReadDiagnosticsAsync(parameters, false, token);
        }, cancellationToken);
    }

    public Task<AgentResult<DiagnosticsResult>> GetDocumentDiagnosticsAsync(DocumentDiagnosticsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<DiagnosticsResult>("documentDiagnostics", options, false, async (parameters, token) =>
        {
            return await ReadDiagnosticsAsync(parameters, true, token);
        }, cancellationToken);
    }

    public Task<AgentResult<AgentSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync<AgentSnapshot>("snapshot", null, false, async (parameters, token) =>
        {
            return await ReadSnapshotAsync(token);
        }, cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> BuildAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<OperationAcceptedResult>("build", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.Execute("build", "Build.BuildSolution");
        }, cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> RebuildAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<OperationAcceptedResult>("rebuild", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.Execute("rebuild", "Build.RebuildSolution");
        }, cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> CleanAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<OperationAcceptedResult>("clean", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.Execute("clean", "Build.CleanSolution");
        }, cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> StartAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<OperationAcceptedResult>("start", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.Execute("start", "Debug.Start");
        }, cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> StartWithoutDebuggingAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<OperationAcceptedResult>("startWithoutDebugging", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.Execute("startWithoutDebugging", "Debug.StartWithoutDebugging");
        }, cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> RestartAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<OperationAcceptedResult>("restart", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _ideEvents.Execute("restart", "Debug.Restart");
        }, cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StepOverAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AcceptedResult>("stepOver", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _inspection.RequireBreak();
            ExecuteDebuggerCommand("Debug.StepOver");
            return new { accepted = true };
        }, cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StepIntoAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AcceptedResult>("stepInto", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _inspection.RequireBreak();
            ExecuteDebuggerCommand("Debug.StepInto");
            return new { accepted = true };
        }, cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StepOutAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AcceptedResult>("stepOut", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _inspection.RequireBreak();
            ExecuteDebuggerCommand("Debug.StepOut");
            return new { accepted = true };
        }, cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> ContinueAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AcceptedResult>("continue", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ExecuteDebuggerCommand("Debug.Start");
            return new { accepted = true };
        }, cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> BreakAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AcceptedResult>("break", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ExecuteDebuggerCommand("Debug.BreakAll");
            return new { accepted = true };
        }, cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StopAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync<AcceptedResult>("stop", options, false, parameters =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ExecuteDebuggerCommand("Debug.StopDebugging");
            return new { accepted = true };
        }, cancellationToken);
    }

    private Task<AgentResult<T>> InvokeAsync<T>(string operation, object? options, bool changesProjectState,
        Func<JObject, object> invoke, CancellationToken cancellationToken)
    {
        return InvokeAsync<T>(operation, options, changesProjectState,
            (parameters, token) => Task.FromResult(invoke(parameters)), cancellationToken);
    }

    private Task<AgentResult<T>> InvokeAsync<T>(string operation, object? options, bool changesProjectState,
        Func<JObject, CancellationToken, Task<object>> invoke, CancellationToken cancellationToken)
    {
        return _requests.ExecuteAsync<T>(operation, options, async (parameters, token) =>
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync(token);
            _operations.Expire();
            if (changesProjectState && _operations.Active != null)
            {
                throw new ProxyException("operationInProgress", "Wait for the active operation before changing project state.",
                    new JObject { ["operation"] = _operations.Active.DeepClone() });
            }

            return await invoke(parameters, token);
        }, cancellationToken);
    }

    private async Task<object> ReadDiagnosticsAsync(JObject parameters, bool documentOnly, CancellationToken token)
    {
        if (documentOnly)
        {
            parameters["file"] = ProtocolSupport.String(parameters, "path") ?? throw new ArgumentException("path is required.");
        }

        await _joinableTaskFactory.SwitchToMainThreadAsync(token);
        var projectErrors = new JArray();
        var faults = new JArray();
        var documentState = documentOnly ? _documents.LanguageStatus((string)parameters["file"]!) : null;
        try
        {
            faults = ProjectService.FaultDiagnostics(_projectService.Read());
        }
        catch (Exception exception)
        {
            projectErrors.Add(ProtocolSupport.ReadError("projectFaults", exception));
        }

        var diagnostics = await Task.Run(() => _diagnosticsService.Read(parameters, faults, projectErrors), token).ConfigureAwait(false);
        if (documentState != null)
        {
            diagnostics["document"] = documentState;
        }

        return diagnostics;
    }

    private async Task<object> ReadSnapshotAsync(CancellationToken token)
    {
        await _joinableTaskFactory.SwitchToMainThreadAsync(token);
        var snapshot = new JObject { ["sessionId"] = _operations.SessionId, ["startedUtc"] = DateTime.UtcNow, ["atomic"] = false };
        Capture(snapshot, "status", () => JObject.FromObject(GetStatus()));
        Capture(snapshot, "projects", _projectService.Read);
        Capture(snapshot, "launchCheck", _launchCheckService.Read);
        Capture(snapshot, "documents", _documents.Read);
        foreach (var pane in new[] { "Build", "Debug" })
        {
            Capture(snapshot, "output" + pane, () => _outputService.Read(new JObject { ["pane"] = pane, ["count"] = 4000 }));
        }

        try
        {
            snapshot["diagnostics"] = await Task.Run(() => _diagnosticsService.Read(new JObject { ["count"] = 200 }), token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            snapshot["diagnostics"] = new JObject { ["error"] = ProtocolSupport.ReadError("diagnostics", exception) };
        }

        snapshot["completedUtc"] = DateTime.UtcNow;
        return snapshot;
    }

    private static JObject TerminalSupport() => new()
    {
        ["availability"] = "unsupported",
        ["terminals"] = null,
        ["unavailableReason"] = "ITerminalService.GetTerminalGuidsAsync lists only terminals created by that service client. It does not enumerate existing JSPS terminals; no public history/PID/exit-code reader is exposed.",
        ["scope"] = "existingVisualStudioTerminals",
        ["outputAvailability"] = "unsupported",
        ["suggestedRead"] = "processes and output (Output window only)"
    };

    private static void Capture(JObject snapshot, string name, Func<object> read)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            snapshot[name] = new JObject { ["capturedUtc"] = DateTime.UtcNow, ["value"] = JToken.FromObject(read()) };
        }
        catch (Exception exception)
        {
            snapshot[name] = new JObject { ["error"] = ProtocolSupport.ReadError(name, exception) };
        }
    }

    private object GetStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var debugger = _dte.Debugger;
        var thread = debugger.CurrentThread;
        return new
        {
            mode = debugger.CurrentMode.ToString(),
            currentProcess = debugger.CurrentProcess?.Name,
            currentThread = thread?.Name,
            currentThreadId = thread?.ID,
            currentThreadName = thread?.Name,
            buildState = SafeRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return _dte.Solution.SolutionBuild.BuildState.ToString(); }),
            sessionId = _operations.SessionId,
            stopGeneration = _ideEvents.StopGeneration,
            solution = _dte.Solution?.FullName ?? string.Empty
        };
    }

    private object[] GetStackTrace()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var thread = _dte.Debugger.CurrentThread;
        if (thread is null)
        {
            return [];
        }

        var result = new List<object>();
        var currentFrame = _dte.Debugger.CurrentStackFrame as DteStackFrame2;
        uint? currentDepth = currentFrame?.Depth;
        var threadId = thread.ID;
        var threadName = thread.Name;
        var index = 0;
        foreach (DteStackFrame frame in thread.StackFrames)
        {
            var frame2 = frame as DteStackFrame2;
            var depth = frame2?.Depth ?? (uint)index;
            var module = frame.Module;
            result.Add(new
            {
                index,
                depth,
                isCurrent = currentDepth.HasValue ? depth == currentDepth.Value : index == 0,
                function = frame.FunctionName,
                module,
                moduleName = string.IsNullOrWhiteSpace(module) ? null : Path.GetFileName(module),
                language = frame.Language,
                file = frame2?.FileName,
                line = frame2 is null || frame2.LineNumber == 0 ? (uint?)null : frame2.LineNumber,
                column = (int?)null,
                userCode = frame2?.UserCode,
                threadId,
                threadName
            });
            index++;
        }

        return result.ToArray();
    }

    private object GetFrameExpressions(JObject parameters, bool arguments)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var frameIndex = Math.Max(0, parameters.Value<int?>("frameIndex") ?? 0);
        var maxDepth = Math.Max(0, Math.Min(parameters.Value<int?>("maxDepth") ?? 1, 3));
        var maxItems = Math.Max(1, Math.Min(parameters.Value<int?>("maxItems") ?? 100, 500));
        var frame = GetStackFrame(frameIndex);
        var remaining = maxItems;
        var truncated = false;
        var items = SerializeExpressions(arguments ? frame.Arguments : frame.Locals, maxDepth, ref remaining, ref truncated);
        return new
        {
            frameIndex,
            frame = frame.FunctionName,
            kind = arguments ? "arguments" : "locals",
            items,
            truncated
        };
    }

    private object Evaluate(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var expressionText = RequireString(parameters, "expression");
        var timeoutMs = Math.Max(100, Math.Min(parameters.Value<int?>("timeoutMs") ?? 1000, 10000));
        var maxDepth = Math.Max(0, Math.Min(parameters.Value<int?>("maxDepth") ?? 1, 3));
        var maxItems = Math.Max(1, Math.Min(parameters.Value<int?>("maxItems") ?? 100, 500));
        var expression = _dte.Debugger.GetExpression(expressionText, true, timeoutMs);
        var remaining = maxItems;
        var truncated = false;
        return new
        {
            expression = SerializeExpression(expression, maxDepth, ref remaining, ref truncated),
            truncated
        };
    }

    private DteStackFrame GetStackFrame(int frameIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var thread = _dte.Debugger.CurrentThread
            ?? throw new InvalidOperationException("There is no current debugger thread.");
        return frameIndex >= thread.StackFrames.Count
            ? throw new ArgumentOutOfRangeException(nameof(frameIndex), "Stack frame index is out of range.")
            : thread.StackFrames.Item(frameIndex + 1);
    }

    private static JArray SerializeExpressions(
        Expressions expressions,
        int depth,
        ref int remaining,
        ref bool truncated)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new JArray();
        foreach (DteExpression expression in expressions)
        {
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            result.Add(SerializeExpression(expression, depth, ref remaining, ref truncated));
        }

        return result;
    }

#pragma warning disable VSTHRD010 // Guarded explicitly; analyzer does not follow the SafeRead lambdas.
    private static JObject SerializeExpression(
        DteExpression expression,
        int depth,
        ref int remaining,
        ref bool truncated)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        remaining--;
        var result = new JObject
        {
            ["name"] = SafeRead(() => expression.Name),
            ["type"] = SafeRead(() => expression.Type),
            ["value"] = SafeRead(() => expression.Value),
            ["isValid"] = SafeRead(() => expression.IsValidValue, false)
        };

        try
        {
            var members = expression.DataMembers;
            result["hasChildren"] = members.Count > 0;
            if (members.Count > 0 && depth > 0 && remaining > 0)
            {
                result["children"] = SerializeExpressions(members, depth - 1, ref remaining, ref truncated);
            }
        }
        catch (Exception exception)
        {
            result["hasChildren"] = null;
            result["childrenError"] = exception.Message;
        }

        return result;
    }
#pragma warning restore VSTHRD010

    private object GetBreakpoints()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new JArray();
        var index = 0;
        foreach (DteBreakpoint breakpoint in _dte.Debugger.Breakpoints)
        {
            result.Add(SerializeBreakpoint(breakpoint, index));
            index++;
        }

        return result;
    }

    private object AddBreakpoint(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var function = parameters.Value<string>("function") ?? string.Empty;
        var file = parameters.Value<string>("file") ?? string.Empty;
        var data = parameters.Value<string>("data") ?? string.Empty;
        var address = parameters.Value<string>("address") ?? string.Empty;
        var locationCount = new[] { function, file, data, address }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (locationCount != 1)
        {
            throw new ArgumentException("Specify exactly one breakpoint location: file, function, data or address.");
        }

        var added = _dte.Debugger.Breakpoints.Add(
            function,
            file,
            Math.Max(1, parameters.Value<int?>("line") ?? 1),
            Math.Max(1, parameters.Value<int?>("column") ?? 1),
            parameters.Value<string>("condition") ?? string.Empty,
            ParseConditionType(parameters.Value<string>("conditionType")),
            parameters.Value<string>("language") ?? string.Empty,
            data,
            Math.Max(1, parameters.Value<int?>("dataCount") ?? 1),
            address,
            Math.Max(0, parameters.Value<int?>("hitCount") ?? 0),
            ParseHitCountType(parameters.Value<string>("hitCountType")));

        return TagAndSerializeAddedBreakpoints(added, parameters.Value<bool?>("enabled") ?? true);
    }

    private object RemoveBreakpoint(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var selected = FindBreakpoint(parameters);
        var removed = SerializeBreakpoint(selected.Breakpoint, selected.Index);
        selected.Breakpoint.Delete();
        return new { removed };
    }

    private object SetBreakpointEnabled(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var enabled = parameters.Value<bool?>("enabled")
            ?? throw new ArgumentException("Parameter 'enabled' is required.");
        var selected = FindBreakpoint(parameters);
        selected.Breakpoint.Enabled = enabled;
        return SerializeBreakpoint(selected.Breakpoint, selected.Index);
    }

    private object SetBreakpointCriteria(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var selected = FindBreakpoint(parameters);
        var breakpoint = selected.Breakpoint;
        var function = breakpoint.FunctionName ?? string.Empty;
        var file = breakpoint.File ?? string.Empty;
        if (string.IsNullOrWhiteSpace(function) && string.IsNullOrWhiteSpace(file))
        {
            throw new NotSupportedException("Changing criteria is currently supported for file and function breakpoints only.");
        }

        if (!string.IsNullOrWhiteSpace(file))
        {
            function = string.Empty;
        }

        var condition = parameters.Value<string>("condition") ?? breakpoint.Condition ?? string.Empty;
        var conditionType = parameters["conditionType"] is null
            ? breakpoint.ConditionType
            : ParseConditionType(parameters.Value<string>("conditionType"));
        var hitCount = parameters.Value<int?>("hitCount") ?? breakpoint.HitCountTarget;
        var hitCountType = parameters["hitCountType"] is null
            ? breakpoint.HitCountType
            : ParseHitCountType(parameters.Value<string>("hitCountType"));
        var line = Math.Max(1, breakpoint.FileLine);
        var column = Math.Max(1, breakpoint.FileColumn);
        var language = breakpoint.Language ?? string.Empty;

        var replacements = BreakpointReplacement.Replace(breakpoint, () =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _dte.Debugger.Breakpoints.Add(
                function, file, line, column, condition, conditionType, language,
                string.Empty, 1, string.Empty, Math.Max(0, hitCount), hitCountType);
        }, NewBreakpointTag);

        var result = new JArray();
        foreach (var replacement in replacements)
        {
            result.Add(SerializeBreakpoint(replacement, FindBreakpointIndexByTag(replacement.Tag)));
        }

        return result;
    }

    private object TagAndSerializeAddedBreakpoints(Breakpoints added, bool enabled)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new JArray();
        foreach (DteBreakpoint breakpoint in added)
        {
            breakpoint.Tag = NewBreakpointTag();
            breakpoint.Enabled = enabled;
            result.Add(SerializeBreakpoint(breakpoint, FindBreakpointIndexByTag(breakpoint.Tag)));
        }

        return result;
    }

    private int FindBreakpointIndexByTag(string tag)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var index = 0;
        foreach (DteBreakpoint breakpoint in _dte.Debugger.Breakpoints)
        {
            if (breakpoint.Tag == tag)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private (DteBreakpoint Breakpoint, int Index) FindBreakpoint(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var requestedId = parameters.Value<string>("id");
        var requestedIndex = parameters.Value<int?>("index");
        if (string.IsNullOrWhiteSpace(requestedId) && !requestedIndex.HasValue)
        {
            throw new ArgumentException("Breakpoint selector 'id' or 'index' is required.");
        }

        var index = 0;
        foreach (DteBreakpoint breakpoint in _dte.Debugger.Breakpoints)
        {
            var id = GetBreakpointId(breakpoint.Tag);
            if ((!string.IsNullOrWhiteSpace(requestedId) && requestedId == id)
                || (requestedIndex == index))
            {
                return (breakpoint, index);
            }

            index++;
        }

        throw new KeyNotFoundException("Breakpoint was not found.");
    }

#pragma warning disable VSTHRD010 // Guarded explicitly; analyzer does not follow the SafeRead lambdas.
    private JObject SerializeBreakpoint(DteBreakpoint breakpoint, int index)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new JObject
        {
            ["id"] = GetBreakpointId(SafeRead(() => breakpoint.Tag)),
            ["index"] = index,
            ["name"] = SafeRead(() => breakpoint.Name),
            ["enabled"] = SafeRead(() => breakpoint.Enabled, false),
            ["file"] = SafeRead(() => breakpoint.File),
            ["line"] = SafeRead(() => breakpoint.FileLine, 0),
            ["column"] = SafeRead(() => breakpoint.FileColumn, 0),
            ["function"] = SafeRead(() => breakpoint.FunctionName),
            ["language"] = SafeRead(() => breakpoint.Language),
            ["condition"] = SafeRead(() => breakpoint.Condition),
            ["conditionType"] = SafeRead(() => breakpoint.ConditionType.ToString()),
            ["hitCount"] = SafeRead(() => breakpoint.HitCountTarget, 0),
            ["hitCountType"] = SafeRead(() => breakpoint.HitCountType.ToString()),
            ["locationType"] = SafeRead(() => breakpoint.LocationType.ToString())
        };
        _ideEvents.Breakpoints.Enrich(breakpoint, result);
        result["bindingEvents"] = _operations.BindingEvidence((string?)result["file"], (int?)result["line"] ?? 0);
        result["bindingEventsNote"] = "Historical events matched by requested file and line, not breakpoint identity. Check event time and current DTE bindingState; older errors can precede successful binding.";
        return result;
    }
#pragma warning restore VSTHRD010

    private static dbgBreakpointConditionType ParseConditionType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "" or "whentrue" => dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue,
            "whenchanged" => dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged,
            _ => throw new ArgumentException("conditionType must be 'whenTrue' or 'whenChanged'.")
        };

    private static dbgHitCountType ParseHitCountType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "" or "none" => dbgHitCountType.dbgHitCountTypeNone,
            "equal" => dbgHitCountType.dbgHitCountTypeEqual,
            "greaterorequal" => dbgHitCountType.dbgHitCountTypeGreaterOrEqual,
            "multiple" => dbgHitCountType.dbgHitCountTypeMultiple,
            _ => throw new ArgumentException("hitCountType must be none, equal, greaterOrEqual or multiple.")
        };

    private const string _breakpointTagPrefix = "VsAgentProxy:";
    private static string NewBreakpointTag() => _breakpointTagPrefix + Guid.NewGuid().ToString("N");
    private static string? GetBreakpointId(string? tag) =>
        tag?.StartsWith(_breakpointTagPrefix, StringComparison.Ordinal) == true
            ? tag.Substring(_breakpointTagPrefix.Length)
            : null;

    private static string RequireString(JObject parameters, string name)
    {
        var value = parameters.Value<string>(name);
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Parameter '" + name + "' is required.") : value!;
    }

    private static T SafeRead<T>(Func<T> read, T fallback = default!)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private object GetActiveDocument()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var document = _dte.ActiveDocument;
        if (document is null)
        {
            return new { available = false };
        }

        var selection = document.Selection as TextSelection;
        var selectedText = selection?.Text ?? string.Empty;
        const int selectionLimit = 100000;
        var selectionTruncated = selectedText.Length > selectionLimit;
        if (selectionTruncated)
        {
            selectedText = selectedText.Substring(0, selectionLimit);
        }

        return new
        {
            available = true,
            name = document.Name,
            path = document.FullName,
            line = selection?.ActivePoint.Line,
            column = selection?.ActivePoint.DisplayColumn,
            selectedText,
            selectionTruncated
        };
    }

    private void ExecuteDebuggerCommand(string command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_dte.Commands.Item(command).IsAvailable)
        {
            throw new ProxyException("commandUnavailable", "Visual Studio command is not currently available: " + command,
                new JObject { ["command"] = command, ["suggestedRead"] = command == "Debug.Start" || command == "Debug.StartWithoutDebugging" ? "launchCheck" : "status" });
        }

        _ideEvents.Invalidate();
        _dte.ExecuteCommand(command);
    }

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _requests.Dispose();
        _ideEvents.Dispose();
        _engineEvents.Dispose();
        _diagnosticsService.Dispose();
    }
}
