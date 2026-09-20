using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Newtonsoft.Json.Linq;

namespace VsAgentProxy;

internal sealed class DocumentService
{
    private readonly IVsRunningDocumentTable? table;
    private readonly IVsEditorAdaptersFactoryService? adapters;
    public DocumentService(IVsRunningDocumentTable? table, IVsEditorAdaptersFactoryService? adapters = null)
    { this.table = table; this.adapters = adapters; }

    public JObject LanguageStatus(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        path = ProtocolSupport.AbsolutePath(path);
        var result = new JObject { ["path"] = path, ["languageServiceAvailability"] = "unavailable",
            ["activeServerVersion"] = null, ["serverVersionAvailability"] = "unavailable", ["documentVersion"] = null,
            ["diagnosticsFreshness"] = "unknown", ["note"] = "Editor content type or an empty diagnostic list does not confirm Angular Language Service is running." };
        if (!(table is IVsRunningDocumentTable4 current) || !current.IsMonikerValid(path))
        { result["unavailableReason"] = "Document is not open in the Running Document Table."; return result; }
        var cookie = current.GetDocumentCookie(path);
        var data = current.GetDocumentData(cookie);
        current.UpdateDirtyState(cookie);
        result["dirty"] = current.IsDocumentDirty(cookie);
        var errors = new JArray();
        if (data is IVsTextLines lines)
        {
            try
            {
                ErrorHandler.ThrowOnFailure(lines.GetLanguageServiceID(out var language));
                result["languageServiceId"] = language.ToString("D");
                result["languageServiceAvailability"] = language == Guid.Empty ? "unavailable" : "editorRegistered";
            }
            catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("languageServiceId", exception)); }
        }
        if (data is IVsTextBuffer buffer && adapters != null)
        {
            try
            {
                var managed = adapters.GetDocumentBuffer(buffer);
                if (managed != null)
                { result["contentType"] = managed.ContentType.TypeName; result["documentVersion"] = managed.CurrentSnapshot.Version.VersionNumber; }
            }
            catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("editorSnapshot", exception)); }
        }
        result["readErrors"] = errors;
        result["capturedUtc"] = DateTime.UtcNow;
        return result;
    }

    public JObject Read()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (table == null) throw new ProxyException("serviceUnavailable", "Running Document Table is unavailable.");
        ErrorHandler.ThrowOnFailure(table.GetRunningDocumentsEnum(out var enumerator));
        var items = new JArray();
        var cookies = new uint[1];
        while (true)
        {
            ErrorHandler.ThrowOnFailure(enumerator.Next(1, cookies, out var fetched));
            if (fetched == 0) break;
            var errors = new JArray();
            IntPtr data = IntPtr.Zero;
            var item = new JObject { ["cookie"] = cookies[0], ["dirty"] = null };
            try
            {
                ErrorHandler.ThrowOnFailure(table.GetDocumentInfo(cookies[0], out _, out _, out _, out var path, out var hierarchy, out _, out data));
                item["path"] = path;
                if (hierarchy != null && hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Name, out var name) >= 0)
                    item["project"] = name as string;
                if (table is IVsRunningDocumentTable4 currentTable)
                {
                    currentTable.UpdateDirtyState(cookies[0]);
                    item["dirty"] = currentTable.IsDocumentDirty(cookies[0]);
                }
                else if (data != IntPtr.Zero && Marshal.GetObjectForIUnknown(data) is IVsPersistDocData document)
                {
                    ErrorHandler.ThrowOnFailure(document.IsDocDataDirty(out var dirty));
                    item["dirty"] = dirty != 0;
                }
                else item["unavailableReason"] = "Document data does not expose IVsPersistDocData.";
            }
            catch (Exception exception) { errors.Add(ProtocolSupport.ReadError("document", exception)); }
            finally { if (data != IntPtr.Zero) Marshal.Release(data); }
            item["readErrors"] = errors;
            items.Add(item);
        }
        return new JObject { ["documents"] = items, ["source"] = "RunningDocumentTable", ["capturedUtc"] = DateTime.UtcNow };
    }

    public void RequireClean()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var blockers = new JArray(((JArray)Read()["documents"]!).Where(x => (bool?)x["dirty"] != false));
        // All running documents: linked files can belong to more than the primary hierarchy.
        if (blockers.Count != 0) throw new ProxyException("unsavedDocuments", "Reload requires all running documents to have a confirmed clean state. Save explicitly first.", new JObject { ["documents"] = blockers });
    }

    public JObject Save(JObject parameters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var paths = ProtocolSupport.Strings(parameters, "paths").Select(ProtocolSupport.AbsolutePath).ToArray();
        var open = ((JArray)Read()["documents"]!).OfType<JObject>().ToArray();
        var selected = paths.Select(path => open.SingleOrDefault(x => string.Equals((string?)x["path"], path, StringComparison.OrdinalIgnoreCase))
            ?? throw new ProxyException("documentNotFound", "Document is not in the Running Document Table: " + path)).ToArray();
        var results = new JArray();
        foreach (var item in selected)
        {
            IntPtr data = IntPtr.Zero;
            try
            {
                ErrorHandler.ThrowOnFailure(table!.GetDocumentInfo((uint)item["cookie"]!, out _, out _, out _, out var path, out _, out _, out data));
                if (!string.Equals(path, (string?)item["path"], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Document changed before save.");
                if (data == IntPtr.Zero || !(Marshal.GetObjectForIUnknown(data) is IVsPersistDocData document)) throw new InvalidOperationException("Document does not support saving.");
                ErrorHandler.ThrowOnFailure(document.SaveDocData(VSSAVEFLAGS.VSSAVE_SilentSave, out var savedPath, out var cancelled));
                ErrorHandler.ThrowOnFailure(document.IsDocDataDirty(out var dirty));
                results.Add(new JObject { ["path"] = path, ["savedPath"] = savedPath, ["saved"] = cancelled == 0 && dirty == 0, ["cancelled"] = cancelled != 0 });
            }
            catch (Exception exception) { results.Add(new JObject { ["path"] = item["path"], ["saved"] = false, ["error"] = ProtocolSupport.ReadError("save", exception) }); }
            finally { if (data != IntPtr.Zero) Marshal.Release(data); }
        }
        return new JObject { ["documents"] = results, ["allSaved"] = results.All(x => (bool?)x["saved"] == true) };
    }
}
