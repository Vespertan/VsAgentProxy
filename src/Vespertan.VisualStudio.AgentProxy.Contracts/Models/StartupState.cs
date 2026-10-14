namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class StartupState
{
    public int Mode { get; set; }
    public string? DefaultProjectId { get; set; }
    public StartupProjectState[]? Projects { get; set; }
}
