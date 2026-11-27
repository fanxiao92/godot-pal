# Lightweight offline repository hygiene guard (#276 upgraded):
# 1. Verifies .gdignore-isolated directories under Godot projects contain zero stray .uid files.
# 2. Verifies relative Markdown links AND #anchor fragments (cross-doc and intra-doc, GitHub slug rules).
# 3. Verifies active docs reachability (zero orphan docs from AGENTS.md / README.md / ARCHITECTURE.md / docs/README.md;
#    exempts individual files under docs/archive/, docs/generated/, and signals/).
# 4. Verifies literal script references in pal.ps1 and scripts/*.ps1|*.psm1 resolve to existing files.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$AllDocs
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$failures = [Collections.Generic.List[string]]::new()

function ConvertTo-GitHubHeadingSlug([string]$HeadingText) {
    $s = $HeadingText.Trim()
    # Strip trailing closing ATX hashes
    $s = [regex]::Replace($s, '\s+#+\s*$', '')
    # Strip inline markdown links [label](url) -> label
    $s = [regex]::Replace($s, '\[([^\]]+)\]\([^)]+\)', '$1')
    # Strip inline code backticks and emphasis markers
    $s = $s -replace '[`*_~]', ''
    $s = $s.ToLowerInvariant()
    $sb = [Text.StringBuilder]::new($s.Length)
    foreach ($ch in $s.ToCharArray()) {
        if ($ch -eq ' ') { [void]$sb.Append('-') }
        elseif ($ch -eq '-' -or $ch -eq '_' -or [char]::IsLetterOrDigit($ch)) { [void]$sb.Append($ch) }
        # Punctuation (including CJK punctuation, colons, slashes, parens, etc.) is stripped per GitHub slug rules
    }
    return $sb.ToString()
}

$script:HeadingSlugCache = @{}
function Get-MarkdownHeadingSlugs([string]$FilePath) {
    $key = [IO.Path]::GetFullPath($FilePath)
    if ($script:HeadingSlugCache.ContainsKey($key)) { return $script:HeadingSlugCache[$key] }
    $slugs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $counts = @{}
    $inFence = $false
    $fenceChar = ''
    foreach ($line in [IO.File]::ReadAllLines($key, [Text.Encoding]::UTF8)) {
        $trimmed = $line.TrimStart()
        if ($trimmed -match '^(`{3,}|~{3,})') {
            $fc = $Matches[1][0]
            if (-not $inFence) { $inFence = $true; $fenceChar = $fc }
            elseif ($fc -eq $fenceChar) { $inFence = $false; $fenceChar = '' }
            continue
        }
        if ($inFence) { continue }
        if ($line -match '^\s{0,3}#{1,6}\s+(.+?)\s*$') {
            $base = ConvertTo-GitHubHeadingSlug $Matches[1]
            if ([string]::IsNullOrEmpty($base)) { continue }
            if ($counts.ContainsKey($base)) {
                $n = $counts[$base]
                $counts[$base] = $n + 1
                [void]$slugs.Add("$base-$n")
            } else {
                $counts[$base] = 1
                [void]$slugs.Add($base)
            }
        }
        # Also honor explicit HTML <a id="..."> or <a name="..."> anchors
        foreach ($am in [regex]::Matches($line, '<a\s+(?:id|name)="([^"]+)"')) {
            [void]$slugs.Add($am.Groups[1].Value.ToLowerInvariant())
        }
    }
    $script:HeadingSlugCache[$key] = $slugs
    return $slugs
}

function Strip-FencedCodeBlocks([string]$Text) {
    $lines = $Text -split "\r?\n"
    $out = [Collections.Generic.List[string]]::new($lines.Count)
    $inFence = $false
    $fenceChar = ''
    foreach ($line in $lines) {
        $trimmed = $line.TrimStart()
        if ($trimmed -match '^(`{3,}|~{3,})') {
            $fc = $Matches[1][0]
            if (-not $inFence) { $inFence = $true; $fenceChar = $fc }
            elseif ($fc -eq $fenceChar) { $inFence = $false; $fenceChar = '' }
            $out.Add('')
            continue
        }
        if ($inFence) { $out.Add('') } else { $out.Add($line) }
    }
    return ($out -join "`n")
}

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

# 2. Relative Markdown links and #anchor fragments resolve to existing paths and headings.
$docFiles = [Collections.Generic.List[string]]::new()
foreach ($top in @('AGENTS.md', 'ARCHITECTURE.md', 'CODING_STANDARDS.md', 'GLOSSARY.md', 'README.md', 'docs/README.md')) {
    $p = Join-Path $root $top
    if ([IO.File]::Exists($p)) { $docFiles.Add([IO.Path]::GetFullPath($p)) }
}
if ($AllDocs) {
    $docsRoot = Join-Path $root 'docs'
    if ([IO.Directory]::Exists($docsRoot)) {
        foreach ($f in @(Get-ChildItem -LiteralPath $docsRoot -Recurse -Filter '*.md' -File)) {
            $full = [IO.Path]::GetFullPath($f.FullName)
            if ($full -notin $docFiles) { $docFiles.Add($full) }
        }
    }
} else {
    foreach ($sub in @('docs/agents', 'docs/adr', 'docs/testing', 'docs/design')) {
        $subDir = Join-Path $root $sub
        if ([IO.Directory]::Exists($subDir)) {
            foreach ($f in @(Get-ChildItem -LiteralPath $subDir -Filter '*.md' -File)) {
                $full = [IO.Path]::GetFullPath($f.FullName)
                if ($full -notin $docFiles) { $docFiles.Add($full) }
            }
        }
    }
}

$adjacency = @{}
foreach ($doc in $docFiles) {
    $dir = Split-Path $doc -Parent
    $relDoc = [IO.Path]::GetRelativePath($root, $doc).Replace('\', '/')
    $rawText = [IO.File]::ReadAllText($doc, [Text.Encoding]::UTF8)
    $text = Strip-FencedCodeBlocks $rawText
    $edges = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $adjacency[$doc] = $edges

    # Match [label](target#anchor), [label](target), and [label](#anchor)
    foreach ($m in [regex]::Matches($text, '\[[^\]]+\]\(([^)\s]+)\)')) {
        $rawTarget = $m.Groups[1].Value
        if ($rawTarget -match '^(https?:|mailto:|file:|conversation:)') { continue }

        $hashIdx = $rawTarget.IndexOf('#')
        $filePart = if ($hashIdx -ge 0) { $rawTarget.Substring(0, $hashIdx) } else { $rawTarget }
        $anchorPart = if ($hashIdx -ge 0) { $rawTarget.Substring($hashIdx + 1) } else { '' }

        $resolved = $null
        if ([string]::IsNullOrEmpty($filePart)) {
            $resolved = $doc
        } else {
            $resolved = [IO.Path]::GetFullPath((Join-Path $dir $filePart))
            if (-not (Test-Path -LiteralPath $resolved)) {
                $failures.Add("[hygiene:broken-link] $relDoc -> $rawTarget")
                continue
            }
            if ([IO.File]::Exists($resolved) -and $resolved.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
                [void]$edges.Add($resolved)
            }
        }

        if (-not [string]::IsNullOrEmpty($anchorPart) -and [IO.File]::Exists($resolved) -and $resolved.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
            $decodedAnchor = [Uri]::UnescapeDataString($anchorPart).ToLowerInvariant()
            $slugs = Get-MarkdownHeadingSlugs $resolved
            if (-not $slugs.Contains($decodedAnchor)) {
                $failures.Add("[hygiene:broken-anchor] $relDoc -> $rawTarget (anchor '#$anchorPart' not found)")
            }
        }
    }
}

# 3. Orphan document reachability check (active docs must be reachable from root entrypoints).
# Exempts individual files under docs/archive/ (except docs/archive/README.md), docs/generated/ (except reachable index), and signals/.
if ($AllDocs) {
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [Collections.Generic.Queue[string]]::new()
    foreach ($entryRel in @('AGENTS.md', 'README.md', 'ARCHITECTURE.md', 'docs/README.md')) {
        $entryFull = [IO.Path]::GetFullPath((Join-Path $root $entryRel))
        if ([IO.File]::Exists($entryFull) -and $visited.Add($entryFull)) {
            $queue.Enqueue($entryFull)
        }
    }
    while ($queue.Count -gt 0) {
        $curr = $queue.Dequeue()
        if ($adjacency.ContainsKey($curr)) {
            foreach ($next in $adjacency[$curr]) {
                if ($visited.Add($next)) { $queue.Enqueue($next) }
            }
        }
    }

    foreach ($doc in $docFiles) {
        if ($visited.Contains($doc)) { continue }
        $rel = [IO.Path]::GetRelativePath($root, $doc).Replace('\', '/')
        # Exempt individual archived/generated/signal files (directory index docs like docs/archive/README.md stay checked)
        if ($rel -match '^docs/archive/.+/.+\.md$') { continue }
        if ($rel -match '^docs/generated/.+\.md$' -and $rel -ne 'docs/generated/capabilities-ledger.md') { continue }
        if ($rel -match '^signals/.+\.md$') { continue }
        $failures.Add("[hygiene:orphan-doc] Unreachable active markdown document: $rel (link it from AGENTS.md, README.md, ARCHITECTURE.md, or docs/README.md)")
    }
}

# 4. Literal script references in pal.ps1 and scripts/*.ps1|*.psm1 resolve to existing files.
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

Write-Output "PASS repo-hygiene: checked $($docFiles.Count) markdown files, $($psFiles.Count) script files, and all .gdignore directories (0 stray .uid, 0 broken links/anchors, 0 orphan docs, 0 missing script refs)."
exit 0
