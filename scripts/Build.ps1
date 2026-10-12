[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Model,
    [switch]$NoRestore,
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$buildEnvironment = . (Join-Path $PSScriptRoot 'Initialize-Workspace.ps1') -Model:$Model
$buildSolutionName = if ($Model) { 'SmartSql.LocalAssistant.slnx' } else { 'SmartSQL.sln' }
$buildArguments = @('build', (Join-Path $buildEnvironment.ArtifactRoot $buildSolutionName),
    '--configuration', $Configuration, '--nologo',
    ('-p:RestoreConfigFile=' + (Join-Path $buildEnvironment.WorkspaceRoot 'NuGet.Config')))
if ($NoRestore -or $Offline) { $buildArguments += '--no-restore' }

Push-Location -LiteralPath $buildEnvironment.ArtifactRoot
try {
    if ($Offline -and !$NoRestore) {
        # Explicit offline runs use only the prepared package cache. Security
        # auditing is skipped here because contacting its remote feed is unavailable.
        & dotnet restore (Join-Path $buildEnvironment.ArtifactRoot $buildSolutionName) `
            --configfile (Join-Path $buildEnvironment.WorkspaceRoot 'NuGet.Config') `
            --source $env:NUGET_PACKAGES --disable-parallel -p:NuGetAudit=false --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Offline restore failed with exit code $LASTEXITCODE." }
    }
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
