namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SelectLaunchProfileOptions : MutationOptions
{
    public string? Project { get; set; }
    public string? Name { get; set; }
}
