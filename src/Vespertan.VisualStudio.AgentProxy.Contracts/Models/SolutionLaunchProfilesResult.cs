namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class SolutionLaunchProfilesResult
{
    public SolutionLaunchProfile[]? Profiles { get; set; }
    public string? SelectedProfileName { get; set; }
    public string? ActiveProfile { get; set; }
    public bool? SelectionMatchesProfile { get; set; }
    public string? ActiveProfileAvailability { get; set; }
    public string? SelectionAvailability { get; set; }
    public string? Source { get; set; }
    public string? SourceKind { get; set; }
    public string? Compatibility { get; set; }
    public string? NativeVersion { get; set; }
    public string? CompatibilityNote { get; set; }
    public StartupState? StartupState { get; set; }
    public ReadError[]? ReadErrors { get; set; }
    public ReadError? ReadError { get; set; }
    public string? UnavailableReason { get; set; }
    public bool? Accepted { get; set; }
    public bool? Applied { get; set; }
    public StartupState? PreviousStartupState { get; set; }
}
