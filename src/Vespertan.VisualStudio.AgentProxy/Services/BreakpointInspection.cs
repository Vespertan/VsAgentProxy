using System;
using System.Collections.Generic;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Rpc;

namespace Vespertan.VisualStudio.AgentProxy.Services;

internal sealed class BreakpointInspection
{
    private readonly DTE2 _dte;
    private readonly Dictionary<Breakpoint, int> _hits = [];
    private readonly DateTime _since = DateTime.UtcNow;
    public BreakpointInspection(DTE2 dte) => this._dte = dte;

    public JArray ObserveHits()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new JArray();
        var seen = new HashSet<Breakpoint>();
        foreach (Breakpoint breakpoint in _dte.Debugger.AllBreakpointsLastHit)
        {
            var parent = Root(breakpoint);
            if (!seen.Add(parent))
            {
                continue;
            }

            if (_hits.Count >= 2048 && !_hits.ContainsKey(parent))
            {
                continue;
            }

            _hits.TryGetValue(parent, out var count);
            _hits[parent] = count + 1;
            result.Add(new JObject { ["tag"] = parent.Tag, ["file"] = breakpoint.File, ["line"] = breakpoint.FileLine, ["observedHits"] = count + 1 });
        }

        return result;
    }

    private static Breakpoint Root(Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        for (var i = 0; i < 8 && breakpoint.Type == dbgBreakpointType.dbgBreakpointTypeBound; i++)
        {
            var parent = breakpoint.Parent;
            if (parent == null || parent == breakpoint)
            {
                break;
            }

            breakpoint = parent;
        }

        return breakpoint;
    }

    public void Enrich(Breakpoint breakpoint, JObject item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var errors = new JArray();
        item["currentHitsAvailability"] = "reportedByDte";
        item["currentHitsReliability"] = "unverifiedAdapterCounter";
        try
        {
            item["currentHits"] = breakpoint.CurrentHits;
        }
        catch (Exception exception)
        {
            item["currentHits"] = null;
            item["currentHitsAvailability"] = "unavailable";
            errors.Add(ProtocolSupport.ReadError("currentHits", exception));
        }

        _hits.TryGetValue(Root(breakpoint), out var observed);
        item["observedHits"] = observed;
        item["observedSinceUtc"] = _since;
        item["observedHitsSource"] = "DTE.AllBreakpointsLastHit; stops observed since subscription";
        var locations = new JArray();
        try
        {
            var bound = breakpoint.Type == dbgBreakpointType.dbgBreakpointTypeBound;
            foreach (Breakpoint child in breakpoint.Children)
            {
                if (child.Type != dbgBreakpointType.dbgBreakpointTypeBound)
                {
                    continue;
                }

                bound = true;
                locations.Add(new JObject
                {
                    ["file"] = child.File,
                    ["line"] = child.FileLine,
                    ["column"] = child.FileColumn,
                    ["function"] = child.FunctionName,
                    ["language"] = child.Language
                });
            }

            item["bindingState"] = bound ? "bound" : _dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode ? "pending" : "unknown";
        }
        catch (Exception exception)
        {
            item["bindingState"] = "unknown";
            errors.Add(ProtocolSupport.ReadError("binding", exception));
        }

        item["boundLocations"] = locations;
        item["bindingSource"] = "DTE.Breakpoint.Type/Children";
        item["bindingErrorAvailability"] = "unavailable";
        item["sourceMapAvailability"] = "unavailable";
        item["readErrors"] = errors;
    }
}
