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

## Optional Codex skill

The optional skill teaches Codex how to use the Visual Studio proxy. Install it
at:

```text
%USERPROFILE%\.agents\skills\vscodex\SKILL.md
```

PowerShell installation:

```powershell
$skillDir = Join-Path $env:USERPROFILE '.agents\skills\vscodex'
New-Item -ItemType Directory -Force $skillDir | Out-Null
Invoke-WebRequest `
  -Uri 'https://raw.githubusercontent.com/Vespertan/VsCodexProxy/main/.agents/skills/vscodex/SKILL.md' `
  -OutFile (Join-Path $skillDir 'SKILL.md')
```

Source file: [`.agents/skills/vscodex/SKILL.md`](https://github.com/Vespertan/VsCodexProxy/blob/main/.agents/skills/vscodex/SKILL.md)
