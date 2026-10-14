namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SetBreakpointEnabledOptions : BreakpointSelector
{
    public bool? Enabled { get; set; }
}
