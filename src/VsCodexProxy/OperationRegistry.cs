using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

// Accessed on the VS UI thread. Contains no VS objects and is independently testable.
internal sealed class OperationRegistry
{
    private readonly List<JObject> operations = new();
    private readonly Queue<JObject> events = new();
    private long sequence;
    private readonly Func<DateTime> now;
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public JObject? Active { get; private set; }
    public OperationRegistry(Func<DateTime>? now = null) => this.now = now ?? (() => DateTime.UtcNow);

    public JObject Begin(string method)
    {
        Expire();
        if (Active != null) throw new ProxyException("operationInProgress", "An operation is still awaiting confirmation.", new JObject { ["operation"] = Active.DeepClone() });
        var operation = new JObject { ["id"] = Guid.NewGuid().ToString("N"), ["sessionId"] = SessionId,
            ["method"] = method, ["state"] = "queued", ["phase"] = "dispatching", ["startedUtc"] = now(), ["source"] = "proxy",
            ["correlation"] = "exclusiveCommandWindow" };
        operations.Add(operation);
        if (operations.Count > 128) operations.RemoveAt(0);
        Active = operation;
        Emit("operationQueued", new JObject { ["operationId"] = operation["id"] });
        return operation;
    }

    public void Change(string state, string phase, string? evidence = null)
    {
        if (Active == null) return;
        Active["state"] = state; Active["phase"] = phase; Active["updatedUtc"] = now();
        Active["evidence"] = evidence;
        var finished = state == "succeeded" || state == "failed" || state == "cancelled" || state == "unknown";
        if (finished) Active["completedUtc"] = now();
        Emit("operationChanged", (JObject)Active.DeepClone());
        if (finished) Active = null;
    }

    public void Emit(string kind, JObject? data = null)
    {
        events.Enqueue(new JObject { ["sequence"] = ++sequence, ["sessionId"] = SessionId, ["utc"] = now(), ["kind"] = kind, ["data"] = data ?? new JObject() });
        while (events.Count > 512) events.Dequeue();
    }

    public JObject Status(string id)
    {
        Expire();
        return (JObject)(operations.FirstOrDefault(x => (string?)x["id"] == id)
            ?? throw new ProxyException("operationNotFound", "Operation is unknown or expired; IDs belong to one proxy session.")).DeepClone();
    }

    public JObject Events(JObject parameters)
    {
        Expire();
        var after = ProtocolSupport.Integer(parameters, "afterSequence", 0, 0, int.MaxValue);
        var limit = ProtocolSupport.Integer(parameters, "limit", 100, 1, 500);
        var session = ProtocolSupport.String(parameters, "sessionId");
        var reset = session != null && session != SessionId;
        var first = events.Count == 0 ? sequence + 1 : (long)events.Peek()["sequence"]!;
        var selected = events.Where(x => reset || (long)x["sequence"]! > after).Take(limit).ToArray();
        return new JObject { ["sessionId"] = SessionId, ["events"] = new JArray(selected.Select(x => x.DeepClone())),
            ["historyLost"] = reset || after < first - 1 || after > sequence, ["sessionChanged"] = reset,
            ["firstAvailableSequence"] = first, ["lastSequence"] = sequence,
            ["nextSequence"] = selected.Length == 0 ? sequence : (long)selected.Last()["sequence"]! };
    }

    public void Expire()
    {
        if (Active != null && now() - (DateTime)Active["startedUtc"]! > TimeSpan.FromMinutes(2))
            Change("unknown", "confirmationTimedOut", "No terminal event within two minutes; IDE operation was not cancelled.");
        operations.RemoveAll(x => x != Active && now() - (DateTime)x["startedUtc"]! > TimeSpan.FromHours(1));
    }

    public JArray BindingEvidence(string? file, int line) => new JArray(events.Where(x =>
        ((string?)x["kind"] == "breakpointBound" || (string?)x["kind"] == "breakpointBindingError" || (string?)x["kind"] == "breakpointUnbound")
        && string.Equals((string?)x["data"]?["file"], file, StringComparison.OrdinalIgnoreCase)
        && (int?)x["data"]?["line"] == line).Reverse().Take(10).Reverse().Select(x => x.DeepClone()));
}
