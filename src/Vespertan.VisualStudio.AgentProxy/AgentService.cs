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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DiagnosticsProcess = System.Diagnostics.Process;
using DteBreakpoint = EnvDTE.Breakpoint;
using DteExpression = EnvDTE.Expression;
using DteStackFrame = EnvDTE.StackFrame;
using DteStackFrame2 = EnvDTE90a.StackFrame2;

namespace VsAgentProxy;

internal sealed class AgentService : IDisposable
{
    private readonly DTE2 _dte;
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly OperationRegistry _operations = new();
    private readonly DocumentService _documents;
    private readonly IdeEvents _ideEvents;
    private readonly LaunchService _launches;
    private readonly SolutionLaunchService _solutionLaunches;
    private readonly DebugInspectionService _inspection;
    private readonly MutationCache _mutations = new();
    private readonly EngineEvents _engineEvents;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
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

    // Preserve sequential dispatch across RPC clients, including mutation deduplication.
    public async Task<string> ExecuteAsync(string requestJson, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _requestGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var request = JObject.Parse(requestJson);
            var parameters = request["params"] as JObject ?? [];
            var key = ProtocolSupport.String(parameters, "idempotencyKey");
            if (key?.Length > 200)
            {
                throw new ArgumentException("idempotencyKey must be at most 200 characters.");
            }

            var response = key == null ? await DispatchAsync(request, linked.Token).ConfigureAwait(false)
                : _mutations.Find(key, request) ?? await DispatchAndRememberAsync(key, request, linked.Token).ConfigureAwait(false);
            return response.ToString(Formatting.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Error(null, exception).ToString(Formatting.None);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<JObject> DispatchAndRememberAsync(string key, JObject request, CancellationToken token)
    {
        var response = await DispatchAsync(request, token).ConfigureAwait(false);
        _mutations.Store(key, request, response);
        return response;
    }

    private async Task<JObject> DispatchAsync(JObject request, CancellationToken cancellationToken)
    {
        var id = request["id"];
        var method = request.Value<string>("method") ?? string.Empty;
        var parameters = request["params"] as JObject ?? [];

        try
        {
            // Table providers are thread-safe and may contain many entries. Do not
            // enumerate their snapshots on Visual Studio's UI thread.
            if (method == "diagnostics" || method == "documentDiagnostics")
            {
                if (method == "documentDiagnostics")
                {
                    parameters["file"] = ProtocolSupport.String(parameters, "path") ?? throw new ArgumentException("path is required.");
                }

                await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var projectErrors = new JArray();
                var faults = new JArray();
                var documentState = method == "documentDiagnostics" ? _documents.LanguageStatus((string)parameters["file"]!) : null;
                try
                {
                    faults = ProjectService.FaultDiagnostics(_projectService.Read());
                }
                catch (Exception exception)
                {
                    projectErrors.Add(ProtocolSupport.ReadError("projectFaults", exception));
                }

                var diagnostics = await Task.Run(() => _diagnosticsService.Read(parameters, faults, projectErrors), cancellationToken).ConfigureAwait(false);
                if (documentState != null)
                {
                    diagnostics["document"] = documentState;
                }

                return Success(id, diagnostics);
            }

            if (method == "snapshot")
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
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
                    snapshot["diagnostics"] = await Task.Run(() => _diagnosticsService.Read(new JObject { ["count"] = 200 }), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    snapshot["diagnostics"] = new JObject { ["error"] = ProtocolSupport.ReadError("diagnostics", exception) };
                }

                snapshot["completedUtc"] = DateTime.UtcNow;
                return Success(id, snapshot);
            }

            await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _operations.Expire();
            if (_operations.Active != null && method is "projectReload" or "saveDocuments" or "setStartupProjects" or "selectLaunchProfile" or "selectSolutionLaunchProfile")
            {
                throw new ProxyException("operationInProgress", "Wait for the active operation before changing project state.", new JObject { ["operation"] = _operations.Active.DeepClone() });
            }

            object result;
            switch (method)
            {
                case "ping":
                    result = new { version = AgentDocumentation.Version, pid = DiagnosticsProcess.GetCurrentProcess().Id, sessionId = _operations.SessionId };
                    break;
                case "capabilities":
                    result = AgentDocumentation.GetCapabilities();
                    break;
                case "documentation":
                    result = AgentDocumentation.GetDocumentation(parameters.Value<string>("level"));
                    break;
                case "status":
                    result = GetStatus();
                    break;
                case "projects":
                    result = _projectService.Read();
                    break;
                case "documents":
                    result = _documents.Read();
                    break;
                case "saveDocuments":
                    result = _documents.Save(parameters);
                    break;
                case "projectReload":
                    result = _projectService.Reload(parameters, _documents);
                    _operations.Emit("projectReload", JObject.FromObject(result));
                    break;
                case "startupProjects":
                    result = _projectService.Startup();
                    break;
                case "setStartupProjects":
                    result = _projectService.SetStartup(parameters);
                    _operations.Emit("startupProjectsChanged", JObject.FromObject(result));
                    break;
                case "projectProperties":
                    result = _projectService.Properties(parameters);
                    break;
                case "configurations":
                    result = _projectService.Configurations();
                    break;
                case "build":
                    result = _ideEvents.Execute(method, "Build.BuildSolution");
                    break;
                case "rebuild":
                    result = _ideEvents.Execute(method, "Build.RebuildSolution");
                    break;
                case "clean":
                    result = _ideEvents.Execute(method, "Build.CleanSolution");
                    break;
                case "cancelBuild":
                    result = _ideEvents.CancelBuild();
                    break;
                case "operationStatus":
                    result = _operations.Status(ProtocolSupport.String(parameters, "id") ?? throw new ArgumentException("id is required."));
                    break;
                case "events":
                    result = _operations.Events(parameters);
                    break;
                case "stopReason":
                    result = _ideEvents.StopReason;
                    break;
                case "launchProfiles":
                    result = _launches.Read(parameters);
                    break;
                case "selectLaunchProfile":
                    result = _launches.Select(parameters);
                    _operations.Emit("launchProfileChanged", JObject.FromObject(result));
                    break;
                case "solutionLaunchProfiles":
                    result = await _solutionLaunches.ReadAsync(cancellationToken);
                    break;
                case "selectSolutionLaunchProfile":
                    result = await _solutionLaunches.SelectAsync(parameters, cancellationToken);
                    _operations.Emit("solutionLaunchProfileChanged", JObject.FromObject(result));
                    break;
                case "languageServiceStatus":
                    result = _documents.LanguageStatus(ProtocolSupport.String(parameters, "path") ?? throw new ArgumentException("path is required."));
                    break;
                case "terminals":
                    result = TerminalSupport();
                    break;
                case "terminalOutput":
                    throw new ProxyException("unsupported", "Public VS terminal APIs do not expose existing terminals' output history.", TerminalSupport());
                case "scopes":
                    result = _inspection.Scopes(parameters);
                    break;
                case "variables":
                    result = _inspection.Variables(parameters);
                    break;
                case "threads":
                    result = _inspection.Threads();
                    break;
                case "processes":
                    result = _inspection.Processes();
                    break;
                case "selectContext":
                    result = _inspection.SelectContext(parameters);
                    break;
                case "launchCheck":
                    result = _launchCheckService.Read();
                    break;
                case "stackTrace":
                    result = GetStackTrace();
                    break;
                case "locals":
                    _inspection.RequireBreak();
                    result = GetFrameExpressions(parameters, arguments: false);
                    break;
                case "arguments":
                    _inspection.RequireBreak();
                    result = GetFrameExpressions(parameters, arguments: true);
                    break;
                case "evaluate":
                    result = Evaluate(parameters);
                    break;
                case "breakpoints":
                    result = GetBreakpoints();
                    break;
                case "breakpointAdd":
                    result = AddBreakpoint(parameters);
                    break;
                case "breakpointRemove":
                    result = RemoveBreakpoint(parameters);
                    break;
                case "breakpointSetEnabled":
                    result = SetBreakpointEnabled(parameters);
                    break;
                case "breakpointSetCriteria":
                    result = SetBreakpointCriteria(parameters);
                    break;
                case "activeDocument":
                    result = GetActiveDocument();
                    break;
                case "output":
                    result = _outputService.Read(parameters);
                    break;
                case "start":
                    result = _ideEvents.Execute(method, "Debug.Start");
                    break;
                case "startWithoutDebugging":
                    result = _ideEvents.Execute(method, "Debug.StartWithoutDebugging");
                    break;
                case "restart":
                    result = _ideEvents.Execute(method, "Debug.Restart");
                    break;
                case "stepOver":
                    _inspection.RequireBreak();
                    ExecuteDebuggerCommand("Debug.StepOver");
                    result = new { accepted = true };
                    break;
                case "stepInto":
                    _inspection.RequireBreak();
                    ExecuteDebuggerCommand("Debug.StepInto");
                    result = new { accepted = true };
                    break;
                case "stepOut":
                    _inspection.RequireBreak();
                    ExecuteDebuggerCommand("Debug.StepOut");
                    result = new { accepted = true };
                    break;
                case "continue":
                    ExecuteDebuggerCommand("Debug.Start");
                    result = new { accepted = true };
                    break;
                case "break":
                    ExecuteDebuggerCommand("Debug.BreakAll");
                    result = new { accepted = true };
                    break;
                case "stop":
                    ExecuteDebuggerCommand("Debug.StopDebugging");
                    result = new { accepted = true };
                    break;
                default:
                    throw new ProxyException("unknownMethod", "Unknown or disallowed method: " + method);
            }

            return Success(id, result);
        }
        catch (Exception exception)
        {
            return Error(id, exception);
        }
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

        var tag = breakpoint.Tag;
        var enabled = breakpoint.Enabled;
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

        breakpoint.Delete();
        var replacements = _dte.Debugger.Breakpoints.Add(
            function, file, line, column, condition, conditionType, language,
            string.Empty, 1, string.Empty, Math.Max(0, hitCount), hitCountType);

        var result = new JArray();
        var preserveOriginalId = !string.IsNullOrWhiteSpace(tag);
        foreach (DteBreakpoint replacement in replacements)
        {
            replacement.Tag = preserveOriginalId ? tag : NewBreakpointTag();
            preserveOriginalId = false;
            replacement.Enabled = enabled;
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

    private static JObject Success(JToken? id, object result) => new()
    { ["id"] = id?.DeepClone(), ["ok"] = true, ["result"] = JToken.FromObject(result) };

    private static JObject Error(JToken? id, Exception exception) => new()
    {
        ["id"] = id?.DeepClone(),
        ["ok"] = false,
        ["error"] = exception.Message,
        ["errorCode"] = exception is ProxyException proxy ? proxy.Code : exception is ArgumentException ? "invalidParameters" : "ideOperationFailed",
        ["hresult"] = "0x" + exception.HResult.ToString("X8"),
        ["details"] = exception is ProxyException detailed ? detailed.Details : []
    };

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _shutdown.Cancel();
        _ideEvents.Dispose();
        _engineEvents.Dispose();
        _diagnosticsService.Dispose();
    }
}
