using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

// Passive debugger observer. Never resumes synchronous events or changes engine state.
internal sealed class EngineEvents : IDebugEventCallback2, IDisposable
{
    private readonly IVsDebugger? debugger;
    private readonly JoinableTaskFactory factory;
    private readonly OperationRegistry operations;
    private bool subscribed;
    private volatile bool disposed;
    public EngineEvents(IVsDebugger? debugger, JoinableTaskFactory factory, OperationRegistry operations)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.debugger = debugger; this.factory = factory; this.operations = operations;
        if (debugger != null) subscribed = debugger.AdviseDebugEventCallback(this) >= 0;
    }
    public int Event(IDebugEngine2 engine, IDebugProcess2 process, IDebugProgram2 program, IDebugThread2 thread, IDebugEvent2 debugEvent, ref Guid eventId, uint attributes)
    {
        if (disposed) return VSConstants.S_OK;
        try
        {
            JObject? data = null;
            string? kind = null;
            if (debugEvent is IDebugErrorEvent2 error)
            {
                var type = new enum_MESSAGETYPE[1];
                ErrorHandler.ThrowOnFailure(error.GetErrorMessage(type, out var message, out var reason, out _, out _, out _));
                kind = "engineError"; data = new JObject { ["message"] = message, ["hresult"] = "0x" + reason.ToString("X8"), ["messageType"] = type[0].ToString() };
            }
            else if (debugEvent is IDebugBreakpointErrorEvent2 failed)
            {
                ErrorHandler.ThrowOnFailure(failed.GetErrorBreakpoint(out var breakpoint));
                ErrorHandler.ThrowOnFailure(breakpoint.GetPendingBreakpoint(out var pending));
                data = Requested(pending);
                ErrorHandler.ThrowOnFailure(breakpoint.GetBreakpointResolution(out var resolution));
                var info = new BP_ERROR_RESOLUTION_INFO[1];
                ErrorHandler.ThrowOnFailure(resolution.GetResolutionInfo(enum_BPERESI_FIELDS.BPERESI_MESSAGE | enum_BPERESI_FIELDS.BPERESI_TYPE, info));
                data["message"] = info[0].bstrMessage; data["errorType"] = info[0].dwType.ToString(); kind = "breakpointBindingError";
            }
            else if (debugEvent is IDebugBreakpointBoundEvent2 bound)
            {
                ErrorHandler.ThrowOnFailure(bound.GetPendingBreakpoint(out var pending));
                data = Requested(pending);
                ErrorHandler.ThrowOnFailure(bound.EnumBoundBreakpoints(out var enumerator));
                var locations = new JArray();
                var values = new IDebugBoundBreakpoint2[1];
                uint fetched = 0;
                while (locations.Count < 100 && enumerator.Next(1, values, ref fetched) >= 0 && fetched != 0)
                    locations.Add(Resolved(values[0]));
                data["locations"] = locations; kind = "breakpointBound";
            }
            else if (debugEvent is IDebugBreakpointUnboundEvent2 unbound)
            {
                ErrorHandler.ThrowOnFailure(unbound.GetBreakpoint(out var breakpoint));
                ErrorHandler.ThrowOnFailure(breakpoint.GetPendingBreakpoint(out var pending));
                data = Requested(pending);
                var reason = new enum_BP_UNBOUND_REASON[1];
                ErrorHandler.ThrowOnFailure(unbound.GetReason(reason));
                data["reason"] = reason[0].ToString(); kind = "breakpointUnbound";
            }
            if (data != null && kind != null)
            {
                if (engine != null && engine.GetEngineId(out var id) >= 0) data["engineId"] = id.ToString("D");
                data["source"] = "IDebugEventCallback2"; data["observedUtc"] = DateTime.UtcNow;
                var capturedKind = kind;
                var capturedData = data;
                factory.RunAsync(async () =>
                {
                    await factory.SwitchToMainThreadAsync();
                    if (!disposed) operations.Emit(capturedKind, capturedData);
                }).Task.Forget();
            }
        }
        catch (Exception exception) { ActivityLog.TryLogWarning(nameof(VsCodexProxy), "Debug event observation failed: " + exception.Message); }
        return VSConstants.S_OK;
    }

    private static JObject Requested(IDebugPendingBreakpoint2 pending)
    {
        var result = new JObject { ["requestedLocationAvailability"] = "unavailable", ["correlation"] = "engineEventLocationOnly" };
        ErrorHandler.ThrowOnFailure(pending.GetBreakpointRequest(out var request));
        var type = new enum_BP_LOCATION_TYPE[1];
        ErrorHandler.ThrowOnFailure(request.GetLocationType(type));
        if (type[0] != enum_BP_LOCATION_TYPE.BPLT_CODE_FILE_LINE) return result;
        var info = new BP_REQUEST_INFO[1];
        ErrorHandler.ThrowOnFailure(request.GetRequestInfo(enum_BPREQI_FIELDS.BPREQI_BPLOCATION, info));
        var location = info[0].bpLocation;
        try
        {
            if (location.unionmember2 != IntPtr.Zero && Marshal.GetObjectForIUnknown(location.unionmember2) is IDebugDocumentPosition2 position)
            {
                ErrorHandler.ThrowOnFailure(position.GetFileName(out var file));
                var start = new TEXT_POSITION[1]; var end = new TEXT_POSITION[1];
                ErrorHandler.ThrowOnFailure(position.GetRange(start, end));
                result["file"] = file; result["line"] = start[0].dwLine + 1; result["column"] = start[0].dwColumn + 1;
                result["requestedLocationAvailability"] = "available";
            }
        }
        finally
        {
            if (location.unionmember1 != IntPtr.Zero) Marshal.FreeBSTR(location.unionmember1);
            if (location.unionmember2 != IntPtr.Zero) Marshal.Release(location.unionmember2);
        }
        return result;
    }
    private static JObject Resolved(IDebugBoundBreakpoint2 breakpoint)
    {
        var result = new JObject { ["sourceMapAvailability"] = "unavailable" };
        if (breakpoint.GetHitCount(out var hits) >= 0) result["engineReportedHits"] = hits;
        ErrorHandler.ThrowOnFailure(breakpoint.GetBreakpointResolution(out var resolution));
        var types = new enum_BP_TYPE[1];
        ErrorHandler.ThrowOnFailure(resolution.GetBreakpointType(types));
        if (types[0] != enum_BP_TYPE.BPT_CODE) return result;
        var info = new BP_RESOLUTION_INFO[1];
        ErrorHandler.ThrowOnFailure(resolution.GetResolutionInfo(enum_BPRESI_FIELDS.BPRESI_BPRESLOCATION, info));
        var pointer = info[0].bpResLocation.unionmember1;
        try
        {
            if (pointer != IntPtr.Zero && Marshal.GetObjectForIUnknown(pointer) is IDebugCodeContext2 context && context.GetDocumentContext(out var document) >= 0 && document != null)
            {
                if (document.GetName(enum_GETNAME_TYPE.GN_FILENAME, out var file) >= 0) result["file"] = file;
                var start = new TEXT_POSITION[1]; var end = new TEXT_POSITION[1];
                if (document.GetStatementRange(start, end) >= 0) { result["line"] = start[0].dwLine + 1; result["column"] = start[0].dwColumn + 1; }
            }
        }
        finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
        return result;
    }
    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread(); disposed = true;
        if (subscribed) { debugger?.UnadviseDebugEventCallback(this); subscribed = false; }
    }
}
