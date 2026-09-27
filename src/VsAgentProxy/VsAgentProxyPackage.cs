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

namespace VsAgentProxy;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[InstalledProductRegistration("VS Agent Proxy", "Local debugger automation bridge", "0.7")]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
public sealed class VsAgentProxyPackage : AsyncPackage
{
    public const string PackageGuidString = "7b8f6fcc-9bc7-472e-8dbe-75f32de824ec";
    private static readonly Guid CommandSet = new("f087fb30-94d9-476f-a1c0-b0198c674f2f");
    private const int StatusCommandId = 0x0100;

    private CancellationTokenSource? shutdown;
    private ProxyServer? server;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        var commands = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Visual Studio menu command service is unavailable.");
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        commands.AddCommand(new MenuCommand(ShowStatus, new CommandID(CommandSet, StatusCommandId)));

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
            ActivityLog.TryLogWarning(nameof(VsAgentProxy), "Error List service unavailable: " + exception.Message);
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

    private void ShowStatus(object sender, EventArgs args)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(this,
            server?.GetStatusText() ?? "The proxy server is not available. Check the Visual Studio Activity Log for package initialization errors.",
            "Agent Proxy status", OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
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
