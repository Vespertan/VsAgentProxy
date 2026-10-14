namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentCapabilities
{
    public string? Version { get; set; }
    public int ContractVersion { get; set; }
    public string? Protocol { get; set; }
    public string? PipePattern { get; set; }
    public bool MethodNamesCaseSensitive { get; set; }
    public string[]? RecommendedFirstCalls { get; set; }
    public AgentMethod[]? Methods { get; set; }
    public string[]? Warnings { get; set; }
}
