using System;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class ProxyException : Exception
{
    public string Code { get; }
    public JObject Details { get; }

    public ProxyException(string code, string message, JObject? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Details = details ?? [];
        if (inner != null)
        {
            HResult = inner.HResult;
        }
    }
}
