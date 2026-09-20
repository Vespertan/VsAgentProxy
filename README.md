# VS Agent Proxy

Current version: `0.7.1` (extension and client), contract version 2.

VS Agent Proxy is a local bridge between Visual Studio and automation clients.
The extension exposes an explicit allow-list of operations through a Named Pipe:

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
Studio instance creates a pipe named `VsAgentProxy-{PID}`. The client discovers
active instances by enumerating Named Pipes and does not use PID descriptor files.

## Build and install

```powershell
dotnet build ./src/VsAgentProxy.slnx
```

Install the generated `.vsix` from
`src/VsAgentProxy/bin/Debug/net472`. Restart Visual Studio afterwards; the
extension starts in the background.

## Client

Install the client globally:

```powershell
dotnet pack ./src/VsAgentProxy.Client/VsAgentProxy.Client.csproj -c Release --no-restore
dotnet tool install --global --configfile ./NuGet.Tool.config VsAgentProxy.Client --version 0.7.1
```

Version 0.7.0 renamed the product from VS Codex Proxy. Install the new VSIX
and `VsAgentProxy.Client`; the former `vscodex` client does not connect to
the new `VsAgentProxy-{PID}` pipe.

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
