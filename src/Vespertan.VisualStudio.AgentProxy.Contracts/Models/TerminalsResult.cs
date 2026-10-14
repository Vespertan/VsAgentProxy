namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class TerminalsResult
{
    public string? Availability { get; set; }
    public string[]? Terminals { get; set; }
    public string? UnavailableReason { get; set; }
    public string? Scope { get; set; }
    public string? OutputAvailability { get; set; }
    public string? SuggestedRead { get; set; }
}
