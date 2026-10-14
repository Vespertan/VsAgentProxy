namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class FrameExpressionsResult
{
    public int FrameIndex { get; set; }
    public string? Frame { get; set; }
    public string? Kind { get; set; }
    public DebugVariable[]? Items { get; set; }
    public bool Truncated { get; set; }
}
