using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceHub.Framework;

namespace VsAgentProxy.Contracts;

public static class AgentServices
{
    public const string Name = "Vespertan.VisualStudio.AgentProxy";
    public const string Version = "1.0";

    public static readonly ServiceRpcDescriptor Agent = new ServiceJsonRpcDescriptor(
        new ServiceMoniker(Name, new System.Version(Version)),
        ServiceJsonRpcDescriptor.Formatters.UTF8,
        ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
}
