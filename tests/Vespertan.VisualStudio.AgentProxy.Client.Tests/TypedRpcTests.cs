using System.Text.Json.Nodes;
using Nerdbank.Streams;
using Vespertan.VisualStudio.AgentProxy.Client;
using Vespertan.VisualStudio.AgentProxy.Contracts;
using Xunit;

namespace Vespertan.VisualStudio.AgentProxy.Client.Tests;

#pragma warning disable VSTHRD003 // Test synchronization waits for the remote endpoint; no VS UI thread is involved.
public sealed class TypedRpcTests
{
    [Fact]
    public async Task CliCallsTypedMethodsAndPreservesUnicodeNullableValuesAndArrays()
    {
        var pipes = FullDuplexStream.CreatePipePair();
        using var server = AgentServices.Agent.ConstructRpcConnection(pipes.Item1);
        var target = new RpcTarget();
        server.AddLocalRpcTarget(target);
        server.StartListening();
        using var agent = AgentServices.Agent.ConstructRpc<IAgentService>(pipes.Item2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var status = await AgentCommandClient.InvokeAsync(agent, "status", new JsonObject(), timeout.Token);
        Assert.True(status["ok"]!.GetValue<bool>());
        Assert.Equal("C:/żółć.sln", status["result"]!["solution"]!.GetValue<string>());
        Assert.Null(status["result"]!["currentThreadId"]);
        Assert.False(status.ContainsKey("error"));

        var saved = await AgentCommandClient.InvokeAsync(agent, "saveDocuments", new JsonObject
        {
            ["paths"] = new JsonArray("C:/żółć.cs", "C:/file.cs"),
            ["idempotencyKey"] = "save-once"
        }, timeout.Token);
        Assert.Equal("save-once", target.SaveOptions!.IdempotencyKey);
        Assert.Equal(new[] { "C:/żółć.cs", "C:/file.cs" }, target.SaveOptions.Paths);
        Assert.False(saved["result"]!["allSaved"]!.GetValue<bool>());
        Assert.Equal(2, saved["result"]!["documents"]!.AsArray().Count);
        Assert.True(saved["replayed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RpcPreservesDomainFailuresAndProviderSpecificData()
    {
        var pipes = FullDuplexStream.CreatePipePair();
        using var server = AgentServices.Agent.ConstructRpcConnection(pipes.Item1);
        server.AddLocalRpcTarget(new RpcTarget());
        server.StartListening();
        using var agent = AgentServices.Agent.ConstructRpc<IAgentService>(pipes.Item2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var failure = await AgentCommandClient.InvokeAsync(agent, "projectReload",
            new JsonObject { ["path"] = "C:/project.csproj" }, timeout.Token);
        Assert.False(failure["ok"]!.GetValue<bool>());
        Assert.Equal("unsavedDocuments", failure["errorCode"]!.GetValue<string>());
        Assert.Equal("0x80004005", failure["hresult"]!.GetValue<string>());
        Assert.Equal("C:/żółć.cs", failure["details"]!["documents"]![0]!["path"]!.GetValue<string>());
        Assert.Null(failure["details"]!["documents"]![0]!["dirty"]);
        Assert.False(failure.ContainsKey("result"));

        var launch = await AgentCommandClient.InvokeAsync(agent, "launchCheck", new JsonObject(), timeout.Token);
        Assert.False(launch["result"]!["commands"]!["Debug.Start"]!.GetValue<bool>());
        Assert.Null(launch["result"]!["commands"]!["Debug.StartWithoutDebugging"]);
    }

    [Fact]
    public async Task RpcCancellationReachesTheMethodAndConnectionRemainsUsable()
    {
        var pipes = FullDuplexStream.CreatePipePair();
        using var server = AgentServices.Agent.ConstructRpcConnection(pipes.Item1);
        var target = new RpcTarget();
        server.AddLocalRpcTarget(target);
        server.StartListening();
        using var agent = AgentServices.Agent.ConstructRpc<IAgentService>(pipes.Item2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var pending = agent.GetVariablesAsync(new VariablesOptions { Reference = "stop:ref" }, cancel.Token);
        await target.Entered.Task.WaitAsync(timeout.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await target.Cancelled.Task.WaitAsync(timeout.Token);
        Assert.True((await agent.GetStatusAsync(timeout.Token)).Ok);
    }

    [Fact]
    public async Task UnknownCliCommandsDoNotInvokeRpc()
    {
        var response = await AgentCommandClient.InvokeAsync(null!, "unknown", new JsonObject(), CancellationToken.None);
        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Equal("unknownMethod", response["errorCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvalidCliOptionTypesReturnAParameterErrorBeforeRpc()
    {
        var response = await AgentCommandClient.InvokeAsync(null!, "saveDocuments",
            new JsonObject { ["paths"] = "C:/file.cs" }, CancellationToken.None);
        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Equal("invalidParameters", response["errorCode"]!.GetValue<string>());
    }

    public sealed class RpcTarget
    {
        public PathsOptions? SaveOptions { get; private set; }
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentResult<DebuggerStatus>> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult<DebuggerStatus>
            {
                Ok = true,
                Result = new DebuggerStatus { Solution = "C:/żółć.sln", CurrentThreadId = null }
            });
        }

        public Task<AgentResult<SaveDocumentsResult>> SaveDocumentsAsync(PathsOptions options, CancellationToken cancellationToken)
        {
            SaveOptions = options;
            return Task.FromResult(new AgentResult<SaveDocumentsResult>
            {
                Ok = true,
                Replayed = true,
                Result = new SaveDocumentsResult
                {
                    Documents = options.Paths!.Select(path => new SavedDocument { Path = path }).ToArray()
                }
            });
        }

        public Task<AgentResult<ReloadProjectResult>> ReloadProjectAsync(ReloadProjectOptions options, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult<ReloadProjectResult>
            {
                Error = "Save first.",
                ErrorCode = "unsavedDocuments",
                Hresult = "0x80004005",
                Details = new Dictionary<string, object?>
                {
                    ["documents"] = new[] { new { Path = "C:/żółć.cs", Dirty = (bool?)null } }
                }
            });
        }

        public Task<AgentResult<LaunchCheckResult>> GetLaunchCheckAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult<LaunchCheckResult>
            {
                Ok = true,
                Result = new LaunchCheckResult
                {
                    Commands = new Dictionary<string, bool?> { ["Debug.Start"] = false, ["Debug.StartWithoutDebugging"] = null }
                }
            });
        }

        public async Task<AgentResult<VariablesResult>> GetVariablesAsync(VariablesOptions options, CancellationToken cancellationToken)
        {
            Entered.SetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new AgentResult<VariablesResult>();
            }
            catch (OperationCanceledException)
            {
                Cancelled.SetResult(true);
                throw;
            }
        }
    }
}
#pragma warning restore VSTHRD003
