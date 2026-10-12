[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$Offline,
    [switch]$Detached,
    [string]$DataDirectory
)

$ErrorActionPreference = 'Stop'
$runEnvironment = . (Join-Path $PSScriptRoot 'Initialize-Workspace.ps1')
if (!$NoBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -Offline:$Offline
}
if (![string]::IsNullOrWhiteSpace($DataDirectory)) {
    if (![IO.Path]::IsPathFullyQualified($DataDirectory)) { throw 'DataDirectory must be an absolute path.' }
    $env:SMARTSQL_DATA_DIRECTORY = [IO.Path]::GetFullPath($DataDirectory)
}
$runAssembly = Join-Path $runEnvironment.WorkspaceRoot "src/SmartSQL.Desktop/bin/$Configuration/net10.0/SmartSQL.Desktop.dll"
if (!(Test-Path -LiteralPath $runAssembly -PathType Leaf)) {
    throw "The desktop application has not been built: $runAssembly"
}
if ($Detached) {
    $runExecutable = Join-Path $runEnvironment.WorkspaceRoot "src/SmartSQL.Desktop/bin/$Configuration/net10.0/SmartSQL.Desktop.exe"
    if (!(Test-Path -LiteralPath $runExecutable -PathType Leaf)) {
        throw "The desktop executable has not been built: $runExecutable"
    }
    # The child inherits the current process environment; no machine settings change.
    Start-Process -FilePath $runExecutable -WorkingDirectory $runEnvironment.WorkspaceRoot -WindowStyle Hidden -PassThru
    return
}

Push-Location -LiteralPath $runEnvironment.WorkspaceRoot
try {
    & dotnet $runAssembly
    if ($LASTEXITCODE -ne 0) { throw "Desktop application failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
