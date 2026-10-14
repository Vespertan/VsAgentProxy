namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SelectSolutionLaunchProfileOptions : MutationOptions
{
    public string? Name { get; set; }
    public string? Scope { get; set; }
}
