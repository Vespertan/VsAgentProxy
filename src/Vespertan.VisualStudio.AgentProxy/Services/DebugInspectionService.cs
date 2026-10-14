using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Rpc;

namespace Vespertan.VisualStudio.AgentProxy.Services;

internal sealed class DebugInspectionService
{
    private readonly DTE2 _dte;
    private readonly IdeEvents _events;
    private readonly Dictionary<string, Expressions> _references = [];

    public DebugInspectionService(DTE2 dte, IdeEvents events)
    {
        this._dte = dte;
        this._events = events;
        events.ContextInvalidated += () => _references.Clear();
    }

    public void RequireBreak()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
        {
            throw new ProxyException("requiresBreakMode", "Debugger inspection requires break mode.");
        }
    }

    private string Reference(Expressions expressions)
    {
        if (_references.Count >= 2048)
        {
            throw new ProxyException("referenceLimit", "Variable reference limit reached for this stop.");
        }

        var id = _events.StopGeneration + ":" + Guid.NewGuid().ToString("N");
        _references.Add(id, expressions);
        return id;
    }

    public JObject Scopes(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        RequireBreak();
        var index = ProtocolSupport.Integer(parameters, "frameIndex", 0, 0, 10000);
        var frames = _dte.Debugger.CurrentThread.StackFrames;
        if (index >= frames.Count)
        {
            throw new ArgumentException("frameIndex is out of range.");
        }

        var frame = frames.Item(index + 1);
        return new JObject
        {
            ["frameIndex"] = index,
            ["generation"] = _events.StopGeneration,
            ["scopes"] = new JArray(new JObject { ["name"] = "Locals", ["reference"] = Reference(frame.Locals) },
                new JObject { ["name"] = "Arguments", ["reference"] = Reference(frame.Arguments) }),
            ["source"] = "DTE",
            ["evaluationPolicy"] = "No GetExpression calls. DTE enumeration can invoke adapter evaluation; side-effect-free enumeration is not guaranteed."
        };
    }

    public JObject Variables(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var id = ProtocolSupport.String(parameters, "reference") ?? throw new ArgumentException("reference is required.");
        if (!_references.TryGetValue(id, out var expressions))
        {
            throw new ProxyException("staleReference", "Unknown or expired variable reference. Request scopes at the current stop.");
        }

        RequireBreak();
        var offset = ProtocolSupport.Integer(parameters, "offset", 0, 0, int.MaxValue);
        var count = ProtocolSupport.Integer(parameters, "count", 100, 1, 500);
        var total = expressions.Count;
        var items = new JArray();
        for (var i = Math.Min(offset, total); i < total && items.Count < count; i++)
        {
            var expression = expressions.Item(i + 1);
            var item = new JObject();
            var errors = new JArray();
            try
            {
                item["name"] = expression.Name;
                item["type"] = expression.Type;
                item["value"] = expression.Value;
                item["isValid"] = expression.IsValidValue;
            }
            catch (Exception exception)
            {
                errors.Add(ProtocolSupport.ReadError("value", exception));
            }

            try
            {
                var children = expression.DataMembers;
                item["hasChildren"] = children.Count > 0;
                item["childCount"] = children.Count;
                // Container scopes need not have a valid scalar value.
                item["reference"] = children.Count > 0 ? Reference(children) : null;
            }
            catch (Exception exception)
            {
                item["hasChildren"] = null;
                errors.Add(ProtocolSupport.ReadError("children", exception));
            }

            item["readErrors"] = errors;
            items.Add(item);
        }

        return new JObject
        {
            ["items"] = items,
            ["offset"] = Math.Min(offset, total),
            ["totalCount"] = total,
            ["hasMore"] = offset < total - items.Count,
            ["generation"] = _events.StopGeneration,
            ["source"] = "DTE.DataMembers"
        };
    }

    public JObject Processes()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var items = new JArray();
        foreach (Process process in _dte.Debugger.DebuggedProcesses)
        {
            items.Add(new JObject { ["id"] = process.ProcessID, ["name"] = process.Name, ["current"] = process.ProcessID == _dte.Debugger.CurrentProcess?.ProcessID });
        }

        return new JObject { ["processes"] = items, ["scope"] = "debuggedProcesses" };
    }

    public JObject Threads()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        RequireBreak();
        var items = new JArray();
        foreach (Thread thread in _dte.Debugger.CurrentProgram.Threads)
        {
            items.Add(new JObject { ["id"] = thread.ID, ["name"] = thread.Name, ["current"] = thread.ID == _dte.Debugger.CurrentThread?.ID, ["frozen"] = thread.IsFrozen });
        }

        return new JObject { ["threads"] = items, ["scope"] = "currentProgram" };
    }

    public JObject SelectContext(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        RequireBreak();
        var threadId = ProtocolSupport.Integer(parameters, "threadId", -1, 0, int.MaxValue);
        if (threadId < 0)
        {
            throw new ArgumentException("threadId is required.");
        }

        Thread? selected = null;
        foreach (Thread thread in _dte.Debugger.CurrentProgram.Threads)
        {
            if (thread.ID == threadId)
            {
                selected = thread;
            }
        }

        if (selected == null)
        {
            throw new ProxyException("threadNotFound", "Thread does not belong to the current program.");
        }

        var frame = ProtocolSupport.Integer(parameters, "frameIndex", 0, 0, 10000);
        if (frame >= selected.StackFrames.Count)
        {
            throw new ArgumentException("frameIndex is out of range.");
        }

        _events.Invalidate();
        _dte.Debugger.CurrentThread = selected;
        _dte.Debugger.CurrentStackFrame = selected.StackFrames.Item(frame + 1);
        return new JObject { ["threadId"] = _dte.Debugger.CurrentThread.ID, ["frameIndex"] = frame, ["generation"] = _events.StopGeneration };
    }
}
