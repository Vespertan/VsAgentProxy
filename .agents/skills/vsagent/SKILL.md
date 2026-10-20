---
name: vsagent
description: Inspect and control a running Visual Studio instance through the vsagent CLI for debugging, builds, diagnostics, Output, documents, projects, launch profiles, and Solution Explorer. Use for live IDE work through VS Agent Proxy, not ordinary source editing or VS Code.
---

# Visual Studio through vsagent

Use the `vsagent` CLI. Visual Studio must be running with Vespertan Extensions
Hub and VS Agent Proxy installed. For the current 0.8 client/proxy, Hub 1.7+
owns the multiplexed RPC endpoint `VisualStudio.Hub-{PID}`; the proxy provides
the brokered service `Vespertan.VisualStudio.AgentProxy/1.0`.

## Start

1. Run `vsagent instances`. Check Hub and VsAgent readiness separately, then
   select the PID whose solution matches the task. Without `--pid`, the client
   selects the newest instance; use an explicit PID for subsequent commands.
2. Inspect that instance and discover the installed extension's contract:

   ```powershell
   vsagent --pid <PID> status
   vsagent --pid <PID> capabilities
   vsagent --pid <PID> documentation --level short
   ```

The runtime `capabilities` and embedded `documentation` are the source of truth
for supported methods, parameters, side effects, and version differences. Read
`documentation --level full` when the short guide lacks the needed details.
Use `vsagent --version` for the client version and `ping` for the proxy version
and session ID when diagnosing compatibility or retrying an uncertain mutation.

If the global tool is unavailable or incompatible and this repository is
available, run the matching built client from its root:

```powershell
dotnet run --project .\src\Vespertan.VisualStudio.AgentProxy.Client --no-build -- instances
dotnet run --project .\src\Vespertan.VisualStudio.AgentProxy.Client --no-build -- --pid <PID> status
```

If the client has not been built, build it when within task scope. If the required
extension is absent, report that prerequisite; retrying commands will not install it.

## Operating rules

- Query `status` before a mutation to confirm the target and debugger mode,
  then read back the affected state. Verify `status` after execution control.
- `dbgDesignMode` means no debugging session, `dbgRunMode` means running, and
  `dbgBreakMode` means stopped. Stepping, `locals`, and `arguments` require
  break mode. In break mode, `start` continues the existing debugging session.
- For `build`, `rebuild`, `clean`, `start`, `startWithoutDebugging`, and `restart`,
  use `--wait true` on the command or follow its `operationId` with
  `operationStatus --id <id>`. `accepted: true` confirms dispatch, not completion
  or a breakpoint hit. Debugger start confirmation does not prove HTTP readiness.
- A timeout does not cancel Visual Studio work. Inspect the operation and IDE
  before retrying. Reuse the same `idempotencyKey` with identical parameters in
  the same proxy session; after a restart or key expiry, deduplication is lost.
  A replay returns the original response; read `operationStatus` for its outcome.
- Prefer `locals` and `arguments` over `evaluate`; evaluation may execute
  getters, methods, or debuggee code in the current frame.

## Inspect and debug

After `status` confirms break mode, inspect a bounded amount of data:

```powershell
vsagent --pid <PID> stackTrace
vsagent --pid <PID> locals --frameIndex 0 --maxDepth 1 --maxItems 100
vsagent --pid <PID> arguments --frameIndex 0 --maxDepth 1 --maxItems 100
vsagent --pid <PID> stopReason
```

Choose `stepOver`, `stepInto`, `stepOut`, `continue`, `break`, or `stop` according
to the task and current mode, then check `status`. Use `waitForState --state break`
with a bounded `--waitTimeoutMs` when waiting for a stop is necessary.

- For JS/TS scopes, use `scopes --frameIndex 0`, then page `variables --reference
  <id> --offset 0 --count 100`. `isValid: false` does not exclude children.
  Handles expire after stepping, continuing, restarting, or changing context;
  reacquire them on `staleReference`.
- Use stable breakpoint `id` values returned by `breakpointAdd`. For manual
  breakpoints, refresh `breakpoints` before using their unstable `index`.
- `observedHits` counts stops observed by the proxy. `currentHits` is an
  unverified adapter counter. Interpret `bindingEvents` with their timestamps;
  a historical error may precede successful binding.

## Diagnose launch and build failures

Read `launchCheck`, `projects`, and `diagnostics --severity error --offset 0
--count 200`. Check `isStable` and `readErrors`; an empty first read does not
prove the absence of errors. Resolve uncertain readings with another bounded
read or the relevant Output pane.

List panes with `output`, then select by `paneDetails` GUID using `output
--paneId <guid> --count 5000`. Page larger text with `--offset` and `--count`.
Pane IDs avoid ambiguity from localized or duplicate names.

## Documents, projects, and launch selection

- Before `projectReload`, read `documents`. Treat `dirty: null` as unknown.
  Save only files selected for the task with `saveDocuments --path <file>`
  (repeat `--path` for multiple files). Check `allSaved` and per-document errors
  even when `ok: true`; never automatically save all open documents.
- After `selectLaunchProfile`, verify `launchProfiles` for that project;
  publication is asynchronous and `accepted` alone does not prove selection.
  After other project or launch mutations, verify the corresponding readback.
- `activeDocument` describes editor selection. `selection`, `solutionTree`, and
  bounded `watchSelection --durationMs 60000` describe Solution Explorer and
  call Hub directly, even when VsAgentProxy is unavailable. Watch events are
  coalesced snapshots, not a lossless history.

## Errors and transport

CLI exit codes: `0` means an `ok: true` response; `1` means a protocol error or,
with `--wait true`, a failed/cancelled operation; `2` means no matching instance;
`3` means a connection error or response timeout; `4` means a wait timeout or
an unconfirmed (`unknown`) operation. Inspect response fields and domain results
as well as the exit code.

If the CLI is too old, use the matching repository client. Do not send raw JSON
lines to a Hub pipe; it carries multiplexed broker RPC. A custom client must
use HubConnection and the typed `IAgentService` contract. For implementation
details, consult `docs/AGENT_PROTOCOL.md` when this repository is available.

Some providers expose only partial data. Existing JSPS terminal history and
terminal output are unsupported; Output-pane text cannot substitute for them.
Report these limitations instead of inferring unavailable state.
