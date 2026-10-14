namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class EventsOptions
{
    public int? AfterSequence { get; set; }
    public int? Limit { get; set; }
    public string? SessionId { get; set; }
}
