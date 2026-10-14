using System;
using System.Collections.Generic;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentEvent
{
    public long Sequence { get; set; }
    public string? SessionId { get; set; }
    public DateTime Utc { get; set; }
    public string? Kind { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
}
