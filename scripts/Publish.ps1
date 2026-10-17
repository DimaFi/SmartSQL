[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string]$Deployment = 'SelfContained',
    [string]$OutputRoot = 'D:\Code\SmartSQL-builds',
    [string]$PortableDataDirectory,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string]$BuildId,
    [switch]$Offline,
    [switch]$Online,
    [switch]$Installer,
    [string]$IsccPath
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
if ($Online -and $Offline) { throw 'Choose either -Online or -Offline, not both.' }
if (![OperatingSystem]::IsWindows()) { throw 'This publication script targets Windows x64 only.' }
if (![IO.Path]::IsPathFullyQualified($OutputRoot)) { throw 'OutputRoot must be an absolute path on D:.' }
$deliveryRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\', '/')
if ([IO.Path]::GetPathRoot($deliveryRoot) -ine 'D:\' -or $deliveryRoot -ieq 'D:') {
    throw 'Publication output must be a named directory on D:.'
}
Assert-DeliveryPath $deliveryRoot
$sourceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\', '/')
if ($deliveryRoot -ieq $sourceRoot -or $deliveryRoot.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must be outside the source workspace so direct portable launches cannot find its SmartSQL.sln/data.'
}
Assert-DeliveryPath $sourceRoot
$deliveryEnvironment = . (Join-Path $PSScriptRoot 'Initialize-Workspace.ps1')
$sourceCommit = (& git -C $sourceRoot rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot determine source Git commit.' }
$sourceStatus = @(& git -C $sourceRoot status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source Git status.' }
$sourceDirty = $sourceStatus.Count -gt 0
if ([string]::IsNullOrWhiteSpace($BuildId)) {
    $BuildId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ') + '-' + $sourceCommit.Substring(0, 12)
}
$deploymentLabel = if ($Deployment -eq 'SelfContained') { 'self-contained' } else { 'framework-dependent' }
$versionRoot = Join-Path $deliveryRoot $Version
$buildRoot = [IO.Path]::GetFullPath((Join-Path $versionRoot "win-x64-$deploymentLabel-$BuildId"))
if (!$buildRoot.StartsWith($deliveryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Resolved output escaped OutputRoot.' }
Assert-DeliveryPath $buildRoot
if (Test-Path -LiteralPath $buildRoot) { throw "Publication already exists; choose a new BuildId. Nothing was overwritten: $buildRoot" }
if ($PSBoundParameters.ContainsKey('PortableDataDirectory')) {
    if ([string]::IsNullOrWhiteSpace($PortableDataDirectory) -or ![IO.Path]::IsPathFullyQualified($PortableDataDirectory)) {
        throw 'PortableDataDirectory must be a non-empty absolute directory on D:.'
    }
    $portableDataRoot = [IO.Path]::GetFullPath($PortableDataDirectory).TrimEnd('\', '/')
    if ([IO.Path]::GetPathRoot($portableDataRoot) -ine 'D:\' -or $portableDataRoot -ieq 'D:') {
        throw 'PortableDataDirectory must be a named directory on D:.'
    }
    Assert-DeliveryPath $portableDataRoot
    if ($portableDataRoot -ieq $sourceRoot -or
        $portableDataRoot.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $sourceRoot.StartsWith($portableDataRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'PortableDataDirectory must be outside the source workspace.'
    }
    if ($portableDataRoot -ieq $deliveryRoot -or $portableDataRoot.StartsWith($deliveryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'PortableDataDirectory must be outside OutputRoot so replacing a publication cannot replace user data.'
    }
    if ($buildRoot -ieq $portableDataRoot -or $buildRoot.StartsWith($portableDataRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The publication build directory must not be inside PortableDataDirectory.'
    }
    if (Test-Path -LiteralPath $portableDataRoot -PathType Leaf) {
        throw 'PortableDataDirectory points to a file, not a directory.'
    }
}
[IO.Directory]::CreateDirectory($versionRoot) | Out-Null
# Atomic creation refuses concurrent publication to the same version/build identifier.
New-Item -ItemType Directory -Path $buildRoot -ErrorAction Stop | Out-Null
$applicationRoot = Join-Path $buildRoot 'app'
$artifactCache = Join-Path $buildRoot '.build'
$logPath = Join-Path $buildRoot 'publish.log'
Assert-DeliveryPath $applicationRoot
Assert-DeliveryPath $artifactCache
[IO.Directory]::CreateDirectory($artifactCache) | Out-Null
$runtimeIdentifier = 'win-x64'
$selfContained = if ($Deployment -eq 'SelfContained') { 'true' } else { 'false' }
$publishArguments = @('publish', (Join-Path $sourceRoot 'src/SmartSQL.Desktop/SmartSQL.Desktop.csproj'),
    '--configuration', 'Release', '--runtime', $runtimeIdentifier, '--self-contained', $selfContained,
    '--output', $applicationRoot, '--artifacts-path', $artifactCache, '--nologo',
    "-p:Version=$Version", "-p:InformationalVersion=$Version", "-p:SourceRevisionId=$sourceCommit",
    '-p:Deterministic=true', '-p:ContinuousIntegrationBuild=true', '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false',
    ('-p:RestoreConfigFile=' + (Join-Path $sourceRoot 'NuGet.Config')))
if ($Offline) { $publishArguments += @('--source', $env:NUGET_PACKAGES, '-p:NuGetAudit=false') }
elseif ($Online) {
    # Explicit command-local feed; never add/change machine or user NuGet sources.
    # NuGet still reuses the workspace global-packages cache before downloading.
    $publishArguments += @('--source', 'https://api.nuget.org/v3/index.json')
}
try {
    & dotnet @publishArguments 2>&1 | Tee-Object -FilePath $logPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed ($LASTEXITCODE). No deployment-mode fallback was attempted. The failed directory/log is preserved: $buildRoot"
    }
    if (!(Test-Path -LiteralPath (Join-Path $applicationRoot 'SmartSQL.Desktop.exe') -PathType Leaf)) { throw 'Publish did not produce the Windows executable.' }
    if (Test-Path -LiteralPath (Join-Path $applicationRoot 'data')) { throw 'Published application unexpectedly contains local data; publication stopped.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-Published.ps1') -Destination (Join-Path $applicationRoot 'Run-SmartSQL.ps1')
    $publishedIcon = Join-Path $sourceRoot 'src/SmartSQL.Desktop/Assets/SmartSQL.ico'
    if (Test-Path -LiteralPath $publishedIcon -PathType Leaf) {
        Copy-Item -LiteralPath $publishedIcon -Destination (Join-Path $applicationRoot 'SmartSQL.ico')
    }
    if ($PSBoundParameters.ContainsKey('PortableDataDirectory')) {
        [ordered]@{ schemaVersion = 1; dataDirectory = $portableDataRoot } |
            ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $applicationRoot 'smartsql.portable.json') -Encoding utf8NoBOM
    }
    $fileHashes = @(Get-ChildItem -LiteralPath $applicationRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($applicationRoot, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $sdkVersion = (& dotnet --version | Out-String).Trim()
    $manifest = [ordered]@{
        application = 'SmartSQL'; version = $Version; buildId = $BuildId; sourceCommit = $sourceCommit
        sourceDirty = $sourceDirty; runtimeIdentifier = $runtimeIdentifier; deployment = $deploymentLabel
        restoreMode = if ($Online) { 'explicit-nuget-org' } elseif ($Offline) { 'workspace-cache-only' } else { 'configured-sources' }
        sdk = $sdkVersion; publishedUtc = (Get-Date).ToUniversalTime().ToString('o')
        minimumFramework = if ($Deployment -eq 'FrameworkDependent') { '.NET 10 x64 runtime (not bundled)' } else { $null }
        notes = 'Hashes cover application payload excluding manifest/SHA256SUMS. No source/local databases/caches are packaged.'
        files = $fileHashes
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $applicationRoot 'manifest.json') -Encoding utf8NoBOM
    $fileHashes | ForEach-Object { $_.sha256 + '  ' + $_.path } | Set-Content -LiteralPath (Join-Path $applicationRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM
    $zipPath = Join-Path $buildRoot "SmartSQL-$Version-win-x64-$deploymentLabel.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($applicationRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zipPath)) |
        Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ascii
    if ($Installer) {
        if ($Deployment -ne 'SelfContained') { throw 'The installer requires an explicitly self-contained publication.' }
        & (Join-Path $sourceRoot 'packaging/windows/Build-Installer.ps1') -BuildDirectory $buildRoot -IsccPath $IsccPath
    }
    [pscustomobject]@{ BuildDirectory = $buildRoot; ApplicationDirectory = $applicationRoot; Zip = $zipPath; Deployment = $deploymentLabel; SourceCommit = $sourceCommit; SourceDirty = $sourceDirty }
}
catch {
    # Preserve any partial artifacts for diagnosis. Existing publications are never removed.
    throw
}
