using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Contracts;

namespace Vespertan.VisualStudio.AgentProxy.Rpc;

internal sealed class AgentRequestExecutor : IDisposable
{
    private readonly MutationCache _mutations = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    // Keep mutations serialized across clients so idempotency and IDE state checks share one boundary.
    public async Task<AgentResult<T>> ExecuteAsync<T>(string operation, object? options,
        Func<JObject, CancellationToken, Task<object>> invoke, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _requestGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var serializer = AgentContractSerializer.Create();
            var parameters = options == null ? new JObject() : JObject.FromObject(options, serializer);
            var request = new JObject { ["method"] = operation, ["params"] = parameters };
            var key = ProtocolSupport.String(parameters, "idempotencyKey");
            if (key?.Length > 200)
            {
                throw new ArgumentException("idempotencyKey must be at most 200 characters.");
            }

            if (key != null && _mutations.Find(key, request) is JObject replay)
            {
                return replay.ToObject<AgentResult<T>>(serializer)!;
            }

            AgentResult<T> response;
            try
            {
                var result = await invoke(parameters, linked.Token).ConfigureAwait(false);
                response = new AgentResult<T> { Ok = true, Result = JToken.FromObject(result).ToObject<T>(serializer) };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                response = AgentContractSerializer.Failure<T>(exception);
            }

            if (key != null)
            {
                _mutations.Store(key, request, JObject.FromObject(response, serializer));
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return AgentContractSerializer.Failure<T>(exception);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
    }
}
