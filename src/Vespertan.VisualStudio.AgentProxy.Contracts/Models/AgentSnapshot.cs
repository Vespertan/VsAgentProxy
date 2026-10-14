using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentSnapshot
{
    public string? SessionId { get; set; }
    public DateTime StartedUtc { get; set; }
    public bool Atomic { get; set; }
    public CapturedValue<DebuggerStatus>? Status { get; set; }
    public CapturedValue<ProjectState>? Projects { get; set; }
    public CapturedValue<LaunchCheckResult>? LaunchCheck { get; set; }
    public CapturedValue<DocumentsResult>? Documents { get; set; }
    public CapturedValue<OutputResult>? OutputBuild { get; set; }
    public CapturedValue<OutputResult>? OutputDebug { get; set; }
    public SnapshotDiagnostics? Diagnostics { get; set; }
    public DateTime CompletedUtc { get; set; }
}
