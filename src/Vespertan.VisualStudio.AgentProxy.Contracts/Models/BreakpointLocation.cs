namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class BreakpointLocation
{
    public string? File { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string? Function { get; set; }
    public string? Language { get; set; }
}
