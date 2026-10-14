namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SavedDocument
{
    public string? Path { get; set; }
    public string? SavedPath { get; set; }
    public bool Saved { get; set; }
    public bool? Cancelled { get; set; }
    public ReadError? Error { get; set; }
}
