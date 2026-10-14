namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SaveDocumentsResult
{
    public SavedDocument[]? Documents { get; set; }
    public bool AllSaved { get; set; }
}
