using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
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

namespace VsCodexProxy;

internal sealed class ProxyServer : IDisposable
{
    private readonly DTE2 dte;
    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly string solutionPath;
    private readonly string pipeName;
    private readonly string descriptorPath;
    private Task? listener;

    public ProxyServer(DTE2 dte, JoinableTaskFactory joinableTaskFactory, string solutionPath)
    {
        this.dte = dte;
        this.joinableTaskFactory = joinableTaskFactory;
        this.solutionPath = solutionPath;
        pipeName = "VsCodexProxy-" + DiagnosticsProcess.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VsCodexProxy",
            "instances");
        Directory.CreateDirectory(directory);
        descriptorPath = Path.Combine(directory, DiagnosticsProcess.GetCurrentProcess().Id + ".json");
    }

    public void Start(CancellationToken cancellationToken)
    {
        WriteDescriptor();
        listener = Task.Run(() => ListenAsync(cancellationToken), cancellationToken);
    }

    private void WriteDescriptor()
    {
        var descriptor = new
        {
            pid = DiagnosticsProcess.GetCurrentProcess().Id,
            pipe = pipeName,
            solution = solutionPath,
            startedUtc = DateTime.UtcNow
        };
        File.WriteAllText(descriptorPath, JsonConvert.SerializeObject(descriptor, Formatting.Indented), Encoding.UTF8);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipe = CreatePipe();

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await ServeClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A client may disconnect halfway through a request; accept the next one.
            }
            catch (Exception exception)
            {
                ActivityLog.TryLogError(nameof(VsCodexProxy), exception.ToString());
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private async Task ServeClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested && stream.CanRead)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
                return;

            JObject response;
            try
            {
                var request = JObject.Parse(line);
                response = await DispatchAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                response = Error(null, exception.Message);
            }

            await writer.WriteLineAsync(response.ToString(Formatting.None)).ConfigureAwait(false);
        }
    }

    private async Task<JObject> DispatchAsync(JObject request, CancellationToken cancellationToken)
    {
        var id = request["id"];
        var method = request.Value<string>("method") ?? string.Empty;
        var parameters = request["params"] as JObject ?? new JObject();

        await joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        try
        {
            object result;
            switch (method)
            {
                case "ping":
                    result = new { version = "0.4.0", pid = DiagnosticsProcess.GetCurrentProcess().Id };
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
                case "stackTrace":
                    result = GetStackTrace();
                    break;
                case "locals":
                    result = GetFrameExpressions(parameters, arguments: false);
                    break;
                case "arguments":
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
                    var count = parameters.Value<int?>("count")
                        ?? parameters.Value<int?>("maxChars")
                        ?? 20000;
                    result = GetOutput(
                        parameters.Value<string>("pane"),
                        parameters.Value<int?>("offset"),
                        Math.Max(1, Math.Min(count, 200000)));
                    break;
                case "start":
                    ExecuteDebuggerCommand("Debug.Start");
                    result = new { accepted = true, mode = "debug" };
                    break;
                case "startWithoutDebugging":
                    ExecuteDebuggerCommand("Debug.StartWithoutDebugging");
                    result = new { accepted = true, mode = "withoutDebugging" };
                    break;
                case "restart":
                    ExecuteDebuggerCommand("Debug.Restart");
                    result = new { accepted = true };
                    break;
                case "stepOver":
                    ExecuteDebuggerCommand("Debug.StepOver");
                    result = new { accepted = true };
                    break;
                case "stepInto":
                    ExecuteDebuggerCommand("Debug.StepInto");
                    result = new { accepted = true };
                    break;
                case "stepOut":
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
                    return Error(id, "Unknown or disallowed method: " + method);
            }

            return new JObject
            {
                ["id"] = id?.DeepClone(),
                ["ok"] = true,
                ["result"] = JToken.FromObject(result)
            };
        }
        catch (Exception exception)
        {
            return Error(id, exception.Message);
        }
    }

    private object GetStatus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var debugger = dte.Debugger;
        var thread = debugger.CurrentThread;
        return new
        {
            mode = debugger.CurrentMode.ToString(),
            currentProcess = debugger.CurrentProcess?.Name,
            currentThread = thread?.Name,
            currentThreadId = thread?.ID,
            currentThreadName = thread?.Name,
            solution = dte.Solution?.FullName ?? string.Empty
        };
    }

    private object[] GetStackTrace()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var thread = dte.Debugger.CurrentThread;
        if (thread is null)
            return Array.Empty<object>();

        var result = new List<object>();
        var currentFrame = dte.Debugger.CurrentStackFrame as DteStackFrame2;
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
        var expression = dte.Debugger.GetExpression(expressionText, true, timeoutMs);
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
        var thread = dte.Debugger.CurrentThread
            ?? throw new InvalidOperationException("There is no current debugger thread.");
        if (frameIndex >= thread.StackFrames.Count)
            throw new ArgumentOutOfRangeException("frameIndex", "Stack frame index is out of range.");
        return thread.StackFrames.Item(frameIndex + 1);
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

        if (depth <= 0 || remaining <= 0)
            return result;

        try
        {
            var members = expression.DataMembers;
            if (members.Count > 0)
                result["children"] = SerializeExpressions(members, depth - 1, ref remaining, ref truncated);
        }
        catch (Exception exception)
        {
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
        foreach (DteBreakpoint breakpoint in dte.Debugger.Breakpoints)
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
            throw new ArgumentException("Specify exactly one breakpoint location: file, function, data or address.");

        var added = dte.Debugger.Breakpoints.Add(
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
            throw new NotSupportedException("Changing criteria is currently supported for file and function breakpoints only.");
        if (!string.IsNullOrWhiteSpace(file))
            function = string.Empty;

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
        var replacements = dte.Debugger.Breakpoints.Add(
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
        foreach (DteBreakpoint breakpoint in dte.Debugger.Breakpoints)
        {
            if (breakpoint.Tag == tag)
                return index;
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
            throw new ArgumentException("Breakpoint selector 'id' or 'index' is required.");

        var index = 0;
        foreach (DteBreakpoint breakpoint in dte.Debugger.Breakpoints)
        {
            var id = GetBreakpointId(breakpoint.Tag);
            if ((!string.IsNullOrWhiteSpace(requestedId) && requestedId == id)
                || (requestedIndex.HasValue && requestedIndex.Value == index))
                return (breakpoint, index);
            index++;
        }
        throw new KeyNotFoundException("Breakpoint was not found.");
    }

#pragma warning disable VSTHRD010 // Guarded explicitly; analyzer does not follow the SafeRead lambdas.
    private static JObject SerializeBreakpoint(DteBreakpoint breakpoint, int index)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return new JObject
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
            ["currentHits"] = SafeRead(() => breakpoint.CurrentHits, 0),
            ["hitCount"] = SafeRead(() => breakpoint.HitCountTarget, 0),
            ["hitCountType"] = SafeRead(() => breakpoint.HitCountType.ToString()),
            ["locationType"] = SafeRead(() => breakpoint.LocationType.ToString())
        };
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

    private const string BreakpointTagPrefix = "VsCodexProxy:";
    private static string NewBreakpointTag() => BreakpointTagPrefix + Guid.NewGuid().ToString("N");
    private static string? GetBreakpointId(string? tag) =>
        tag?.StartsWith(BreakpointTagPrefix, StringComparison.Ordinal) == true
            ? tag.Substring(BreakpointTagPrefix.Length)
            : null;

    private static string RequireString(JObject parameters, string name)
    {
        var value = parameters.Value<string>(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Parameter '" + name + "' is required.");
        return value!;
    }

    private static T SafeRead<T>(Func<T> read, T fallback = default!)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private object GetActiveDocument()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var document = dte.ActiveDocument;
        if (document is null)
            return new { available = false };

        var selection = document.Selection as TextSelection;
        var selectedText = selection?.Text ?? string.Empty;
        const int selectionLimit = 100000;
        var selectionTruncated = selectedText.Length > selectionLimit;
        if (selectionTruncated)
            selectedText = selectedText.Substring(0, selectionLimit);

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

    private object GetOutput(string? requestedPane, int? requestedOffset, int count)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var output = dte.ToolWindows.OutputWindow;
        var panes = new List<OutputWindowPane>();
        foreach (OutputWindowPane pane in output.OutputWindowPanes)
            panes.Add(pane);

        if (string.IsNullOrWhiteSpace(requestedPane))
        {
            var names = new List<string>();
            foreach (var pane in panes)
                names.Add(pane.Name);
            return new { panes = names.ToArray() };
        }

        OutputWindowPane? selected = null;
        foreach (var pane in panes)
        {
            if (string.Equals(pane.Name, requestedPane, StringComparison.OrdinalIgnoreCase))
            {
                selected = pane;
                break;
            }
        }
        if (selected is null)
            throw new InvalidOperationException("Output pane not found: " + requestedPane);

        if (requestedOffset < 0)
            throw new ArgumentOutOfRangeException("offset", "Offset cannot be negative.");

        var document = selected.TextDocument;
        var fullText = document.StartPoint.CreateEditPoint().GetText(document.EndPoint);
        var offset = requestedOffset.HasValue
            ? Math.Min(requestedOffset.Value, fullText.Length)
            : Math.Max(0, fullText.Length - count);
        var actualCount = Math.Min(count, fullText.Length - offset);
        var text = fullText.Substring(offset, actualCount);

        return new
        {
            pane = selected.Name,
            text,
            offset,
            count = actualCount,
            totalChars = fullText.Length,
            hasMoreBefore = offset > 0,
            hasMoreAfter = offset + actualCount < fullText.Length
        };
    }

    private void ExecuteDebuggerCommand(string command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!dte.Commands.Item(command).IsAvailable)
            throw new InvalidOperationException("Visual Studio command is not currently available: " + command);
        dte.ExecuteCommand(command);
    }

    private static JObject Error(JToken? id, string message) =>
        new()
        {
            ["id"] = id?.DeepClone(),
            ["ok"] = false,
            ["error"] = message
        };

    public void Dispose()
    {
        try
        {
            if (File.Exists(descriptorPath))
                File.Delete(descriptorPath);
        }
        catch (IOException)
        {
            // A stale descriptor is harmless; the client verifies the PID.
        }
    }
}
