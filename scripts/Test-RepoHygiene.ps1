# Lightweight offline repository hygiene guard:
# 1. Verifies .gdignore-isolated directories under Godot projects contain zero stray .uid files.
# 2. Verifies relative Markdown links in active agent/testing docs resolve to existing files.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$AllDocs
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$failures = [Collections.Generic.List[string]]::new()

# 1. No stray .uid files inside .gdignore-isolated directories under Godot projects.
foreach ($godotDir in @('src/Pal.Game', 'tests/Pal.Game.Tests')) {
    $projectRoot = Join-Path $root $godotDir
    if (-not [IO.Directory]::Exists($projectRoot)) { continue }
    foreach ($ignore in @(Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Filter '.gdignore' -Force)) {
        $isolated = Split-Path $ignore.FullName -Parent
        foreach ($uid in @(Get-ChildItem -LiteralPath $isolated -Recurse -File -Filter '*.uid' -Force)) {
            $rel = [IO.Path]::GetRelativePath($root, $uid.FullName).Replace('\', '/')
            $failures.Add("[hygiene:stray-uid] Stray .uid inside .gdignore directory: $rel")
        }
    }
}

# 2. Relative Markdown links in active docs resolve to existing paths.
$docFiles = [Collections.Generic.List[string]]::new()
foreach ($top in @('AGENTS.md', 'CODING_STANDARDS.md')) {
    $p = Join-Path $root $top
    if ([IO.File]::Exists($p)) { $docFiles.Add($p) }
}
$agentsDocs = Join-Path $root 'docs/agents'
if ([IO.Directory]::Exists($agentsDocs)) {
    foreach ($f in @(Get-ChildItem -LiteralPath $agentsDocs -Filter '*.md' -File)) { $docFiles.Add($f.FullName) }
}
$testingDocs = Join-Path $root 'docs/testing'
if ([IO.Directory]::Exists($testingDocs)) {
    $activeTesting = @('getting-started.md', 'game-test-layers.md', 'test-layers.md', 'time-boundaries.md', 'merge-protection.md')
    foreach ($f in @(Get-ChildItem -LiteralPath $testingDocs -Filter '*.md' -File)) {
        if ($AllDocs -or ($f.Name -cin $activeTesting)) { $docFiles.Add($f.FullName) }
    }
}

foreach ($doc in $docFiles) {
    $dir = Split-Path $doc -Parent
    $text = [IO.File]::ReadAllText($doc, [Text.Encoding]::UTF8)
    foreach ($m in [regex]::Matches($text, '\[[^\]]+\]\(([^)#\s]+)(?:#[^)\s]+)?\)')) {
        $target = $m.Groups[1].Value
        if ($target -match '^(https?:|mailto:|file:|conversation:)') { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $dir $target))
        if (-not (Test-Path -LiteralPath $resolved)) {
            $relDoc = [IO.Path]::GetRelativePath($root, $doc).Replace('\', '/')
            $failures.Add("[hygiene:broken-link] $relDoc -> $target")
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($line in $failures) { [Console]::Error.WriteLine($line) }
    exit 1
}

Write-Output "PASS repo-hygiene: checked $($docFiles.Count) markdown files and all .gdignore directories (0 stray .uid, 0 broken links)."
exit 0
