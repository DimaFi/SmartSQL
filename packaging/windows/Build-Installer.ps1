[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildDirectory,
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
if (![IO.Path]::IsPathFullyQualified($BuildDirectory)) { throw 'BuildDirectory must be absolute.' }
$installerBuild = [IO.Path]::GetFullPath($BuildDirectory)
if ([IO.Path]::GetPathRoot($installerBuild) -ine 'D:\') { throw 'Installer artifacts must remain on D:.' }
Assert-DeliveryPath $installerBuild
$installerPayload = Join-Path $installerBuild 'app'
$installerManifest = Join-Path $installerPayload 'manifest.json'
Assert-DeliveryPath $installerManifest
if (!(Test-Path -LiteralPath $installerManifest -PathType Leaf)) { throw 'A completed Publish.ps1 manifest is required.' }
$installerMetadata = Get-Content -LiteralPath $installerManifest -Raw | ConvertFrom-Json
if ($installerMetadata.version -isnot [string] -or $installerMetadata.version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
    throw 'Manifest version must be a safe three-part version with an optional prerelease suffix.'
}
$installerNumericVersion = $installerMetadata.version.Split('-')[0]
foreach ($versionPart in $installerNumericVersion.Split('.')) {
    $numericVersionPart = 0
    if (![int]::TryParse($versionPart, [ref]$numericVersionPart) -or $numericVersionPart -gt 65535) {
        throw 'Windows version resource components must be between 0 and 65535.'
    }
}
if ($installerMetadata.deployment -ne 'self-contained' -or $installerMetadata.runtimeIdentifier -ne 'win-x64') {
    throw 'This installer requires a self-contained win-x64 publication.'
}
Get-ChildItem -LiteralPath $installerPayload -Recurse -Force | ForEach-Object { Assert-DeliveryPath $_.FullName }
$installerExpected = @('manifest.json', 'SHA256SUMS.txt') + @($installerMetadata.files | ForEach-Object { $_.path })
foreach ($payloadItem in (Get-ChildItem -LiteralPath $installerPayload -File -Recurse)) {
    $payloadRelative = [IO.Path]::GetRelativePath($installerPayload, $payloadItem.FullName).Replace('\', '/')
    if ($payloadRelative -cnotin $installerExpected) { throw 'Unexpected files in application payload (possibly runtime data). Publish a fresh application before packaging.' }
}
foreach ($entry in $installerMetadata.files) {
    $payloadFile = [IO.Path]::GetFullPath((Join-Path $installerPayload $entry.path))
    if (!$payloadFile.StartsWith($installerPayload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped application payload.' }
    Assert-DeliveryPath $payloadFile
    if ((Get-FileHash -LiteralPath $payloadFile -Algorithm SHA256).Hash -ine $entry.sha256) { throw 'Payload changed after publication; refuse installer.' }
}
$installerIcon = Join-Path $installerPayload 'SmartSQL.ico'
if (!(Test-Path -LiteralPath $installerIcon -PathType Leaf)) { throw 'Published SmartSQL.ico is required.' }
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $installerCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($installerCompiler) { $IsccPath = $installerCompiler.Source }
    else {
        foreach ($candidate in @('C:\Program Files (x86)\Inno Setup 6\ISCC.exe', 'C:\Program Files\Inno Setup 6\ISCC.exe')) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $IsccPath = $candidate; break }
        }
    }
}
if ([string]::IsNullOrWhiteSpace($IsccPath) -or !(Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup compiler ISCC.exe is not installed/found. Portable app/ZIP remain usable; no installer was built. Nothing was installed automatically.'
}
if (![IO.Path]::IsPathFullyQualified($IsccPath)) { throw 'IsccPath must identify an absolute installed compiler path.' }
$installerOutput = Join-Path $installerBuild 'installer'
Assert-DeliveryPath $installerOutput
if (Test-Path -LiteralPath $installerOutput) { throw 'Installer output already exists; choose a new publication. No overwrite is allowed.' }
[IO.Directory]::CreateDirectory($installerOutput) | Out-Null
$installerOldTemp = @{}
try {
    $installerTemp = Join-Path $installerBuild '.build/installer-temp'
    Assert-DeliveryPath $installerTemp
    [IO.Directory]::CreateDirectory($installerTemp) | Out-Null
    foreach ($name in @('TEMP', 'TMP', 'TMPDIR')) {
        $installerOldTemp[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $installerTemp, 'Process')
    }
    & $IsccPath "/DPublishRoot=$installerPayload" "/DBuildVersion=$($installerMetadata.version)" "/DBuildNumericVersion=$installerNumericVersion" "/DInstallerOutput=$installerOutput" (Join-Path $PSScriptRoot 'SmartSQL.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed ($LASTEXITCODE); partial output is preserved." }
    Get-ChildItem -LiteralPath $installerOutput -File -Filter '*.exe' | ForEach-Object {
        ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name) |
            Set-Content -LiteralPath ($_.FullName + '.sha256') -Encoding ascii
        $_.FullName
    }
}
finally {
    foreach ($entry in $installerOldTemp.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
}
