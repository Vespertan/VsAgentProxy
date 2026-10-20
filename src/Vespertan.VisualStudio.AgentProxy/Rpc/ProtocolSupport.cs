using System;
using Newtonsoft.Json.Linq;

namespace Vespertan.VisualStudio.AgentProxy.Rpc;

internal static class ProtocolSupport
{
    public static (string? Id, int? Index) BreakpointSelector(JObject parameters)
    {
        var id = String(parameters, "id");
        int? index = parameters["index"] == null ? null : Integer(parameters, "index", 0, 0, int.MaxValue);
        if ((id != null) == index.HasValue)
        {
            throw new ArgumentException("Specify exactly one breakpoint selector: 'id' or 'index'.");
        }

        return (id, index);
    }

    public static string AbsolutePath(string path)
    {
        var root = System.IO.Path.GetPathRoot(path);
        return !System.IO.Path.IsPathRooted(path) || string.IsNullOrEmpty(root)
            || root.Length == 1 || root.EndsWith(":", StringComparison.Ordinal)
            ? throw new ArgumentException("An absolute file path is required.")
            : System.IO.Path.GetFullPath(path);
    }

    public static string[] Strings(JObject parameters, string name)
    {
        return parameters[name] is not JArray array || array.Count == 0 || array.Count > 100
            || System.Linq.Enumerable.Any(array, x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)x))
            ? throw new ArgumentException(name + " must be an array of 1..100 non-empty strings.")
            : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(array, x => (string)x!));
    }

    public static int Integer(JObject parameters, string name, int defaultValue, int min, int max)
    {
        var token = parameters[name];
        if (token == null)
        {
            return defaultValue;
        }

        return token.Type != JTokenType.Integer || !long.TryParse(token.ToString(), out var value)
            || value < min || value > max
            ? throw new ArgumentException($"{name} must be an integer between {min} and {max}.")
            : (int)value;
    }

    public static string? String(JObject parameters, string name)
    {
        var token = parameters[name];
        if (token == null)
        {
            return null;
        }

        return token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)token)
            ? throw new ArgumentException(name + " must be a non-empty string.")
            : (string?)token;
    }

    public static (int Offset, int Count) Range(int length, int? offset, int count)
    {
        if (length < 0 || offset < 0 || count < 1)
        {
            throw new ArgumentOutOfRangeException();
        }

        var start = offset.HasValue ? Math.Min(offset.Value, length) : Math.Max(0, length - count);
        return (start, Math.Min(count, length - start));
    }

    public static JObject ReadError(string field, Exception exception) => new()
    {
        ["field"] = field,
        ["message"] = exception.Message,
        ["hresult"] = "0x" + exception.HResult.ToString("X8")
    };
}
