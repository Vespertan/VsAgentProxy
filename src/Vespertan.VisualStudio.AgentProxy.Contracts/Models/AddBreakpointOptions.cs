namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AddBreakpointOptions : MutationOptions
{
    public string? File { get; set; }
    public string? Function { get; set; }
    public string? Data { get; set; }
    public string? Address { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? Condition { get; set; }
    public string? ConditionType { get; set; }
    public string? Language { get; set; }
    public int? DataCount { get; set; }
    public int? HitCount { get; set; }
    public string? HitCountType { get; set; }
    public bool? Enabled { get; set; }
}
