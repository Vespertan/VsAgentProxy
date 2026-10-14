namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class LaunchProfilesResult
{
    public AgentProject? Project { get; set; }
    public string? Availability { get; set; }
    public string? Source { get; set; }
    public string? UnavailableReason { get; set; }
    public LaunchProfile[]? Profiles { get; set; }
    public string? ActiveProfile { get; set; }
    public string? ActiveTargetType { get; set; }
    public uint? ActiveTargetTypeId { get; set; }
    public ConfiguredLaunchProfile[]? ConfiguredProfiles { get; set; }
    public ReadError[]? ConfigurationReadErrors { get; set; }
    public string? EffectiveCommandAvailability { get; set; }
    public string? ConfigurationNote { get; set; }
    public ProjectPropertiesResult? ProjectProperties { get; set; }
    public string? EvaluatedStartupCommand { get; set; }
    public string? EvaluatedBuildWorkingDirectory { get; set; }
    public string? EffectiveCommandNote { get; set; }
    public bool? Applied { get; set; }
    public bool? Accepted { get; set; }
    public LaunchProfilesResult? Previous { get; set; }
}
