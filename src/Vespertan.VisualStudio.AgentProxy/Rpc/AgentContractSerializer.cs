using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Vespertan.VisualStudio.AgentProxy.Contracts;

namespace Vespertan.VisualStudio.AgentProxy.Rpc;

internal static class AgentContractSerializer
{
    public static JsonSerializer Create()
    {
        return JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
            },
            NullValueHandling = NullValueHandling.Ignore
        });
    }

    public static AgentResult<T> Failure<T>(Exception exception)
    {
        return new AgentResult<T>
        {
            Ok = false,
            Error = exception.Message,
            ErrorCode = exception is ProxyException proxy ? proxy.Code : exception is ArgumentException ? "invalidParameters" : "ideOperationFailed",
            Hresult = "0x" + exception.HResult.ToString("X8"),
            Details = exception is ProxyException detailed ? detailed.Details.ToObject<Dictionary<string, object?>>() : new Dictionary<string, object?>()
        };
    }
}
