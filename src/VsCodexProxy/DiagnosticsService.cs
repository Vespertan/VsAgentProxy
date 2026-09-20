using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableManager;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

// Read on a background thread. The source subscriptions persist between requests,
// allowing asynchronous providers to publish their initial entries after Subscribe.
internal sealed class DiagnosticsService : IDisposable
{
    private readonly ITableManager? manager;
    private readonly Dictionary<ITableDataSource, Subscription> subscriptions = new();
    private readonly object gate = new();
    private bool disposed;

    public DiagnosticsService(ITableManager? manager) => this.manager = manager;

    public JObject Read(JObject parameters, JArray? projectDiagnostics = null, JArray? projectReadErrors = null)
    {
        var offset = ProtocolSupport.Integer(parameters, "offset", 0, 0, int.MaxValue);
        var count = ProtocolSupport.Integer(parameters, "count", 200, 1, 2000);
        var severity = ProtocolSupport.String(parameters, "severity");
        var origin = ProtocolSupport.String(parameters, "origin");
        var project = ProtocolSupport.String(parameters, "project");
        var file = ProtocolSupport.String(parameters, "file");
        if (severity != null && !new[] { "error", "warning", "message", "unknown" }.Contains(severity))
            throw new ArgumentException("severity must be error, warning, message or unknown.");
        if (origin != null && !new[] { "build", "intellisense", "project-load", "unknown" }.Contains(origin))
            throw new ArgumentException("origin must be build, intellisense, project-load or unknown.");
        if (manager == null) throw new ProxyException("serviceUnavailable", "The Error List table manager is unavailable.");

        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(DiagnosticsService));
            var sources = manager.Sources.ToArray();
            foreach (var removed in subscriptions.Keys.Except(sources).ToArray())
            {
                subscriptions[removed].Dispose();
                subscriptions.Remove(removed);
            }
            var errors = projectReadErrors == null ? new JArray() : (JArray)projectReadErrors.DeepClone();
            foreach (var source in sources)
            {
                if (subscriptions.ContainsKey(source)) continue;
                var sink = new DiagnosticSink();
                try { subscriptions.Add(source, new Subscription(sink, source.Subscribe(sink))); }
                catch (Exception exception) { sink.Dispose(); errors.Add(ProtocolSupport.ReadError("subscribe:" + source.Identifier, exception)); }
            }
            var all = projectDiagnostics?.OfType<JObject>().Select(row => (JObject)row.DeepClone()).ToList() ?? new List<JObject>();
            var providers = new JArray();
            foreach (var pair in subscriptions)
            {
                var source = pair.Key;
                try
                {
                    var rows = pair.Value.Sink.Read();
                    foreach (var row in rows)
                    {
                        row["source"] = source.Identifier;
                        row["sourceType"] = source.SourceTypeIdentifier;
                        row["provider"] = source.DisplayName;
                        all.Add(row);
                    }
                    providers.Add(new JObject { ["id"] = source.Identifier, ["isStable"] = pair.Value.Sink.IsStable, ["count"] = rows.Count });
                }
                catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("source:" + source.Identifier, exception)); }
            }
            var filtered = all.Where(row => (severity == null || (string?)row["severity"] == severity)
                && (origin == null || (string?)row["origin"] == origin)
                && (project == null || EqualsText(row["project"], project) || EqualsText(row["projectId"], project))
                && (file == null || EqualsText(row["file"], file)))
                .OrderBy(row => (string?)row["source"], StringComparer.Ordinal)
                .ThenBy(row => (string?)row["project"], StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => (string?)row["file"], StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => (int?)row["line"])
                .ThenBy(row => (string?)row["code"], StringComparer.Ordinal)
                .ThenBy(row => (string?)row["message"], StringComparer.Ordinal).ToArray();
            var start = Math.Min(offset, filtered.Length);
            var items = new JArray(filtered.Skip(start).Take(count));
            return new JObject
            {
                ["items"] = items, ["offset"] = start, ["count"] = items.Count, ["totalCount"] = filtered.Length,
                ["hasMore"] = start + items.Count < filtered.Length,
                ["isStable"] = subscriptions.Count > 0 && errors.Count == 0 && subscriptions.Values.All(s => s.Sink.IsStable),
                ["complete"] = errors.Count == 0, ["providers"] = providers, ["readErrors"] = errors,
                ["capturedUtc"] = DateTime.UtcNow,
                ["scope"] = "errorListSourcesAndProjectFaults",
                ["limitations"] = new JArray("Provider data may arrive asynchronously. Empty results do not prove absence of errors.",
                    "Origin is unknown when the provider does not identify it. Historical project-load failures may be absent; also inspect projects.")
            };
        }
    }

    private static bool EqualsText(JToken? token, string value) => string.Equals((string?)token, value, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var subscription in subscriptions.Values) subscription.Dispose();
            subscriptions.Clear();
        }
    }

    private sealed class Subscription : IDisposable
    {
        public DiagnosticSink Sink { get; }
        private readonly IDisposable token;
        public Subscription(DiagnosticSink sink, IDisposable token) { Sink = sink; this.token = token; }
        public void Dispose() { token.Dispose(); Sink.Dispose(); }
    }
}

internal sealed class DiagnosticSink : ITableDataSink, IDisposable
{
    private readonly object gate = new();
    private readonly List<ITableEntry> entries = new();
    private readonly List<ITableEntriesSnapshot> snapshots = new();
    private readonly List<ITableEntriesSnapshotFactory> factories = new();
    private readonly List<ITableEntriesSnapshot> retired = new();
    private readonly List<ITableEntriesSnapshotFactory> retiredFactories = new();
    private bool stable;
    private bool disposed;
    public bool IsStable { get { lock (gate) return stable; } set { lock (gate) stable = value; } }

    public List<JObject> Read()
    {
        // Source callbacks only update these collections and never call back into
        // the service. Retired snapshots are released after the read finishes.
        ITableEntry[] currentEntries;
        ITableEntriesSnapshot[] currentSnapshots;
        ITableEntriesSnapshotFactory[] currentFactories;
        lock (gate)
        {
            currentEntries = entries.ToArray(); currentSnapshots = snapshots.ToArray(); currentFactories = factories.ToArray();
        }
        var result = new List<JObject>();
        foreach (var entry in currentEntries) result.Add(Serialize(key => entry.TryGetValue(key, out var value) ? value : null));
        foreach (var snapshot in currentSnapshots) ReadSnapshot(snapshot, result);
        foreach (var factory in currentFactories)
        {
            using var snapshot = factory.GetCurrentSnapshot();
            if (snapshot != null) ReadSnapshot(snapshot, result);
        }
        lock (gate)
        {
            foreach (var snapshot in retired) snapshot.Dispose();
            retired.Clear();
            foreach (var factory in retiredFactories) factory.Dispose();
            retiredFactories.Clear();
        }
        return result;
    }

    private static void ReadSnapshot(ITableEntriesSnapshot snapshot, List<JObject> result)
    {
        snapshot.StartCaching();
        try
        {
            for (var index = 0; index < snapshot.Count; index++)
            {
                var row = index;
                result.Add(Serialize(key => snapshot.TryGetValue(row, key, out var value) ? value : null));
            }
        }
        finally { snapshot.StopCaching(); }
    }

    internal static JObject Serialize(Func<string, object?> get)
    {
        var severity = get(StandardTableKeyNames.ErrorSeverity);
        var source = get(StandardTableKeyNames.ErrorSource);
        return new JObject
        {
            ["severity"] = severity is __VSERRORCATEGORY category ? category switch
            { __VSERRORCATEGORY.EC_ERROR => "error", __VSERRORCATEGORY.EC_WARNING => "warning", __VSERRORCATEGORY.EC_MESSAGE => "message", _ => "unknown" } : "unknown",
            ["origin"] = source is ErrorSource errorSource ? errorSource switch { ErrorSource.Build => "build", ErrorSource.Other => "intellisense", _ => "unknown" } : "unknown",
            ["rawOrigin"] = source?.ToString(), ["buildTool"] = get(StandardTableKeyNames.BuildTool)?.ToString(),
            ["project"] = get(StandardTableKeyNames.ProjectName)?.ToString(), ["projectId"] = get(StandardTableKeyNames.ProjectGuid)?.ToString(),
            ["file"] = get(StandardTableKeyNames.DocumentName)?.ToString(),
            ["line"] = Position(get(StandardTableKeyNames.Line)), ["column"] = Position(get(StandardTableKeyNames.Column)),
            ["code"] = get(StandardTableKeyNames.ErrorCode)?.ToString(), ["message"] = get(StandardTableKeyNames.Text)?.ToString()
        };
    }

    private static JToken Position(object? value) => value is int number && number >= 0 && number < int.MaxValue ? new JValue(number + 1) : JValue.CreateNull();
    public void AddEntries(IReadOnlyList<ITableEntry> values, bool removeAllEntries = false) { lock (gate) { if (disposed) return; if (removeAllEntries) entries.Clear(); entries.AddRange(values); } }
    public void RemoveEntries(IReadOnlyList<ITableEntry> values) { lock (gate) foreach (var value in values) entries.Remove(value); }
    public void ReplaceEntries(IReadOnlyList<ITableEntry> oldEntries, IReadOnlyList<ITableEntry> newEntries) { lock (gate) { RemoveEntries(oldEntries); AddEntries(newEntries); } }
    public void RemoveAllEntries() { lock (gate) entries.Clear(); }
    public void AddSnapshot(ITableEntriesSnapshot value, bool removeAllSnapshots = false) { lock (gate) { if (disposed) return; if (removeAllSnapshots) RemoveAllSnapshots(); snapshots.Add(value); } }
    public void RemoveSnapshot(ITableEntriesSnapshot value) { lock (gate) { if (snapshots.Remove(value)) retired.Add(value); } }
    public void ReplaceSnapshot(ITableEntriesSnapshot oldSnapshot, ITableEntriesSnapshot newSnapshot) { lock (gate) { RemoveSnapshot(oldSnapshot); AddSnapshot(newSnapshot); } }
    public void RemoveAllSnapshots() { lock (gate) { retired.AddRange(snapshots); snapshots.Clear(); } }
    public void AddFactory(ITableEntriesSnapshotFactory value, bool removeAllFactories = false) { lock (gate) { if (disposed) return; if (removeAllFactories) RemoveAllFactories(); factories.Add(value); } }
    public void RemoveFactory(ITableEntriesSnapshotFactory value) { lock (gate) { if (factories.Remove(value)) retiredFactories.Add(value); } }
    public void ReplaceFactory(ITableEntriesSnapshotFactory oldFactory, ITableEntriesSnapshotFactory newFactory) { lock (gate) { RemoveFactory(oldFactory); AddFactory(newFactory); } }
    public void FactorySnapshotChanged(ITableEntriesSnapshotFactory factory) { /* Read obtains the current snapshot. */ }
    public void RemoveAllFactories() { lock (gate) { retiredFactories.AddRange(factories); factories.Clear(); } }
    public void Dispose() { lock (gate) { disposed = true; entries.Clear(); RemoveAllFactories(); RemoveAllSnapshots(); foreach (var snapshot in retired) snapshot.Dispose(); retired.Clear(); foreach (var factory in retiredFactories) factory.Dispose(); retiredFactories.Clear(); } }
}
