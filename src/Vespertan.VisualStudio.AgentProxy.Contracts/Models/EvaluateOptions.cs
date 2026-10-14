namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class EvaluateOptions : MutationOptions
{
    public string? Expression { get; set; }
    public int? TimeoutMs { get; set; }
    public int? MaxDepth { get; set; }
    public int? MaxItems { get; set; }
}
