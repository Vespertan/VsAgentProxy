namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SnapshotDiagnostics : DiagnosticsResult
{
    public ReadError? Error { get; set; }
}
