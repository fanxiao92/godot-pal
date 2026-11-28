[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$scriptPath = Join-Path $repoRoot 'scripts/Test-RepoHygiene.ps1'
$utf8 = [Text.UTF8Encoding]::new($false)
$workDir = Join-Path ([IO.Path]::GetTempPath()) ("repo-hygiene-tests-" + [guid]::NewGuid().ToString('N'))
$count = 0

function Put([string]$Rel, [string]$Text) {
    $full = Join-Path $workDir $Rel
    [void][IO.Directory]::CreateDirectory((Split-Path $full -Parent))
    [IO.File]::WriteAllText($full, $Text, $utf8)
}

function Reset-Fixture {
    if (Test-Path -LiteralPath $workDir) { Remove-Item -LiteralPath $workDir -Recurse -Force }
    [void][IO.Directory]::CreateDirectory($workDir)
    Put 'README.md' "# Root`n`nSee [Docs](docs/README.md) and [Arch](ARCHITECTURE.md#overview).`n"
    Put 'AGENTS.md' "# Agents`n`nSee [Docs](docs/README.md).`n"
    Put 'ARCHITECTURE.md' "# Architecture`n`n## Overview`n`nDetails.`n"
    Put 'docs/README.md' "# Docs`n`n- [Guide](design/guide.md#section-one)`n- [Archive](archive/README.md)`n"
    Put 'docs/design/guide.md' "# Guide`n`n## Section One`n`nSee [self](#section-one).`n"
    Put 'docs/archive/README.md' "# Archive Index`n"
    Put 'docs/archive/design/old.md' "# Old Archived Doc`n"
}

function Check([string]$Name, [int]$ExpectedExit, [string]$ExpectedPattern) {
    $output = & pwsh -NoProfile -NonInteractive -File $scriptPath -RepositoryRoot $workDir -AllDocs 2>&1
    $code = $LASTEXITCODE
    $joined = ($output | ForEach-Object { [string]$_ }) -join "`n"
    if ($code -ne $ExpectedExit) {
        throw "FAIL [$Name]: expected exit $ExpectedExit, got $code. Output:`n$joined"
    }
    if ($ExpectedPattern -and $joined -notmatch $ExpectedPattern) {
        throw "FAIL [$Name]: expected pattern '$ExpectedPattern' not found. Output:`n$joined"
    }
    $script:count++
    Write-Host "PASS $Name"
}

try {
    Reset-Fixture
    Check 'valid links, valid cross/self anchors, and exempted archive doc pass' 0 'PASS repo-hygiene'

    Reset-Fixture
    Put 'docs/README.md' "# Docs`n`n- [Guide](design/guide.md#missing-anchor)`n- [Archive](archive/README.md)`n"
    Check 'broken cross-doc #anchor rejected' 1 '\[hygiene:broken-anchor\]'

    Reset-Fixture
    Put 'docs/design/guide.md' "# Guide`n`n## Section One`n`nSee [bad](#nonexistent).`n"
    Check 'broken intra-doc #anchor rejected' 1 '\[hygiene:broken-anchor\]'

    Reset-Fixture
    Put 'docs/design/orphan.md' "# Orphan`n`nNobody links here.`n"
    Check 'orphan active doc rejected' 1 '\[hygiene:orphan-doc\]'

    Reset-Fixture
    Put 'docs/design/guide.md' "# Guide`n`n## Section One`n`n``````markdown`n[fake](missing.md#bad)`n```````n"
    Check 'links inside fenced code blocks ignored' 0 'PASS repo-hygiene'
} finally {
    if (Test-Path -LiteralPath $workDir) { Remove-Item -LiteralPath $workDir -Recurse -Force }
}

Write-Host "PASS RepoHygiene regression: $count cases."
