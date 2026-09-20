---
name: vsagent
description: Use when working with a running Visual Studio instance through vsagent or its local Named Pipe protocol for debugging, projects, launch profiles, diagnostics, Output, and execution control.
---

# Visual Studio through vsagent

Use the `vsagent` CLI as the default client. The proxy also exposes a local
Named Pipe when direct JSON-lines communication is required.

## Start

1. Run `vsagent instances` and select the Visual Studio PID. It enumerates
   `VsAgentProxy-{PID}` named pipes.
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

If the CLI is unavailable, enumerate `\\.\pipe\VsAgentProxy-*` and connect to
the selected pipe. Send one UTF-8 JSON object per line and read one response
per line.
