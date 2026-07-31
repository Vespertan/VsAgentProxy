using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace VsCodexProxy;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("VS Codex Proxy", "Local debugger automation bridge", "0.4")]
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

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = (DTE2)service;
        shutdown = new CancellationTokenSource();
        server = new ProxyServer(dte, JoinableTaskFactory, dte.Solution?.FullName ?? string.Empty);
        server.Start(shutdown.Token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            shutdown?.Cancel();
            server?.Dispose();
            shutdown?.Dispose();
        }

        base.Dispose(disposing);
    }
}
