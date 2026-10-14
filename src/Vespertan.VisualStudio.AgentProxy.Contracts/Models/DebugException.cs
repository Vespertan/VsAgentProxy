using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class DebugException
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public int Code { get; set; }
    public string? Description { get; set; }
    public DateTime Utc { get; set; }
}
