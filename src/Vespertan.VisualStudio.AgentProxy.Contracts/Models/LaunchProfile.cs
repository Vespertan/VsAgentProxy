namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class LaunchProfile
{
    public string? Name { get; set; }
    public string? TargetType { get; set; }
    public uint TargetTypeId { get; set; }
    public bool Active { get; set; }
}
