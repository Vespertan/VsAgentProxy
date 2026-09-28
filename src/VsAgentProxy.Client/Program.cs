using Microsoft.ServiceHub.Framework;
using Vespertan.VsExtensionsHub.Client;
using Vespertan.VsExtensionsHub.Services.Contracts;
using VsAgentProxy.Contracts;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;

try { return await RunAsync(args); }
catch (ArgumentException exception) { await Console.Error.WriteLineAsync(exception.Message); return 1; }
catch (JsonException exception) { await Console.Error.WriteLineAsync("Invalid JSON: " + exception.Message); return 1; }

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 1 && (args[0] is "-v" or "--version" or "version"))
    {
        await Console.Out.WriteLineAsync(ClientVersion());
        return 0;
    }

    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        await Console.Out.WriteLineAsync(
            """
            VS Agent Proxy client

              vsagent status
              vsagent capabilities | documentation --level full
              vsagent projects
              vsagent launchCheck
              vsagent documents | startupProjects | configurations
              vsagent saveDocuments --path <file> [--path <file>]
              vsagent projectReload --path <project>
              vsagent setStartupProjects --path <project> [--path <project>]
              vsagent projectProperties --project <path> --name <property> [--name <property>]
              vsagent launchProfiles --project <path>
              vsagent selectLaunchProfile --project <path> --name <profile>
              vsagent solutionLaunchProfiles
              vsagent selectSolutionLaunchProfile --name <profile> [--scope shared|user]
              vsagent build | rebuild | clean | cancelBuild
              vsagent operationStatus --id <operationId>
              vsagent events --afterSequence 0 --limit 100
              vsagent scopes --frameIndex 0
              vsagent variables --reference <id> --offset 0 --count 100
              vsagent stopReason | threads | processes
              vsagent selectContext --threadId <id> --frameIndex 0
              vsagent documentDiagnostics --path <file>
              vsagent waitForState --state break --waitTimeoutMs 120000
              vsagent waitForState --operationId <id> --waitTimeoutMs 120000
              vsagent start --wait true --idempotencyKey <unique-key>
              Any command: --paramsJson <JSON object>, --connectTimeoutMs 5000, --responseTimeoutMs 15000
              Arrays: --pathsJson '["C:/a.csproj","C:/b.csproj"]'
              vsagent diagnostics [--severity error] [--origin build] [--project <name|guid>]
              vsagent diagnostics [--file <fullPath>] [--offset 0] [--count 200]
              vsagent stackTrace
              vsagent locals [--frameIndex 0] [--maxDepth 1] [--maxItems 100]
              vsagent arguments [--frameIndex 0] [--maxDepth 1] [--maxItems 100]
              vsagent evaluate --expression <text> [--timeoutMs 1000]
              vsagent breakpoints
              vsagent breakpointAdd --file <path> --line <n> [--condition <text>]
              vsagent breakpointRemove --id <id> | --index <n>
              vsagent breakpointSetEnabled --id <id> --enabled <true|false>
              vsagent breakpointSetCriteria --id <id> [criteria options]
              vsagent activeDocument
              vsagent output [pane] [maxChars]
              vsagent output <pane> --offset <n> --count <n>
              vsagent output --paneId <guid> --offset <n> --count <n>
              vsagent start | startWithoutDebugging | restart
              vsagent stepOver | stepInto | stepOut | continue | break | stop
              vsagent --pid <visual-studio-pid> <command>
              vsagent hubStatus | selection | solutionTree
              vsagent watchSelection [--durationMs 60000]
              vsagent instances
              vsagent --version
            """);
        return 0;
    }

    int? requestedPid = null;
    var arguments = args.ToList();
    if (arguments[0] == "--pid" && (arguments.Count < 2 || !int.TryParse(arguments[1], out var checkedPid) || checkedPid <= 0))
        throw new ArgumentException("--pid requires a positive Visual Studio process ID.");
    if (arguments.Count >= 2 && arguments[0] == "--pid" && int.TryParse(arguments[1], out var pid))
    {
        requestedPid = pid;
        arguments.RemoveRange(0, 2);
    }

    if (arguments.Count == 0)
    {
        await Console.Error.WriteLineAsync("Command is required.");
        return 1;
    }

    var command = arguments[0];

    var instances = DiscoverInstances().ToArray();
    if (command == "instances")
    {
        var checkedInstances = await Task.WhenAll(instances.Where(i => !requestedPid.HasValue || i.Pid == requestedPid.Value).Select(ProbeAsync));
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(checkedInstances, CreateJsonOptions()));
        return 0;
    }

    var instance = requestedPid.HasValue
        ? instances.FirstOrDefault(item => item.Pid == requestedPid.Value)
        : instances.OrderByDescending(item => item.StartedUtc ?? DateTime.MinValue).FirstOrDefault();
    if (instance is null)
    {
        await Console.Error.WriteLineAsync(requestedPid.HasValue
            ? $"Visual Studio instance {requestedPid.Value} with Vespertan Extensions Hub was not found."
            : "No running Visual Studio instance with Vespertan Extensions Hub was found.");
        return 2;
    }

    var parameters = new JsonObject();
    if (command == "output")
    {
        if (arguments.Count > 1 && !arguments[1].StartsWith("--", StringComparison.Ordinal))
            parameters["pane"] = arguments[1];
        if (arguments.Count > 2 && int.TryParse(arguments[2], out var maxChars))
            parameters["maxChars"] = maxChars;

        for (var index = 1; index + 1 < arguments.Count; index++)
        {
            if (!int.TryParse(arguments[index + 1], out var value))
                continue;
            if (arguments[index] == "--offset")
                parameters["offset"] = value;
            else if (arguments[index] == "--count")
                parameters["count"] = value;
        }
    }

    AddNamedOptions(parameters, arguments, 1, command);
    var connectTimeout = TakeInteger(parameters, "connectTimeoutMs", 5000, 100, 120000);
    var responseTimeout = TakeInteger(parameters, "responseTimeoutMs", 15000, 100, 300000);
    var waitTimeout = TakeInteger(parameters, "waitTimeoutMs", 120000, 100, 3600000);
    var pollMs = TakeInteger(parameters, "pollMs", 300, 100, 10000);
    var wait = parameters["wait"]?.GetValue<bool>() == true;
    parameters.Remove("wait");

    var request = new JsonObject
    {
        ["id"] = Guid.NewGuid().ToString("N"),
        ["method"] = command,
        ["params"] = parameters
    };

    try
    {
        if (command == "watchSelection")
            return await WatchSelectionAsync(instance, connectTimeout, responseTimeout, TakeInteger(parameters, "durationMs", 60000, 100, 3600000));
        if (command == "waitForState")
            return await WaitAsync(instance, parameters["operationId"]?.GetValue<string>(), parameters["state"]?.GetValue<string>(), connectTimeout, responseTimeout, waitTimeout, pollMs);
        var response = await SendAsync(instance, request, connectTimeout, responseTimeout);
        if (wait && response["ok"]?.GetValue<bool>() == true && response["result"]?["operationId"] is JsonValue operationId)
            return await WaitAsync(instance, operationId.GetValue<string>(), null, connectTimeout, responseTimeout, waitTimeout, pollMs);
        await Console.Out.WriteLineAsync(response.ToJsonString(CreateJsonOptions()));
        return response["ok"]?.GetValue<bool>() == true ? 0 : 1;
    }
    catch (Exception exception)
    {
        await Console.Error.WriteLineAsync(exception.Message);
        return 3;
    }
}

static void AddNamedOptions(JsonObject parameters, IReadOnlyList<string> arguments, int startIndex, string command)
{
    for (var index = startIndex; index < arguments.Count; index++)
    {
        var option = arguments[index];
        if (!option.StartsWith("--", StringComparison.Ordinal))
        {
            if (command != "output") throw new ArgumentException("Unexpected positional argument: " + option);
            continue;
        }
        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value for " + option);
        var name = option.Substring(2);
        var text = arguments[index + 1];
        if (name == "paramsJson")
        {
            if (JsonNode.Parse(text) is not JsonObject values) throw new ArgumentException("paramsJson must be a JSON object.");
            foreach (var value in values) parameters[value.Key] = value.Value?.DeepClone();
        }
        else if (name.EndsWith("Json", StringComparison.Ordinal))
            parameters[name[..^4]] = JsonNode.Parse(text);
        else if ((name == "path" && command is "setStartupProjects" or "saveDocuments") || (name == "name" && command == "projectProperties"))
        {
            var key = name == "path" ? "paths" : "names";
            if (parameters[key] is not JsonArray) parameters[key] = new JsonArray();
            ((JsonArray)parameters[key]!).Add(text);
        }
        else if (name is "path" or "project" or "file" or "name" or "id" or "idempotencyKey" or "expression" or "condition" or "reference" or "pane" or "paneId" or "sessionId" or "operationId")
            parameters[name] = text;
        else if (bool.TryParse(text, out var boolean))
            parameters[name] = boolean;
        else if (int.TryParse(text, out var integer))
            parameters[name] = integer;
        else
            parameters[name] = text;
        index++;
    }
}

static int TakeInteger(JsonObject values, string key, int fallback, int min, int max)
{
    if (!values.TryGetPropertyValue(key, out var value)) return fallback;
    if (value is not JsonValue scalar || !scalar.TryGetValue<int>(out var number) || number < min || number > max)
        throw new ArgumentException($"{key} must be an integer between {min} and {max}.");
    values.Remove(key); return number;
}

static JsonObject Request(string method, JsonObject parameters) => new() { ["id"] = Guid.NewGuid().ToString("N"), ["method"] = method, ["params"] = parameters };

static async Task<JsonObject> SendAsync(PipeInstance instance, JsonObject request, int connectMs, int responseMs, CancellationToken cancellationToken = default)
{
    using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    connect.CancelAfter(connectMs);
    await using var connection = await HubConnection.ConnectAsync(instance.Pid, connect.Token);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(responseMs);
    var command = request["method"]?.GetValue<string>();
    if (command == "hubStatus")
    {
        using var hub = await connection.Broker.GetProxyAsync<IHubService>(HubServices.Hub, cancellationToken: timeout.Token)
            ?? throw new InvalidOperationException("Hub status service is unavailable.");
        return new JsonObject { ["id"] = request["id"]?.DeepClone(), ["ok"] = true,
            ["result"] = JsonSerializer.SerializeToNode(await hub.GetStatusAsync(timeout.Token).WaitAsync(timeout.Token)) };
    }
    if (command is "selection" or "solutionTree")
    {
        using var explorer = await connection.Broker.GetProxyAsync<ISolutionExplorerService>(HubServices.SolutionExplorer, cancellationToken: timeout.Token)
            ?? throw new InvalidOperationException("Hub Solution Explorer service is unavailable.");
        var items = await (command == "selection" ? explorer.GetSelectedAsync(timeout.Token) : explorer.GetTreeAsync(timeout.Token)).WaitAsync(timeout.Token);
        return new JsonObject { ["id"] = request["id"]?.DeepClone(), ["ok"] = true, ["result"] = JsonSerializer.SerializeToNode(items) };
    }
    using var agent = await connection.Broker.GetProxyAsync<IAgentService>(AgentServices.Agent, cancellationToken: timeout.Token);
    if (agent == null) return new JsonObject { ["id"] = request["id"]?.DeepClone(), ["ok"] = false,
        ["errorCode"] = "serviceUnavailable", ["error"] = "VsAgent service 1.0 is unavailable. Install/enable VsAgentProxy 0.8+ with Hub 1.5+." };
    var response = await agent.ExecuteAsync(request.ToJsonString(), timeout.Token).WaitAsync(timeout.Token);
    return JsonNode.Parse(response) as JsonObject ?? throw new IOException("Invalid VsAgent response.");
}

static async Task<int> WaitAsync(PipeInstance instance, string? operationId, string? requestedState, int connectMs, int responseMs, int waitMs, int pollMs)
{
    var state = requestedState switch { "break" => "dbgBreakMode", "run" => "dbgRunMode", "design" => "dbgDesignMode", _ => requestedState };
    if (operationId == null && state is not ("dbgBreakMode" or "dbgRunMode" or "dbgDesignMode")) throw new ArgumentException("Specify operationId or state break|run|design.");
    var elapsed = Stopwatch.StartNew();
    using var deadline = new CancellationTokenSource(waitMs);
    JsonObject? last = null;
    while (elapsed.ElapsedMilliseconds < waitMs)
    {
        var remaining = Math.Max(1, waitMs - (int)elapsed.ElapsedMilliseconds);
        try { last = await SendAsync(instance, operationId == null ? Request("status", new()) : Request("operationStatus", new() { ["id"] = operationId }), Math.Min(connectMs, remaining), Math.Min(responseMs, remaining), deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested || elapsed.ElapsedMilliseconds >= waitMs) { break; }
        if (last["ok"]?.GetValue<bool>() != true) { await Console.Out.WriteLineAsync(last.ToJsonString(CreateJsonOptions())); return 1; }
        var actual = last["result"]?[operationId == null ? "mode" : "state"]?.GetValue<string>();
        if ((operationId == null && actual == state) || (operationId != null && actual is "succeeded" or "failed" or "cancelled" or "unknown"))
        { await Console.Out.WriteLineAsync(last.ToJsonString(CreateJsonOptions())); return operationId == null || actual == "succeeded" ? 0 : actual == "unknown" ? 4 : 1; }
        await Task.Delay(Math.Min(pollMs, Math.Max(1, waitMs - (int)elapsed.ElapsedMilliseconds)));
    }
    await Console.Out.WriteLineAsync(new JsonObject { ["ok"] = false, ["errorCode"] = "waitTimedOut", ["operationCancelled"] = false, ["lastResponse"] = last }.ToJsonString(CreateJsonOptions()));
    return 4;
}

static async Task<object> ProbeAsync(PipeInstance instance)
{
    var hubState = "notResponding";
    try
    {
        using var timeout = new CancellationTokenSource(3000);
        await using var connection = await HubConnection.ConnectAsync(instance.Pid, timeout.Token);
        using var hub = await connection.Broker.GetProxyAsync<IHubService>(HubServices.Hub, cancellationToken: timeout.Token)
            ?? throw new InvalidOperationException("Hub status service unavailable.");
        var status = await hub.GetStatusAsync(timeout.Token).WaitAsync(timeout.Token);
        if (status.ProcessId != instance.Pid) throw new IOException("Hub PID does not match discovery.");
        hubState = "ready";
        using var agent = await connection.Broker.GetProxyAsync<IAgentService>(AgentServices.Agent, cancellationToken: timeout.Token);
        if (agent == null) return new { instance.Pid, instance.Pipe, instance.StartedUtc, Hub = hubState, VsAgent = "unavailable", Error = (string?)null };
        var response = JsonNode.Parse(await agent.ExecuteAsync(Request("ping", new()).ToJsonString(), timeout.Token).WaitAsync(timeout.Token));
        return new { instance.Pid, instance.Pipe, instance.StartedUtc, Hub = hubState,
            VsAgent = response?["ok"]?.GetValue<bool>() == true ? "ready" : "error", Error = (string?)null };
    }
    catch (Exception ex)
    {
        return new { instance.Pid, instance.Pipe, instance.StartedUtc, Hub = hubState,
            VsAgent = hubState == "ready" ? "error" : "notChecked", Error = ex.Message };
    }
}

static async Task<int> WatchSelectionAsync(PipeInstance instance, int connectMs, int responseMs, int durationMs)
{
    using var connect = new CancellationTokenSource(connectMs);
    await using var connection = await HubConnection.ConnectAsync(instance.Pid, connect.Token);
    using var duration = new CancellationTokenSource(durationMs);
    ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; duration.Cancel(); };
    Console.CancelKeyPress += cancel;
    try
    {
        using var service = await connection.Broker.GetProxyAsync<ISolutionExplorerService>(HubServices.SolutionExplorer, cancellationToken: duration.Token)
            ?? throw new InvalidOperationException("Hub Solution Explorer service is unavailable.");
        // Coalesce bursts; all output is written by this one consumer.
        using var changed = new SemaphoreSlim(0, 1);
        EventHandler handler = (_, _) => { try { changed.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { } };
        service.SelectionChanged += handler;
        try
        {
            handler(null, EventArgs.Empty);
            while (!duration.IsCancellationRequested)
            {
                var signal = changed.WaitAsync(duration.Token);
                if (await Task.WhenAny(signal, connection.Completion) == connection.Completion)
                    throw new IOException("Hub disconnected while watching selection.");
                await signal;
                using var response = CancellationTokenSource.CreateLinkedTokenSource(duration.Token);
                response.CancelAfter(responseMs);
                var items = await service.GetSelectedAsync(response.Token).WaitAsync(response.Token);
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { Event = "SelectionChanged", Items = items }));
            }
        }
        finally { service.SelectionChanged -= handler; }
    }
    catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
    finally { Console.CancelKeyPress -= cancel; }
    return 0;
}

static IEnumerable<PipeInstance> DiscoverInstances()
{
    const string prefix = HubServices.PipePrefix;
    IEnumerable<string> pipes;
    try
    {
        pipes = Directory.EnumerateFileSystemEntries(@"\\.\pipe\");
    }
    catch
    {
        yield break;
    }

    foreach (var path in pipes)
    {
        var pipe = Path.GetFileName(path);
        if (!pipe.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(pipe[prefix.Length..], out var pid)
            || pid <= 0)
            continue;

        DateTime? startedUtc = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            startedUtc = process.StartTime.ToUniversalTime();
        }
        catch
        {
            // The pipe is still a valid discovery result even when process metadata is unavailable.
        }

        yield return new PipeInstance(pid, pipe, startedUtc);
    }
}

static JsonSerializerOptions CreateJsonOptions() => new() { WriteIndented = true };

static string ClientVersion() => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";

internal sealed record PipeInstance(int Pid, string Pipe, DateTime? StartedUtc);
