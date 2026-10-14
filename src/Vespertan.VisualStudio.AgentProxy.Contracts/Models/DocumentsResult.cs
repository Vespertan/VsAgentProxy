using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class DocumentsResult
{
    public AgentDocument[]? Documents { get; set; }
    public string? Source { get; set; }
    public DateTime CapturedUtc { get; set; }
}
