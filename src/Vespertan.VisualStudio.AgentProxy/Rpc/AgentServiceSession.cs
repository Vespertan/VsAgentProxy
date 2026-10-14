using System;
using System.Threading;
using System.Threading.Tasks;
using Vespertan.VisualStudio.AgentProxy.Contracts;

namespace Vespertan.VisualStudio.AgentProxy.Rpc;

// A broker creates/disposes one session per client. Shared IDE state survives it.
internal sealed class AgentServiceSession : IAgentService
{
    private readonly AgentService _service;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public AgentServiceSession(AgentService service)
    {
        _service = service;
    }

    public Task<AgentResult<AgentInfo>> PingAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.PingAsync(token), cancellationToken);
    }

    public Task<AgentResult<AgentCapabilities>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetCapabilitiesAsync(token), cancellationToken);
    }

    public Task<AgentResult<AgentGuide>> GetDocumentationAsync(DocumentationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetDocumentationAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<DebuggerStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetStatusAsync(token), cancellationToken);
    }

    public Task<AgentResult<ProjectState>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetProjectsAsync(token), cancellationToken);
    }

    public Task<AgentResult<DocumentsResult>> GetDocumentsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetDocumentsAsync(token), cancellationToken);
    }

    public Task<AgentResult<SaveDocumentsResult>> SaveDocumentsAsync(PathsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.SaveDocumentsAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<ReloadProjectResult>> ReloadProjectAsync(ReloadProjectOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.ReloadProjectAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<StartupProjectsResult>> GetStartupProjectsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetStartupProjectsAsync(token), cancellationToken);
    }

    public Task<AgentResult<StartupProjectsResult>> SetStartupProjectsAsync(PathsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.SetStartupProjectsAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<ProjectPropertiesResult>> GetProjectPropertiesAsync(ProjectPropertiesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetProjectPropertiesAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<ConfigurationsResult>> GetConfigurationsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetConfigurationsAsync(token), cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> CancelBuildAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.CancelBuildAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AgentOperation>> GetOperationStatusAsync(OperationStatusOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetOperationStatusAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<EventsResult>> GetEventsAsync(EventsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetEventsAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<StopReasonResult>> GetStopReasonAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetStopReasonAsync(token), cancellationToken);
    }

    public Task<AgentResult<LaunchProfilesResult>> GetLaunchProfilesAsync(LaunchProfilesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetLaunchProfilesAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<LaunchProfilesResult>> SelectLaunchProfileAsync(SelectLaunchProfileOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.SelectLaunchProfileAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<SolutionLaunchProfilesResult>> GetSolutionLaunchProfilesAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetSolutionLaunchProfilesAsync(token), cancellationToken);
    }

    public Task<AgentResult<SolutionLaunchProfilesResult>> SelectSolutionLaunchProfileAsync(SelectSolutionLaunchProfileOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.SelectSolutionLaunchProfileAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<LanguageServiceStatus>> GetLanguageServiceStatusAsync(DocumentPathOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetLanguageServiceStatusAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<TerminalsResult>> GetTerminalsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetTerminalsAsync(token), cancellationToken);
    }

    public Task<AgentResult<TerminalsResult>> GetTerminalOutputAsync(TerminalOutputOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetTerminalOutputAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<ScopesResult>> GetScopesAsync(ScopesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetScopesAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<VariablesResult>> GetVariablesAsync(VariablesOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetVariablesAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<ThreadsResult>> GetThreadsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetThreadsAsync(token), cancellationToken);
    }

    public Task<AgentResult<ProcessesResult>> GetProcessesAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetProcessesAsync(token), cancellationToken);
    }

    public Task<AgentResult<SelectContextResult>> SelectContextAsync(SelectContextOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.SelectContextAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<LaunchCheckResult>> GetLaunchCheckAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetLaunchCheckAsync(token), cancellationToken);
    }

    public Task<AgentResult<StackFrameInfo[]>> GetStackTraceAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetStackTraceAsync(token), cancellationToken);
    }

    public Task<AgentResult<FrameExpressionsResult>> GetLocalsAsync(FrameExpressionsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetLocalsAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<FrameExpressionsResult>> GetArgumentsAsync(FrameExpressionsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetArgumentsAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<EvaluateResult>> EvaluateAsync(EvaluateOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.EvaluateAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo[]>> GetBreakpointsAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetBreakpointsAsync(token), cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo[]>> AddBreakpointAsync(AddBreakpointOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.AddBreakpointAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<RemoveBreakpointResult>> RemoveBreakpointAsync(BreakpointSelector options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.RemoveBreakpointAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo>> SetBreakpointEnabledAsync(SetBreakpointEnabledOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.SetBreakpointEnabledAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<BreakpointInfo[]>> SetBreakpointCriteriaAsync(SetBreakpointCriteriaOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.SetBreakpointCriteriaAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<ActiveDocumentResult>> GetActiveDocumentAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetActiveDocumentAsync(token), cancellationToken);
    }

    public Task<AgentResult<OutputResult>> GetOutputAsync(OutputOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetOutputAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<DiagnosticsResult>> GetDiagnosticsAsync(DiagnosticsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetDiagnosticsAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<DiagnosticsResult>> GetDocumentDiagnosticsAsync(DocumentDiagnosticsOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetDocumentDiagnosticsAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AgentSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.GetSnapshotAsync(token), cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> BuildAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.BuildAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> RebuildAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.RebuildAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> CleanAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.CleanAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> StartAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.StartAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> StartWithoutDebuggingAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.StartWithoutDebuggingAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<OperationAcceptedResult>> RestartAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.RestartAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StepOverAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.StepOverAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StepIntoAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.StepIntoAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StepOutAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.StepOutAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> ContinueAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.ContinueAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> BreakAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.BreakAsync(options, token), cancellationToken);
    }

    public Task<AgentResult<AcceptedResult>> StopAsync(MutationOptions options, CancellationToken cancellationToken)
    {
        return InvokeAsync(token => _service.StopAsync(options, token), cancellationToken);
    }

    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> invoke, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(AgentServiceSession));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await invoke(linked.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifetime.Cancel();
        }
    }
}
