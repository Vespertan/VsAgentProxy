# VS Codex Proxy — full agent documentation

## 1. Purpose and architecture

VS Codex Proxy is a local bridge between an agent and Visual Studio. The VSIX
extension runs inside the `devenv.exe` process, uses EnvDTE on the Visual Studio
main thread, and exposes a closed allow-list of operations through a named pipe.
It does not expose a TCP server or an arbitrary `ExecuteCommand`.

Each instance creates a named pipe:

```text
pipe:       VsCodexProxy-{PID}
```

The client discovers active instances by enumerating `\\.\pipe\` and filtering
the `VsCodexProxy-{PID}` pattern. The pipe name supplies the Visual Studio PID.
Use an explicit PID to select an instance unambiguously. The selected pipe is
connected directly for the requested operation; the client does not send an
implicit `ping` before every command. Call `ping` explicitly when you need to
inspect the proxy version or session.

## 2. CLI client

`vscodex` is a global .NET tool and can be run from any directory. Install it
from the repository:

```powershell
dotnet pack .\src\VsCodexProxy.Client\VsCodexProxy.Client.csproj -c Release --no-restore
dotnet tool install --global --configfile .\NuGet.Tool.config VsCodexProxy.Client --version 0.6.1
```

Fallback without a global installation:

```powershell
dotnet run --project .\src\VsCodexProxy.Client --no-build -- --pid 12345 status
```

### Selecting an instance

```powershell
vscodex instances
vscodex --pid 12345 status
```

`instances` lists currently enumerated proxy pipes. Without `--pid`, the client
selects the candidate with the newest available Visual Studio process start
time. Do not rely on automatic selection when more than one Visual Studio
instance is running.

### Response format

Success:

```json
{
  "id": "request-id",
  "ok": true,
  "result": {}
}
```

Error:

```json
{
  "id": "request-id",
  "ok": false,
  "error": "error description"
}
```

Client exit codes:

| Code | Meaning |
|---:|---|
| 0 | response `ok: true`, or a valid local command |
| 1 | server returned `ok: false`, or a command was missing |
| 2 | selected Visual Studio instance was not found |
| 3 | connection error or client timeout |
| 4 | `waitForState` timeout, or an unconfirmed (`unknown`) operation result |

`--wait true` after a command with an `operationId` waits for completion. It
returns 1 for `failed` or `cancelled`, even when the status read itself had
`ok: true`. Connection limit: `--connectTimeoutMs 5000`; response limit:
`--responseTimeoutMs 15000`; wait limit: `--waitTimeoutMs 120000`. A timeout
never cancels work in the IDE.

Pass arrays as repeated `--path` options for `saveDocuments` and
`setStartupProjects`, repeated `--name` options for `projectProperties`, or
`--pathsJson '["C:/a.csproj"]'`. `--paramsJson` accepts the complete
parameter object. Option values are required.

## 3. Recommended session flow

1. Call `instances` and choose a PID.
2. Call `status`.
3. If needed, set breakpoints with `breakpointAdd`.
4. Start the application with `start`.
5. When execution stops, get `stackTrace`, `locals`, `arguments`, and Output.
6. Control execution with `stepOver`, `stepInto`, `stepOut`, or `continue`.
7. Query `status` again after every control command.
8. Optionally remove proxy breakpoints and call `stop` at the end.

Modes returned by `status`:

- `dbgDesignMode` — no active debugging session,
- `dbgRunMode` — the debugged program is running,
- `dbgBreakMode` — the program is stopped.

## 4. Read operations

### `capabilities` and `documentation`

```powershell
vscodex --pid 12345 capabilities
vscodex --pid 12345 documentation --level short
vscodex --pid 12345 documentation --level full
```

`capabilities` returns a machine-readable catalog of methods, parameters,
side effects, and warnings. `documentation` returns Markdown embedded in the
extension DLL. The `full` version is a copy of this document from the time the
VSIX was built and is the source of truth for the installed extension version.

### `ping`

No parameters. Returns the proxy version, the Visual Studio process PID, and
the current proxy session ID. The CLI keeps this as an explicit diagnostic
operation; it is not sent automatically before every other command.

### `status`

No parameters. Returns:

```json
{
  "mode": "dbgBreakMode",
  "currentProcess": "Application.exe",
  "currentThread": "Main Thread",
  "solution": "C:\\Project\\Application.sln"
}
```

### `stackTrace`

No parameters. Returns frames for the current thread: `index`, `depth`,
`isCurrent`, `function`, `module`, `moduleName`, `language`, `file`,
`line`, `column`, `userCode`, `threadId`, and `threadName`. `column`
is currently `null` because the DTE stack interface does not expose the
instruction column.

### `projects` (since 0.5.0)

```powershell
vscodex --pid 12345 projects
```

Returns `solution`, `isOpen`, `isFullyLoaded`, a `projects` array, the DTE
`startupProjects` selection, `startupProjectsAvailable`, and `readErrors`.
Each project has `id` (the solution GUID), `name`, `path`, `uniqueName`,
`typeGuid` (the DTE project type), `hierarchyTypeGuid` (the hierarchy item
type), `type` (file extension or `solutionFolder`), `isSolutionFolder`,
`isStartup`, and `loadState`: `loaded`, `unloaded`, or `failed`.

The `failed` state requires `IVsHierarchy.IsFaulted == true`. `loadError`
comes from `FaultMessage`; `loadErrorAvailability`, `loadErrorSource`, and
`loadErrorUnavailableReason` describe provider limitations. An unloaded
project is not automatically considered broken. `isStartup: null` means that
the DTE startup selection was unavailable.

### `diagnostics` (since 0.5.0)

```powershell
vscodex --pid 12345 diagnostics --severity error --count 200
vscodex --pid 12345 diagnostics --origin build --offset 0 --count 100
vscodex --pid 12345 diagnostics --origin project-load
vscodex --pid 12345 diagnostics --file C:\Project\app.html
```

Filters: `severity` = `error|warning|message|unknown`, `origin` =
`build|intellisense|project-load|unknown`, `project` = an exact name or GUID,
and `file` = a full path. Project and file comparisons are case-insensitive.
`offset` must be >= 0; `count` is 1–2000 (default 200). Filters are combined
with AND.

The response contains `items`, `offset`, `count`, `totalCount`, `hasMore`,
`isStable`, `complete`, `providers`, `readErrors`, `capturedUtc`,
`scope`, and `limitations`. An item contains `severity`, `origin`,
`source`, `sourceType`, `provider`, `project`, `projectId`, `file`,
`line`, `column`, `code`, and `message`; Error List entries additionally
contain `rawOrigin` and `buildTool`. Coordinates are one-based; unavailable
data is `null` or `unknown`.

The read includes Error List sources regardless of the visible window filters,
as well as current project hierarchy errors. The SDK category
`ErrorSource.Other` represents background compilation diagnostics and is mapped
to `intellisense`; this does not prove that a particular language service is
running. `project-load` errors do not receive guessed codes or file positions.
Entries from different sources may describe the same problem; duplicates are
not removed automatically.

Subscriptions are kept between calls. Initial results may arrive after the
first request has returned: check `isStable` and read again. `complete` means
that no read errors were detected, not that IDE analysis has finished.
Pagination describes the current read; data may change between pages. Reading
does not open documents or force another analysis.

### `launchCheck` (since 0.5.0)

```powershell
vscodex --pid 12345 launchCheck
```

Returns `canExecuteStartCommand` (true/false/null), `startAction`, `mode`,
`buildState`, `configuration`, `lastBuildFailedProjects` (when a build
finished), availability of both commands in `commands`, `reasons` with a
code, evidence, and confidence, `reasonUnknown`, `projectState`, and
`readErrors`. It does not start or build the application and does not change
the startup project.

In break mode, `Debug.Start` continues the existing session. `IsAvailable`
does not guarantee a successful start and does not expose Visual Studio's
internal refusal reason. `reasonUnknown` remains true for an unavailable or
unreadable command even when possible blockers were found. A missing DTE
project has `suspected` confidence because another launch provider may use a
different selection. Since 0.6.0, `launchProfilesAvailability: perProject`
indicates the separate `launchProfiles(project)` read. Availability and the
active profile depend on the project provider.

### `locals` and `arguments`

```powershell
vscodex --pid 12345 locals --frameIndex 0 --maxDepth 2 --maxItems 200
vscodex --pid 12345 arguments --frameIndex 0 --maxDepth 1 --maxItems 100
```

Parameters:

| Parameter | Default | Range | Meaning |
|---|---:|---:|---|
| `frameIndex` | 0 | >= 0 | zero-based stack frame index |
| `maxDepth` | 1 | 0–3 | `DataMembers` depth |
| `maxItems` | 100 | 1–500 | global limit of returned expressions |

An item contains `name`, `type`, `value`, `isValid`, and optional
`children` or `childrenError`. `truncated: true` means that a limit was
reached.

### `evaluate`

```powershell
vscodex --pid 12345 evaluate --expression "customer.Address.City" --timeoutMs 1000 --maxDepth 1 --maxItems 100
```

Required parameter: `expression`. Optional parameters are `timeoutMs`
(100–10000), `maxDepth`, and `maxItems`. Evaluation runs in the current
debugger frame. It can run getters, debug-display overloads, or methods. Use it
only deliberately; prefer `locals` and `arguments` for ordinary inspection.

### `activeDocument`

No parameters. Returns `available`, `name`, `path`, `line`, `column`,
`selectedText`, and `selectionTruncated`. Selection is limited to 100,000
characters.

### `output`

List panes:

```powershell
vscodex --pid 12345 output
```

Tail of a pane (backward-compatible form):

```powershell
vscodex --pid 12345 output Debug 20000
```

Character range:

```powershell
vscodex --pid 12345 output Debug --offset 10000 --count 5000
```

`count` must be in the range 1–200000. Without `offset`, the last `count`
characters are returned. The response contains `offset`, the actual `count`,
`totalChars`, `hasMoreBefore`, and `hasMoreAfter`.

Since 0.5.0, the list also contains `paneDetails: [{ name, id }]` with the
pane GUID. Use `--paneId <guid>` instead of the name; specifying both
selectors is an error. A read response contains `paneId` and `source`
(`textBuffer` or the `dte` fallback). An empty buffer returns
`text: ""`, `count: 0`, `totalChars: 0`; it is not read through a text
operation that requires a non-empty range. E_FAIL is not automatically treated
as an empty buffer. `outputReadFailed` contains a buffer/DTE read attempt and
the HRESULT in `details.attempts`. `outputPaneNotFound` is a separate error.
If Visual Studio has not created the buffer and DTE returns E_FAIL, the read may
temporarily activate the pane and then restore the previous pane, Output
visibility, and active window. The response reports this as
`paneInitialized: true`; restoration failures go to `restorationErrors`. If
no pane was selected previously, there is no selection to restore:
`previousPaneAvailable` and `paneSelectionRestored` are false. The read pane
remains selected while Output visibility and the active window are preserved.
Invalid limits have been rejected with `invalidParameters` since 0.5.0 rather
than being silently truncated.

### `breakpoints`

No parameters. Returns, among other fields, `id`, `index`, `enabled`,
`file`, `line`, `column`, `function`, `condition`, `conditionType`,
`currentHits`, `hitCount`, `hitCountType`, and `locationType`.

`id` is available for breakpoints created by the proxy. A manually created
breakpoint may have only an `index`.

## 5. Launch control

Operations without parameters:

| Operation | Action |
|---|---|
| `start` | starts the configured startup project with the debugger |
| `startWithoutDebugging` | starts the project without the debugger |
| `restart` | restarts the debugging session |
| `continue` | continues; in design mode it behaves like start |
| `break` | Break All |
| `stop` | ends debugging |
| `stepOver` | Step Over |
| `stepInto` | Step Into |
| `stepOut` | Step Out |

Before executing, the proxy checks `Command.IsAvailable`. An unavailable
command returns `ok: false`. `accepted: true` confirms that the command was
sent to Visual Studio, not that a new state was reached; confirm the state with
`status`.

Since 0.5.0, an unavailable command also returns `errorCode:
commandUnavailable` and suggests `launchCheck`. Handled exceptions preserve
the `error` text and add `errorCode`, `hresult`, and `details`. Some older
error responses do not have these fields.

## 6. Breakpoint management

### Adding — `breakpointAdd`

Exactly one location type must be supplied:

- `file` and optional `line` (default 1) and `column` (default 1),
- `function`,
- `data` and optional `dataCount`,
- `address`.

Common parameters:

- `condition`: condition text,
- `conditionType`: `whenTrue` or `whenChanged`,
- `language`: optional language name,
- `hitCount`: hit count,
- `hitCountType`: `none`, `equal`, `greaterOrEqual`, or `multiple`,
- `enabled`: default `true`.

```powershell
vscodex --pid 12345 breakpointAdd --file C:\Project\Program.cs --line 42 --condition "retryCount > 2" --conditionType whenTrue
```

The response is an array because Visual Studio can create several breakpoints,
for example for an overloaded function. Each receives a separate stable `id`.

### Enable and disable — `breakpointSetEnabled`

```powershell
vscodex --pid 12345 breakpointSetEnabled --id <id> --enabled false
vscodex --pid 12345 breakpointSetEnabled --index 0 --enabled true
```

### Criteria — `breakpointSetCriteria`

```powershell
vscodex --pid 12345 breakpointSetCriteria --id <id> --condition "retryCount > 5" --conditionType whenTrue --hitCount 3 --hitCountType greaterOrEqual
```

All criteria parameters are optional; omitted values retain their current
values. An empty `condition` removes the condition. Criteria changes are
supported for file and function breakpoints. DTE cannot change these fields
directly, so the proxy removes and recreates the breakpoint while preserving its
state and ID.

### Remove — `breakpointRemove`

```powershell
vscodex --pid 12345 breakpointRemove --id <id>
vscodex --pid 12345 breakpointRemove --index 0
```

Prefer `id`. `index` is zero-based and unstable after an item is added or
removed.

## 7. Raw named-pipe protocol

The client sends one JSON object as one UTF-8 line:

```json
{"id":"abc","method":"locals","params":{"frameIndex":0,"maxDepth":1}}
```

The server responds with one JSON line. Method and parameter names are
case-sensitive. The server handles one connected client at a time and accepts
the next client after disconnection.

## 8. Projects, operations, and inspection since 0.6.0

### Documents and launch configuration

| Method | Parameters | Behavior |
|---|---|---|
| `documents` | none | RDT documents, path, project, and `dirty: true/false/null`. `null` does not mean a clean document. |
| `saveDocuments` | `paths: string[]` | Explicitly saves selected open documents, without Save As; per-file results and `allSaved`. Saving multiple files is not a transaction. |
| `projectReload` | `path` | Design mode, no build, fully loaded solution. Blocks every dirty or unreadable document, including shared documents. Loaded: unload/load; unloaded/failed: reload. `loaded` describes the readback result. |
| `startupProjects` | none | `paths`, `uniqueNames`, availability, and DTE source. |
| `setStartupProjects` | `paths: string[]` | Validates the complete list and loaded state, sets order, and verifies readback. On failure it attempts rollback and returns the actual state. |
| `launchProfiles` | `project: path\|guid` | Profiles and the active menu selection through `IVsProjectCfgDebugTargetSelection`, including CPS export. `configuredProfiles` comes separately from files and does not prove active selection. |
| `selectLaunchProfile` | `project`, `name` | Only an existing, unambiguous profile in idle state. `accepted` confirms acceptance; `applied` confirms readback. CPS publishes asynchronously: read `launchProfiles` again. |
| `solutionLaunchProfiles` | none | Shared/user `.slnLaunch` profiles with active name, actions, order, and debug targets. Live read through the versioned Visual Studio 2026 adapter; compatibility is explicit. |
| `selectSolutionLaunchProfile` | `name`, `scope?` | Sets the native profile and verifies every action, order, and target. `scope` disambiguates shared/user profiles with the same name; failures trigger rollback. |
| `projectProperties` | `project`, `names: string[]` | Active-configuration properties through `IVsBuildPropertyStorage`; errors are reported per property. |
| `configurations` | none | Solution configurations and platforms, plus the current selection. |

`launchProfiles` distinguishes values computed by MSBuild, configuration stored
on disk, and the final process command. It does not pretend to know the final
debug-adapter substitutions. Profile environment variables are not returned.

### Operations and events

`build`, `rebuild`, `clean`, `start`, `startWithoutDebugging`, and
`restart` return `accepted` and an `operationId`.
`operationStatus --id <id>` returns `queued`, `running`, `succeeded`,
`failed`, `cancelled`, or `unknown`; phase is a separate field. Builds are
confirmed by `IVsUpdateSolutionEvents`, and debugger starts by run/break
events. This is not an HTTP readiness test. For Ctrl+F5, failure to confirm a
process produces `unknown`. No finishing event for two minutes produces
`unknown`, without cancelling Visual Studio work. `cancelBuild` uses the
actual cancellation support.

Only one operation is tracked at a time. `exclusiveCommandWindow` correlation
means that the operation is associated with a command sent in the window; it is
not a Visual Studio transaction ID. A build without an active operation has
source `external`. Manual user actions while waiting can make correlation
difficult. The registry keeps at most 128 operations for one hour in one proxy
session.

`events --afterSequence 0 --limit 100 [--sessionId <id>]` returns up to 500
events from a 512-event buffer. The next cursor is `nextSequence`. Check
`historyLost` and `sessionChanged`. History includes build, debugger,
exception, proxy changes, and available engine breakpoint binding/error events.
These are events observed since subscription; earlier history is not replayed
and there is no push channel.

When retrying a mutation, use the same `idempotencyKey`. The proxy remembers up
to 256 responses for one hour; the key belongs to the proxy session. The same
key with different parameters returns `idempotencyConflict`. `replayed: true`
returns the original response, so read the current operation result with
`operationStatus`. After a proxy restart or key expiry, check the IDE state
first; deduplication is not durable.

```powershell
vscodex --pid 12345 build --wait true --idempotencyKey build-001
vscodex --pid 12345 start --wait true --idempotencyKey start-001
vscodex --pid 12345 waitForState --state break --waitTimeoutMs 60000
vscodex --pid 12345 waitForState --operationId <id> --waitTimeoutMs 120000
```

### Variables, breakpoints, and context

`scopes --frameIndex 0` returns Locals/Arguments handles.
`variables --reference <id> --offset 0 --count 100` pages children (limit
500). `hasChildren` is independent of `isValid`: JS Module/Global scopes
often have no scalar value. Handles expire after step, continue, restart, the
end of a session, or a context change; using an old one returns
`staleReference`. There is no implicit `GetExpression`, but DTE enumeration
may trigger adapter-side evaluation.

`processes` returns debugged processes, `threads` returns current program
threads in break mode, and `selectContext --threadId <id> --frameIndex 0`
changes the current thread/frame. `stopReason` reports the observed stop
reason and available exception information.

Contract 2: `breakpoints.currentHits` may be `null` when reading it failed.
Even a non-empty value has `currentHitsReliability:
unverifiedAdapterCounter`; JS can return zero after a hit. `observedHits`
counts stops observed through `DTE.AllBreakpointsLastHit` since
`observedSinceUtc`, excluding hits before the proxy started and tracepoints
that did not stop. `bindingState` and `boundLocations` come from DTE.
Historical `bindingEvents` contain engine errors and resolved locations and are
matched by file/line, not breakpoint identity. An older error may precede a
successful binding. The complete source-map is still `unavailable`; a resolved
TypeScript location is not the complete mapping.

### Document diagnostics, snapshots, and terminals

`documentDiagnostics --path <file>` filters Error List and adds editor state.
`languageServiceStatus --path <file>` returns content type, language-service
GUID, dirty state, and the open document snapshot version. It does not open a
document for the user. The active server version and the relationship between
diagnostic versions and snapshots are unavailable. HTML content type or no
errors does not prove that Angular Language Service is running.

`snapshot` collects status, projects, launchCheck, documents, 200 diagnostics,
and the last 4000 characters of Build/Debug. Sections have read times and
errors; a complete IDE read is not atomic.

`terminals` explicitly reports `unsupported` for existing JSPS terminals, and
`terminalOutput` returns an `unsupported` error. The verified Visual Studio
2026 API `ITerminalService.GetTerminalGuidsAsync` enumerates only terminals
created by that service client. It does not expose history, commands, or exit
codes for an existing Angular terminal. The proxy does not replace those data
with Output-pane text and does not intercept processes started elsewhere.

Other limitations: `evaluate` operates in the current frame, the DTE stack
does not provide a column, and changing criteria for data/address breakpoints
is still unsupported. Fault, profile, and engine data availability depends on
the provider. Full solution reload, process selection, Test Explorer, and HTTP
readiness control are outside this version.

## 9. Building and updating

```powershell
dotnet build .\src\VsCodexProxy.slnx
```

Output:

```text
src\VsCodexProxy\bin\Debug\net472\VsCodexProxy.vsix
```

After changing the extension, install the new VSIX and restart Visual Studio.
Current manifest version: `0.6.1`.
