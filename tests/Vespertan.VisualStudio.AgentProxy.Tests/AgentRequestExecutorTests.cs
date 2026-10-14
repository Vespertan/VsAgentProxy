using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Contracts;
using Xunit;
using Vespertan.VisualStudio.AgentProxy.Rpc;

namespace Vespertan.VisualStudio.AgentProxy.Tests;

#pragma warning disable VSTHRD003 // These test gates coordinate executor calls without a VS UI thread dependency.
public sealed class AgentRequestExecutorTests
{
    [Fact]
    public async Task TypedMutationsReplayOriginalResultsAndRejectChangedParameters()
    {
        using var executor = new AgentRequestExecutor();
        var calls = 0;
        Task<object> invokeAsync(JObject parameters, CancellationToken token)
        {
            calls++;
            Assert.Null(parameters["index"]);
            Assert.Equal(false, (bool?)parameters["enabled"]);
            return Task.FromResult<object>(new JObject { ["enabled"] = false, ["currentHits"] = null });
        }

        var options = new SetBreakpointEnabledOptions { Id = "breakpoint", Enabled = false, IdempotencyKey = "key" };
        var first = await executor.ExecuteAsync<BreakpointInfo>("breakpointSetEnabled", options, invokeAsync, CancellationToken.None);
        var replay = await executor.ExecuteAsync<BreakpointInfo>("breakpointSetEnabled", options, invokeAsync, CancellationToken.None);
        Assert.True(first.Ok);
        Assert.True(replay.Replayed);
        Assert.False(replay.Result!.Enabled);
        Assert.Null(replay.Result.CurrentHits);
        Assert.Equal(1, calls);

        options.Enabled = true;
        var conflict = await executor.ExecuteAsync<BreakpointInfo>("breakpointSetEnabled", options, invokeAsync, CancellationToken.None);
        Assert.False(conflict.Ok);
        Assert.Equal("idempotencyConflict", conflict.ErrorCode);
        Assert.Equal(1, calls);
        var otherMethod = await executor.ExecuteAsync<BreakpointInfo>("breakpointRemove", options, invokeAsync, CancellationToken.None);
        Assert.Equal("idempotencyConflict", otherMethod.ErrorCode);
    }

    [Fact]
    public async Task DomainFailuresRetainDetailsAndAreReplayed()
    {
        using var executor = new AgentRequestExecutor();
        var calls = 0;
        Task<object> invokeAsync(JObject parameters, CancellationToken token)
        {
            calls++;
            throw new ProxyException("unsavedDocuments", "Save first.", new JObject
            {
                ["documents"] = new JArray(new JObject { ["path"] = "C:/żółć.cs", ["dirty"] = null })
            });
        }

        var options = new ReloadProjectOptions { Path = "C:/project.csproj", IdempotencyKey = "reload" };
        var first = await executor.ExecuteAsync<ReloadProjectResult>("projectReload", options, invokeAsync, CancellationToken.None);
        var replay = await executor.ExecuteAsync<ReloadProjectResult>("projectReload", options, invokeAsync, CancellationToken.None);
        Assert.False(first.Ok);
        Assert.Equal("unsavedDocuments", replay.ErrorCode);
        Assert.True(replay.Replayed);
        Assert.Equal("C:/żółć.cs", (string?)JToken.FromObject(replay.Details!)["documents"]![0]!["path"]);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationIsPropagatedAndDoesNotCacheOrHoldTheGate()
    {
        using var executor = new AgentRequestExecutor();
        using var cancelled = new CancellationTokenSource();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new MutationOptions { IdempotencyKey = "start" };
        var pending = executor.ExecuteAsync<AcceptedResult>("start", options, async (parameters, token) =>
        {
            entered.SetResult(true);
            await Task.Delay(Timeout.Infinite, token);
            return new { accepted = true };
        }, cancelled.Token);
        await entered.Task;
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        var retried = await executor.ExecuteAsync<AcceptedResult>("start", options,
            (parameters, token) => Task.FromResult<object>(new { accepted = true }), CancellationToken.None);
        Assert.True(retried.Ok);
        Assert.True(retried.Result!.Accepted);
        Assert.Null(retried.Replayed);
    }

    [Fact]
    public async Task ConcurrentClientsWithTheSameKeyExecuteOnlyOnce()
    {
        using var executor = new AgentRequestExecutor();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<object> invokeAsync(JObject parameters, CancellationToken token)
        {
            calls++;
            entered.SetResult(true);
            await release.Task;
            return new { accepted = true, operationId = "original" };
        }

        var options = new MutationOptions { IdempotencyKey = "shared" };
        var first = executor.ExecuteAsync<OperationAcceptedResult>("build", options, invokeAsync, CancellationToken.None);
        await entered.Task;
        var second = executor.ExecuteAsync<OperationAcceptedResult>("build", options, invokeAsync, CancellationToken.None);
        release.SetResult(true);
        var responses = await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
        Assert.True(responses[1].Replayed);
        Assert.Equal("original", responses[1].Result!.OperationId);
    }

    [Fact]
    public async Task ShutdownCancelsActiveAndQueuedCalls()
    {
        using var executor = new AgentRequestExecutor();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = executor.ExecuteAsync<AcceptedResult>("start", null, async (parameters, token) =>
        {
            entered.SetResult(true);
            await Task.Delay(Timeout.Infinite, token);
            return new { accepted = true };
        }, CancellationToken.None);
        await entered.Task;
        var queued = executor.ExecuteAsync<AcceptedResult>("stop", null,
            (parameters, token) => Task.FromResult<object>(new { accepted = true }), CancellationToken.None);
        executor.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
    }

    [Fact]
    public async Task TypedSnapshotsPreserveUnavailableValuesAndPartialReadErrors()
    {
        using var executor = new AgentRequestExecutor();
        var snapshot = await executor.ExecuteAsync<AgentSnapshot>("snapshot", null,
            (parameters, token) => Task.FromResult<object>(JObject.Parse("""
                {
                    "status": { "value": { "currentThreadId": null, "mode": "dbgDesignMode" } },
                    "projects": { "error": { "field": "projects", "message": "unavailable" } },
                    "diagnostics": { "error": { "field": "diagnostics", "message": "unavailable" } },
                    "outputBuild": { "value": { "text": "", "totalChars": 0 } }
                }
                """)), CancellationToken.None);
        Assert.True(snapshot.Ok);
        Assert.Null(snapshot.Result!.Status!.Value!.CurrentThreadId);
        Assert.Null(snapshot.Result.Projects!.Value);
        Assert.Equal("projects", snapshot.Result.Projects.Error!.Field);
        Assert.Equal("diagnostics", snapshot.Result.Diagnostics!.Error!.Field);
        Assert.Equal(string.Empty, snapshot.Result.OutputBuild!.Value!.Text);
    }
}
#pragma warning restore VSTHRD003
