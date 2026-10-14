[CmdletBinding()]
param(
    [string]$WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ModelRoot,
    [switch]$Model,
    [switch]$ForTests
)

$ErrorActionPreference = 'Stop'

# The compatibility switch delegates to the isolated workspace. Model builds
# must not create caches or data in the desktop repository.
if ($Model) {
    if ([string]::IsNullOrWhiteSpace($ModelRoot)) {
        $ModelRoot = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($WorkspaceRoot))) 'SmartSQL-model'
    }
    $modelInitializer = Join-Path ([IO.Path]::GetFullPath($ModelRoot)) 'scripts/Initialize-Workspace.ps1'
    if (!(Test-Path -LiteralPath $modelInitializer -PathType Leaf)) {
        throw "Model-local workspace scripts are missing: $modelInitializer"
    }
    return (. $modelInitializer -ForTests:$ForTests)
}

# This changes only the current process and its children. Installed SDKs and
# Codex remain where they are installed; project-created files stay in this workspace.
$workspaceResolvedRoot = (Resolve-Path -LiteralPath $WorkspaceRoot).Path
if (!(Test-Path -LiteralPath (Join-Path $workspaceResolvedRoot 'SmartSQL.sln') -PathType Leaf)) {
    throw "Not a SmartSQL workspace: $workspaceResolvedRoot"
}

if ([string]::IsNullOrWhiteSpace($ModelRoot)) {
    $ModelRoot = Join-Path (Split-Path -Parent $workspaceResolvedRoot) 'SmartSQL-model'
}
$workspaceModelRoot = [System.IO.Path]::GetFullPath($ModelRoot)
$workspaceArtifactRoot = $workspaceResolvedRoot

$workspaceCacheRoot = Join-Path $workspaceResolvedRoot '.workspace'
$workspaceArtifactCacheRoot = Join-Path $workspaceArtifactRoot '.workspace'
$workspaceTempName = if ($ForTests) { 'test-temp' } else { 'temp' }
$workspaceDirectories = [ordered]@{
    TEMP = (Join-Path $workspaceArtifactCacheRoot $workspaceTempName)
    TMP = (Join-Path $workspaceArtifactCacheRoot $workspaceTempName)
    TMPDIR = (Join-Path $workspaceArtifactCacheRoot $workspaceTempName)
    DOTNET_CLI_HOME = (Join-Path $workspaceCacheRoot 'dotnet-home')
    DOTNET_BUNDLE_EXTRACT_BASE_DIR = (Join-Path $workspaceCacheRoot 'dotnet-bundles')
    NUGET_PACKAGES = (Join-Path $workspaceCacheRoot 'nuget/packages')
    NUGET_HTTP_CACHE_PATH = (Join-Path $workspaceCacheRoot 'nuget/http-cache')
    NUGET_SCRATCH = (Join-Path $workspaceCacheRoot 'nuget/scratch')
    MSBUILDDEBUGPATH = (Join-Path $workspaceCacheRoot 'msbuild-debug')
    XDG_CACHE_HOME = (Join-Path $workspaceArtifactCacheRoot 'cache')
    SMARTSQL_DATA_DIRECTORY = (Join-Path $workspaceResolvedRoot 'data/local')
    SMARTSQL_TEMP_DIRECTORY = (Join-Path $workspaceArtifactCacheRoot $workspaceTempName)
    SMARTSQL_TEST_DIRECTORY = (Join-Path $workspaceArtifactCacheRoot 'test-data')
    SMARTSQL_TEST_RESULTS_DIRECTORY = (Join-Path $workspaceArtifactCacheRoot 'test-results')
}

# Validate each destination before setting the environment. Only our unique
# write-check file is removed; existing user data is never deleted or overwritten.
foreach ($workspaceDirectory in ($workspaceDirectories.Values | Select-Object -Unique)) {
    [System.IO.Directory]::CreateDirectory($workspaceDirectory) | Out-Null
    $workspaceProbe = Join-Path $workspaceDirectory ('.smartsql-write-check-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $workspaceProbeStream = [System.IO.File]::Open($workspaceProbe, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $workspaceProbeStream.WriteByte(0)
    }
    finally {
        $workspaceProbeStream.Dispose()
        [System.IO.File]::Delete($workspaceProbe)
    }
}

foreach ($workspaceEntry in $workspaceDirectories.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($workspaceEntry.Key, $workspaceEntry.Value, 'Process')
}
[Environment]::SetEnvironmentVariable('SMARTSQL_WORKSPACE_ROOT', $workspaceResolvedRoot, 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', '1', 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_SKIP_FIRST_TIME_EXPERIENCE', '1', 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE', 'true', 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'false', 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'false', 'Process')
[Environment]::SetEnvironmentVariable('AVALONIA_TELEMETRY_OPTOUT', '1', 'Process')
[pscustomobject]@{
    WorkspaceRoot = $workspaceResolvedRoot
    ArtifactRoot = $workspaceArtifactRoot
    ModelRoot = $workspaceModelRoot
    DataDirectory = $workspaceDirectories['SMARTSQL_DATA_DIRECTORY']
    TempDirectory = $workspaceDirectories['TEMP']
    TestResultsDirectory = $workspaceDirectories['SMARTSQL_TEST_RESULTS_DIRECTORY']
    NuGetPackages = $workspaceDirectories['NUGET_PACKAGES']
}
