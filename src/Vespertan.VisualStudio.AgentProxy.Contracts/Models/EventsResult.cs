namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class EventsResult
{
    public string? SessionId { get; set; }
    public AgentEvent[]? Events { get; set; }
    public bool HistoryLost { get; set; }
    public bool SessionChanged { get; set; }
    public long FirstAvailableSequence { get; set; }
    public long LastSequence { get; set; }
    public long NextSequence { get; set; }
}
