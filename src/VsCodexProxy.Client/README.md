# VsCodexProxy.Client

`VsCodexProxy.Client` is the .NET global tool client for the VS Codex Proxy
Visual Studio extension.

It connects to a running Visual Studio instance through the extension's local
Named Pipe and exposes the `vscodex` command for debugger inspection, project
and launch-profile management, diagnostics, Output access, and execution control.

## Requirements

- Visual Studio with the VS Codex Proxy extension installed and running.
- .NET 10 SDK or runtime for the global tool.

## Install

```powershell
dotnet tool install --global VsCodexProxy.Client
```

## Quick start

```powershell
vscodex instances
vscodex --version
vscodex --pid <PID> status
```

Use the repository documentation for the complete protocol and command reference:

https://github.com/Vespertan/VsCodexProxy
