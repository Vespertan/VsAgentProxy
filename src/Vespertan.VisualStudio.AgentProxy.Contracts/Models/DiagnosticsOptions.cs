namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public class DiagnosticsOptions
{
    public string? Severity { get; set; }
    public string? Origin { get; set; }
    public string? Project { get; set; }
    public string? File { get; set; }
    public int? Offset { get; set; }
    public int? Count { get; set; }
}
