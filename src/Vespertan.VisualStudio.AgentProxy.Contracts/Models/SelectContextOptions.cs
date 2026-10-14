namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SelectContextOptions : MutationOptions
{
    public int? ThreadId { get; set; }
    public int? FrameIndex { get; set; }
}
