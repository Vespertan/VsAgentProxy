using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class LanguageServiceStatus
{
    public string? Path { get; set; }
    public string? LanguageServiceAvailability { get; set; }
    public string? ActiveServerVersion { get; set; }
    public string? ServerVersionAvailability { get; set; }
    public int? DocumentVersion { get; set; }
    public string? DiagnosticsFreshness { get; set; }
    public string? Note { get; set; }
    public string? UnavailableReason { get; set; }
    public bool? Dirty { get; set; }
    public string? LanguageServiceId { get; set; }
    public string? ContentType { get; set; }
    public ReadError[]? ReadErrors { get; set; }
    public DateTime? CapturedUtc { get; set; }
}
