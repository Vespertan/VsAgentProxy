---
name: vscodex
description: Use when working with a running Visual Studio instance through vscodex or its local Named Pipe protocol for debugging, projects, launch profiles, diagnostics, Output, and execution control.
---

# Visual Studio through vscodex

Use the `vscodex` CLI as the default client. The proxy also exposes a local
Named Pipe for direct JSON-lines communication.

## Discover and select an instance

1. Run `vscodex instances`. The client enumerates `\\.\pipe\` and filters
   `VsCodexProxy-{PID}`.
2. Use `--pid <PID>` when more than one Visual Studio instance is running.
3. Use `vscodex --pid <PID> ping` explicitly when proxy version or session
   information is needed.
4. Use `vscodex --version` to read the client version from its assembly.

## State and debugging

Before and after a state-changing operation, query `status`.

Use `stackTrace`, `locals`, and `arguments` when the debugger is stopped.
Prefer `locals` and `arguments` over `evaluate`; evaluation may execute
getters, methods, or debug display code.

Use stable breakpoint `id` values returned by the proxy. Manual breakpoint
indexes are unstable after additions or removals.

## Projects and launch configuration

Use `projects`, `configurations`, `startupProjects`, and `launchCheck` to
inspect the solution. Use `launchProfiles` and `selectLaunchProfile` for a
project, and `solutionLaunchProfiles` and `selectSolutionLaunchProfile` for
`.slnLaunch` solution profiles. Read the selection again after changing it.

## Asynchronous operations

Build and launch commands return an `operationId`. Use `--wait true` or
`operationStatus` and treat `accepted` as dispatch confirmation only.
Retry the same mutation with the same `idempotencyKey`.

## Output and diagnostics

Read Output in bounded ranges with `--offset` and `--count`. Use
`diagnostics` and `documentDiagnostics` with filters; check `isStable` before
treating diagnostics as final.

## Direct Named Pipe protocol

Enumerate `\\.\pipe\VsCodexProxy-*`, parse the PID from the pipe name, and
connect to the selected pipe. Send one UTF-8 JSON object per line and read one
JSON response per line. Method and parameter names are case-sensitive.

Transport failures (pipe unavailable, disconnect, timeout) are different from
proxy errors where the response contains `ok: false`. The complete method
contract and limitations are available through:

```powershell
vscodex --pid <PID> documentation --level full
```
