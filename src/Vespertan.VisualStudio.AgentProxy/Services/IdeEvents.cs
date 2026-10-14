using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Rpc;

namespace Vespertan.VisualStudio.AgentProxy.Services;

internal sealed class IdeEvents : IVsUpdateSolutionEvents, IDisposable
{
    private readonly DTE2 _dte;
    private readonly IVsSolutionBuildManager2? _build;
    private readonly OperationRegistry _operations;
    private readonly DebuggerEvents _debuggerEvents;
    private readonly SolutionEvents _solutionEvents;
    private readonly uint _cookie;
    public long StopGeneration { get; private set; }
    public JObject StopReason { get; private set; } = new() { ["availability"] = "unavailable", ["reason"] = "Subscription started after the last stop, or no stop observed." };
    public event Action? ContextInvalidated;
    public event Action? SolutionChanged;
    public BreakpointInspection Breakpoints { get; }
    private JObject? _lastException;

    public IdeEvents(DTE2 dte, IVsSolutionBuildManager2? build, OperationRegistry operations)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this._dte = dte;
        this._build = build;
        this._operations = operations;
        Breakpoints = new BreakpointInspection(dte);
        _debuggerEvents = dte.Events.DebuggerEvents;
        _solutionEvents = dte.Events.SolutionEvents;
        _debuggerEvents.OnEnterRunMode += OnRun;
        _debuggerEvents.OnEnterBreakMode += OnBreak;
        _debuggerEvents.OnEnterDesignMode += OnDesign;
        _debuggerEvents.OnContextChanged += OnContext;
        _debuggerEvents.OnExceptionThrown += OnException;
        _debuggerEvents.OnExceptionNotHandled += OnException;
        _solutionEvents.Opened += OnSolutionChanged;
        _solutionEvents.AfterClosing += OnSolutionChanged;
        if (build != null)
        {
            ErrorHandler.ThrowOnFailure(build.AdviseUpdateSolutionEvents(this, out _cookie));
        }
    }

    public JObject Execute(string method, string command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
        {
            throw new ProxyException("ideBusy", "A build is already in progress.");
        }

        if (!_dte.Commands.Item(command).IsAvailable)
        {
            throw new ProxyException("commandUnavailable", "Visual Studio command is unavailable: " + command);
        }

        if ((method == "build" || method == "rebuild" || method == "clean") && _build == null)
        {
            throw new ProxyException("serviceUnavailable", "Build event tracking is unavailable.");
        }

        var operation = _operations.Begin(method);
        try
        {
            Invalidate();
            _dte.ExecuteCommand(command);
            if (_operations.Active == operation)
            {
                if (method == "startWithoutDebugging")
                {
                    _operations.Change("unknown", "dispatched", "The provider does not expose process-start confirmation for Ctrl+F5.");
                }
                else if ((string?)operation["state"] == "queued")
                {
                    _operations.Change("running", "awaitingEvent");
                }
            }
        }
        catch (Exception exception)
        {
            _operations.Change("failed", "dispatchFailed", exception.Message);
            throw new ProxyException("commandFailed", "Visual Studio rejected the command.", new JObject { ["operationId"] = operation["id"] }, exception);
        }

        return new JObject { ["accepted"] = true, ["operationId"] = operation["id"], ["operation"] = operation.DeepClone() };
    }

    public JObject CancelBuild()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_build == null)
        {
            throw new ProxyException("serviceUnavailable", "Build manager unavailable.");
        }

        ErrorHandler.ThrowOnFailure(_build.CanCancelUpdateSolutionConfiguration(out var canCancel));
        if (canCancel == 0)
        {
            throw new ProxyException("commandUnavailable", "There is no cancellable build.");
        }

        ErrorHandler.ThrowOnFailure(_build.CancelUpdateSolutionConfiguration());
        _operations.Emit("buildCancellationRequested");
        return new JObject { ["accepted"] = true };
    }

    public void Invalidate()
    {
        StopGeneration++;
        ContextInvalidated?.Invoke();
    }

    private void OnRun(dbgEventReason reason)
    {
        Invalidate();
        _lastException = null;
        StopReason = new JObject { ["availability"] = "unavailable", ["reason"] = "Debugger is running." };
        _operations.Emit("debuggerRunning", new JObject { ["reason"] = reason.ToString() });
        if (IsStart())
        {
            _operations.Change("succeeded", "debuggerAttached", "DTE.OnEnterRunMode");
        }
    }

    private void OnBreak(dbgEventReason reason, ref dbgExecutionAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Invalidate();
        StopReason = new JObject
        {
            ["availability"] = "available",
            ["reason"] = reason.ToString(),
            ["source"] = "DTE.OnEnterBreakMode",
            ["generation"] = StopGeneration,
            ["exceptionDetailsAvailability"] = "unavailable",
            ["capturedUtc"] = DateTime.UtcNow
        };
        if (_lastException != null)
        {
            StopReason["exception"] = _lastException.DeepClone();
            StopReason["exceptionDetailsAvailability"] = "available";
        }

        _operations.Emit("debuggerStopped", (JObject)StopReason.DeepClone());
        if (reason == dbgEventReason.dbgEventReasonBreakpoint)
        {
            try
            {
                _operations.Emit("breakpointHit", new JObject { ["breakpoints"] = Breakpoints.ObserveHits() });
            }
            catch (Exception exception)
            {
                _operations.Emit("breakpointObservationError", ProtocolSupport.ReadError("hits", exception));
            }
        }

        if (IsStart())
        {
            _operations.Change("succeeded", "debuggerAttached", "DTE.OnEnterBreakMode");
        }
    }

    private void OnDesign(dbgEventReason reason)
    {
        Invalidate();
        StopReason = new JObject { ["availability"] = "unavailable", ["reason"] = "Debugger session ended." };
        _operations.Emit("debuggerEnded", new JObject { ["reason"] = reason.ToString() });
        if ((string?)_operations.Active?["method"] == "restart")
        {
            _operations.Change("running", "restarting", "Old debugger session ended; awaiting the new session.");
        }
        else if (IsStart())
        {
            _operations.Change("unknown", "endedBeforeConfirmation", "DTE entered design mode before a run/break event confirmed launch.");
        }
    }

    private bool IsStart() => (string?)_operations.Active?["method"] is "start" or "restart";
    private void OnContext(Process process, Program program, EnvDTE.Thread thread, StackFrame frame) => Invalidate();

    private void OnException(string type, string name, int code, string description, ref dbgExceptionAction action)
    {
        _lastException = new JObject { ["type"] = type, ["name"] = name, ["code"] = code, ["description"] = description, ["utc"] = DateTime.UtcNow };
        _operations.Emit("exception", (JObject)_lastException.DeepClone());
    }

    private void OnSolutionChanged()
    {
        Invalidate();
        _operations.Emit("solutionChanged");
        SolutionChanged?.Invoke();
    }

    public int UpdateSolution_Begin(ref int pfCancelUpdate)
    {
        _operations.Emit("buildBegin", new JObject { ["operationId"] = _operations.Active?["id"], ["source"] = _operations.Active == null ? "external" : "commandWindow" });
        _operations.Change("running", "building");
        return VSConstants.S_OK;
    }

    public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
    {
        _operations.Emit("buildDone", new JObject { ["succeeded"] = fSucceeded != 0, ["cancelled"] = fCancelCommand != 0, ["operationId"] = _operations.Active?["id"] });
        if (_operations.Active != null)
        {
            if (fCancelCommand != 0)
            {
                _operations.Change("cancelled", "buildCancelled", "IVsUpdateSolutionEvents.UpdateSolution_Done");
            }
            else if (fSucceeded == 0)
            {
                _operations.Change("failed", "buildFailed", "IVsUpdateSolutionEvents.UpdateSolution_Done");
            }
            else if (IsStart())
            {
                _operations.Change("running", "launching", "Build succeeded; awaiting debugger event.");
            }
            else
            {
                _operations.Change("succeeded", "completed", "IVsUpdateSolutionEvents.UpdateSolution_Done");
            }
        }

        return VSConstants.S_OK;
    }

    public int UpdateSolution_StartUpdate(ref int pfCancelUpdate) => VSConstants.S_OK;

    public int UpdateSolution_Cancel()
    {
        _operations.Emit("buildCancel");
        _operations.Change("cancelled", "buildCancelled", "IVsUpdateSolutionEvents.UpdateSolution_Cancel");
        return VSConstants.S_OK;
    }

    public int OnActiveProjectCfgChange(IVsHierarchy hierarchy)
    {
        _operations.Emit("configurationChanged");
        return VSConstants.S_OK;
    }

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_cookie != 0)
        {
            _build?.UnadviseUpdateSolutionEvents(_cookie);
        }

        _debuggerEvents.OnEnterRunMode -= OnRun;
        _debuggerEvents.OnEnterBreakMode -= OnBreak;
        _debuggerEvents.OnEnterDesignMode -= OnDesign;
        _debuggerEvents.OnContextChanged -= OnContext;
        _debuggerEvents.OnExceptionThrown -= OnException;
        _debuggerEvents.OnExceptionNotHandled -= OnException;
        _solutionEvents.Opened -= OnSolutionChanged;
        _solutionEvents.AfterClosing -= OnSolutionChanged;
    }
}
