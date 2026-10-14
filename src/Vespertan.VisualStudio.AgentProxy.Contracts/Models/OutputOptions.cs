namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class OutputOptions
{
    public string? Pane { get; set; }
    public string? PaneId { get; set; }
    public int? Offset { get; set; }
    public int? Count { get; set; }
    public int? MaxChars { get; set; }
}
