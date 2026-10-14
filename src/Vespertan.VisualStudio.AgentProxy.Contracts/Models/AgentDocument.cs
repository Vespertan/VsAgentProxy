namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentDocument
{
    public uint Cookie { get; set; }
    public bool? Dirty { get; set; }
    public string? Path { get; set; }
    public string? Project { get; set; }
    public string? UnavailableReason { get; set; }
    public ReadError[]? ReadErrors { get; set; }
}
