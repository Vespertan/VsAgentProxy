# VS Agent Proxy

Current version: `0.8.0` (extension and client), contract version 2.

VS Agent Proxy is a local bridge between Visual Studio and automation clients.
The extension publishes an explicit allow-list of operations through the central
Vespertan Extensions Hub RPC endpoint:

- debugger state and the current process/thread,
- loaded and unloaded projects, project-system load errors, and startup-project selection,
- filtered and paginated Error List diagnostics independent of the visible IDE filters,
- `launchCheck`: Start-command availability and observed launch blockers,
- documents, explicit saving, and safe project reload,
- startup-project selection and .NET/JavaScript profiles from the IDE menu,
- build/rebuild/clean with operation IDs, cancellation, outcomes, and event history,
- JS/TS scopes, paginated variables, stop reasons, and thread/frame selection,
- the current-thread call stack,
- the active document, caret position, and selected text,
- locals, arguments, and controlled expression evaluation,
- breakpoint listing, creation, removal, enablement, and conditions,
- Output-pane listing, tail reads, and bounded range reads,
- start with or without the debugger and restart,
- Step Over, Step Into, Step Out, Continue, Break All, and Stop Debugging.

The extension is self-describing: `capabilities` returns the API catalog, and
`documentation` returns the short or full agent guide embedded in the DLL.

There is no arbitrary `ExecuteCommand` and no TCP server. `evaluate` is explicit;
enumerating DTE variables can also trigger adapter-side evaluation. Each Visual
Studio instance has a Hub pipe named `VsExtensionsHub-{PID}`. The client discovers
active instances by enumerating Named Pipes and does not use PID descriptor files.

## Build and install

Building requires the `Vespertan.VsExtensionsHub.Contracts` 1.1.0 NuGet package
from a configured package source (on the development workstation:
`LocalVespertanNuget`). It supplies shared menu identifiers at build time only.

```powershell
dotnet build ./src/VsAgentProxy.slnx
```

Install **Vespertan Extensions Hub 1.5.0 or newer (below 2.0)** first, then the generated `.vsix` from
`src/VsAgentProxy/bin/Debug/net472`. Restart Visual Studio afterwards; the
extension starts in the background.

**Extensions → Vespertan → Agent Proxy status** displays the pipe path, Visual
Studio PID, extension version, service registration, start time, session ID,
solution, debugger mode, and a CLI command for that instance. The dialog is a
snapshot taken when opened; Ctrl+C copies its contents. The VSIX dependency
uses the Hub's stable installation ID `Vespertan.VisualStudio.ExtensionHost`.
Contracts and the Hub have independent versions: Contracts 1.1.0 is a build-time
dependency; Hub 1.5.0 is the separately installed VSIX providing the shared menu
and its native Visual Studio 2026 settings. The proxy does not bundle the Hub DLL.

## Client

Install the client globally:

```powershell
dotnet pack ./src/VsAgentProxy.Client/VsAgentProxy.Client.csproj -c Release --no-restore
dotnet tool install --global --configfile ./NuGet.Tool.config VsAgentProxy.Client --version 0.8.0
```

Version 0.8 requires Hub 1.5+ and the 0.8 CLI. The old JSON-lines pipe is removed.
The proxy registers `Vespertan.VsAgent/1.0`; Hub owns transport and routes service
requests. `instances` verifies Hub health and VsAgent availability separately.
Runtime dependencies are `Vespertan.VsExtensionsHub.Services.Contracts` 1.1.0
and (CLI only) `Vespertan.VsExtensionsHub.Client` 1.0.2 from LocalVespertanNuget.
Hub service commands: `hubStatus`, `selection`, `solutionTree`, and
`watchSelection --durationMs 60000` (Ctrl+C stops listening).

After installation, these are the preferred commands:

```powershell
vsagent instances
vsagent --version
vsagent status
vsagent stackTrace
vsagent projects
vsagent launchCheck
vsagent diagnostics --severity error --count 200
vsagent diagnostics --origin project-load
vsagent output --paneId <guid> --offset 0 --count 10000
vsagent --pid 12345 documents
vsagent --pid 12345 projectReload --path C:/Project/App.esproj
vsagent --pid 12345 setStartupProjects --path C:/Project/App.esproj
vsagent --pid 12345 launchProfiles --project C:/Project/App.esproj
vsagent --pid 12345 selectLaunchProfile --project C:/Project/App.esproj --name "Demo (Chrome)"
vsagent --pid 12345 solutionLaunchProfiles
vsagent --pid 12345 selectSolutionLaunchProfile --name "Demo" --scope shared
vsagent --pid 12345 build --wait true --idempotencyKey build-001
vsagent --pid 12345 start --wait true --idempotencyKey start-001
vsagent --pid 12345 waitForState --state break --waitTimeoutMs 60000
vsagent --pid 12345 scopes --frameIndex 0
vsagent --pid 12345 variables --reference <id> --offset 0 --count 50
vsagent --pid 12345 events --afterSequence 0
```

Update the installed version with:

```powershell
dotnet tool update --global --configfile ./NuGet.Tool.config VsAgentProxy.Client
```

The client can also be run without a global installation:

```powershell
dotnet run --project ./src/VsAgentProxy.Client -- instances
dotnet run --project ./src/VsAgentProxy.Client -- status
dotnet run --project ./src/VsAgentProxy.Client -- capabilities
dotnet run --project ./src/VsAgentProxy.Client -- documentation --level full
dotnet run --project ./src/VsAgentProxy.Client -- stackTrace
dotnet run --project ./src/VsAgentProxy.Client -- output
dotnet run --project ./src/VsAgentProxy.Client -- output Build 50000
dotnet run --project ./src/VsAgentProxy.Client -- output Build --offset 0 --count 10000
dotnet run --project ./src/VsAgentProxy.Client -- activeDocument
dotnet run --project ./src/VsAgentProxy.Client -- start
dotnet run --project ./src/VsAgentProxy.Client -- startWithoutDebugging
dotnet run --project ./src/VsAgentProxy.Client -- restart
dotnet run --project ./src/VsAgentProxy.Client -- stepOver
dotnet run --project ./src/VsAgentProxy.Client -- --pid 12345 status
dotnet run --project ./src/VsAgentProxy.Client -- locals --frameIndex 0 --maxDepth 2
dotnet run --project ./src/VsAgentProxy.Client -- arguments --frameIndex 0
dotnet run --project ./src/VsAgentProxy.Client -- evaluate --expression "customer.Address.City"
dotnet run --project ./src/VsAgentProxy.Client -- breakpoints
dotnet run --project ./src/VsAgentProxy.Client -- breakpointAdd --file C:/Project/Program.cs --line 42 --condition "retryCount > 2" --conditionType whenTrue
dotnet run --project ./src/VsAgentProxy.Client -- breakpointSetEnabled --id 0123456789abcdef --enabled false
dotnet run --project ./src/VsAgentProxy.Client -- breakpointSetCriteria --id 0123456789abcdef --condition "retryCount > 5" --hitCount 3 --hitCountType greaterOrEqual
dotnet run --project ./src/VsAgentProxy.Client -- breakpointRemove --id 0123456789abcdef
```

Responses are JSON, so the client can also be called from agent tooling. When
multiple Visual Studio instances are running, the client selects the process
with the newest available start time by default. `instances` enumerates active
pipes, and `--pid` selects a specific instance. The client does not send an
automatic `ping` before every operation; `ping` remains an explicit diagnostic
command.

`offset` and `count` are character indexes. An `output` response also includes
`totalChars`, `hasMoreBefore`, and `hasMoreAfter`, so a large pane can be read
in bounded portions. Without `offset`, the tail of the pane is returned, matching
the legacy `maxChars` parameter.

The Output listing retains `panes` (names) and also returns `paneDetails` with
stable GUIDs. Reading an empty buffer returns empty text; a real access failure
contains `errorCode`, `hresult`, and `details`. The proxy prefers reading a range
from the Visual Studio buffer; the DTE fallback reads the complete text while
preserving the previous UTF-16/CRLF indexes. An uninitialized pane may need
temporary activation; the proxy restores the pane selection when one existed,
Output visibility, and the active window, and reports `restorationErrors`.
No previous pane selection means `previousPaneAvailable: false`.

`projects` distinguishes `loaded`, `unloaded`, and hierarchy-confirmed `failed`.
Not every project system exposes a load error; the response reports data
availability and read errors. `launchCheck` is a read operation: it does not
start the application or change settings. The active profile is read separately
through `launchProfiles`.

`diagnostics` returns the source, severity, origin, project, file, code, and
message. Check `isStable`, `complete`, and `readErrors`: providers publish results
asynchronously, so an initially empty read does not prove that no errors exist.
Line and column numbers are one-based. Details: [agent protocol](docs/AGENT_PROTOCOL.md).

## Tests

Contract and snapshot handling tests:

```powershell
dotnet test ./src/VsAgentProxy.Tests/VsAgentProxy.Tests.csproj
```

`currentHits` may be `null`; DTE counters can be unreliable, so a separate
`observedHits` value is available. Variable handles become `staleReference`
after stepping or continuing.

Limitations are explicit: existing JSPS terminals do not expose history through
the verified public API; complete source maps and the active Angular Language
Service version remain unavailable. `.slnLaunch` profiles are supported in
Visual Studio 2026 through a versioned internal-contract adapter, which may
return `unsupported` after a Visual Studio version change. HTML diagnostics
depend on publication through Error List; an empty result does not prove that
Angular Language Service is running.

`locals`, `arguments`, and `evaluate` limit `maxDepth` to 3 and `maxItems` to
500. Expression evaluation may execute getters or methods in the debuggee, so
`evaluate` is an explicit operation.

A breakpoint can be addressed by its stable `id` (for breakpoints created by the
proxy) or by its current zero-based `index`. Supported condition types are
`whenTrue` and `whenChanged`; hit-count types are `none`, `equal`,
`greaterOrEqual`, and `multiple`.
