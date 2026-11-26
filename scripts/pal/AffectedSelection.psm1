Set-StrictMode -Version Latest

if (-not (Get-Command Read-SafeXmlDocument -ErrorAction SilentlyContinue)) {
    Import-Module (Join-Path $PSScriptRoot 'PalCommon.psm1') -Global
}

function ConvertTo-RepoRelativePath {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path
    )
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $full = if ([IO.Path]::IsPathRooted($Path)) { [IO.Path]::GetFullPath($Path) } else { [IO.Path]::GetFullPath((Join-Path $root $Path)) }
    $rel = [IO.Path]::GetRelativePath($root, $full)
    return ($rel -replace '\\', '/')
}

function Convert-GlobToRegex {
    param([Parameter(Mandatory)][string]$Glob)
    $norm = ($Glob -replace '\\', '/') -replace '^\./', ''
    $sb = [Text.StringBuilder]::new('^')
    $i = 0
    while ($i -lt $norm.Length) {
        $c = $norm[$i]
        if ($c -eq '*') {
            if (($i + 1) -lt $norm.Length -and $norm[$i + 1] -eq '*') {
                if (($i + 2) -lt $norm.Length -and $norm[$i + 2] -eq '/') {
                    [void]$sb.Append('(?:.*/)?')
                    $i += 3
                    continue
                }
                [void]$sb.Append('.*')
                $i += 2
                continue
            }
            [void]$sb.Append('[^/]*')
            $i++
            continue
        }
        if ($c -eq '?') {
            [void]$sb.Append('[^/]')
            $i++
            continue
        }
        [void]$sb.Append([regex]::Escape([string]$c))
        $i++
    }
    [void]$sb.Append('$')
    return $sb.ToString()
}

function Test-GlobMatch {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Glob
    )
    $normPath = ($RelativePath -replace '\\', '/') -replace '^\./', ''
    $pattern = Convert-GlobToRegex -Glob $Glob
    if ($IsWindows) {
        return ($normPath -imatch $pattern)
    }
    return ($normPath -cmatch $pattern)
}

function Get-SolutionProjects {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $slnPath = Join-Path $RepositoryRoot 'GodotPal.sln'
    if (-not (Test-Path -LiteralPath $slnPath -PathType Leaf)) {
        throw "Solution file not found: $slnPath"
    }
    $content = [IO.File]::ReadAllText($slnPath, [Text.Encoding]::UTF8)
    $matches = [regex]::Matches($content, 'Project\("\{[^"]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"')
    $projects = [Collections.Generic.List[string]]::new()
    foreach ($m in $matches) {
        $rel = ($m.Groups[1].Value -replace '\\', '/')
        $projects.Add($rel)
    }
    return @($projects | Sort-Object -Unique)
}

function Get-StaticProjectGraph {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $projects = Get-SolutionProjects -RepositoryRoot $RepositoryRoot
    $forward = @{}
    $reverse = @{}
    foreach ($p in $projects) {
        $forward[$p] = [Collections.Generic.List[string]]::new()
        $reverse[$p] = [Collections.Generic.List[string]]::new()
    }
    foreach ($p in $projects) {
        $fullPath = Join-Path $RepositoryRoot $p
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Solution project missing on disk: $p"
        }
        $xml = Read-SafeXmlDocument -Path $fullPath
        $projDir = Split-Path $fullPath -Parent
        foreach ($node in @($xml.SelectNodes('//ProjectReference'))) {
            $include = $node.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            $refFull = [IO.Path]::GetFullPath((Join-Path $projDir $include))
            $refRel = ConvertTo-RepoRelativePath -RepositoryRoot $RepositoryRoot -Path $refFull
            if (-not $forward[$p].Contains($refRel)) { $forward[$p].Add($refRel) }
            if (-not $reverse.ContainsKey($refRel)) { $reverse[$refRel] = [Collections.Generic.List[string]]::new() }
            if (-not $reverse[$refRel].Contains($p)) { $reverse[$refRel].Add($p) }
        }
    }
    return @{
        Projects = $projects
        Forward  = $forward
        Reverse  = $reverse
    }
}

function Get-ReverseClosure {
    param(
        [Parameter(Mandatory)][hashtable]$Graph,
        [Parameter(Mandatory)][string]$Project
    )
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [Collections.Generic.Queue[string]]::new()
    [void]$visited.Add($Project)
    $queue.Enqueue($Project)
    while ($queue.Count -gt 0) {
        $curr = $queue.Dequeue()
        if ($Graph.Reverse.ContainsKey($curr)) {
            foreach ($dep in $Graph.Reverse[$curr]) {
                if ($visited.Add($dep)) {
                    $queue.Enqueue($dep)
                }
            }
        }
    }
    return @($visited | Sort-Object)
}

function Get-FileGameTestCategories {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )
    $full = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return @() }
    if ([IO.Path]::GetExtension($full) -ine '.cs') { return @() }
    $text = [IO.File]::ReadAllText($full, [Text.Encoding]::UTF8)
    $matches = [regex]::Matches($text, 'TestCategory\("([^"]+)"\)')
    $cats = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($m in $matches) {
        $val = $m.Groups[1].Value
        if ($val -ine 'RealAssets') { [void]$cats.Add($val) }
    }
    return @($cats | Sort-Object)
}

function Get-RepositoryGameCategories {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $testsDir = Join-Path $RepositoryRoot 'tests/Pal.Game.Tests/Tests'
    $cats = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if (Test-Path -LiteralPath $testsDir -PathType Container) {
        foreach ($file in @(Get-ChildItem -LiteralPath $testsDir -Recurse -File -Filter '*.cs')) {
            $text = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8)
            foreach ($m in [regex]::Matches($text, 'TestCategory\("([^"]+)"\)')) {
                [void]$cats.Add($m.Groups[1].Value)
            }
        }
    }
    return @($cats | Sort-Object)
}

function Get-ChangedFilesFromGit {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string]$BaseRef
    )
    Push-Location $RepositoryRoot
    try {
        $resolvedBase = $null
        if (-not [string]::IsNullOrWhiteSpace($BaseRef)) {
            $mb = (& git merge-base $BaseRef HEAD 2>$null | Out-String).Trim()
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mb)) {
                $resolvedBase = $mb
            } else {
                $rev = (& git rev-parse --verify $BaseRef 2>$null | Out-String).Trim()
                if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($rev)) {
                    throw "Unable to resolve git base ref '$BaseRef'."
                }
                $resolvedBase = $rev
            }
        } else {
            foreach ($candidate in @('origin/main', 'main')) {
                $mb = (& git merge-base $candidate HEAD 2>$null | Out-String).Trim()
                if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mb)) {
                    $resolvedBase = $mb
                    break
                }
            }
            if (-not $resolvedBase) {
                $resolvedBase = (& git rev-parse HEAD 2>$null | Out-String).Trim()
                if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedBase)) {
                    throw 'Unable to resolve git HEAD for affected test selection.'
                }
            }
        }

        $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $diffLines = @(& git diff --name-status -M $resolvedBase)
        if ($LASTEXITCODE -ne 0) { throw "git diff --name-status failed (exit $LASTEXITCODE)." }
        foreach ($line in $diffLines) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $parts = $line -split "`t"
            if ($parts.Count -ge 2) {
                $status = $parts[0]
                if ($status.StartsWith('R') -or $status.StartsWith('C')) {
                    if ($parts.Count -ge 3) {
                        [void]$files.Add(($parts[1] -replace '\\', '/'))
                        [void]$files.Add(($parts[2] -replace '\\', '/'))
                    }
                } else {
                    [void]$files.Add(($parts[1] -replace '\\', '/'))
                }
            }
        }

        $untracked = @(& git ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw "git ls-files --others failed (exit $LASTEXITCODE)." }
        foreach ($u in $untracked) {
            if (-not [string]::IsNullOrWhiteSpace($u)) {
                [void]$files.Add(($u.Trim() -replace '\\', '/'))
            }
        }

        return @{
            BaseCommit = $resolvedBase
            Files      = @($files | Sort-Object)
        }
    } finally {
        Pop-Location
    }
}

function Resolve-AffectedSelection {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [AllowEmptyCollection()][string[]]$ChangedFiles = @(),
        [string]$MapPath = 'tests/affected-map.psd1',
        [switch]$ForceAll
    )
    $fullMapPath = if ([IO.Path]::IsPathRooted($MapPath)) { $MapPath } else { Join-Path $RepositoryRoot $MapPath }
    if (-not (Test-Path -LiteralPath $fullMapPath -PathType Leaf)) {
        throw "Affected map not found: $fullMapPath"
    }
    $map = Import-PowerShellDataFile -LiteralPath $fullMapPath
    $graph = Get-StaticProjectGraph -RepositoryRoot $RepositoryRoot
    $smokeCategory = if ($map.ContainsKey('SmokeGameCategory') -and $map.SmokeGameCategory) { [string]$map.SmokeGameCategory } else { 'Smoke' }

    if ($ForceAll) {
        return @{
            MapVersion     = $map.Version
            Mode           = 'full'
            Reason         = 'Explicit --all flag requested full test run.'
            ChangedFiles   = @($ChangedFiles)
            FileDecisions  = @()
            UnmatchedFiles = @()
            Targets        = @('core-unit', 'extract-unit', 'extract-integration', 'prepare', 'game:*')
        }
    }

    if (-not $ChangedFiles -or $ChangedFiles.Count -eq 0) {
        return @{
            MapVersion     = $map.Version
            Mode           = 'no-op'
            Reason         = 'No changed files detected.'
            ChangedFiles   = @()
            FileDecisions  = @()
            UnmatchedFiles = @()
            Targets        = @()
        }
    }

    $decisions = [Collections.Generic.List[object]]::new()
    $unmatched = [Collections.Generic.List[string]]::new()
    $targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $fallbackReasons = [Collections.Generic.List[string]]::new()
    $directGameTarget = $false
    $coreTransitiveGame = $false

    foreach ($rawFile in $ChangedFiles) {
        $file = ($rawFile -replace '\\', '/') -replace '^\./', ''
        if ([string]::IsNullOrWhiteSpace($file)) { continue }

        $matchedRules = [Collections.Generic.List[object]]::new()
        foreach ($rule in $map.Rules) {
            foreach ($glob in $rule.Paths) {
                if (Test-GlobMatch -RelativePath $file -Glob $glob) {
                    $matchedRules.Add($rule)
                    break
                }
            }
        }

        if ($matchedRules.Count -eq 0) {
            $unmatched.Add($file)
            $fallbackReasons.Add("Unmatched path '$file' triggers safe fallback to full.")
            $decisions.Add([ordered]@{
                file    = $file
                rules   = @()
                action  = 'full'
                targets = @('full')
                reason  = 'unmatched path -> safe fallback to full'
            })
            continue
        }

        $hasFull = $false
        $fileTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $ruleNames = [Collections.Generic.List[string]]::new()

        foreach ($r in $matchedRules) {
            $ruleNames.Add([string]$r.Name)
            $action = if ($r.ContainsKey('Action')) { [string]$r.Action } else { '' }
            if ($action -eq 'full') {
                $hasFull = $true
            }
            if ($r.ContainsKey('Project') -and $r.Project) {
                $closure = Get-ReverseClosure -Graph $graph -Project ([string]$r.Project)
                foreach ($proj in $closure) {
                    if ($map.TestProjects.ContainsKey($proj)) {
                        $t = [string]$map.TestProjects[$proj]
                        if ($t -eq 'game:*' -and ([string]$r.Project -ieq 'src/Pal.Core/Pal.Core.csproj')) {
                            $coreTransitiveGame = $true
                            [void]$fileTargets.Add("game:$smokeCategory")
                        } else {
                            [void]$fileTargets.Add($t)
                        }
                    }
                }
            }
            if ($r.ContainsKey('Targets') -and $r.Targets) {
                $narrow = $r.ContainsKey('NarrowGameTestCategory') -and [bool]$r.NarrowGameTestCategory
                foreach ($t in $r.Targets) {
                    $targetStr = [string]$t
                    if ($narrow -and $targetStr -eq 'game:*') {
                        $cats = @(Get-FileGameTestCategories -RepositoryRoot $RepositoryRoot -RelativePath $file)
                        if ($cats.Count -gt 0) {
                            foreach ($c in $cats) {
                                [void]$fileTargets.Add("game:$c")
                                $directGameTarget = $true
                            }
                        } else {
                            [void]$fileTargets.Add('game:*')
                            $directGameTarget = $true
                        }
                    } else {
                        [void]$fileTargets.Add($targetStr)
                        if ($targetStr.StartsWith('game:', [StringComparison]::OrdinalIgnoreCase)) {
                            $directGameTarget = $true
                        }
                    }
                }
            }
        }

        if ($hasFull) {
            $fallbackReasons.Add("File '$file' matched full-fallback rule ($($ruleNames -join ', ')).")
            $decisions.Add([ordered]@{
                file    = $file
                rules   = @($ruleNames)
                action  = 'full'
                targets = @('full')
                reason  = 'matched mandatory full-fallback rule'
            })
        } elseif ($fileTargets.Count -gt 0) {
            foreach ($ft in $fileTargets) { [void]$targets.Add($ft) }
            $decisions.Add([ordered]@{
                file    = $file
                rules   = @($ruleNames)
                action  = 'targeted'
                targets = @($fileTargets | Sort-Object)
                reason  = 'matched target rules'
            })
        } else {
            $decisions.Add([ordered]@{
                file    = $file
                rules   = @($ruleNames)
                action  = 'none'
                targets = @()
                reason  = 'docs/reference only (no-op)'
            })
        }
    }

    if ($fallbackReasons.Count -gt 0) {
        return @{
            MapVersion     = $map.Version
            Mode           = 'full'
            Reason         = ($fallbackReasons -join ' ')
            ChangedFiles   = @($ChangedFiles)
            FileDecisions  = @($decisions)
            UnmatchedFiles = @($unmatched)
            Targets        = @('core-unit', 'extract-unit', 'extract-integration', 'prepare', 'game:*')
        }
    }

    if ($targets.Count -eq 0) {
        return @{
            MapVersion     = $map.Version
            Mode           = 'no-op'
            Reason         = 'Only documentation/reference files changed; no tests required.'
            ChangedFiles   = @($ChangedFiles)
            FileDecisions  = @($decisions)
            UnmatchedFiles = @()
            Targets        = @()
        }
    }

    # If game:* is present alongside specific game:<cat> targets, collapse to game:*.
    if ($targets.Contains('game:*')) {
        $toRemove = @($targets | Where-Object { $_.StartsWith('game:', [StringComparison]::OrdinalIgnoreCase) -and $_ -ine 'game:*' })
        foreach ($rem in $toRemove) { [void]$targets.Remove($rem) }
    }

    $orderedTargets = @($targets | Sort-Object)
    $reason = if ($coreTransitiveGame -and -not $directGameTarget) {
        "Targeted incremental selection (Pal.Core transitive Game narrowed to game:$smokeCategory)."
    } else {
        'Targeted incremental selection.'
    }

    return @{
        MapVersion     = $map.Version
        Mode           = 'targeted'
        Reason         = $reason
        ChangedFiles   = @($ChangedFiles)
        FileDecisions  = @($decisions)
        UnmatchedFiles = @()
        Targets        = $orderedTargets
    }
}

function Test-AffectedMapIntegrity {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string]$MapPath = 'tests/affected-map.psd1'
    )
    $errors = [Collections.Generic.List[string]]::new()
    $fullMapPath = if ([IO.Path]::IsPathRooted($MapPath)) { $MapPath } else { Join-Path $RepositoryRoot $MapPath }
    if (-not (Test-Path -LiteralPath $fullMapPath -PathType Leaf)) {
        return @{ Passed = $false; Errors = @("Missing affected map: $fullMapPath") }
    }

    $map = Import-PowerShellDataFile -LiteralPath $fullMapPath
    $staticGraph = Get-StaticProjectGraph -RepositoryRoot $RepositoryRoot
    $repoRootFull = [IO.Path]::GetFullPath($RepositoryRoot)

    # 1) Compare static .csproj ProjectReference graph against live MSBuild evaluation, and verify external Compile links.
    foreach ($proj in $staticGraph.Projects) {
        $projFull = Join-Path $RepositoryRoot $proj
        $projDir = [IO.Path]::GetFullPath((Split-Path $projFull -Parent))
        $msbuildRaw = (& dotnet msbuild $projFull -nologo -getItem:ProjectReference -getItem:Compile 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            $errors.Add("MSBuild evaluation failed for ${proj}: $msbuildRaw")
            continue
        }
        $jsonStart = $msbuildRaw.IndexOf('{')
        if ($jsonStart -lt 0) {
            $errors.Add("MSBuild did not emit JSON for ${proj}: $msbuildRaw")
            continue
        }
        $parsed = $msbuildRaw.Substring($jsonStart) | ConvertFrom-Json

        # Check ProjectReference edges
        $evalRefs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        if ($parsed.Items -and $parsed.Items.ProjectReference) {
            foreach ($item in @($parsed.Items.ProjectReference)) {
                $refPath = if ($item.FullPath) { [string]$item.FullPath } else { Join-Path $projDir ([string]$item.Identity) }
                $rel = ConvertTo-RepoRelativePath -RepositoryRoot $RepositoryRoot -Path $refPath
                [void]$evalRefs.Add($rel)
            }
        }
        $staticRefs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($sr in $staticGraph.Forward[$proj]) { [void]$staticRefs.Add($sr) }

        foreach ($er in $evalRefs) {
            if (-not $staticRefs.Contains($er)) {
                $errors.Add("Graph drift in ${proj}: MSBuild reports ProjectReference '$er' missing from static parser.")
            }
        }
        foreach ($sr in $staticRefs) {
            if (-not $evalRefs.Contains($sr)) {
                $errors.Add("Graph drift in ${proj}: static parser found ProjectReference '$sr' absent from MSBuild evaluation.")
            }
        }

        # Check external Compile items (outside project directory, inside repo root, excluding obj/bin)
        if ($parsed.Items -and $parsed.Items.Compile) {
            foreach ($item in @($parsed.Items.Compile)) {
                $compileFull = if ($item.FullPath) { [IO.Path]::GetFullPath([string]$item.FullPath) } else { [IO.Path]::GetFullPath((Join-Path $projDir ([string]$item.Identity))) }
                $cmp = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
                $insideRepo = $compileFull.StartsWith($repoRootFull.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $cmp)
                $insideProj = $compileFull.StartsWith($projDir.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $cmp)
                if ($insideRepo -and -not $insideProj) {
                    $relCompile = ConvertTo-RepoRelativePath -RepositoryRoot $RepositoryRoot -Path $compileFull
                    if ($relCompile -match '(^|/)(obj|bin)/') { continue }
                    $sel = Resolve-AffectedSelection -RepositoryRoot $RepositoryRoot -ChangedFiles @($relCompile) -MapPath $MapPath
                    if ($map.TestProjects.ContainsKey($proj)) {
                        $expectedTarget = [string]$map.TestProjects[$proj]
                        if ($sel.Mode -ne 'full' -and ($expectedTarget -notin $sel.Targets)) {
                            $errors.Add("Linked source '$relCompile' compiled by '$proj' is not mapped to target '$expectedTarget' (got: $($sel.Targets -join ', ')).")
                        }
                    }
                }
            }
        }
    }

    # 2) Ensure every test project in GodotPal.sln is registered in map.TestProjects.
    foreach ($proj in $staticGraph.Projects) {
        if ($proj.StartsWith('tests/', [StringComparison]::OrdinalIgnoreCase)) {
            if (-not $map.TestProjects.ContainsKey($proj)) {
                $errors.Add("Solution test project '$proj' is missing from TestProjects in $MapPath.")
            }
        }
    }

    # 3) Verify every game:<category> referenced in map exists in tests/Pal.Game.Tests.
    $repoCategories = @(Get-RepositoryGameCategories -RepositoryRoot $RepositoryRoot)
    $smokeCat = if ($map.ContainsKey('SmokeGameCategory')) { [string]$map.SmokeGameCategory } else { 'Smoke' }
    if ($repoCategories.Count -gt 0 -and ($smokeCat -notin $repoCategories)) {
        $errors.Add("SmokeGameCategory '$smokeCat' does not exist in tests/Pal.Game.Tests (found: $($repoCategories -join ', ')).")
    }
    foreach ($rule in $map.Rules) {
        if ($rule.ContainsKey('Targets') -and $rule.Targets) {
            foreach ($t in $rule.Targets) {
                $ts = [string]$t
                if ($ts.StartsWith('game:', [StringComparison]::OrdinalIgnoreCase) -and $ts -ine 'game:*') {
                    $cat = $ts.Substring(5)
                    if ($repoCategories.Count -gt 0 -and ($cat -notin $repoCategories)) {
                        $errors.Add("Rule '$($rule.Name)' references unknown Game TestCategory '$cat' (found: $($repoCategories -join ', ')).")
                    }
                }
            }
        }
    }

    return @{
        Passed = ($errors.Count -eq 0)
        Errors = @($errors)
        CheckedProjects = $staticGraph.Projects.Count
        CategoriesFound = $repoCategories
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-RepoRelativePath',
    'Test-GlobMatch',
    'Get-SolutionProjects',
    'Get-StaticProjectGraph',
    'Get-ReverseClosure',
    'Get-FileGameTestCategories',
    'Get-RepositoryGameCategories',
    'Get-ChangedFilesFromGit',
    'Resolve-AffectedSelection',
    'Test-AffectedMapIntegrity'
)
