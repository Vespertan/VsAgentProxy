using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public class DiagnosticsResult
{
    public AgentDiagnostic[]? Items { get; set; }
    public int Offset { get; set; }
    public int Count { get; set; }
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
    public bool IsStable { get; set; }
    public bool Complete { get; set; }
    public DiagnosticProvider[]? Providers { get; set; }
    public ReadError[]? ReadErrors { get; set; }
    public DateTime CapturedUtc { get; set; }
    public string? Scope { get; set; }
    public string[]? Limitations { get; set; }
    public LanguageServiceStatus? Document { get; set; }
}
