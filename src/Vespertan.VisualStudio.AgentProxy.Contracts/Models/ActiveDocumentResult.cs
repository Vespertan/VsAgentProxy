namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ActiveDocumentResult
{
    public bool Available { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? SelectedText { get; set; }
    public bool? SelectionTruncated { get; set; }
}
