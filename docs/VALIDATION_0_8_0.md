# Validation 0.8.0 — central Hub RPC

Verified on 2026-09-28, Visual Studio 2026 18.10, isolated `HubMenuProbe` profile.
Hub 1.5.0 and VsAgentProxy 0.8.0 were deployed together; the .NET 10 CLI ran in
a separate process. The normal working VS instance was not modified.

- Release solution build: no warnings or errors; existing unit tests: 53/53 passed.
- `instances`: Hub ready, VsAgent ready, pipe `VsExtensionsHub-23040`.
- `hubStatus`: correct VS PID and Hub version, three published services:
  Hub status, Solution Explorer, VsAgent.
- `status`, `capabilities`, `documentation --level short`: successful RPC responses;
  VsAgent reports 0.8.0 and the new transport.
- `solutionTree` and `selection`: actual solution data and nested file DTOs.
- `watchSelection`: initial solution-folder snapshot, followed by an event-driven
  snapshot of `Models/Nested/NestedModel.cs`. Concurrent status/build clients worked.
- `build --wait true --idempotencyKey hub-rpc-test-001`: succeeded, confirmed by
  `IVsUpdateSolutionEvents.UpdateSolution_Done`.
- Repeating that idempotency key returned `replayed: true` and the same operation ID.
- Own test VS instance closed normally after validation.

Hub's automated cross-process tests additionally cover unpublished services,
blocked external registration, callback responses, cancellation, simultaneous
clients, withdrawal of a service and transport shutdown. They use the production
Hub endpoint and client libraries with a .NET Framework server and .NET 8 client.

This validates migration of transport and representative operations. It does not
repeat the full debugger/JS adapter matrix from previous validation reports.
The CLI's JSON operation envelope remains contract version 2; old JSON-lines
clients cannot connect to Hub and must be upgraded with the extension.
