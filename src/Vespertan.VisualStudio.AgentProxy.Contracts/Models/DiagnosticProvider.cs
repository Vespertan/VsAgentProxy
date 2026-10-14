namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class DiagnosticProvider
{
    public string? Id { get; set; }
    public bool IsStable { get; set; }
    public int Count { get; set; }
}
