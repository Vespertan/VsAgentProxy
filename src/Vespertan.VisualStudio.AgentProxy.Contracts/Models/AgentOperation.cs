using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentOperation
{
    public string? Id { get; set; }
    public string? SessionId { get; set; }
    public string? Method { get; set; }
    public string? State { get; set; }
    public string? Phase { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? Source { get; set; }
    public string? Correlation { get; set; }
    public string? Evidence { get; set; }
}
