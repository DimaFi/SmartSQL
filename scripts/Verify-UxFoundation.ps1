[CmdletBinding()]
param(
    [switch]$Combined,
    [switch]$FullTests
)

$ErrorActionPreference = 'Stop'

# This script deliberately does not merge any pull request into main.
# Combined mode merges open UX PRs in a fresh, ignored worktree on D:.
# The usual build/test scripts retain the existing temp and data policy.
$sourceRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
if (!$sourceRoot.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SmartSQL verification must run from a D:\ workspace, never C:\.'
}

$verificationRoot = $sourceRoot
if ($Combined) {
    $worktreeParent = Join-Path $sourceRoot '.workspace\ux-foundation'
    [IO.Directory]::CreateDirectory($worktreeParent) | Out-Null
    $verificationRoot = Join-Path $worktreeParent ('verify-' + [Guid]::NewGuid().ToString('N'))

    & git -C $sourceRoot worktree add --detach $verificationRoot HEAD
    if ($LASTEXITCODE -ne 0) {
        throw 'Creating the isolated worktree failed. The current checkout was not modified.'
    }

    $pullRequests = @(30, 31, 32, 33, 34)
    foreach ($number in $pullRequests) {
        # Fetch one PR head into FETCH_HEAD without changing main or local branches.
        & git -C $verificationRoot fetch --no-tags origin "refs/pull/$number/head"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not fetch PR #$number. Isolated worktree kept: $verificationRoot"
        }

        & git -C $verificationRoot -c user.name='SmartSQL Verification' -c user.email='verification@smartsql.invalid' merge --no-edit --no-ff FETCH_HEAD
        if ($LASTEXITCODE -ne 0) {
            throw "Could not combine PR #$number. Inspect only the isolated worktree: $verificationRoot"
        }
        Write-Host "Combined PR #$number into the isolated worktree."
    }
}

$filter = @(
    'FullyQualifiedName~FieldRowOrderingTests'
    'FullyQualifiedName~SqlTextSearchTests'
    'FullyQualifiedName~ManagedBooleanChoicesTests'
    'FullyQualifiedName~WorkspaceSourceLabelsTests'
    'FullyQualifiedName~DatabaseBooleanChoicesTests'
) -join '|'

Write-Host "SmartSQL UX verification root: $verificationRoot"
Write-Host 'Running full solution build with the approved workspace script.'
& (Join-Path $verificationRoot 'scripts\Build.ps1')
if (!$?) { throw 'SmartSQL build failed.' }

Write-Host 'Running five focused desktop test classes.'
& (Join-Path $verificationRoot 'scripts\Test.ps1') -NoBuild -Filter $filter
if (!$?) { throw 'SmartSQL focused UX test run failed.' }

if ($FullTests) {
    Write-Host 'Running full solution test suite. External PostgreSQL fixtures may be required.'
    & (Join-Path $verificationRoot 'scripts\Test.ps1') -NoBuild
    if (!$?) { throw 'SmartSQL full test run failed.' }
}

Write-Host 'Build and focused automated tests completed.'
Write-Host "Isolated evidence retained on D: $verificationRoot"
Write-Host 'Manual GUI, DPI, accessibility and real-provider verification remain separate acceptance gates.'
