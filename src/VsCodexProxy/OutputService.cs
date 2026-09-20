using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Newtonsoft.Json.Linq;

namespace VsCodexProxy;

internal sealed class OutputService
{
    private readonly DTE2 dte;
    private readonly IVsOutputWindow? outputWindow;

    public OutputService(DTE2 dte, IVsOutputWindow? outputWindow)
    {
        this.dte = dte;
        this.outputWindow = outputWindow;
    }

    public object Read(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var name = ProtocolSupport.String(parameters, "pane");
        var id = ProtocolSupport.String(parameters, "paneId");
        if (name != null && id != null) throw new ArgumentException("Use either pane or paneId, not both.");
        Guid requestedId = default;
        if (id != null && !Guid.TryParse(id, out requestedId)) throw new ArgumentException("paneId must be a GUID.");
        var count = ProtocolSupport.Integer(parameters, parameters["count"] != null ? "count" : "maxChars", 20000, 1, 200000);
        int? offset = parameters["offset"] == null ? null : ProtocolSupport.Integer(parameters, "offset", 0, 0, int.MaxValue);
        var panes = new List<OutputWindowPane>();
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes) panes.Add(pane);
        if (name == null && id == null)
            return new
            {
                panes = panes.Select(p => { ThreadHelper.ThrowIfNotOnUIThread(); return p.Name; }).ToArray(),
                paneDetails = panes.Select(p => { ThreadHelper.ThrowIfNotOnUIThread(); return new { name = p.Name, id = NormalizeId(p.Guid) }; }).ToArray()
            };

        var selected = panes.FirstOrDefault(p => { ThreadHelper.ThrowIfNotOnUIThread(); return id != null
            ? Guid.TryParse(p.Guid, out var guid) && guid == requestedId
            : string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase); });
        if (selected == null)
            throw new ProxyException("outputPaneNotFound", "Output pane not found: " + (id ?? name));

        try { return ReadPane(selected, offset, count); }
        catch (ProxyException exception) when (exception.Code == "outputReadFailed" && exception.InnerException?.HResult == unchecked((int)0x80004005))
        {
            // VS creates some pane buffers lazily on first activation. An empty
            // write does not initialize them. Retry after activation, never infer
            // emptiness from E_FAIL, and restore the user's UI even on failure.
            return ReadAfterInitialization(selected, offset, count);
        }
    }

    private object ReadAfterInitialization(OutputWindowPane selected, int? offset, int count)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var output = dte.ToolWindows.OutputWindow;
        var previousPane = output.ActivePane;
        var previousWindow = dte.ActiveWindow;
        var outputWasVisible = output.Parent.Visible;
        var restorationErrors = new JArray();
        JObject? result = null;
        ProxyException? failure = null;
        try
        {
            selected.Activate();
            result = JObject.FromObject(ReadPane(selected, offset, count));
            result["paneInitialized"] = true;
            result["previousPaneAvailable"] = previousPane != null;
            return result;
        }
        catch (Exception exception)
        {
            failure = new ProxyException("outputReadFailed", "Cannot read Output pane after initialization: " + selected.Name,
                new JObject { ["paneId"] = NormalizeId(selected.Guid), ["initializationAttempted"] = true,
                    ["readError"] = ProtocolSupport.ReadError("initializedPane", exception),
                    ["innerDetails"] = (exception as ProxyException)?.Details }, exception);
            throw failure;
        }
        finally
        {
            try { if (previousPane != null) previousPane.Activate(); }
            catch (Exception exception) { restorationErrors.Add(ProtocolSupport.ReadError("previousPane", exception)); }
            try { if (output.Parent.Visible != outputWasVisible) output.Parent.Visible = outputWasVisible; }
            catch (Exception exception) { restorationErrors.Add(ProtocolSupport.ReadError("outputVisibility", exception)); }
            try { if (previousWindow != null && dte.ActiveWindow != previousWindow) previousWindow.Activate(); }
            catch (Exception exception) { restorationErrors.Add(ProtocolSupport.ReadError("activeWindow", exception)); }
            if (result != null)
            {
                result["restorationErrors"] = restorationErrors;
                result["paneSelectionRestored"] = previousPane != null && !restorationErrors.OfType<JObject>().Any(e => (string?)e["field"] == "previousPane");
            }
            if (failure != null) failure.Details["restorationErrors"] = restorationErrors;
        }
    }

    private object ReadPane(OutputWindowPane selected, int? offset, int count)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var errors = new JArray();
        // Use the pane's own buffer and read only the requested range.
        if (outputWindow != null && Guid.TryParse(selected.Guid, out var paneGuid))
        {
            try
            {
                ErrorHandler.ThrowOnFailure(outputWindow.GetPane(ref paneGuid, out var nativePane));
                var buffer = nativePane as IVsTextLines;
                if (buffer == null && nativePane is IVsTextBufferProvider provider)
                    ErrorHandler.ThrowOnFailure(provider.GetTextBuffer(out buffer));
                if (buffer == null && nativePane is IVsTextView view)
                    ErrorHandler.ThrowOnFailure(view.GetBuffer(out buffer));
                if (buffer != null)
                {
                    ErrorHandler.ThrowOnFailure(buffer.GetSize(out var length));
                    var range = ProtocolSupport.Range(length, offset, count);
                    var text = string.Empty;
                    if (range.Count > 0)
                    {
                        ErrorHandler.ThrowOnFailure(buffer.GetLineIndexOfPosition(range.Offset, out var startLine, out var startColumn));
                        ErrorHandler.ThrowOnFailure(buffer.GetLineIndexOfPosition(range.Offset + range.Count, out var endLine, out var endColumn));
                        ErrorHandler.ThrowOnFailure(buffer.GetLineText(startLine, startColumn, endLine, endColumn, out text));
                    }
                    return Result(selected, text, range.Offset, length, "textBuffer");
                }
                errors.Add(new JObject { ["field"] = "textBuffer", ["message"] = "The pane does not expose a text buffer." });
            }
            catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("textBuffer", exception)); }
        }

        try
        {
            var document = selected.TextDocument;
            // AbsoluteCharOffset is 1-based. GetText uses physical character counts;
            // avoid converting DTE logical offsets across CRLF line endings.
            var start = document.StartPoint.CreateEditPoint();
            // DTE fallback retains the established UTF-16/CRLF offset contract.
            var text = start.AtEndOfDocument ? string.Empty : start.GetText(document.EndPoint);
            var range = ProtocolSupport.Range(text.Length, offset, count);
            return Result(selected, text.Substring(range.Offset, range.Count), range.Offset, text.Length, "dte");
        }
        catch (Exception exception)
        {
            errors.Add(ProtocolSupport.ReadError("textDocument", exception));
            throw new ProxyException("outputReadFailed", "Cannot read Output pane '" + selected.Name + "'.",
                new JObject { ["paneId"] = NormalizeId(selected.Guid), ["attempts"] = errors }, exception);
        }
    }

    private static string NormalizeId(string value) => Guid.TryParse(value, out var guid) ? guid.ToString("D") : value;

    private static object Result(OutputWindowPane pane, string text, int offset, int total, string source)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return new
        {
            pane = pane.Name, paneId = NormalizeId(pane.Guid), text, offset, count = text.Length,
            totalChars = total, hasMoreBefore = offset > 0, hasMoreAfter = offset + text.Length < total, source
        };
    }
}
