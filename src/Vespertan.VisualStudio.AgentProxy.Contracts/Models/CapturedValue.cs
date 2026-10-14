using System;

namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class CapturedValue<T>
{
    public DateTime? CapturedUtc { get; set; }
    public T? Value { get; set; }
    public ReadError? Error { get; set; }
}
