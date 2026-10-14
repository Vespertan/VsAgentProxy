namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class DebuggerStatus
{
    public string? Mode { get; set; }
    public string? CurrentProcess { get; set; }
    public string? CurrentThread { get; set; }
    public int? CurrentThreadId { get; set; }
    public string? CurrentThreadName { get; set; }
    public string? BuildState { get; set; }
    public string? SessionId { get; set; }
    public long StopGeneration { get; set; }
    public string? Solution { get; set; }
}
