namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SolutionLaunchProject
{
    public string? Path { get; set; }
    public string? Action { get; set; }
    public string? DebugTarget { get; set; }
}
