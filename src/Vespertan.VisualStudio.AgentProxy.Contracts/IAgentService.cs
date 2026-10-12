using System;
using System.Threading;
using System.Threading.Tasks;

namespace VsAgentProxy.Contracts;

public interface IAgentService : IDisposable
{
    /// <summary>Existing command envelope and result JSON. RPC owns framing,
    /// cancellation and connection lifetime; domain operations keep their IDs.</summary>
    Task<string> ExecuteAsync(string requestJson, CancellationToken cancellationToken);
}
