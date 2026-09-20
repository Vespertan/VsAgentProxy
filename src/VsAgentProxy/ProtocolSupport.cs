using System;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class ProxyException : Exception
{
    public string Code { get; }
    public JObject Details { get; }

    public ProxyException(string code, string message, JObject? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Details = details ?? new JObject();
        if (inner != null) HResult = inner.HResult;
    }
}

internal static class ProtocolSupport
{
    public static string AbsolutePath(string path)
    {
        var root = System.IO.Path.GetPathRoot(path);
        if (!System.IO.Path.IsPathRooted(path) || string.IsNullOrEmpty(root)
            || root.Length == 1 || root.EndsWith(":", StringComparison.Ordinal))
            throw new ArgumentException("An absolute file path is required.");
        return System.IO.Path.GetFullPath(path);
    }
    public static string[] Strings(JObject parameters, string name)
    {
        if (!(parameters[name] is JArray array) || array.Count == 0 || array.Count > 100
            || System.Linq.Enumerable.Any(array, x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)x)))
            throw new ArgumentException(name + " must be an array of 1..100 non-empty strings.");
        return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(array, x => (string)x!));
    }

    public static int Integer(JObject parameters, string name, int defaultValue, int min, int max)
    {
        var token = parameters[name];
        if (token == null) return defaultValue;
        if (token.Type != JTokenType.Integer || !long.TryParse(token.ToString(), out var value)
            || value < min || value > max)
            throw new ArgumentException($"{name} must be an integer between {min} and {max}.");
        return (int)value;
    }

    public static string? String(JObject parameters, string name)
    {
        var token = parameters[name];
        if (token == null) return null;
        if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)token))
            throw new ArgumentException(name + " must be a non-empty string.");
        return (string?)token;
    }

    public static (int Offset, int Count) Range(int length, int? offset, int count)
    {
        if (length < 0 || offset < 0 || count < 1) throw new ArgumentOutOfRangeException();
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
