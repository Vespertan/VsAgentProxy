namespace Vespertan.VisualStudio.AgentProxy.Client;

internal sealed record PipeInstance(int Pid, string Pipe, DateTime? StartedUtc);
