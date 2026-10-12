using System;
using System.Threading;
using System.Threading.Tasks;
using VsAgentProxy.Contracts;

namespace VsAgentProxy;

// A broker creates/disposes one session per client. Shared IDE state survives it.
internal sealed class AgentServiceSession : IAgentService
{
    private readonly AgentService _service;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;
    public AgentServiceSession(AgentService service) => _service = service;

    public async Task<string> ExecuteAsync(string requestJson, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(AgentServiceSession));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await _service.ExecuteAsync(requestJson, linked.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifetime.Cancel();
        }
    }
}
