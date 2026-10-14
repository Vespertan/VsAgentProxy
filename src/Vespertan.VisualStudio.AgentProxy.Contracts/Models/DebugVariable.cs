namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class DebugVariable
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Value { get; set; }
    public bool? IsValid { get; set; }
    public bool? HasChildren { get; set; }
    public int? ChildCount { get; set; }
    public string? Reference { get; set; }
    public ReadError[]? ReadErrors { get; set; }
    public DebugVariable[]? Children { get; set; }
    public string? ChildrenError { get; set; }
}
