[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Model,
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$Offline,
    [string]$Filter
)

$ErrorActionPreference = 'Stop'
$testEnvironment = . (Join-Path $PSScriptRoot 'Initialize-Workspace.ps1') -Model:$Model -ForTests
$testSolutionName = if ($Model) { 'SmartSql.LocalAssistant.slnx' } else { 'SmartSQL.sln' }
$testArguments = @('test', (Join-Path $testEnvironment.ArtifactRoot $testSolutionName),
    '--configuration', $Configuration, '--nologo',
    '--results-directory', $testEnvironment.TestResultsDirectory,
    '--logger', 'trx',
    ('-p:RestoreConfigFile=' + (Join-Path $testEnvironment.WorkspaceRoot 'NuGet.Config')))
if ($NoBuild) { $testArguments += '--no-build' }
if ($NoRestore -or $Offline -or $NoBuild) { $testArguments += '--no-restore' }
if (![string]::IsNullOrWhiteSpace($Filter)) { $testArguments += @('--filter', $Filter) }

Push-Location -LiteralPath $testEnvironment.ArtifactRoot
try {
    if ($Offline -and !$NoRestore -and !$NoBuild) {
        & dotnet restore (Join-Path $testEnvironment.ArtifactRoot $testSolutionName) `
            --configfile (Join-Path $testEnvironment.WorkspaceRoot 'NuGet.Config') `
            --source $env:NUGET_PACKAGES --disable-parallel -p:NuGetAudit=false --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Offline restore failed with exit code $LASTEXITCODE." }
    }
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
