namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class StackFrameInfo
{
    public int Index { get; set; }
    public uint Depth { get; set; }
    public bool IsCurrent { get; set; }
    public string? Function { get; set; }
    public string? Module { get; set; }
    public string? ModuleName { get; set; }
    public string? Language { get; set; }
    public string? File { get; set; }
    public uint? Line { get; set; }
    public int? Column { get; set; }
    public bool? UserCode { get; set; }
    public int ThreadId { get; set; }
    public string? ThreadName { get; set; }
}
