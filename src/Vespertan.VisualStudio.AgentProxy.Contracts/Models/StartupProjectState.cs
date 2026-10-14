namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class StartupProjectState
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int Mode { get; set; }
    public int Order { get; set; }
    public string? DebugTarget { get; set; }
    public ReadError? TargetReadError { get; set; }
}
