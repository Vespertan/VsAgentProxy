using System;
using System.Collections.Generic;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class BreakpointInspection
{
    private readonly DTE2 dte;
    private readonly Dictionary<Breakpoint, int> hits = new();
    private readonly DateTime since = DateTime.UtcNow;
    public BreakpointInspection(DTE2 dte) => this.dte = dte;

    public JArray ObserveHits()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new JArray();
        var seen = new HashSet<Breakpoint>();
        foreach (Breakpoint breakpoint in dte.Debugger.AllBreakpointsLastHit)
        {
            var parent = Root(breakpoint);
            if (!seen.Add(parent)) continue;
            if (hits.Count >= 2048 && !hits.ContainsKey(parent)) continue;
            hits.TryGetValue(parent, out var count);
            hits[parent] = count + 1;
            result.Add(new JObject { ["tag"] = parent.Tag, ["file"] = breakpoint.File, ["line"] = breakpoint.FileLine, ["observedHits"] = count + 1 });
        }
        return result;
    }
    private static Breakpoint Root(Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        for (var i = 0; i < 8 && breakpoint.Type == dbgBreakpointType.dbgBreakpointTypeBound; i++)
        { var parent = breakpoint.Parent; if (parent == null || parent == breakpoint) break; breakpoint = parent; }
        return breakpoint;
    }
    public void Enrich(Breakpoint breakpoint, JObject item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var errors = new JArray();
        item["currentHitsAvailability"] = "reportedByDte";
        item["currentHitsReliability"] = "unverifiedAdapterCounter";
        try { item["currentHits"] = breakpoint.CurrentHits; }
        catch (Exception exception) { item["currentHits"] = null; item["currentHitsAvailability"] = "unavailable"; errors.Add(ProtocolSupport.ReadError("currentHits", exception)); }
        hits.TryGetValue(Root(breakpoint), out var observed);
        item["observedHits"] = observed;
        item["observedSinceUtc"] = since;
        item["observedHitsSource"] = "DTE.AllBreakpointsLastHit; stops observed since subscription";
        var locations = new JArray();
        var bound = false;
        try
        {
            bound = breakpoint.Type == dbgBreakpointType.dbgBreakpointTypeBound;
            foreach (Breakpoint child in breakpoint.Children)
            {
                if (child.Type != dbgBreakpointType.dbgBreakpointTypeBound) continue;
                bound = true;
                locations.Add(new JObject { ["file"] = child.File, ["line"] = child.FileLine, ["column"] = child.FileColumn,
                    ["function"] = child.FunctionName, ["language"] = child.Language });
            }
            item["bindingState"] = bound ? "bound" : dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode ? "pending" : "unknown";
        }
        catch (Exception exception) { item["bindingState"] = "unknown"; errors.Add(ProtocolSupport.ReadError("binding", exception)); }
        item["boundLocations"] = locations;
        item["bindingSource"] = "DTE.Breakpoint.Type/Children";
        item["bindingErrorAvailability"] = "unavailable";
        item["sourceMapAvailability"] = "unavailable";
        item["readErrors"] = errors;
    }
}
