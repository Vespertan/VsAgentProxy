namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ProjectPropertiesResult
{
    public AgentProject? Project { get; set; }
    public string? Configuration { get; set; }
    public ProjectProperty[]? Properties { get; set; }
    public string? Source { get; set; }
}
