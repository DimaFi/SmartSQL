[CmdletBinding()]
param([string]$Destination = 'D:\Code\SmartSQL-model', [switch]$UpdateExisting)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$sourceRoot = Join-Path $workspaceRoot '_model_build'
$destinationRoot = [IO.Path]::GetFullPath($Destination)
if ($destinationRoot -eq $sourceRoot -or $destinationRoot.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar)) {
    throw 'Model destination must be separate from the staging directory.'
}
$backupRoot = Join-Path $destinationRoot ('.workspace/migration-backup/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$manifest = [Collections.Generic.List[object]]::new()
if (!$UpdateExisting) {
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Force | Where-Object {
        $_.Name -ne '.gitignore' -and $_.FullName -notmatch '[\\/](bin|obj|\.git|\.workspace)[\\/]'
    }) {
        $targetFile = Join-Path $destinationRoot ([IO.Path]::GetRelativePath($sourceRoot, $sourceFile.FullName))
        if ((Test-Path -LiteralPath $targetFile) -and
            (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash) {
            throw "Target has different source; review it before using -UpdateExisting: $targetFile"
        }
    }
}
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Force | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git|\.workspace)[\\/]'
}) {
    $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
    $target = Join-Path $destinationRoot $relative
    if ($file.Name -eq '.gitignore' -and (Test-Path -LiteralPath $target)) { continue }
    $expected = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) {
        $backup = Join-Path $backupRoot $relative
        New-Item -ItemType Directory -Force (Split-Path $backup -Parent) | Out-Null
        Copy-Item -LiteralPath $target -Destination $backup
        if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
            throw "Backup verification failed: $target"
        }
    }
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw "Verification failed: $target" }
    $manifest.Add([pscustomobject]@{File=$relative;SHA256=$expected})
}
New-Item -ItemType Directory -Force (Join-Path $destinationRoot '.workspace') | Out-Null
$ignorePath = Join-Path $destinationRoot '.gitignore'
$ignoreText = if (Test-Path -LiteralPath $ignorePath) { [IO.File]::ReadAllText($ignorePath) } else { '' }
foreach ($ignoreEntry in @('.workspace/', 'local/', 'models/', '*.gguf', '*.safetensors')) {
    if (!$ignoreText.Contains($ignoreEntry)) { $ignoreText += "`n$ignoreEntry`n" }
}
[IO.File]::WriteAllText($ignorePath, $ignoreText)
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destinationRoot '.workspace/migration-manifest.json') -Encoding utf8
Write-Output "Verified $($manifest.Count) model source files at $destinationRoot; previous differing files preserved under $backupRoot."
