using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Vespertan.VisualStudio.AgentProxy.Rpc;

namespace Vespertan.VisualStudio.AgentProxy.Interop;

// Isolated compatibility adapter for the VS 2026 implementation. These are not public SDK
// contracts. Do not load a DLL from the solution, write .suo, or emulate only part of a profile.
internal sealed class NativeSolutionLaunch
{
    private readonly MethodInfo _list;
    private readonly MethodInfo _apply;
    private readonly MethodInfo _setSelected;
    private readonly FieldInfo _selected;
    public ISolutionStartupNative Startup { get; }
    public string Version { get; }

    public NativeSolutionLaunch()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Microsoft.VisualStudio.CommonIDE")
            ?? throw new ProxyException("unsupported", "The native solution profile component is not loaded.");
        var version = FileVersionInfo.GetVersionInfo(assembly.Location);
        Version = version.FileVersion;
        if (version.FileMajorPart != 18)
        {
            throw new ProxyException("unsupported", "Native solution profiles are currently supported only by the VS 2026 compatibility adapter.");
        }

        var type = assembly.GetType("Microsoft.VisualStudio.CommonIDE.Solutions.MultiProjectLaunchProfilesPersistence", true)!;
        var profile = assembly.GetType("Microsoft.VisualStudio.CommonIDE.Solutions.LaunchProfile", true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        _list = type.GetMethod("GetLaunchProfilesAsync", flags, null, [typeof(CancellationToken)], null) ?? throw Missing();
        _apply = type.GetMethod("UpdateProjectStartupSettingsFromProfileAsync", flags, null, [profile, typeof(CancellationToken)], null) ?? throw Missing();
        _setSelected = type.GetMethod("SetSelectedLaunchProfile", flags, null, [profile], null) ?? throw Missing();
        _selected = type.GetField("selectedLaunchProfile", flags) ?? throw Missing();
        foreach (var property in new[] { "Name", "Projects", "IsShared" })
        {
            if (profile.GetProperty(property) == null)
            {
                throw Missing();
            }
        }

        Startup = ServiceProvider.GlobalProvider.GetService(typeof(SolutionStartupServiceNative)) as ISolutionStartupNative ?? throw Missing();
    }

    private static ProxyException Missing() => new("unsupported", "The native solution profile contract does not match this compatibility adapter.");
    public object? Selected => _selected.GetValue(null);
    public void RestoreSelected(object? value) => Invoke(_setSelected, value);
    public async Task<object[]> ProfilesAsync(CancellationToken token) => ((IEnumerable)(await AwaitAsync(Invoke(_list, token)!))!).Cast<object>().ToArray();
    public async Task ApplyAsync(object profile, CancellationToken token) => await AwaitAsync(Invoke(_apply, profile, token)!);

    public static object? Property(object? value, string name)
    {
        if (value == null)
        {
            return null;
        }

        var type = value.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
        {
            return property.GetValue(value);
        }

        // IVsProjectInfo is implemented explicitly by VS's internal Project class.
        foreach (var iface in type.GetInterfaces())
        {
            property = iface.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? iface.GetProperties().FirstOrDefault(p => p.Name.EndsWith("." + name, StringComparison.Ordinal));
            if (property != null)
            {
                return property.GetValue(value);
            }
        }

        return null;
    }

    private static object? Invoke(MethodInfo method, params object?[] args)
    {
        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    private static async Task<object?> AwaitAsync(object valueTask)
    {
        var task = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)?.Invoke(valueTask, null) as Task ?? throw Missing();
        await task;
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
