using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace VsAgentProxy.Tests;

public sealed class InspectionTests
{
    [Theory]
    [InlineData(0, null, 100, 0, 0)]
    [InlineData(10, null, 3, 7, 3)]
    [InlineData(10, 3, 4, 3, 4)]
    [InlineData(10, 100, 4, 10, 0)]
    [InlineData(10, 9, 100, 9, 1)]
    [InlineData(int.MaxValue, int.MaxValue - 1, 200000, int.MaxValue - 1, 1)]
    public void OutputRangesHandleEmptyTailAndEndOfBuffer(int length, int? offset, int count, int expectedOffset, int expectedCount)
    {
        var actual = ProtocolSupport.Range(length, offset, count);
        Assert.Equal(expectedOffset, actual.Offset);
        Assert.Equal(expectedCount, actual.Count);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("true")]
    [InlineData("2147483648")]
    [InlineData("\"5\"")]
    public void InvalidOffsetsAreRejected(string json)
    {
        Assert.Throws<ArgumentException>(() => ProtocolSupport.Integer(JObject.Parse("{\"offset\":" + json + "}"), "offset", 0, 0, int.MaxValue));
    }

    [Fact]
    public void MissingDiagnosticMetadataIsNotInvented()
    {
        var result = JObject.Parse(DiagnosticSink.Serialize(_ => null).ToString());
        Assert.Equal("unknown", result.Value<string>("severity"));
        Assert.Equal("unknown", result.Value<string>("origin"));
        Assert.Equal(JTokenType.Null, result["line"]!.Type);
        Assert.Equal(JTokenType.Null, result["code"]!.Type);
    }

    [Fact]
    public void DiagnosticCoordinatesAreOneBasedAndOtherUsesTheIdeIntellisenseCategory()
    {
        var values = new Dictionary<string, object>
        {
            [StandardTableKeyNames.Line] = 0, [StandardTableKeyNames.Column] = -1,
            [StandardTableKeyNames.ErrorSource] = ErrorSource.Other,
            [StandardTableKeyNames.ErrorSeverity] = __VSERRORCATEGORY.EC_WARNING
        };
        var result = DiagnosticSink.Serialize(key => values.TryGetValue(key, out var value) ? value : null);
        Assert.Equal(1, result.Value<int>("line"));
        Assert.Equal(JTokenType.Null, result["column"]!.Type);
        Assert.Equal("warning", result.Value<string>("severity"));
        Assert.Equal("intellisense", result.Value<string>("origin"));
    }

    [Fact]
    public void SinkTracksReplaceRemoveAndAsynchronousPublication()
    {
        using var sink = new DiagnosticSink();
        Assert.False(sink.IsStable);
        Assert.Empty(sink.Read());
        var first = new Entry("first");
        var second = new Entry("second");
        sink.AddEntries(new[] { first });
        sink.IsStable = true;
        Assert.Equal("first", Assert.Single(sink.Read()).Value<string>("message"));
        sink.ReplaceEntries(new[] { first }, new[] { second });
        Assert.Equal("second", Assert.Single(sink.Read()).Value<string>("message"));
        sink.RemoveAllEntries();
        Assert.Empty(sink.Read());
    }

    [Fact]
    public void FactoryReadRefreshesVersionAndBalancesSnapshotLifetime()
    {
        using var sink = new DiagnosticSink();
        var factory = new Factory();
        sink.AddFactory(factory);
        Assert.Equal("version 0", Assert.Single(sink.Read()).Value<string>("message"));
        Assert.Equal(1, factory.Last!.CacheStarts);
        Assert.Equal(1, factory.Last.CacheStops);
        Assert.True(factory.Last.Disposed);
        factory.CurrentVersionNumber = 1;
        sink.FactorySnapshotChanged(factory);
        Assert.Equal("version 1", Assert.Single(sink.Read()).Value<string>("message"));
        sink.RemoveFactory(factory);
        Assert.Empty(sink.Read());
    }

    [Fact]
    public void DirectSnapshotsAreReleasedAfterReplacement()
    {
        using var sink = new DiagnosticSink();
        var first = new Snapshot(1);
        var second = new Snapshot(2);
        sink.AddSnapshot(first);
        Assert.Single(sink.Read());
        Assert.False(first.Disposed);
        sink.ReplaceSnapshot(first, second);
        Assert.Equal("version 2", Assert.Single(sink.Read()).Value<string>("message"));
        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        sink.Dispose();
        Assert.True(second.Disposed);
    }

    [Fact]
    public void StartupPathsMatchWithoutConfusingDuplicateProjectNames()
    {
        Assert.True(ProjectService.MatchesStartup(@"Demo\App.esproj", @"Demo\App.esproj", @"C:\Solution\Demo\App.esproj", @"C:\Solution"));
        Assert.True(ProjectService.MatchesStartup(@"c:\solution\Demo\App.esproj", @"Demo\App.esproj", @"C:\Solution\Demo\App.esproj", @"C:\Solution"));
        Assert.False(ProjectService.MatchesStartup(@"Tests\App.esproj", @"Demo\App.esproj", @"C:\Solution\Demo\App.esproj", @"C:\Solution"));
    }

    [Fact]
    public void LaunchCheckDoesNotCallPausedSessionANewLaunchOrGuessNoStartup()
    {
        var state = JObject.Parse("{ 'isOpen':true, 'isFullyLoaded':true, 'startupProjectsAvailable':false, 'startupProjects':[], 'projects':[] }");
        var reasons = LaunchCheckService.Explain(state, "dbgBreakMode", "vsBuildStateDone");
        Assert.Equal("debuggerPaused", Assert.Single(reasons).Value<string>("code"));
        state["startupProjectsAvailable"] = true;
        var missing = LaunchCheckService.Explain(state, "dbgDesignMode", "vsBuildStateDone");
        Assert.Equal("suspected", Assert.Single(missing).Value<string>("confidence"));
    }

    [Fact]
    public void DiagnosticsFilterBeforePagingAndIncludeProjectFaults()
    {
        var source = new Source();
        var manager = new Manager(source);
        using var service = new DiagnosticsService(manager);
        var faultState = JObject.Parse("{ 'projects':[{ 'name':'Broken', 'id':'project-id', 'path':'C:/Broken.esproj', 'loadState':'failed', 'loadError':'SDK missing' }, { 'name':'Unloaded', 'loadState':'unloaded' }] }");
        var faults = ProjectService.FaultDiagnostics(faultState);
        Assert.Single(faults);
        var projectResult = service.Read(JObject.Parse("{ 'origin':'project-load' }"), faults);
        Assert.Equal("SDK missing", Assert.Single((JArray)projectResult["items"]!).Value<string>("message"));
        var buildResult = service.Read(JObject.Parse("{ 'origin':'build', 'offset':1, 'count':1 }"), faults);
        Assert.Equal(2, buildResult.Value<int>("totalCount"));
        Assert.Equal("second", Assert.Single((JArray)buildResult["items"]!).Value<string>("message"));
        Assert.False(buildResult.Value<bool>("hasMore"));
        Assert.True(buildResult.Value<bool>("isStable"));
        Assert.Equal(1, source.Subscriptions);
        manager.RemoveSource(source);
        Assert.Empty((JArray)service.Read(new JObject())["items"]!);
        Assert.True(source.Unsubscribed);
    }

    [Fact]
    public void FailedProviderIsReportedAlongsideHealthyResults()
    {
        using var service = new DiagnosticsService(new Manager(new Source(), new Source { Fail = true }));
        var result = service.Read(new JObject());
        Assert.Equal(2, result.Value<int>("count"));
        Assert.False(result.Value<bool>("complete"));
        Assert.False(result.Value<bool>("isStable"));
        Assert.Single((JArray)result["readErrors"]!);
    }

    [Fact]
    public void ClosedSolutionIsNotReportedAsLoading()
    {
        var state = JObject.Parse("{ 'isOpen':false, 'isFullyLoaded':false, 'startupProjectsAvailable':true, 'startupProjects':[], 'projects':[] }");
        var reasons = LaunchCheckService.Explain(state, "dbgDesignMode", "vsBuildStateNotStarted");
        Assert.Equal("noSolution", Assert.Single(reasons).Value<string>("code"));
    }

    private sealed class Entry : ITableEntry
    {
        private readonly string message;
        public Entry(string message) => this.message = message;
        public object Identity => this;
        public bool TryGetValue(string keyName, out object content) { content = keyName == StandardTableKeyNames.Text ? message : keyName == StandardTableKeyNames.ErrorSource ? (object)ErrorSource.Build : null!; return content != null; }
        public bool CanSetValue(string keyName) => false;
        public bool TrySetValue(string keyName, object content) => false;
    }

    private sealed class Source : ITableDataSource, IDisposable
    {
        public bool Fail { get; set; }
        public int Subscriptions { get; private set; }
        public bool Unsubscribed { get; private set; }
        public string SourceTypeIdentifier => "test";
        public string Identifier => Fail ? "failed" : "healthy";
        public string DisplayName => Identifier;
        public IDisposable Subscribe(ITableDataSink sink)
        {
            if (Fail) throw new InvalidOperationException("Provider failed");
            Subscriptions++;
            sink.AddEntries(new[] { new Entry("first"), new Entry("second") });
            sink.IsStable = true;
            return this;
        }
        public void Dispose() => Unsubscribed = true;
    }

    private sealed class Manager : ITableManager
    {
        private readonly List<ITableDataSource> sources;
        public Manager(params ITableDataSource[] sources) => this.sources = new List<ITableDataSource>(sources);
        public string Identifier => "test";
        public IReadOnlyList<ITableDataSource> Sources => sources;
        public event EventHandler? SourcesChanged { add { } remove { } }
        public bool AddSource(ITableDataSource source, IReadOnlyCollection<string> columns) { sources.Add(source); return true; }
        public bool AddSource(ITableDataSource source, params string[] columns) { sources.Add(source); return true; }
        public bool RemoveSource(ITableDataSource source) => sources.Remove(source);
        public IReadOnlyList<string> GetColumnsForSources(IEnumerable<ITableDataSource> values) => Array.Empty<string>();
    }

    private sealed class Factory : ITableEntriesSnapshotFactory
    {
        public int CurrentVersionNumber { get; set; }
        public Snapshot? Last { get; private set; }
        public ITableEntriesSnapshot GetCurrentSnapshot() => Last = new Snapshot(CurrentVersionNumber);
        public ITableEntriesSnapshot GetSnapshot(int versionNumber) => new Snapshot(versionNumber);
        public void Dispose() { }
    }

    private sealed class Snapshot : ITableEntriesSnapshot
    {
        public Snapshot(int version) => VersionNumber = version;
        public int Count => 1;
        public int VersionNumber { get; }
        public int CacheStarts { get; private set; }
        public int CacheStops { get; private set; }
        public bool Disposed { get; private set; }
        public void StartCaching() => CacheStarts++;
        public void StopCaching() => CacheStops++;
        public bool TryGetValue(int index, string keyName, out object content) { content = keyName == StandardTableKeyNames.Text ? "version " + VersionNumber : null!; return content != null; }
        public int IndexOf(int currentIndex, ITableEntriesSnapshot newSnapshot) => currentIndex;
        public void Dispose() => Disposed = true;
    }
}
