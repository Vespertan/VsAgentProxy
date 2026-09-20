using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class IdeEvents : IVsUpdateSolutionEvents, IDisposable
{
    private readonly DTE2 dte;
    private readonly IVsSolutionBuildManager2? build;
    private readonly OperationRegistry operations;
    private readonly DebuggerEvents debuggerEvents;
    private readonly SolutionEvents solutionEvents;
    private uint cookie;
    public long StopGeneration { get; private set; }
    public JObject StopReason { get; private set; } = new() { ["availability"] = "unavailable", ["reason"] = "Subscription started after the last stop, or no stop observed." };
    public event Action? ContextInvalidated;
    public event Action? SolutionChanged;
    public BreakpointInspection Breakpoints { get; }
    private JObject? lastException;

    public IdeEvents(DTE2 dte, IVsSolutionBuildManager2? build, OperationRegistry operations)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.dte = dte; this.build = build; this.operations = operations;
        Breakpoints = new BreakpointInspection(dte);
        debuggerEvents = dte.Events.DebuggerEvents;
        solutionEvents = dte.Events.SolutionEvents;
        debuggerEvents.OnEnterRunMode += OnRun;
        debuggerEvents.OnEnterBreakMode += OnBreak;
        debuggerEvents.OnEnterDesignMode += OnDesign;
        debuggerEvents.OnContextChanged += OnContext;
        debuggerEvents.OnExceptionThrown += OnException;
        debuggerEvents.OnExceptionNotHandled += OnException;
        solutionEvents.Opened += OnSolutionChanged;
        solutionEvents.AfterClosing += OnSolutionChanged;
        if (build != null) ErrorHandler.ThrowOnFailure(build.AdviseUpdateSolutionEvents(this, out cookie));
    }

    public JObject Execute(string method, string command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress) throw new ProxyException("ideBusy", "A build is already in progress.");
        if (!dte.Commands.Item(command).IsAvailable) throw new ProxyException("commandUnavailable", "Visual Studio command is unavailable: " + command);
        if ((method == "build" || method == "rebuild" || method == "clean") && build == null) throw new ProxyException("serviceUnavailable", "Build event tracking is unavailable.");
        var operation = operations.Begin(method);
        try
        {
            Invalidate();
            dte.ExecuteCommand(command);
            if (operations.Active == operation)
            {
                if (method == "startWithoutDebugging") operations.Change("unknown", "dispatched", "The provider does not expose process-start confirmation for Ctrl+F5.");
                else if ((string?)operation["state"] == "queued") operations.Change("running", "awaitingEvent");
            }
        }
        catch (Exception exception)
        {
            operations.Change("failed", "dispatchFailed", exception.Message);
            throw new ProxyException("commandFailed", "Visual Studio rejected the command.", new JObject { ["operationId"] = operation["id"] }, exception);
        }
        return new JObject { ["accepted"] = true, ["operationId"] = operation["id"], ["operation"] = operation.DeepClone() };
    }

    public JObject CancelBuild()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (build == null) throw new ProxyException("serviceUnavailable", "Build manager unavailable.");
        ErrorHandler.ThrowOnFailure(build.CanCancelUpdateSolutionConfiguration(out var canCancel));
        if (canCancel == 0) throw new ProxyException("commandUnavailable", "There is no cancellable build.");
        ErrorHandler.ThrowOnFailure(build.CancelUpdateSolutionConfiguration());
        operations.Emit("buildCancellationRequested");
        return new JObject { ["accepted"] = true };
    }

    public void Invalidate() { StopGeneration++; ContextInvalidated?.Invoke(); }

    private void OnRun(dbgEventReason reason)
    {
        Invalidate();
        lastException = null;
        StopReason = new JObject { ["availability"] = "unavailable", ["reason"] = "Debugger is running." };
        operations.Emit("debuggerRunning", new JObject { ["reason"] = reason.ToString() });
        if (IsStart()) operations.Change("succeeded", "debuggerAttached", "DTE.OnEnterRunMode");
    }
    private void OnBreak(dbgEventReason reason, ref dbgExecutionAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Invalidate();
        StopReason = new JObject { ["availability"] = "available", ["reason"] = reason.ToString(), ["source"] = "DTE.OnEnterBreakMode", ["generation"] = StopGeneration,
            ["exceptionDetailsAvailability"] = "unavailable", ["capturedUtc"] = DateTime.UtcNow };
        if (lastException != null) { StopReason["exception"] = lastException.DeepClone(); StopReason["exceptionDetailsAvailability"] = "available"; }
        operations.Emit("debuggerStopped", (JObject)StopReason.DeepClone());
        if (reason == dbgEventReason.dbgEventReasonBreakpoint)
        {
            try { operations.Emit("breakpointHit", new JObject { ["breakpoints"] = Breakpoints.ObserveHits() }); }
            catch (Exception exception) { operations.Emit("breakpointObservationError", ProtocolSupport.ReadError("hits", exception)); }
        }
        if (IsStart()) operations.Change("succeeded", "debuggerAttached", "DTE.OnEnterBreakMode");
    }
    private void OnDesign(dbgEventReason reason)
    {
        Invalidate();
        StopReason = new JObject { ["availability"] = "unavailable", ["reason"] = "Debugger session ended." };
        operations.Emit("debuggerEnded", new JObject { ["reason"] = reason.ToString() });
        if ((string?)operations.Active?["method"] == "restart") operations.Change("running", "restarting", "Old debugger session ended; awaiting the new session.");
        else if (IsStart()) operations.Change("unknown", "endedBeforeConfirmation", "DTE entered design mode before a run/break event confirmed launch.");
    }
    private bool IsStart() => (string?)operations.Active?["method"] is "start" or "restart";
    private void OnContext(Process process, Program program, EnvDTE.Thread thread, StackFrame frame) => Invalidate();
    private void OnException(string type, string name, int code, string description, ref dbgExceptionAction action)
    {
        lastException = new JObject { ["type"] = type, ["name"] = name, ["code"] = code, ["description"] = description, ["utc"] = DateTime.UtcNow };
        operations.Emit("exception", (JObject)lastException.DeepClone());
    }
    private void OnSolutionChanged()
    {
        Invalidate();
        operations.Emit("solutionChanged");
        SolutionChanged?.Invoke();
    }
    public int UpdateSolution_Begin(ref int pfCancelUpdate)
    {
        operations.Emit("buildBegin", new JObject { ["operationId"] = operations.Active?["id"], ["source"] = operations.Active == null ? "external" : "commandWindow" });
        operations.Change("running", "building"); return VSConstants.S_OK;
    }
    public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
    {
        operations.Emit("buildDone", new JObject { ["succeeded"] = fSucceeded != 0, ["cancelled"] = fCancelCommand != 0, ["operationId"] = operations.Active?["id"] });
        if (operations.Active != null)
        {
            if (fCancelCommand != 0) operations.Change("cancelled", "buildCancelled", "IVsUpdateSolutionEvents.UpdateSolution_Done");
            else if (fSucceeded == 0) operations.Change("failed", "buildFailed", "IVsUpdateSolutionEvents.UpdateSolution_Done");
            else if (IsStart()) operations.Change("running", "launching", "Build succeeded; awaiting debugger event.");
            else operations.Change("succeeded", "completed", "IVsUpdateSolutionEvents.UpdateSolution_Done");
        }
        return VSConstants.S_OK;
    }
    public int UpdateSolution_StartUpdate(ref int pfCancelUpdate) => VSConstants.S_OK;
    public int UpdateSolution_Cancel() { operations.Emit("buildCancel"); operations.Change("cancelled", "buildCancelled", "IVsUpdateSolutionEvents.UpdateSolution_Cancel"); return VSConstants.S_OK; }
    public int OnActiveProjectCfgChange(IVsHierarchy hierarchy) { operations.Emit("configurationChanged"); return VSConstants.S_OK; }
    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (cookie != 0) build?.UnadviseUpdateSolutionEvents(cookie);
        debuggerEvents.OnEnterRunMode -= OnRun; debuggerEvents.OnEnterBreakMode -= OnBreak; debuggerEvents.OnEnterDesignMode -= OnDesign;
        debuggerEvents.OnContextChanged -= OnContext; debuggerEvents.OnExceptionThrown -= OnException; debuggerEvents.OnExceptionNotHandled -= OnException;
        solutionEvents.Opened -= OnSolutionChanged; solutionEvents.AfterClosing -= OnSolutionChanged;
    }
}
