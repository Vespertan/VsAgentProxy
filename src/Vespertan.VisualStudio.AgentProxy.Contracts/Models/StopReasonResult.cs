using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class StopReasonResult
{
    public string? Availability { get; set; }
    public string? Reason { get; set; }
    public string? Source { get; set; }
    public long? Generation { get; set; }
    public string? ExceptionDetailsAvailability { get; set; }
    public DateTime? CapturedUtc { get; set; }
    public DebugException? Exception { get; set; }
    public BreakpointHit[]? Breakpoints { get; set; }
}
