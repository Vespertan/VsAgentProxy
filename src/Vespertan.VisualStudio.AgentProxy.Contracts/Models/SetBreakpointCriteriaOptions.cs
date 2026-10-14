namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SetBreakpointCriteriaOptions : BreakpointSelector
{
    public string? Condition { get; set; }
    public string? ConditionType { get; set; }
    public int? HitCount { get; set; }
    public string? HitCountType { get; set; }
}
