namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ScopesResult
{
    public int FrameIndex { get; set; }
    public long Generation { get; set; }
    public VariableScope[]? Scopes { get; set; }
    public string? Source { get; set; }
    public string? EvaluationPolicy { get; set; }
}
