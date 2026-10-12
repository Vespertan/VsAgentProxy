using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace VsAgentProxy;

// Exact COM vtables from Microsoft.Internal.VisualStudio.Interop in VS 18.10.
[ComImport, Guid("552BC6D4-73D2-4A00-84C7-81157143A24B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface SolutionStartupServiceNative { }
