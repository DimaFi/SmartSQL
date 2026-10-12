[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$localTarget = Join-Path $workspaceRoot 'data/local'
$archiveTarget = Join-Path $workspaceRoot '.workspace/legacy-local'
$manifest = [Collections.Generic.List[object]]::new()

function Copy-VerifiedFile([string]$source, [string]$destination) {
    $before = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $before) {
            throw "Destination differs; nothing overwritten: $destination"
        }
    } else { Copy-Item -LiteralPath $source -Destination $destination }
    $copied = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    $after = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($before -ne $copied -or $before -ne $after) { throw "File changed or copy verification failed: $source" }
    $manifest.Add([pscustomobject]@{Source=$source;Destination=$destination;SHA256=$copied})
}

$legacyData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SmartSQL'
if (Test-Path -LiteralPath $legacyData) {
    $journals = @(Get-ChildItem -LiteralPath $legacyData -File | Where-Object Name -match '-(wal|journal)$')
    if ($journals.Count -gt 0) { throw 'Close SmartSQL before copying an active SQLite database.' }
    foreach ($file in Get-ChildItem -LiteralPath $legacyData -File) {
        Copy-VerifiedFile $file.FullName (Join-Path $localTarget $file.Name)
    }
    if (@(Get-ChildItem -LiteralPath $legacyData -File | Where-Object Name -match '-(wal|journal)$').Count -gt 0) {
        throw 'A database became active during copying. Validate a fresh snapshot before continuing.'
    }
}
# These are prior SmartSQL-only local test artifacts. Keep their existing names and contents.
$legacyTemp = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Temp'
$legacyDirectories = @(Get-ChildItem -LiteralPath $legacyTemp -Directory | Where-Object {
    $_.Name -in @('smartsql-tests','smartsql-export-tests') -or $_.Name -like 'SmartSQL-ui-*'
})
foreach ($directory in $legacyDirectories) {
    foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($directory.FullName, $file.FullName)
        Copy-VerifiedFile $file.FullName (Join-Path (Join-Path $archiveTarget $directory.Name) $relative)
    }
}
New-Item -ItemType Directory -Force $archiveTarget | Out-Null
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $archiveTarget 'copy-manifest.json') -Encoding utf8
Write-Output "Verified $($manifest.Count) local files. Original files retained. Manifest: $archiveTarget/copy-manifest.json"
