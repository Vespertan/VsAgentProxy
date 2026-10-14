namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentInfo
{
    public string? Version { get; set; }
    public int Pid { get; set; }
    public string? SessionId { get; set; }
}
