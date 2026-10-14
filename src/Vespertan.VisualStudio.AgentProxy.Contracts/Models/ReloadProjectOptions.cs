namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ReloadProjectOptions : MutationOptions
{
    public string? Path { get; set; }
}
