namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class VariablesResult
{
    public DebugVariable[]? Items { get; set; }
    public int Offset { get; set; }
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
    public long Generation { get; set; }
    public string? Source { get; set; }
}
