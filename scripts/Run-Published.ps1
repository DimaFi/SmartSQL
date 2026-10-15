[CmdletBinding()]
param(
    [string]$BuildDirectory,
    [string]$DataDirectory,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'

# Standalone copy: the published launcher cannot depend on source-workspace helpers.
function Assert-DeliveryPath {
    param([Parameter(Mandatory)][string]$Path)
    if (![IO.Path]::IsPathFullyQualified($Path)) { throw "An absolute path on D: is required: $Path" }
    $checkedPath = [IO.Path]::GetFullPath($Path)
    if ([IO.Path]::GetPathRoot($checkedPath) -ine 'D:\') { throw "Delivery/runtime paths must remain on D: $checkedPath" }
    while (![string]::IsNullOrEmpty($checkedPath)) {
        try {
            $checkedAttributes = [IO.File]::GetAttributes($checkedPath)
            if (($checkedAttributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points/junctions are not accepted in delivery/runtime paths: $checkedPath"
            }
        }
        catch [IO.FileNotFoundException] { }
        catch [IO.DirectoryNotFoundException] { }
        # All other errors, including denied attribute access, fail closed.
        $checkedParent = [IO.Directory]::GetParent($checkedPath)
        $checkedPath = if ($null -eq $checkedParent) { $null } else { $checkedParent.FullName }
    }
}
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $publishedApplication = $PSScriptRoot
}
else {
    if (![IO.Path]::IsPathFullyQualified($BuildDirectory)) { throw 'BuildDirectory must be absolute.' }
    $publishedApplication = Join-Path ([IO.Path]::GetFullPath($BuildDirectory)) 'app'
}
Assert-DeliveryPath $publishedApplication
$publishedExecutable = Join-Path $publishedApplication 'SmartSQL.Desktop.exe'
Assert-DeliveryPath $publishedExecutable
if (!(Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) { throw "Published executable not found: $publishedExecutable" }
if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
    # Published builds use <build>/app; a ZIP extracted directly to its own folder
    # keeps runtime data inside that folder, never a shared parent such as D:\Apps.
    $publishedRuntimeRoot = if ([IO.Path]::GetFileName($publishedApplication) -ieq 'app') {
        Split-Path -Parent $publishedApplication
    } else { $publishedApplication }
    $DataDirectory = Join-Path $publishedRuntimeRoot 'run-data/local'
}
if (![IO.Path]::IsPathFullyQualified($DataDirectory)) { throw 'DataDirectory must be an absolute directory on D:.' }
$publishedData = [IO.Path]::GetFullPath($DataDirectory)
if ([IO.Path]::GetPathRoot($publishedData) -ine 'D:\' -or $publishedData.TrimEnd('\') -ieq 'D:') { throw 'Runtime data must be in a named directory on D:.' }
$publishedTemp = Join-Path $publishedData 'temp'
Assert-DeliveryPath $publishedData
Assert-DeliveryPath $publishedTemp
Assert-DeliveryPath (Join-Path $publishedData 'dotnet-bundles')
Assert-DeliveryPath (Join-Path $publishedData 'cache')
$publishedEnvironment = [ordered]@{
    SMARTSQL_DATA_DIRECTORY = $publishedData
    SMARTSQL_TEMP_DIRECTORY = $publishedTemp
    TEMP = $publishedTemp; TMP = $publishedTemp; TMPDIR = $publishedTemp
    DOTNET_BUNDLE_EXTRACT_BASE_DIR = (Join-Path $publishedData 'dotnet-bundles')
    XDG_CACHE_HOME = (Join-Path $publishedData 'cache')
    AVALONIA_TELEMETRY_OPTOUT = '1'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
}
$publishedPrevious = @{}
try {
    [IO.Directory]::CreateDirectory($publishedData) | Out-Null
    [IO.Directory]::CreateDirectory($publishedTemp) | Out-Null
    foreach ($entry in $publishedEnvironment.GetEnumerator()) {
        $publishedPrevious[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    # This is the requested interactive application; the child inherits only process-local settings.
    $publishedProcess = Start-Process -FilePath $publishedExecutable -WorkingDirectory $publishedApplication -PassThru
    Write-Host "SmartSQL PID: $($publishedProcess.Id); isolated data: $publishedData"
    if ($Wait) {
        $publishedProcess.WaitForExit()
        if ($publishedProcess.ExitCode -ne 0) { throw "SmartSQL exited with code $($publishedProcess.ExitCode). Data preserved: $publishedData" }
    }
    $publishedProcess
}
finally {
    foreach ($entry in $publishedPrevious.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
