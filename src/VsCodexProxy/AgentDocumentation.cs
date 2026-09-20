using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

internal static class AgentDocumentation
{
    internal const string Version = "0.6.0";
    private const string ResourceName = "VsCodexProxy.AgentProtocol.md";

    public static JObject GetCapabilities()
    {
        var methods = new JArray
        {
            Method("ping", false, "none", "Proxy version and Visual Studio PID."),
            Method("capabilities", false, "none", "Machine-readable operation catalog."),
            Method("documentation", false, "level?: short|full", "Embedded agent documentation."),
            Method("status", false, "none", "Debugger mode, process, thread and solution."),
            Method("projects", false, "none", "Loaded/unloaded projects, hierarchy fault information and startup selection."),
            Method("diagnostics", false, "severity?: error|warning|message|unknown, origin?: build|intellisense|project-load|unknown, project?: name|guid, file?: fullPath, offset?: int, count?: 1..2000", "Unfiltered Error List sources; unavailable origin remains unknown. Check isStable and readErrors."),
            Method("launchCheck", false, "none", "Start command availability and observed project/build/debugger conditions; does not start the application."),
            Method("snapshot", false, "none", "Non-atomic status, project, document, diagnostic and Output snapshot with capture times."),
            Method("documents", false, "none", "Running documents including dirty state; unknown is distinct from clean."),
            Method("saveDocuments", true, "paths: string[]", "Explicitly save only selected open documents. Check allSaved and individual results."),
            Method("projectReload", true, "path: string", "Unload/reload one project in idle design mode; refuses any dirty or unreadable document."),
            Method("startupProjects", false, "none", "Startup selection observed through DTE."),
            Method("setStartupProjects", true, "paths: string[]", "Validate all projects, set startup selection, verify readback, attempt rollback on failure."),
            Method("configurations", false, "none", "Solution configurations and current selection."),
            Method("projectProperties", false, "project: path|guid, names: string[]", "Selected properties from the active IDE project configuration."),
            Method("launchProfiles", false, "project: path|guid", "IDE debug targets and active selection if supported; disk configurations separately labeled."),
            Method("selectLaunchProfile", true, "project: path|guid, name: string", "Select an IDE target in idle design mode. accepted differs from applied; poll launchProfiles."),
            Method("solutionLaunchProfiles", false, "none", "Live shared/user .slnLaunch profiles, active profile, startup actions, order and debug targets. Uses a version-checked VS 2026 compatibility adapter."),
            Method("selectSolutionLaunchProfile", true, "name: string, scope?: shared|user", "Apply one live solution profile and verify all project actions, order and debug targets; rolls back on failure."),
            Method("build", true, "idempotencyKey?: string", "Build the solution; returns operationId. Observe operationStatus."),
            Method("rebuild", true, "idempotencyKey?: string", "Rebuild the solution with event-confirmed outcome."),
            Method("clean", true, "idempotencyKey?: string", "Clean the solution with event-confirmed outcome."),
            Method("cancelBuild", true, "none", "Request build cancellation when supported by the build manager."),
            Method("operationStatus", false, "id: string", "Tracked result; confirmation timeout is unknown and does not cancel VS."),
            Method("events", false, "afterSequence?: int, limit?: 1..500, sessionId?: string", "Bounded event history, engine errors and binding locations. Check historyLost/sessionChanged."),
            Method("scopes", false, "frameIndex?: int", "Locals/arguments scope references in break mode; valid only for the current context."),
            Method("variables", false, "reference: string, offset?: int, count?: 1..500", "Page children, including containers with invalid scalar values. Stale references fail."),
            Method("stopReason", false, "none", "Last observed stop reason and exception details when exposed by DTE."),
            Method("processes", false, "none", "Processes attached to the debugger."),
            Method("threads", false, "none", "Threads of the current program in break mode."),
            Method("selectContext", true, "threadId: int, frameIndex?: int", "Select thread/frame within the current program; invalidates variable references."),
            Method("documentDiagnostics", false, "path: string, severity?, origin?, offset?, count?", "Error List rows for a document with editor version metadata; freshness remains unknown."),
            Method("languageServiceStatus", false, "path: string", "Editor content type, language service GUID and document version; no inferred Angular server/version."),
            Method("terminals", false, "none", "Reports unsupported for existing JSPS terminals; public service lists only client-owned terminals."),
            Method("terminalOutput", false, "id: string, cursor?: int, count?: int", "Unsupported for existing VS terminals; never substitutes Output window text."),
            Method("stackTrace", false, "none", "Current thread frames with source location, depth and user-code metadata."),
            Method("locals", false, "frameIndex?: int, maxDepth?: 0..3, maxItems?: 1..500", "Locals for a stack frame."),
            Method("arguments", false, "frameIndex?: int, maxDepth?: 0..3, maxItems?: 1..500", "Arguments for a stack frame."),
            Method("evaluate", true, "expression: string, timeoutMs?: 100..10000, maxDepth?: 0..3, maxItems?: 1..500", "Evaluate in current frame; getters or methods may run."),
            Method("activeDocument", false, "none", "Active file, caret and selected text."),
            Method("output", false, "pane?: string OR paneId?: guid, offset?: int, count?: 1..200000, maxChars?: 1..200000", "List pane names and stable GUIDs or read a text range, including empty buffers."),
            Method("breakpoints", false, "none", "List breakpoints and their criteria."),
            Method("breakpointAdd", true, "exactly one of file|function|data|address; line?, column?, condition?, conditionType?, hitCount?, hitCountType?, enabled?", "Create breakpoint(s)."),
            Method("breakpointRemove", true, "id?: string or index?: int", "Delete one breakpoint."),
            Method("breakpointSetEnabled", true, "id?: string or index?: int, enabled: bool", "Enable or disable one breakpoint."),
            Method("breakpointSetCriteria", true, "id?: string or index?: int, condition?, conditionType?, hitCount?, hitCountType?", "Replace criteria for file/function breakpoint."),
            Method("start", true, "none", "Start configured startup project with debugger."),
            Method("startWithoutDebugging", true, "none", "Start configured startup project without debugger."),
            Method("restart", true, "none", "Restart debugging."),
            Method("continue", true, "none", "Continue or start debugging."),
            Method("break", true, "none", "Break all."),
            Method("stop", true, "none", "Stop debugging."),
            Method("stepOver", true, "none", "Step over."),
            Method("stepInto", true, "none", "Step into."),
            Method("stepOut", true, "none", "Step out.")
        };

        return new JObject
        {
            ["version"] = Version,
            ["contractVersion"] = 2,
            ["protocol"] = "json-lines/named-pipe",
            ["pipePattern"] = "VsCodexProxy-{visualStudioPid}",
            ["methodNamesCaseSensitive"] = true,
            ["recommendedFirstCalls"] = new JArray("status", "capabilities"),
            ["methods"] = methods,
            ["warnings"] = new JArray(
                "Select the intended Visual Studio instance by PID when more than one is running.",
                "locals, arguments and step operations require break mode.",
                "evaluate may execute debuggee getters or methods.",
                "output may briefly activate a lazily initialized pane; it restores pane selection, visibility and active window. Check restorationErrors.",
                "accepted=true confirms dispatch, not completion; poll operationStatus or status afterwards.",
                "Variable enumeration uses DTE and can trigger adapter evaluation; no GetExpression calls are made.",
                "currentHits can be null in contract version 2. Even non-null DTE counts may be unsupported by an adapter.",
                "idempotencyKey caches up to 256 requests for one hour in the current proxy session. Replayed operation responses are original dispatch snapshots.")
        };
    }

    public static JObject GetDocumentation(string? requestedLevel)
    {
        var level = string.IsNullOrWhiteSpace(requestedLevel) ? "short" : requestedLevel!.ToLowerInvariant();
        if (level != "short" && level != "full")
            throw new ArgumentException("level must be 'short' or 'full'.");

        return new JObject
        {
            ["version"] = Version,
            ["level"] = level,
            ["format"] = "text/markdown",
            ["text"] = level == "full" ? ReadEmbeddedDocumentation() : ShortDocumentation
        };
    }

    private static JObject Method(string name, bool mutatesState, string parameters, string description) =>
        new()
        {
            ["name"] = name,
            ["mutatesState"] = mutatesState,
            ["parameters"] = parameters,
            ["description"] = description
        };

    private static string ReadEmbeddedDocumentation()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded agent documentation was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private const string ShortDocumentation = @"# VS Codex Proxy quick help

1. Discover instances with the CLI `instances` command and select one with `--pid`.
2. Call `status`, then `capabilities` when operation metadata is needed.
3. Set breakpoints, call `start`, and poll `status` until `dbgBreakMode`.
4. Inspect `stackTrace`, `locals`, `arguments`, `output` and `activeDocument`.
5. Use step/continue operations and poll `status` again after each state change.

Prefer `locals`/`arguments` over `evaluate`; evaluation may execute debuggee code.
Call `documentation` with `{ ""level"": ""full"" }` for the complete embedded guide.";
}
