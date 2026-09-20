using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

try { return await RunAsync(args); }
catch (ArgumentException exception) { Console.Error.WriteLine(exception.Message); return 1; }
catch (JsonException exception) { Console.Error.WriteLine("Invalid JSON: " + exception.Message); return 1; }

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        Console.WriteLine(
            """
            VS Codex Proxy client

              vscodex status
              vscodex capabilities | documentation --level full
              vscodex projects
              vscodex launchCheck
              vscodex documents | startupProjects | configurations
              vscodex saveDocuments --path <file> [--path <file>]
              vscodex projectReload --path <project>
              vscodex setStartupProjects --path <project> [--path <project>]
              vscodex projectProperties --project <path> --name <property> [--name <property>]
              vscodex launchProfiles --project <path>
              vscodex selectLaunchProfile --project <path> --name <profile>
              vscodex solutionLaunchProfiles
              vscodex selectSolutionLaunchProfile --name <profile> [--scope shared|user]
              vscodex build | rebuild | clean | cancelBuild
              vscodex operationStatus --id <operationId>
              vscodex events --afterSequence 0 --limit 100
              vscodex scopes --frameIndex 0
              vscodex variables --reference <id> --offset 0 --count 100
              vscodex stopReason | threads | processes
              vscodex selectContext --threadId <id> --frameIndex 0
              vscodex documentDiagnostics --path <file>
              vscodex waitForState --state break --waitTimeoutMs 120000
              vscodex waitForState --operationId <id> --waitTimeoutMs 120000
              vscodex start --wait true --idempotencyKey <unique-key>
              Any command: --paramsJson <JSON object>, --connectTimeoutMs 5000, --responseTimeoutMs 15000
              Arrays: --pathsJson '["C:/a.csproj","C:/b.csproj"]'
              vscodex diagnostics [--severity error] [--origin build] [--project <name|guid>]
              vscodex diagnostics [--file <fullPath>] [--offset 0] [--count 200]
              vscodex stackTrace
              vscodex locals [--frameIndex 0] [--maxDepth 1] [--maxItems 100]
              vscodex arguments [--frameIndex 0] [--maxDepth 1] [--maxItems 100]
              vscodex evaluate --expression <text> [--timeoutMs 1000]
              vscodex breakpoints
              vscodex breakpointAdd --file <path> --line <n> [--condition <text>]
              vscodex breakpointRemove --id <id> | --index <n>
              vscodex breakpointSetEnabled --id <id> --enabled <true|false>
              vscodex breakpointSetCriteria --id <id> [criteria options]
              vscodex activeDocument
              vscodex output [pane] [maxChars]
              vscodex output <pane> --offset <n> --count <n>
              vscodex output --paneId <guid> --offset <n> --count <n>
              vscodex start | startWithoutDebugging | restart
              vscodex stepOver | stepInto | stepOut | continue | break | stop
              vscodex --pid <visual-studio-pid> <command>
              vscodex instances
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
        Console.Error.WriteLine("Command is required.");
        return 1;
    }

    var command = arguments[0];

    var instances = DiscoverInstances().ToArray();
    if (command == "instances")
    {
        Console.WriteLine(JsonSerializer.Serialize(instances, CreateJsonOptions()));
        return 0;
    }

    var instance = requestedPid.HasValue
        ? instances.FirstOrDefault(item => item.Pid == requestedPid.Value)
        : instances.OrderByDescending(item => item.StartedUtc ?? DateTime.MinValue).FirstOrDefault();
    if (instance is null)
    {
        Console.Error.WriteLine(requestedPid.HasValue
            ? $"Visual Studio instance {requestedPid.Value} with VS Codex Proxy was not found."
            : "No running Visual Studio instance with VS Codex Proxy was found.");
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
        if (command == "waitForState")
            return await WaitAsync(instance, parameters["operationId"]?.GetValue<string>(), parameters["state"]?.GetValue<string>(), connectTimeout, responseTimeout, waitTimeout, pollMs);
        var response = await SendAsync(instance, request, connectTimeout, responseTimeout);
        if (wait && response["ok"]?.GetValue<bool>() == true && response["result"]?["operationId"] is JsonValue operationId)
            return await WaitAsync(instance, operationId.GetValue<string>(), null, connectTimeout, responseTimeout, waitTimeout, pollMs);
        Console.WriteLine(response.ToJsonString(CreateJsonOptions()));
        return response["ok"]?.GetValue<bool>() == true ? 0 : 1;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
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
    await using var pipe = new NamedPipeClientStream(".", instance.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
    using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) { connect.CancelAfter(connectMs); await pipe.ConnectAsync(connect.Token); }
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(responseMs);
    using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
    await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
    await writer.WriteLineAsync(request.ToJsonString().AsMemory(), timeout.Token);
    var response = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Proxy disconnected before sending a response.");
    return JsonNode.Parse(response) as JsonObject ?? throw new IOException("Invalid proxy response.");
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
        if (last["ok"]?.GetValue<bool>() != true) { Console.WriteLine(last.ToJsonString(CreateJsonOptions())); return 1; }
        var actual = last["result"]?[operationId == null ? "mode" : "state"]?.GetValue<string>();
        if ((operationId == null && actual == state) || (operationId != null && actual is "succeeded" or "failed" or "cancelled" or "unknown"))
        { Console.WriteLine(last.ToJsonString(CreateJsonOptions())); return operationId == null || actual == "succeeded" ? 0 : actual == "unknown" ? 4 : 1; }
        await Task.Delay(Math.Min(pollMs, Math.Max(1, waitMs - (int)elapsed.ElapsedMilliseconds)));
    }
    Console.WriteLine(new JsonObject { ["ok"] = false, ["errorCode"] = "waitTimedOut", ["operationCancelled"] = false, ["lastResponse"] = last }.ToJsonString(CreateJsonOptions()));
    return 4;
}

static IEnumerable<PipeInstance> DiscoverInstances()
{
    const string prefix = "VsCodexProxy-";
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

internal sealed record PipeInstance(int Pid, string Pipe, DateTime? StartedUtc);
