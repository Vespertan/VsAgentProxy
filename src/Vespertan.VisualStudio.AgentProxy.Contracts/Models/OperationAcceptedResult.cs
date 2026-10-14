namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class OperationAcceptedResult : AcceptedResult
{
    public string? OperationId { get; set; }
    public AgentOperation? Operation { get; set; }
}
