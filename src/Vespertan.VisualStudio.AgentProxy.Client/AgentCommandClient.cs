using System.Text.Json;
using System.Text.Json.Nodes;
using Vespertan.VisualStudio.AgentProxy.Contracts;

namespace Vespertan.VisualStudio.AgentProxy.Client;

internal static class AgentCommandClient
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<JsonObject> InvokeAsync(IAgentService agent, string command, JsonObject parameters, CancellationToken cancellationToken)
    {
        try
        {
            return command switch
            {
                "ping" => Serialize(await agent.PingAsync(cancellationToken).WaitAsync(cancellationToken)),
                "capabilities" => Serialize(await agent.GetCapabilitiesAsync(cancellationToken).WaitAsync(cancellationToken)),
                "documentation" => Serialize(await agent.GetDocumentationAsync(Read<DocumentationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "status" => Serialize(await agent.GetStatusAsync(cancellationToken).WaitAsync(cancellationToken)),
                "projects" => Serialize(await agent.GetProjectsAsync(cancellationToken).WaitAsync(cancellationToken)),
                "documents" => Serialize(await agent.GetDocumentsAsync(cancellationToken).WaitAsync(cancellationToken)),
                "saveDocuments" => Serialize(await agent.SaveDocumentsAsync(Read<PathsOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "projectReload" => Serialize(await agent.ReloadProjectAsync(Read<ReloadProjectOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "startupProjects" => Serialize(await agent.GetStartupProjectsAsync(cancellationToken).WaitAsync(cancellationToken)),
                "setStartupProjects" => Serialize(await agent.SetStartupProjectsAsync(Read<PathsOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "projectProperties" => Serialize(await agent.GetProjectPropertiesAsync(Read<ProjectPropertiesOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "configurations" => Serialize(await agent.GetConfigurationsAsync(cancellationToken).WaitAsync(cancellationToken)),
                "cancelBuild" => Serialize(await agent.CancelBuildAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "operationStatus" => Serialize(await agent.GetOperationStatusAsync(Read<OperationStatusOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "events" => Serialize(await agent.GetEventsAsync(Read<EventsOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "stopReason" => Serialize(await agent.GetStopReasonAsync(cancellationToken).WaitAsync(cancellationToken)),
                "launchProfiles" => Serialize(await agent.GetLaunchProfilesAsync(Read<LaunchProfilesOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "selectLaunchProfile" => Serialize(await agent.SelectLaunchProfileAsync(Read<SelectLaunchProfileOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "solutionLaunchProfiles" => Serialize(await agent.GetSolutionLaunchProfilesAsync(cancellationToken).WaitAsync(cancellationToken)),
                "selectSolutionLaunchProfile" => Serialize(await agent.SelectSolutionLaunchProfileAsync(Read<SelectSolutionLaunchProfileOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "languageServiceStatus" => Serialize(await agent.GetLanguageServiceStatusAsync(Read<DocumentPathOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "terminals" => Serialize(await agent.GetTerminalsAsync(cancellationToken).WaitAsync(cancellationToken)),
                "terminalOutput" => Serialize(await agent.GetTerminalOutputAsync(Read<TerminalOutputOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "scopes" => Serialize(await agent.GetScopesAsync(Read<ScopesOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "variables" => Serialize(await agent.GetVariablesAsync(Read<VariablesOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "threads" => Serialize(await agent.GetThreadsAsync(cancellationToken).WaitAsync(cancellationToken)),
                "processes" => Serialize(await agent.GetProcessesAsync(cancellationToken).WaitAsync(cancellationToken)),
                "selectContext" => Serialize(await agent.SelectContextAsync(Read<SelectContextOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "launchCheck" => Serialize(await agent.GetLaunchCheckAsync(cancellationToken).WaitAsync(cancellationToken)),
                "stackTrace" => Serialize(await agent.GetStackTraceAsync(cancellationToken).WaitAsync(cancellationToken)),
                "locals" => Serialize(await agent.GetLocalsAsync(Read<FrameExpressionsOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "arguments" => Serialize(await agent.GetArgumentsAsync(Read<FrameExpressionsOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "evaluate" => Serialize(await agent.EvaluateAsync(Read<EvaluateOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "breakpoints" => Serialize(await agent.GetBreakpointsAsync(cancellationToken).WaitAsync(cancellationToken)),
                "breakpointAdd" => Serialize(await agent.AddBreakpointAsync(Read<AddBreakpointOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "breakpointRemove" => Serialize(await agent.RemoveBreakpointAsync(Read<BreakpointSelector>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "breakpointSetEnabled" => Serialize(await agent.SetBreakpointEnabledAsync(Read<SetBreakpointEnabledOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "breakpointSetCriteria" => Serialize(await agent.SetBreakpointCriteriaAsync(Read<SetBreakpointCriteriaOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "activeDocument" => Serialize(await agent.GetActiveDocumentAsync(cancellationToken).WaitAsync(cancellationToken)),
                "output" => Serialize(await agent.GetOutputAsync(Read<OutputOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "diagnostics" => Serialize(await agent.GetDiagnosticsAsync(Read<DiagnosticsOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "documentDiagnostics" => Serialize(await agent.GetDocumentDiagnosticsAsync(Read<DocumentDiagnosticsOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "snapshot" => Serialize(await agent.GetSnapshotAsync(cancellationToken).WaitAsync(cancellationToken)),
                "build" => Serialize(await agent.BuildAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "rebuild" => Serialize(await agent.RebuildAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "clean" => Serialize(await agent.CleanAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "start" => Serialize(await agent.StartAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "startWithoutDebugging" => Serialize(await agent.StartWithoutDebuggingAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "restart" => Serialize(await agent.RestartAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "stepOver" => Serialize(await agent.StepOverAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "stepInto" => Serialize(await agent.StepIntoAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "stepOut" => Serialize(await agent.StepOutAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "continue" => Serialize(await agent.ContinueAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "break" => Serialize(await agent.BreakAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                "stop" => Serialize(await agent.StopAsync(Read<MutationOptions>(parameters), cancellationToken).WaitAsync(cancellationToken)),
                _ => new JsonObject { ["ok"] = false, ["errorCode"] = "unknownMethod", ["error"] = "Unknown or disallowed method: " + command }
            };
        }
        catch (JsonException exception)
        {
            return new JsonObject { ["ok"] = false, ["errorCode"] = "invalidParameters", ["error"] = exception.Message };
        }
    }

    private static T Read<T>(JsonObject parameters)
    {
        return parameters.Deserialize<T>(s_options) ?? throw new ArgumentException("Command options are required.");
    }

    private static JsonObject Serialize<T>(AgentResult<T> response)
    {
        // Provider-specific event/error data can contain Newtonsoft JSON values after RPC deserialization.
        var serializer = Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
            }
        });
        var result = JsonNode.Parse(Newtonsoft.Json.Linq.JObject.FromObject(response, serializer).ToString(Newtonsoft.Json.Formatting.None))!.AsObject();
        if (response.Ok)
        {
            result.Remove("error");
            result.Remove("errorCode");
            result.Remove("hresult");
            result.Remove("details");
        }
        else
        {
            result.Remove("result");
        }

        if (response.Replayed != true)
        {
            result.Remove("replayed");
        }

        return result;
    }
}
