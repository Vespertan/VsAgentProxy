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
if (-not $msbuild)
{
    throw 'Visual Studio 2026 18.5+ MSBuild is required.'
}
$contractsProject = Join-Path $HubRoot 'src/Vespertan.VisualStudio.Contracts/Vespertan.VisualStudio.Contracts.csproj'
if (-not (Test-Path -LiteralPath $contractsProject -PathType Leaf))
{
    throw "Hub Contracts project not found: $contractsProject. Specify the Hub checkout with -HubRoot."
}
& dotnet pack $contractsProject -c $Configuration -o $feed --nologo -v:q
if ($LASTEXITCODE -ne 0)
{
    throw 'Hub Contracts package failed.'
}
& $msbuild (Join-Path $PSScriptRoot 'src/Vespertan.VisualStudio.AgentProxy.slnx') /restore "/p:Configuration=$Configuration" /p:DeployExtension=false /nologo /v:minimal
if ($LASTEXITCODE -ne 0)
{
    throw 'Vespertan.VisualStudio.AgentProxy build failed.'
}
if ($Test)
{
    foreach ($project in @('Vespertan.VisualStudio.AgentProxy.Tests', 'Vespertan.VisualStudio.AgentProxy.Client.Tests'))
    {
        & dotnet test (Join-Path $PSScriptRoot "tests/$project/$project.csproj") -c $Configuration --no-build --no-restore --nologo
        if ($LASTEXITCODE -ne 0)
        {
            throw "$project tests failed."
        }
    }
}
