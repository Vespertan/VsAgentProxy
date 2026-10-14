namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ProjectProperty
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Availability { get; set; }
    public ReadError? Error { get; set; }
}
