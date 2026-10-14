namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ProcessesResult
{
    public DebugProcess[]? Processes { get; set; }
    public string? Scope { get; set; }
}
