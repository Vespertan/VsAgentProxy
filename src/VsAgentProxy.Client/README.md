# VsAgentProxy.Client

`VsAgentProxy.Client` is the .NET global tool client for the VS Agent Proxy
Visual Studio extension.

It connects to a running Visual Studio instance through the extension's local
Named Pipe and exposes the `vsagent` command for debugger inspection, project
and launch-profile management, diagnostics, Output access, and execution control.

## Requirements

- Visual Studio with the VS Agent Proxy extension installed and running.
- .NET 10 SDK or runtime for the global tool.

## Install

```powershell
dotnet tool install --global VsAgentProxy.Client
```

## Quick start

```powershell
vsagent instances
vsagent --version
vsagent --pid <PID> status
```

Use the repository documentation for the complete protocol and command reference:

https://github.com/Vespertan/VsAgentProxy

## Optional agent skill

The optional skill teaches an agent how to use the Visual Studio proxy. Install it
at:

```text
%USERPROFILE%\.agents\skills\vsagent\SKILL.md
```

PowerShell installation:

```powershell
$skillDir = Join-Path $env:USERPROFILE '.agents\skills\vsagent'
New-Item -ItemType Directory -Force $skillDir | Out-Null
Invoke-WebRequest `
  -Uri 'https://raw.githubusercontent.com/Vespertan/VsAgentProxy/main/.agents/skills/vsagent/SKILL.md' `
  -OutFile (Join-Path $skillDir 'SKILL.md')
```

Source file: [`.agents/skills/vsagent/SKILL.md`](https://github.com/Vespertan/VsAgentProxy/blob/main/.agents/skills/vsagent/SKILL.md)
