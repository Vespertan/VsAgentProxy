namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class EvaluateResult
{
    public DebugVariable? Expression { get; set; }
    public bool Truncated { get; set; }
}
