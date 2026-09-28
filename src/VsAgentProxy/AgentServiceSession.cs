using System;
using System.Threading;
using System.Threading.Tasks;
using VsAgentProxy.Contracts;

namespace VsAgentProxy;

// A broker creates/disposes one session per client. Shared IDE state survives it.
internal sealed class AgentServiceSession : IAgentService
{
    private readonly AgentService service;
    private readonly CancellationTokenSource lifetime = new();
    private int disposed;
    public AgentServiceSession(AgentService service) => this.service = service;
    public async Task<string> ExecuteAsync(string requestJson, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(AgentServiceSession));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        return await service.ExecuteAsync(requestJson, linked.Token).ConfigureAwait(false);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) lifetime.Cancel();
    }
}
