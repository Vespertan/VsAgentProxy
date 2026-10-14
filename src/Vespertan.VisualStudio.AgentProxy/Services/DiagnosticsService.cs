using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableManager;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Rpc;

namespace Vespertan.VisualStudio.AgentProxy.Services;

// Read on a background thread. The source subscriptions persist between requests,
// allowing asynchronous providers to publish their initial entries after Subscribe.
internal sealed class DiagnosticsService : IDisposable
{
    private readonly ITableManager? _manager;
    private readonly Dictionary<ITableDataSource, Subscription> _subscriptions = [];
    private readonly object _gate = new();
    private bool _disposed;

    public DiagnosticsService(ITableManager? manager) => this._manager = manager;

    public JObject Read(JObject parameters, JArray? projectDiagnostics = null, JArray? projectReadErrors = null)
    {
        var offset = ProtocolSupport.Integer(parameters, "offset", 0, 0, int.MaxValue);
        var count = ProtocolSupport.Integer(parameters, "count", 200, 1, 2000);
        var severity = ProtocolSupport.String(parameters, "severity");
        var origin = ProtocolSupport.String(parameters, "origin");
        var project = ProtocolSupport.String(parameters, "project");
        var file = ProtocolSupport.String(parameters, "file");
        if (severity != null && !new[] { "error", "warning", "message", "unknown" }.Contains(severity))
        {
            throw new ArgumentException("severity must be error, warning, message or unknown.");
        }

        if (origin != null && !new[] { "build", "intellisense", "project-load", "unknown" }.Contains(origin))
        {
            throw new ArgumentException("origin must be build, intellisense, project-load or unknown.");
        }

        if (_manager == null)
        {
            throw new ProxyException("serviceUnavailable", "The Error List table manager is unavailable.");
        }

        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DiagnosticsService));
            }

            var sources = _manager.Sources.ToArray();
            foreach (var removed in _subscriptions.Keys.Except(sources).ToArray())
            {
                _subscriptions[removed].Dispose();
                _subscriptions.Remove(removed);
            }

            var errors = projectReadErrors == null ? [] : (JArray)projectReadErrors.DeepClone();
            foreach (var source in sources)
            {
                if (_subscriptions.ContainsKey(source))
                {
                    continue;
                }

                var sink = new DiagnosticSink();
                try
                {
                    _subscriptions.Add(source, new Subscription(sink, source.Subscribe(sink)));
                }
                catch (Exception exception)
                {
                    sink.Dispose();
                    errors.Add(ProtocolSupport.ReadError("subscribe:" + source.Identifier, exception));
                }
            }

            var all = projectDiagnostics?.OfType<JObject>().Select(row => (JObject)row.DeepClone()).ToList() ?? [];
            var providers = new JArray();
            foreach (var pair in _subscriptions)
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
                catch (Exception exception)
                {
                    errors.Add(ProtocolSupport.ReadError("source:" + source.Identifier, exception));
                }
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
                ["items"] = items,
                ["offset"] = start,
                ["count"] = items.Count,
                ["totalCount"] = filtered.Length,
                ["hasMore"] = start + items.Count < filtered.Length,
                ["isStable"] = _subscriptions.Count > 0 && errors.Count == 0 && _subscriptions.Values.All(s => s.Sink.IsStable),
                ["complete"] = errors.Count == 0,
                ["providers"] = providers,
                ["readErrors"] = errors,
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
        lock (_gate)
        {
            _disposed = true;
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }

    private sealed class Subscription : IDisposable
    {
        public DiagnosticSink Sink { get; }
        private readonly IDisposable _token;

        public Subscription(DiagnosticSink sink, IDisposable token)
        {
            Sink = sink;
            this._token = token;
        }

        public void Dispose()
        {
            _token.Dispose();
            Sink.Dispose();
        }
    }
}
