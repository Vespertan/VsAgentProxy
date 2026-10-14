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
using Vespertan.VisualStudio.Contracts;
using Task = System.Threading.Tasks.Task;
using Vespertan.VisualStudio.AgentProxy.Contracts;
using Vespertan.VisualStudio.AgentProxy.Rpc;
using Vespertan.VisualStudio.AgentProxy.Services;

namespace Vespertan.VisualStudio.AgentProxy;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideBrokeredService(AgentServices.Name, AgentServices.Version, Audience = ServiceAudience.Local | ServiceAudience.PublicSdk)]
[InstalledProductRegistration("Vespertan Agent Proxy", "Local debugger automation bridge", "0.10")]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
public sealed class VsAgentProxyPackage : AsyncPackage
{
    public const string PackageGuidString = "24117002-8054-4182-8801-f90a0180ade3";
    private static readonly Guid _commandSet = new("f087fb30-94d9-476f-a1c0-b0198c674f2f");
    private const int _statusCommandId = 0x0100;

    private AgentService? _server;
    private IDisposable? _serviceRegistration;
#pragma warning disable ISB001 // AsyncPackage.Dispose(bool) below releases this proxy before the shared service.
    private IExternalServiceRegistration? _externalRegistration;
#pragma warning restore ISB001

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        var commands = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Visual Studio menu command service is unavailable.");
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        commands.AddCommand(new MenuCommand(ShowStatus, new CommandID(_commandSet, _statusCommandId)));

        var service = await GetServiceAsync(typeof(EnvDTE.DTE));
        if (service is null)
        {
            return;
        }

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
            ActivityLog.TryLogWarning(nameof(Vespertan.VisualStudio.AgentProxy), "Error List service unavailable: " + exception.Message);
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var solution = solutionService as IVsSolution;
        var buildManager = buildService as IVsSolutionBuildManager2;
        var targetSelection = targetService as IVsDebugTargetSelectionService;
        var output = outputService as IVsOutputWindow;
        var dte = (DTE2)service;
        _server = new AgentService(dte, JoinableTaskFactory,
            new OutputService(dte, output), new ProjectService(dte, solution), new DiagnosticsService(errorTable), new DocumentService(documentTable, editorAdapters), buildManager, targetSelection, debuggerService as IVsDebugger);
        var container = await GetServiceAsync(typeof(SVsBrokeredServiceContainer)) as IBrokeredServiceContainer
            ?? throw new InvalidOperationException("Visual Studio service broker is unavailable.");
        _serviceRegistration = container.Proffer(AgentServices.Agent,
            (_, _, _, _) => new ValueTask<object?>(new AgentServiceSession(_server)));
        _externalRegistration?.Dispose();
        _externalRegistration = await container.GetFullAccessServiceBroker().GetProxyAsync<IExternalServiceRegistration>(
            HubServices.ExternalServiceRegistration, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Vespertan Extensions Hub 1.7 or newer is required.");
        await _externalRegistration.RegisterAsync(AgentServices.Name, AgentServices.Version, cancellationToken);
    }

    private void ShowStatus(object sender, EventArgs args)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(this,
            _server?.GetStatusText() ?? "The proxy server is not available. Check the Visual Studio Activity Log for package initialization errors.",
            "Agent Proxy status", OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing)
        {
            _externalRegistration?.Dispose();
            _externalRegistration = null;

            _serviceRegistration?.Dispose();
            _server?.Dispose();
        }

        base.Dispose(disposing);
    }
}
