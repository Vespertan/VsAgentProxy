using System.Collections.Generic;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class LaunchCheckResult
{
    public bool? CanExecuteStartCommand { get; set; }
    public string? StartAction { get; set; }
    public string? Mode { get; set; }
    public string? BuildState { get; set; }
    public string? Configuration { get; set; }
    public int? LastBuildFailedProjects { get; set; }
    public Dictionary<string, bool?>? Commands { get; set; }
    public LaunchBlocker[]? Reasons { get; set; }
    public bool ReasonUnknown { get; set; }
    public ProjectState? ProjectState { get; set; }
    public ReadError[]? ReadErrors { get; set; }
    public string? LaunchProfilesAvailability { get; set; }
    public string? LaunchProfilesSuggestedRead { get; set; }
    public string? Note { get; set; }
}
