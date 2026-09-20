using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace VsCodexProxy;

// Isolated compatibility adapter for the VS 2026 implementation. These are not public SDK
// contracts. Do not load a DLL from the solution, write .suo, or emulate only part of a profile.
internal sealed class NativeSolutionLaunch
{
    private readonly MethodInfo list;
    private readonly MethodInfo apply;
    private readonly MethodInfo setSelected;
    private readonly FieldInfo selected;
    public ISolutionStartupNative Startup { get; }
    public string Version { get; }

    public NativeSolutionLaunch()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Microsoft.VisualStudio.CommonIDE")
            ?? throw new ProxyException("unsupported", "The native solution profile component is not loaded.");
        var version = FileVersionInfo.GetVersionInfo(assembly.Location);
        Version = version.FileVersion;
        if (version.FileMajorPart != 18) throw new ProxyException("unsupported", "Native solution profiles are currently supported only by the VS 2026 compatibility adapter.");
        var type = assembly.GetType("Microsoft.VisualStudio.CommonIDE.Solutions.MultiProjectLaunchProfilesPersistence", true)!;
        var profile = assembly.GetType("Microsoft.VisualStudio.CommonIDE.Solutions.LaunchProfile", true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        list = type.GetMethod("GetLaunchProfilesAsync", flags, null, new[] { typeof(CancellationToken) }, null) ?? throw Missing();
        apply = type.GetMethod("UpdateProjectStartupSettingsFromProfileAsync", flags, null, new[] { profile, typeof(CancellationToken) }, null) ?? throw Missing();
        setSelected = type.GetMethod("SetSelectedLaunchProfile", flags, null, new[] { profile }, null) ?? throw Missing();
        selected = type.GetField("selectedLaunchProfile", flags) ?? throw Missing();
        foreach (var property in new[] { "Name", "Projects", "IsShared" })
            if (profile.GetProperty(property) == null) throw Missing();
        Startup = ServiceProvider.GlobalProvider.GetService(typeof(SolutionStartupServiceNative)) as ISolutionStartupNative ?? throw Missing();
    }

    private static ProxyException Missing() => new("unsupported", "The native solution profile contract does not match this compatibility adapter.");
    public object? Selected => selected.GetValue(null);
    public void RestoreSelected(object? value) => Invoke(setSelected, value);
    public async Task<object[]> ProfilesAsync(CancellationToken token) => ((IEnumerable)(await AwaitAsync(Invoke(list, token)!))!).Cast<object>().ToArray();
    public async Task ApplyAsync(object profile, CancellationToken token) => await AwaitAsync(Invoke(apply, profile, token)!);
    public static object? Property(object? value, string name)
    {
        if (value == null) return null;
        var type = value.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null) return property.GetValue(value);
        // IVsProjectInfo is implemented explicitly by VS's internal Project class.
        foreach (var iface in type.GetInterfaces())
        {
            property = iface.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? iface.GetProperties().FirstOrDefault(p => p.Name.EndsWith("." + name, StringComparison.Ordinal));
            if (property != null) return property.GetValue(value);
        }
        return null;
    }

    private static object? Invoke(MethodInfo method, params object?[] args)
    {
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }

    private static async Task<object?> AwaitAsync(object valueTask)
    {
        var task = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)?.Invoke(valueTask, null) as Task ?? throw Missing();
        await task;
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}

// Exact COM vtables from Microsoft.Internal.VisualStudio.Interop in VS 18.10.
[ComImport, Guid("552BC6D4-73D2-4A00-84C7-81157143A24B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface SolutionStartupServiceNative { }

[ComImport, Guid("9C8369E4-CEFF-44FE-B5E0-93CC111B5712"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISolutionStartupNative
{
    IStartupProjectsNative AvailableStartupProjects { [return: MarshalAs(UnmanagedType.Interface)] get; }
    int StartupMode { get; set; }
    object DefaultStartupProject { [return: MarshalAs(UnmanagedType.Interface)] get; }
    void SetDefaultStartupProject(Guid id);
    void SetProjectStartupSettings(Guid id, int mode, ushort order);
    void CommitChanges();
}

[ComImport, Guid("D3409A06-1AB9-4CAB-9028-CE85675C2B44"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStartupProjectsNative
{
    void Reset();
    bool MoveNext();
    object Current { [return: MarshalAs(UnmanagedType.Interface)] get; }
    int ItemCount { get; }
}

[ComImport, Guid("6E71FE11-D672-4A4E-A4A8-94A0388F8574"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStartupProjectNative
{
    string ProjectName { [return: MarshalAs(UnmanagedType.BStr)] get; }
    Guid ProjectGuid { get; }
    int StartupMode { get; }
    ushort StartupOrder { get; }
}
