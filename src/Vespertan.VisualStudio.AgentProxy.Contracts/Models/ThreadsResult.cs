namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ThreadsResult
{
    public DebugThread[]? Threads { get; set; }
    public string? Scope { get; set; }
}
