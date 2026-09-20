using System;
using Newtonsoft.Json.Linq;
using Xunit;

namespace VsAgentProxy.Tests;

public sealed class OperationTests
{
    [Theory]
    [InlineData("document.cs")]
    [InlineData(@"C:document.cs")]
    [InlineData(@"\document.cs")]
    [InlineData("/document.cs")]
    public void DocumentPathsCannotDependOnProcessWorkingDirectory(string path)
    {
        Assert.Throws<ArgumentException>(() => ProtocolSupport.AbsolutePath(path));
        Assert.Equal(@"C:\Project\document.cs", ProtocolSupport.AbsolutePath(@"C:\Project\document.cs"));
        Assert.Equal(@"\\server\share\document.cs", ProtocolSupport.AbsolutePath(@"\\server\share\document.cs"));
    }

    [Fact]
    public void TimeoutDoesNotClaimCancellationAndReleasesOperationSlot()
    {
        var clock = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var registry = new OperationRegistry(() => clock);
        var first = registry.Begin("start");
        Assert.Throws<ProxyException>(() => registry.Begin("build"));
        clock = clock.AddMinutes(3);
        var state = registry.Status((string)first["id"]!);
        Assert.Equal("unknown", (string?)state["state"]);
        Assert.Equal("confirmationTimedOut", (string?)state["phase"]);
        Assert.NotEqual((string?)first["id"], (string?)registry.Begin("build")["id"]);
    }

    [Fact]
    public void CompletedOperationCannotBeChangedByUnrelatedEvents()
    {
        var registry = new OperationRegistry();
        var operation = registry.Begin("build");
        registry.Change("failed", "buildFailed");
        registry.Change("succeeded", "completed");
        Assert.Equal("failed", (string?)registry.Status((string)operation["id"]!)["state"]);
    }

    [Fact]
    public void EventCursorReportsLostHistoryAndSessionChanges()
    {
        var registry = new OperationRegistry();
        for (var i = 0; i < 600; i++) registry.Emit("test");
        var page = registry.Events(new JObject { ["afterSequence"] = 1, ["limit"] = 10 });
        Assert.True((bool)page["historyLost"]!);
        Assert.Equal(89, (int)page["firstAvailableSequence"]!);
        Assert.Equal(98, (int)page["nextSequence"]!);
        var next = registry.Events(new JObject { ["afterSequence"] = 98, ["limit"] = 10 });
        Assert.False((bool)next["historyLost"]!);
        var restarted = registry.Events(new JObject { ["sessionId"] = "old", ["afterSequence"] = 1000 });
        Assert.True((bool)restarted["sessionChanged"]!);
        Assert.NotEmpty((JArray)restarted["events"]!);
    }

    [Fact]
    public void IdempotencyReplaysAcrossRequestIdsButRejectsChangedMutation()
    {
        var cache = new MutationCache();
        var request = JObject.Parse("{ 'id':'a', 'method':'start', 'params':{ 'idempotencyKey':'key' } }");
        cache.Store("key", request, JObject.Parse("{ 'id':'a','ok':true,'result':{'operationId':'op'} }"));
        request["id"] = "b";
        var replay = cache.Find("key", request)!;
        Assert.Equal("b", (string?)replay["id"]);
        Assert.Equal("op", (string?)replay["result"]?["operationId"]);
        replay["result"]!["operationId"] = "changed";
        Assert.Equal("op", (string?)cache.Find("key", request)!["result"]?["operationId"]);
        request["method"] = "build";
        Assert.Equal("idempotencyConflict", Assert.Throws<ProxyException>(() => cache.Find("key", request)).Code);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("['']")]
    [InlineData("[42]")]
    [InlineData("'path'")]
    public void InvalidPathArraysAreRejectedBeforeMutation(string value)
    {
        Assert.Throws<ArgumentException>(() => ProtocolSupport.Strings(JObject.Parse("{'paths':" + value + "}"), "paths"));
    }

    [Fact]
    public void ConfiguredProfileNeverClaimsActiveOrLeaksEnvironment()
    {
        var profile = LaunchService.ConfiguredProfile(JObject.Parse("{'type':'chrome','url':'http://localhost:4200','env':{'SECRET':'value'}}"), "Chrome", "launch.json");
        Assert.Equal(JTokenType.Null, profile["active"]!.Type);
        Assert.Null(profile["env"]);
    Assert.Equal("diskConfiguration", (string?)profile["sourceKind"]);
    }

    [Fact]
    public void DocumentationVersionComesFromProxyAssembly()
    {
        var assemblyVersion = typeof(AgentDocumentation).Assembly.GetName().Version!.ToString(3);
        Assert.Equal(assemblyVersion, (string?)AgentDocumentation.GetCapabilities()["version"]);
        Assert.Equal(assemblyVersion, (string?)AgentDocumentation.GetDocumentation("short")["version"]);
    }
}
