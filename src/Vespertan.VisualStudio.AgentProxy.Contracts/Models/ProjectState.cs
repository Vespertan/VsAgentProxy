namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ProjectState
{
    public string? Solution { get; set; }
    public bool IsOpen { get; set; }
    public bool? IsFullyLoaded { get; set; }
    public AgentProject[]? Projects { get; set; }
    public string[]? StartupProjects { get; set; }
    public bool StartupProjectsAvailable { get; set; }
    public ReadError[]? ReadErrors { get; set; }
}
