using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableManager;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Vespertan.VisualStudio.AgentProxy.Services;

internal sealed class DiagnosticSink : ITableDataSink, IDisposable
{
    private readonly object _gate = new();
    private readonly List<ITableEntry> _entries = [];
    private readonly List<ITableEntriesSnapshot> _snapshots = [];
    private readonly List<ITableEntriesSnapshotFactory> _factories = [];
    private readonly List<ITableEntriesSnapshot> _retired = [];
    private readonly List<ITableEntriesSnapshotFactory> _retiredFactories = [];
    private bool _stable;
    private bool _disposed;

    public bool IsStable
    {
        get
        {
            lock (_gate)
            {
                return _stable;
            }
        }
        set
        {
            lock (_gate)
            {
                _stable = value;
            }
        }
    }

    public List<JObject> Read()
    {
        // Source callbacks only update these collections and never call back into
        // the service. Retired snapshots are released after the read finishes.
        ITableEntry[] currentEntries;
        ITableEntriesSnapshot[] currentSnapshots;
        ITableEntriesSnapshotFactory[] currentFactories;
        lock (_gate)
        {
            currentEntries = _entries.ToArray();
            currentSnapshots = _snapshots.ToArray();
            currentFactories = _factories.ToArray();
        }

        var result = new List<JObject>();
        foreach (var entry in currentEntries)
        {
            result.Add(Serialize(key => entry.TryGetValue(key, out var value) ? value : null));
        }

        foreach (var snapshot in currentSnapshots)
        {
            ReadSnapshot(snapshot, result);
        }

        foreach (var factory in currentFactories)
        {
            using var snapshot = factory.GetCurrentSnapshot();
            if (snapshot != null)
            {
                ReadSnapshot(snapshot, result);
            }
        }

        lock (_gate)
        {
            foreach (var snapshot in _retired)
            {
                snapshot.Dispose();
            }

            _retired.Clear();
            foreach (var factory in _retiredFactories)
            {
                factory.Dispose();
            }

            _retiredFactories.Clear();
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
        finally
        {
            snapshot.StopCaching();
        }
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
            ["rawOrigin"] = source?.ToString(),
            ["buildTool"] = get(StandardTableKeyNames.BuildTool)?.ToString(),
            ["project"] = get(StandardTableKeyNames.ProjectName)?.ToString(),
            ["projectId"] = get(StandardTableKeyNames.ProjectGuid)?.ToString(),
            ["file"] = get(StandardTableKeyNames.DocumentName)?.ToString(),
            ["line"] = Position(get(StandardTableKeyNames.Line)),
            ["column"] = Position(get(StandardTableKeyNames.Column)),
            ["code"] = get(StandardTableKeyNames.ErrorCode)?.ToString(),
            ["message"] = get(StandardTableKeyNames.Text)?.ToString()
        };
    }

    private static JToken Position(object? value) => value is int number && number >= 0 && number < int.MaxValue ? new JValue(number + 1) : JValue.CreateNull();

    public void AddEntries(IReadOnlyList<ITableEntry> values, bool removeAllEntries = false)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (removeAllEntries)
            {
                _entries.Clear();
            }

            _entries.AddRange(values);
        }
    }

    public void RemoveEntries(IReadOnlyList<ITableEntry> values)
    {
        lock (_gate)
        {
            foreach (var value in values)
            {
                _entries.Remove(value);
            }
        }
    }

    public void ReplaceEntries(IReadOnlyList<ITableEntry> oldEntries, IReadOnlyList<ITableEntry> newEntries)
    {
        lock (_gate)
        {
            RemoveEntries(oldEntries);
            AddEntries(newEntries);
        }
    }

    public void RemoveAllEntries()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public void AddSnapshot(ITableEntriesSnapshot value, bool removeAllSnapshots = false)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (removeAllSnapshots)
            {
                RemoveAllSnapshots();
            }

            _snapshots.Add(value);
        }
    }

    public void RemoveSnapshot(ITableEntriesSnapshot value)
    {
        lock (_gate)
        {
            if (_snapshots.Remove(value))
            {
                _retired.Add(value);
            }
        }
    }

    public void ReplaceSnapshot(ITableEntriesSnapshot oldSnapshot, ITableEntriesSnapshot newSnapshot)
    {
        lock (_gate)
        {
            RemoveSnapshot(oldSnapshot);
            AddSnapshot(newSnapshot);
        }
    }

    public void RemoveAllSnapshots()
    {
        lock (_gate)
        {
            _retired.AddRange(_snapshots);
            _snapshots.Clear();
        }
    }

    public void AddFactory(ITableEntriesSnapshotFactory value, bool removeAllFactories = false)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (removeAllFactories)
            {
                RemoveAllFactories();
            }

            _factories.Add(value);
        }
    }

    public void RemoveFactory(ITableEntriesSnapshotFactory value)
    {
        lock (_gate)
        {
            if (_factories.Remove(value))
            {
                _retiredFactories.Add(value);
            }
        }
    }

    public void ReplaceFactory(ITableEntriesSnapshotFactory oldFactory, ITableEntriesSnapshotFactory newFactory)
    {
        lock (_gate)
        {
            RemoveFactory(oldFactory);
            AddFactory(newFactory);
        }
    }

    public void FactorySnapshotChanged(ITableEntriesSnapshotFactory factory)
    { /* Read obtains the current snapshot. */
    }

    public void RemoveAllFactories()
    {
        lock (_gate)
        {
            _retiredFactories.AddRange(_factories);
            _factories.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _entries.Clear();
            RemoveAllFactories();
            RemoveAllSnapshots();
            foreach (var snapshot in _retired)
            {
                snapshot.Dispose();
            }

            _retired.Clear();
            foreach (var factory in _retiredFactories)
            {
                factory.Dispose();
            }

            _retiredFactories.Clear();
        }
    }
}
