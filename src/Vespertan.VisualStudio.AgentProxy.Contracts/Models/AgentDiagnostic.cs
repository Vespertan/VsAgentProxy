namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentDiagnostic
{
    public string? Severity { get; set; }
    public string? Origin { get; set; }
    public string? RawOrigin { get; set; }
    public string? BuildTool { get; set; }
    public string? Project { get; set; }
    public string? ProjectId { get; set; }
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? Source { get; set; }
    public string? SourceType { get; set; }
    public string? Provider { get; set; }
    public bool? Faulted { get; set; }
}
