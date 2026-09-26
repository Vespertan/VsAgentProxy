using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Xunit;

namespace VsAgentProxy.Tests;

public sealed class DocumentSaveTests
{
    private const string SolutionPath = @"C:\Test\Solution.slnx";

    [Fact]
    public void HierarchyDocumentSaveTargetsOneCookieAndVerifiesFreshDirtyState()
    {
        var table = new DocumentTable();
        var result = DocumentService.SaveThroughRunningDocumentTable(table.Instance, 7, SolutionPath);

        Assert.True(result.Value<bool>("saved"));
        Assert.False(result.Value<bool>("cancelled"));
        Assert.Equal(new[] { "save:7", "refresh:7", "dirty:7" }, table.Calls);
        Assert.True(table.Dirty[8]); // An unrelated modified document must remain unsaved.
    }

    [Theory]
    [InlineData(VSConstants.S_OK, true, false)]
    [InlineData(VSConstants.S_FALSE, false, true)]
    public void SuccessRequiresBothSuccessfulSaveAndConfirmedCleanState(int hr, bool staysDirty, bool cancelled)
    {
        var table = new DocumentTable { SaveResult = hr, StaysDirty = staysDirty };
        var result = DocumentService.SaveThroughRunningDocumentTable(table.Instance, 7, SolutionPath);
        Assert.False(result.Value<bool>("saved"));
        Assert.Equal(cancelled, result.Value<bool>("cancelled"));
    }

    [Fact]
    public void SaveFailureIsNotReportedAsSuccess()
    {
        var table = new DocumentTable { SaveResult = VSConstants.E_FAIL };
        Assert.ThrowsAny<Exception>(() => DocumentService.SaveThroughRunningDocumentTable(table.Instance, 7, SolutionPath));
        Assert.Equal(new[] { "save:7" }, table.Calls);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(99u)]
    public void MissingOrReplacedCookieCannotSaveOtherDocuments(uint cookie)
    {
        var table = new DocumentTable();
        Assert.Throws<InvalidOperationException>(() => DocumentService.SaveThroughRunningDocumentTable(table.Instance, cookie, SolutionPath));
        Assert.Empty(table.Calls);
    }

    [Fact]
    public void DocumentReplacedDuringSaveCannotBeReportedAsSaved()
    {
        var table = new DocumentTable { ReplaceDuringSave = true };
        Assert.Throws<InvalidOperationException>(() => DocumentService.SaveThroughRunningDocumentTable(table.Instance, 7, SolutionPath));
        Assert.Equal(new[] { "save:7" }, table.Calls);
    }

    [Fact]
    public void DirtyStateReadFailureCannotBeReportedAsSaved()
    {
        var table = new DocumentTable { FailRefresh = true };
        Assert.Throws<InvalidOperationException>(() => DocumentService.SaveThroughRunningDocumentTable(table.Instance, 7, SolutionPath));
    }

    public interface ITestDocumentTable : IVsRunningDocumentTable, IVsRunningDocumentTable4 { }

    private sealed class DocumentTable : RealProxy
    {
        public readonly List<string> Calls = new List<string>();
        public readonly Dictionary<uint, bool> Dirty = new Dictionary<uint, bool> { [7] = true, [8] = true };
        public int SaveResult = VSConstants.S_OK;
        public bool StaysDirty, ReplaceDuringSave, FailRefresh;
        private uint currentCookie = 7;
        public IVsRunningDocumentTable Instance => (IVsRunningDocumentTable)GetTransparentProxy();

        public DocumentTable() : base(typeof(ITestDocumentTable)) { }

        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object? result = null;
                switch (call.MethodName)
                {
                    case "IsMonikerValid": result = (string)call.Args[0] == SolutionPath; break;
                    case "GetDocumentCookie": result = currentCookie; break;
                    case "SaveDocuments":
                        var flags = (uint)call.Args[0];
                        Assert.Equal((uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveNoChildren
                            | (uint)__VSRDTSAVEOPTIONS2.RDTSAVEOPT_SkipNewUnsaved
                            | (uint)__VSRDTSAVEOPTIONS3.RDTSAVEOPT_SilentSave, flags);
                        Assert.Null(call.Args[1]);
                        Assert.Equal(VSConstants.VSITEMID_NIL, (uint)call.Args[2]);
                        var cookie = (uint)call.Args[3];
                        Calls.Add("save:" + cookie);
                        if (ReplaceDuringSave) currentCookie = 9;
                        result = SaveResult;
                        break;
                    case "UpdateDirtyState":
                        Calls.Add("refresh:" + call.Args[0]);
                        if (FailRefresh) throw new InvalidOperationException("Cannot read document state.");
                        Dirty[(uint)call.Args[0]] = StaysDirty;
                        break;
                    case "IsDocumentDirty":
                        Calls.Add("dirty:" + call.Args[0]);
                        result = Dirty[(uint)call.Args[0]];
                        break;
                    default: throw new NotSupportedException(call.MethodName);
                }
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
            catch (Exception exception) { return new ReturnMessage(exception, call); }
        }
    }
}
