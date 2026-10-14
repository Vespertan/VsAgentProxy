namespace Vespertan.VisualStudio.AgentProxy.Contracts;

public sealed class ReadError
{
    public string? Field { get; set; }
    public string? Message { get; set; }
    public string? Hresult { get; set; }
}
