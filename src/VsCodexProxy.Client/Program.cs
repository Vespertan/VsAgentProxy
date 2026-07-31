using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        Console.WriteLine(
            """
            VS Codex Proxy client

              vscodex status
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
              vscodex start | startWithoutDebugging | restart
              vscodex stepOver | stepInto | stepOut | continue | break | stop
              vscodex --pid <visual-studio-pid> <command>
              vscodex instances
            """);
        return 0;
    }

    int? requestedPid = null;
    var arguments = args.ToList();
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
        : instances.OrderByDescending(item => item.StartedUtc).FirstOrDefault();
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

    AddNamedOptions(parameters, arguments, 1);

    var request = new JsonObject
    {
        ["id"] = Guid.NewGuid().ToString("N"),
        ["method"] = command,
        ["params"] = parameters
    };

    try
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            instance.Pipe,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await writer.WriteLineAsync(request.ToJsonString());
        var response = await reader.ReadLineAsync(timeout.Token);
        Console.WriteLine(Pretty(response ?? "{}"));
        return response?.Contains("\"ok\":true", StringComparison.Ordinal) == true ? 0 : 1;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 3;
    }
}

static void AddNamedOptions(JsonObject parameters, IReadOnlyList<string> arguments, int startIndex)
{
    for (var index = startIndex; index + 1 < arguments.Count; index++)
    {
        var option = arguments[index];
        if (!option.StartsWith("--", StringComparison.Ordinal))
            continue;
        var name = option.Substring(2);
        var text = arguments[index + 1];
        if (bool.TryParse(text, out var boolean))
            parameters[name] = boolean;
        else if (int.TryParse(text, out var integer))
            parameters[name] = integer;
        else
            parameters[name] = text;
        index++;
    }
}

static IEnumerable<InstanceDescriptor> DiscoverInstances()
{
    var directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VsCodexProxy",
        "instances");
    if (!Directory.Exists(directory))
        yield break;

    foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
    {
        InstanceDescriptor? descriptor = null;
        try
        {
            descriptor = JsonSerializer.Deserialize<InstanceDescriptor>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (descriptor is null)
                continue;
            Process.GetProcessById(descriptor.Pid);
        }
        catch
        {
            continue;
        }

        yield return descriptor;
    }
}

static string Pretty(string json)
{
    var node = JsonNode.Parse(json);
    return node?.ToJsonString(CreateJsonOptions()) ?? json;
}

static JsonSerializerOptions CreateJsonOptions() => new() { WriteIndented = true };

internal sealed record InstanceDescriptor(int Pid, string Pipe, string Solution, DateTime StartedUtc);
