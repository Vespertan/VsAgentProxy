[CmdletBinding()]
param(
    [string]$HubRoot = (Join-Path $PSScriptRoot '../Vespertan/VsHub'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$feed = Join-Path $PSScriptRoot 'artifacts/feed'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -version '[18.5,)' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio 2026 18.5+ MSBuild is required.' }
foreach ($project in @('Contracts', 'Vsct')) {
    & dotnet pack (Join-Path $HubRoot "src/Vespertan.VisualStudio.$project/Vespertan.VisualStudio.$project.csproj") -c $Configuration -o $feed --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "Hub package failed: $project" }
}
& $msbuild (Join-Path $PSScriptRoot 'src/VsAgentProxy.slnx') /restore "/p:Configuration=$Configuration" /p:DeployExtension=false /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'VsAgentProxy build failed.' }
if ($Test) {
    & dotnet test (Join-Path $PSScriptRoot 'src/VsAgentProxy.Tests/VsAgentProxy.Tests.csproj') -c $Configuration --no-build --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'VsAgentProxy tests failed.' }
}
