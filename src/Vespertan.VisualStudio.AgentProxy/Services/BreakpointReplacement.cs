using System;
using System.Collections.Generic;
using EnvDTE;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Rpc;

namespace Vespertan.VisualStudio.AgentProxy.Services;

// The caller owns the UI-thread boundary; keeping replacement separate allows failure-path tests without VS.
#pragma warning disable VSTHRD010
internal static class BreakpointReplacement
{
    public static IReadOnlyList<Breakpoint> Replace(Breakpoint original, Func<Breakpoints> create, Func<string> newTag)
    {
        var tag = original.Tag;
        var enabled = original.Enabled;
        var replacements = new List<Breakpoint>();
        var originalDeleteAttempted = false;
        try
        {
            foreach (Breakpoint replacement in create())
            {
                replacements.Add(replacement);
            }

            if (replacements.Count == 0)
            {
                throw new InvalidOperationException("Visual Studio did not create a replacement breakpoint.");
            }

            var preserveOriginalId = !string.IsNullOrWhiteSpace(tag);
            foreach (var replacement in replacements)
            {
                replacement.Tag = preserveOriginalId ? tag : newTag();
                preserveOriginalId = false;
                replacement.Enabled = enabled;
            }

            // Keep the original until every replacement has been created and configured.
            originalDeleteAttempted = true;
            original.Delete();
            return replacements;
        }
        catch (Exception exception)
        {
            var cleanupErrors = new JArray();
            foreach (var replacement in replacements)
            {
                try
                {
                    replacement.Delete();
                }
                catch (Exception cleanup)
                {
                    cleanupErrors.Add(ProtocolSupport.ReadError("replacementCleanup", cleanup));
                }
            }

            throw new ProxyException("breakpointReplacementFailed", "Breakpoint replacement failed; inspect cleanup details and refresh breakpoints.",
                new JObject { ["originalDeleteAttempted"] = originalDeleteAttempted, ["cleanupErrors"] = cleanupErrors }, exception);
        }
    }
}
#pragma warning restore VSTHRD010
