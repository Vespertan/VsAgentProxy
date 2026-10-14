namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public class BreakpointSelector : MutationOptions
{
    public string? Id { get; set; }
    public int? Index { get; set; }
}
