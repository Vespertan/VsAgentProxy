using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class MutationCache
{
    private readonly Dictionary<string, (JObject Request, JObject Response, DateTime Utc)> entries = new();
    public JObject? Find(string key, JObject request)
    {
        Prune();
        if (!entries.TryGetValue(key, out var entry)) return null;
        if (!JToken.DeepEquals(Identity(request), entry.Request)) throw new ProxyException("idempotencyConflict", "The key was already used with a different method or parameters.");
        var response = (JObject)entry.Response.DeepClone(); response["id"] = request["id"]?.DeepClone(); response["replayed"] = true;
        return response;
    }
    public void Store(string key, JObject request, JObject response)
    {
        Prune();
        if (entries.Count >= 256) entries.Remove(entries.OrderBy(x => x.Value.Utc).First().Key);
        entries[key] = (Identity(request), (JObject)response.DeepClone(), DateTime.UtcNow);
    }
    private static JObject Identity(JObject request) => new() { ["method"] = request["method"]?.DeepClone(), ["params"] = request["params"]?.DeepClone() };
    private void Prune()
    {
        foreach (var key in entries.Where(x => DateTime.UtcNow - x.Value.Utc > TimeSpan.FromHours(1)).Select(x => x.Key).ToArray()) entries.Remove(key);
    }
}
