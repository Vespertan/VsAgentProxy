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
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Vespertan.VsExtensionsHub.Services.Contracts;
using VsAgentProxy.Contracts;
using Task = System.Threading.Tasks.Task;

namespace VsAgentProxy;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideBrokeredService(AgentServices.Name, AgentServices.Version, Audience = ServiceAudience.Local | ServiceAudience.PublicSdk)]
[InstalledProductRegistration("VS Agent Proxy", "Local debugger automation bridge", "0.8")]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
public sealed class VsAgentProxyPackage : AsyncPackage
{
    public const string PackageGuidString = "7b8f6fcc-9bc7-472e-8dbe-75f32de824ec";
    private static readonly Guid CommandSet = new("f087fb30-94d9-476f-a1c0-b0198c674f2f");
    private const int StatusCommandId = 0x0100;

    private AgentService? server;
    private IDisposable? serviceRegistration;
#pragma warning disable ISB001 // AsyncPackage.Dispose(bool) below releases this proxy before the shared service.
    private IExternalServiceRegistration? externalRegistration;
#pragma warning restore ISB001

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
        server = new AgentService(dte, JoinableTaskFactory,
            new OutputService(dte, output), new ProjectService(dte, solution), new DiagnosticsService(errorTable), new DocumentService(documentTable, editorAdapters), buildManager, targetSelection, debuggerService as IVsDebugger);
        var container = await GetServiceAsync(typeof(SVsBrokeredServiceContainer)) as IBrokeredServiceContainer
            ?? throw new InvalidOperationException("Visual Studio service broker is unavailable.");
        serviceRegistration = container.Proffer(AgentServices.Agent,
            (_, _, _, _) => new ValueTask<object?>(new AgentServiceSession(server)));
        externalRegistration?.Dispose();
        externalRegistration = await container.GetFullAccessServiceBroker().GetProxyAsync<IExternalServiceRegistration>(
            HubServices.ExternalServiceRegistration, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Vespertan Extensions Hub 1.5 or newer is required.");
        await externalRegistration.RegisterAsync(AgentServices.Name, AgentServices.Version, cancellationToken);
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
            if (externalRegistration != null)
            {
                externalRegistration.Dispose();
                externalRegistration = null;
            }
            serviceRegistration?.Dispose();
            server?.Dispose();
        }

        base.Dispose(disposing);
    }
}
