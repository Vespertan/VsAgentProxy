namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class BreakpointHit
{
    public string? Tag { get; set; }
    public string? File { get; set; }
    public int Line { get; set; }
    public int ObservedHits { get; set; }
}
