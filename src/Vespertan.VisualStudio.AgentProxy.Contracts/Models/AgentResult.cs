using System.Collections.Generic;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class AgentResult<T>
{
    public bool Ok { get; set; }
    public T? Result { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
    public string? Hresult { get; set; }
    public Dictionary<string, object?>? Details { get; set; }
    public bool? Replayed { get; set; }
}
