namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class DebugProcess
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool Current { get; set; }
}
