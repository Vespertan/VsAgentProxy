namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class OutputResult
{
    public string[]? Panes { get; set; }
    public OutputPaneInfo[]? PaneDetails { get; set; }
    public string? Pane { get; set; }
    public string? PaneId { get; set; }
    public string? Text { get; set; }
    public int? Offset { get; set; }
    public int? Count { get; set; }
    public int? TotalChars { get; set; }
    public bool? HasMoreBefore { get; set; }
    public bool? HasMoreAfter { get; set; }
    public string? Source { get; set; }
    public bool? PaneInitialized { get; set; }
    public bool? PreviousPaneAvailable { get; set; }
    public ReadError[]? RestorationErrors { get; set; }
    public bool? PaneSelectionRestored { get; set; }
}
