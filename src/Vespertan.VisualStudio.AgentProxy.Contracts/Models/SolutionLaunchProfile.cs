namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SolutionLaunchProfile
{
    public string? Name { get; set; }
    public bool? IsShared { get; set; }
    public string? Scope { get; set; }
    public bool? Active { get; set; }
    public SolutionLaunchProject[]? Projects { get; set; }
    public string? Source { get; set; }
}
