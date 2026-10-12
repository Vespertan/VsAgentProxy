using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace VsAgentProxy;

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
