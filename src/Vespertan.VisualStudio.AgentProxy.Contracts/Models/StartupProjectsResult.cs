namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class StartupProjectsResult
{
    public string[]? Paths { get; set; }
    public string[]? UniqueNames { get; set; }
    public bool Available { get; set; }
    public string? Source { get; set; }
    public bool? Applied { get; set; }
}
