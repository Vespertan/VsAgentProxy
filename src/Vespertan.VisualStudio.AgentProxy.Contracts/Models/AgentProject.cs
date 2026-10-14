namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentProject
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? UniqueName { get; set; }
    public string? TypeGuid { get; set; }
    public string? HierarchyTypeGuid { get; set; }
    public string? Type { get; set; }
    public bool IsSolutionFolder { get; set; }
    public string? LoadState { get; set; }
    public bool? IsStartup { get; set; }
    public string? LoadError { get; set; }
    public string? LoadErrorAvailability { get; set; }
    public string? LoadErrorSource { get; set; }
    public string? LoadErrorUnavailableReason { get; set; }
    public ReadError[]? ReadErrors { get; set; }
}
