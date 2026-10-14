namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentMethod
{
    public string? Name { get; set; }
    public bool MutatesState { get; set; }
    public string? Parameters { get; set; }
    public string? Description { get; set; }
}
