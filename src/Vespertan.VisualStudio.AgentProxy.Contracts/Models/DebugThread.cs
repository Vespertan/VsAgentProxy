namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class DebugThread
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool Current { get; set; }
    public bool Frozen { get; set; }
}
