# Lightweight offline repository hygiene guard:
# 1. Verifies .gdignore-isolated directories under Godot projects contain zero stray .uid files.
# 2. Verifies relative Markdown links in active agent/testing docs resolve to existing files.
# 3. Verifies literal script references in pal.ps1 and scripts/*.ps1|*.psm1 resolve to existing files.
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

# 2. Relative Markdown links in active docs (or all docs when -AllDocs) resolve to existing paths.
$docFiles = [Collections.Generic.List[string]]::new()
foreach ($top in @('AGENTS.md', 'CODING_STANDARDS.md', 'README.md', 'docs/README.md')) {
    $p = Join-Path $root $top
    if ([IO.File]::Exists($p)) { $docFiles.Add($p) }
}
if ($AllDocs) {
    $docsRoot = Join-Path $root 'docs'
    if ([IO.Directory]::Exists($docsRoot)) {
        foreach ($f in @(Get-ChildItem -LiteralPath $docsRoot -Recurse -Filter '*.md' -File)) {
            if ($f.FullName -notin $docFiles) { $docFiles.Add($f.FullName) }
        }
    }
} else {
    foreach ($sub in @('docs/agents', 'docs/adr', 'docs/testing')) {
        $subDir = Join-Path $root $sub
        if ([IO.Directory]::Exists($subDir)) {
            foreach ($f in @(Get-ChildItem -LiteralPath $subDir -Filter '*.md' -File)) { $docFiles.Add($f.FullName) }
        }
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

# 3. Literal script references in pal.ps1 and scripts/*.ps1|*.psm1 resolve to existing files.
$psFiles = [Collections.Generic.List[string]]::new()
$palFile = Join-Path $root 'pal.ps1'
if ([IO.File]::Exists($palFile)) { $psFiles.Add($palFile) }
$scriptsDir = Join-Path $root 'scripts'
if ([IO.Directory]::Exists($scriptsDir)) {
    foreach ($f in @(Get-ChildItem -LiteralPath $scriptsDir -Recurse -File | Where-Object { $_.Extension -in '.ps1', '.psm1' })) {
        $psFiles.Add($f.FullName)
    }
}
foreach ($psFile in $psFiles) {
    $psDir = Split-Path $psFile -Parent
    $relPs = [IO.Path]::GetRelativePath($root, $psFile).Replace('\', '/')
    foreach ($line in [IO.File]::ReadAllLines($psFile, [Text.Encoding]::UTF8)) {
        if ($line.TrimStart().StartsWith('#')) { continue }
        foreach ($m in [regex]::Matches($line, "Join-Path\s+\`$PSScriptRoot\s+'([^'\$\*]+?\.(?:ps1|psm1))'")) {
            $target = $m.Groups[1].Value
            $resolved = [IO.Path]::GetFullPath((Join-Path $psDir $target))
            if (-not [IO.File]::Exists($resolved)) {
                $failures.Add("[hygiene:missing-script-ref] $relPs -> `$PSScriptRoot/$target")
            }
        }
        foreach ($m in [regex]::Matches($line, "'(scripts/[^'\$\*]+?\.(?:ps1|psm1))'")) {
            $target = $m.Groups[1].Value
            $resolved = [IO.Path]::GetFullPath((Join-Path $root $target))
            if (-not [IO.File]::Exists($resolved)) {
                $failures.Add("[hygiene:missing-script-ref] $relPs -> $target")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($line in $failures) { [Console]::Error.WriteLine($line) }
    exit 1
}

Write-Output "PASS repo-hygiene: checked $($docFiles.Count) markdown files, $($psFiles.Count) script files, and all .gdignore directories (0 stray .uid, 0 broken links, 0 missing script refs)."
exit 0
