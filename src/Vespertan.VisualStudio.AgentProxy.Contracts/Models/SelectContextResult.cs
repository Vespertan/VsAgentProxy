namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SelectContextResult
{
    public int ThreadId { get; set; }
    public int FrameIndex { get; set; }
    public long Generation { get; set; }
}
