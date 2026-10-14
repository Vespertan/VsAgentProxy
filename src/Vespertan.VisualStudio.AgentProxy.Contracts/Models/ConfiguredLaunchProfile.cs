namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ConfiguredLaunchProfile
{
    public string? Name { get; set; }
    public string? Source { get; set; }
    public string? SourceKind { get; set; }
    public bool? Active { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? Program { get; set; }
    public object? Arguments { get; set; }
}
