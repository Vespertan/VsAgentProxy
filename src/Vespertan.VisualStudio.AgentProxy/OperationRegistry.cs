using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

// Accessed on the VS UI thread. Contains no VS objects and is independently testable.
internal sealed class OperationRegistry
{
    private readonly List<JObject> _operations = [];
    private readonly Queue<JObject> _events = new();
    private long _sequence;
    private readonly Func<DateTime> _now;
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public JObject? Active { get; private set; }
    public OperationRegistry(Func<DateTime>? now = null) => this._now = now ?? (() => DateTime.UtcNow);

    public JObject Begin(string method)
    {
        Expire();
        if (Active != null)
        {
            throw new ProxyException("operationInProgress", "An operation is still awaiting confirmation.", new JObject { ["operation"] = Active.DeepClone() });
        }

        var operation = new JObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["sessionId"] = SessionId,
            ["method"] = method,
            ["state"] = "queued",
            ["phase"] = "dispatching",
            ["startedUtc"] = _now(),
            ["source"] = "proxy",
            ["correlation"] = "exclusiveCommandWindow"
        };
        _operations.Add(operation);
        if (_operations.Count > 128)
        {
            _operations.RemoveAt(0);
        }

        Active = operation;
        Emit("operationQueued", new JObject { ["operationId"] = operation["id"] });
        return operation;
    }

    public void Change(string state, string phase, string? evidence = null)
    {
        if (Active == null)
        {
            return;
        }

        Active["state"] = state;
        Active["phase"] = phase;
        Active["updatedUtc"] = _now();
        Active["evidence"] = evidence;
        var finished = state == "succeeded" || state == "failed" || state == "cancelled" || state == "unknown";
        if (finished)
        {
            Active["completedUtc"] = _now();
        }

        Emit("operationChanged", (JObject)Active.DeepClone());
        if (finished)
        {
            Active = null;
        }
    }

    public void Emit(string kind, JObject? data = null)
    {
        _events.Enqueue(new JObject { ["sequence"] = ++_sequence, ["sessionId"] = SessionId, ["utc"] = _now(), ["kind"] = kind, ["data"] = data ?? [] });
        while (_events.Count > 512)
        {
            _events.Dequeue();
        }
    }

    public JObject Status(string id)
    {
        Expire();
        return (JObject)(_operations.FirstOrDefault(x => (string?)x["id"] == id)
            ?? throw new ProxyException("operationNotFound", "Operation is unknown or expired; IDs belong to one proxy session.")).DeepClone();
    }

    public JObject Events(JObject parameters)
    {
        Expire();
        var after = ProtocolSupport.Integer(parameters, "afterSequence", 0, 0, int.MaxValue);
        var limit = ProtocolSupport.Integer(parameters, "limit", 100, 1, 500);
        var session = ProtocolSupport.String(parameters, "sessionId");
        var reset = session != null && session != SessionId;
        var first = _events.Count == 0 ? _sequence + 1 : (long)_events.Peek()["sequence"]!;
        var selected = _events.Where(x => reset || (long)x["sequence"]! > after).Take(limit).ToArray();
        return new JObject
        {
            ["sessionId"] = SessionId,
            ["events"] = new JArray(selected.Select(x => x.DeepClone())),
            ["historyLost"] = reset || after < first - 1 || after > _sequence,
            ["sessionChanged"] = reset,
            ["firstAvailableSequence"] = first,
            ["lastSequence"] = _sequence,
            ["nextSequence"] = selected.Length == 0 ? _sequence : (long)selected.Last()["sequence"]!
        };
    }

    public void Expire()
    {
        if (Active != null && _now() - (DateTime)Active["startedUtc"]! > TimeSpan.FromMinutes(2))
        {
            Change("unknown", "confirmationTimedOut", "No terminal event within two minutes; IDE operation was not cancelled.");
        }

        _operations.RemoveAll(x => x != Active && _now() - (DateTime)x["startedUtc"]! > TimeSpan.FromHours(1));
    }

    public JArray BindingEvidence(string? file, int line) => new(_events.Where(x =>
        ((string?)x["kind"] == "breakpointBound" || (string?)x["kind"] == "breakpointBindingError" || (string?)x["kind"] == "breakpointUnbound")
        && string.Equals((string?)x["data"]?["file"], file, StringComparison.OrdinalIgnoreCase)
        && (int?)x["data"]?["line"] == line).Reverse().Take(10).Reverse().Select(x => x.DeepClone()));
}
