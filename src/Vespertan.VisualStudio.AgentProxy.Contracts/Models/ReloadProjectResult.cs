namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ReloadProjectResult
{
    public bool Accepted { get; set; }
    public bool UnloadedByProxy { get; set; }
    public bool Loaded { get; set; }
    public AgentProject? Project { get; set; }
}
