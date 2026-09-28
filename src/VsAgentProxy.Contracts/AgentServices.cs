using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceHub.Framework;

namespace VsAgentProxy.Contracts;

public interface IAgentService : IDisposable
{
    /// <summary>Existing command envelope and result JSON. RPC owns framing,
    /// cancellation and connection lifetime; domain operations keep their IDs.</summary>
    Task<string> ExecuteAsync(string requestJson, CancellationToken cancellationToken);
}

public static class AgentServices
{
    public const string Name = "Vespertan.VsAgent";
    public const string Version = "1.0";
    public static readonly ServiceRpcDescriptor Agent = new ServiceJsonRpcDescriptor(
        new ServiceMoniker(Name, new System.Version(Version)),
        ServiceJsonRpcDescriptor.Formatters.UTF8,
        ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
}
