using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace VsAgentProxy;

[ComImport, Guid("D3409A06-1AB9-4CAB-9028-CE85675C2B44"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStartupProjectsNative
{
    void Reset();
    bool MoveNext();
    object Current { [return: MarshalAs(UnmanagedType.Interface)] get; }
    int ItemCount { get; }
}
