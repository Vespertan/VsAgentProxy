---
name: vsagent
description: Use when working with a running Visual Studio instance through vsagent or its local Named Pipe protocol for debugging, projects, launch profiles, diagnostics, Output, and execution control.
---

# Visual Studio through vsagent

Use the `vsagent` CLI as the default client. The CLI connects to the central
Vespertan Extensions Hub RPC endpoint (Hub 1.5+, proxy/client 0.8+).

## Start

1. Run `vsagent instances` and select the Visual Studio PID. It enumerates
   `VsExtensionsHub-{PID}` named pipes and checks Hub/VsAgent readiness.
2. Check the selected instance:

   ```powershell
   vsagent --pid <PID> status
   vsagent --pid <PID> capabilities
   vsagent --pid <PID> documentation --level short
   ```

`capabilities` is the machine-readable method catalog. `vsagent --version`
reports the client assembly version. The short documentation
is the version-specific quick guide embedded in the running extension. Use
`documentation --level full` only when method parameters or protocol details
are missing from the quick guide. Use `ping` explicitly for proxy diagnostics;
the CLI does not ping before every operation.

## Operating rules

- Query `status` before and after operations that change state.
- Treat `accepted` as dispatch confirmation; follow the returned `operationId`
  with `--wait true` or `operationStatus`.
- Use `stackTrace`, `locals`, and `arguments` when the debugger is stopped.
- Prefer `locals` and `arguments` over `evaluate`; evaluation may execute
  getters, methods, or debuggee code.
- Use stable breakpoint `id` values returned by the proxy.
- Read large Output and diagnostic results in bounded ranges.

Use `capabilities` and the runtime documentation for project, startup-project,
launch-profile, diagnostics, and Output method names and parameters.

For Solution Explorer selection use `selection`, or `watchSelection --durationMs 60000`
for an initial snapshot and subsequent events. These call Hub directly, without VsAgentProxy.
If the installed CLI is older, use the matching client from this repository.
Do not send JSON lines directly to the pipe: it carries multiplexed broker RPC.
