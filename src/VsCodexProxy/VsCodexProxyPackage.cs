using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell.TableManager;
using Task = System.Threading.Tasks.Task;

namespace VsCodexProxy;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("VS Codex Proxy", "Local debugger automation bridge", "0.6")]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
public sealed class VsCodexProxyPackage : AsyncPackage
{
    public const string PackageGuidString = "7f7132bd-3062-4f81-9359-aa0ebf755a19";

    private CancellationTokenSource? shutdown;
    private ProxyServer? server;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        var service = await GetServiceAsync(typeof(EnvDTE.DTE));
        if (service is null)
            return;

        var solutionService = await GetServiceAsync(typeof(SVsSolution));
        var outputService = await GetServiceAsync(typeof(SVsOutputWindow));
        var documentTable = await GetServiceAsync(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
        var buildService = await GetServiceAsync(typeof(SVsSolutionBuildManager));
        var targetService = await GetServiceAsync(typeof(SVsDebugTargetSelectionService));
        var debuggerService = await GetServiceAsync(typeof(SVsShellDebugger));
        ITableManager? errorTable = null;
        Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService? editorAdapters = null;
        try
        {
            var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            errorTable = componentModel?.GetService<ITableManagerProvider>()?.GetTableManager(StandardTables.ErrorsTable);
            editorAdapters = componentModel?.GetService<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService>();
        }
        catch (Exception exception)
        {
            ActivityLog.TryLogWarning(nameof(VsCodexProxy), "Error List service unavailable: " + exception.Message);
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var solution = solutionService as IVsSolution;
        var buildManager = buildService as IVsSolutionBuildManager2;
        var targetSelection = targetService as IVsDebugTargetSelectionService;
        var output = outputService as IVsOutputWindow;
        var dte = (DTE2)service;
        shutdown = new CancellationTokenSource();
        server = new ProxyServer(dte, JoinableTaskFactory,
            new OutputService(dte, output), new ProjectService(dte, solution), new DiagnosticsService(errorTable), new DocumentService(documentTable, editorAdapters), buildManager, targetSelection, debuggerService as IVsDebugger);
        server.Start(shutdown.Token);
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing)
        {
            shutdown?.Cancel();
            server?.Dispose();
            shutdown?.Dispose();
        }

        base.Dispose(disposing);
    }
}
