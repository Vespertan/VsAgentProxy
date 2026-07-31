using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

internal static class AgentDocumentation
{
    private const string Version = "0.4.0";
    private const string ResourceName = "VsCodexProxy.AgentProtocol.md";

    public static JObject GetCapabilities()
    {
        var methods = new JArray
        {
            Method("ping", false, "none", "Proxy version and Visual Studio PID."),
            Method("capabilities", false, "none", "Machine-readable operation catalog."),
            Method("documentation", false, "level?: short|full", "Embedded agent documentation."),
            Method("status", false, "none", "Debugger mode, process, thread and solution."),
            Method("stackTrace", false, "none", "Current thread frames with source location, depth and user-code metadata."),
            Method("locals", false, "frameIndex?: int, maxDepth?: 0..3, maxItems?: 1..500", "Locals for a stack frame."),
            Method("arguments", false, "frameIndex?: int, maxDepth?: 0..3, maxItems?: 1..500", "Arguments for a stack frame."),
            Method("evaluate", true, "expression: string, timeoutMs?: 100..10000, maxDepth?: 0..3, maxItems?: 1..500", "Evaluate in current frame; getters or methods may run."),
            Method("activeDocument", false, "none", "Active file, caret and selected text."),
            Method("output", false, "pane?: string, offset?: int, count?: 1..200000, maxChars?: 1..200000", "List panes or read a text range."),
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
            ["protocol"] = "json-lines/named-pipe",
            ["pipePattern"] = "VsCodexProxy-{visualStudioPid}",
            ["methodNamesCaseSensitive"] = true,
            ["recommendedFirstCalls"] = new JArray("status", "capabilities"),
            ["methods"] = methods,
            ["warnings"] = new JArray(
                "Select the intended Visual Studio instance by PID when more than one is running.",
                "locals, arguments and step operations require break mode.",
                "evaluate may execute debuggee getters or methods.",
                "accepted=true confirms dispatch, not completion; poll status afterwards.")
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
