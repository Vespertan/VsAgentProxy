using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class BreakpointInfo
{
    public string? Id { get; set; }
    public int Index { get; set; }
    public string? Name { get; set; }
    public bool Enabled { get; set; }
    public string? File { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string? Function { get; set; }
    public string? Language { get; set; }
    public string? Condition { get; set; }
    public string? ConditionType { get; set; }
    public int HitCount { get; set; }
    public string? HitCountType { get; set; }
    public string? LocationType { get; set; }
    public int? CurrentHits { get; set; }
    public string? CurrentHitsAvailability { get; set; }
    public string? CurrentHitsReliability { get; set; }
    public int ObservedHits { get; set; }
    public DateTime ObservedSinceUtc { get; set; }
    public string? ObservedHitsSource { get; set; }
    public string? BindingState { get; set; }
    public BreakpointLocation[]? BoundLocations { get; set; }
    public string? BindingSource { get; set; }
    public string? BindingErrorAvailability { get; set; }
    public string? SourceMapAvailability { get; set; }
    public ReadError[]? ReadErrors { get; set; }
    public AgentEvent[]? BindingEvents { get; set; }
    public string? BindingEventsNote { get; set; }
}
