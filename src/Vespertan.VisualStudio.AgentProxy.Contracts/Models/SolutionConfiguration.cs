namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SolutionConfiguration
{
    public string? Name { get; set; }
    public string? Platform { get; set; }
    public bool Active { get; set; }
}
