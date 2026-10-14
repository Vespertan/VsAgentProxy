using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Vespertan.VisualStudio.AgentProxy.Interop;

[ComImport, Guid("6E71FE11-D672-4A4E-A4A8-94A0388F8574"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStartupProjectNative
{
    string ProjectName { [return: MarshalAs(UnmanagedType.BStr)] get; }
    Guid ProjectGuid { get; }
    int StartupMode { get; }
    ushort StartupOrder { get; }
}
