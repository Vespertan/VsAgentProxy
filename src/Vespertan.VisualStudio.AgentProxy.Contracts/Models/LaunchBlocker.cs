namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class LaunchBlocker
{
    public string? Code { get; set; }
    public string? Confidence { get; set; }
    public string? Evidence { get; set; }
    public string? Project { get; set; }
    public string? LoadError { get; set; }
}
