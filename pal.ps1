#!/usr/bin/env pwsh
# Unified cross-platform (pwsh 7) front-door CLI for godot-pal.
# Pure 0/1 exit codes, no auto-retry, no quarantine.
# Intentionally omits [CmdletBinding()] so POSIX-style flags like --out never collide with PowerShell common parameters.
$CliArgs = @($args | ForEach-Object { [string]$_ })

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$script:RepoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$script:PinnedGodotVersion = '4.7.2.stable.mono.official.ed1daf0bf'

Import-Module (Join-Path $script:RepoRoot 'scripts/pal/AffectedSelection.psm1') -Force
Import-Module (Join-Path $script:RepoRoot 'scripts/pal/TestReport.psm1') -Force

function Show-Usage {
    @'
Usage:
  pwsh -NoProfile -NonInteractive -File ./pal.ps1 <command> [subcommand] [options]

Commands:
  test affected          [T0] Incremental test selection (default when subcommand omitted)
       unit              Run Pal.Core.UnitTests and/or Pal.Extract.UnitTests
       integration       Run Pal.Extract.IntegrationTests and/or Pal.Prepare.Tests
       game              Run tests/Pal.Game.Tests (Godot headless + GdUnit)
       e2e               [T3] End-to-end GPU scenarios (reserved for M7-5)
       lra               Run tools/long-running-agent npm test
       full | all        Run committable full suite (map self-check + unit + integration + game)
       check-map         Run MSBuild dependency graph & tests/affected-map.psd1 self-check

  ci   dotnet            [T1/T2 Job 1] Syntax + boundary + map self-check + Unit (coverlet) + CLI integration
       prepare           [T1/T2 Job 2] Prepare integration tests (requires Godot)
       game              [T1/T2 Job 3] Pal.Game.Tests headless single pass (requires Godot)

  godot install          Download pinned official Godot 4.7.2-stable mono (win64/linux)
        locate           Resolve & verify pinned Godot executable version
        import           Warm Godot --headless --editor --import cache for Pal.Game & Pal.Game.Tests

  assets prepare         Wrap scripts/Prepare-*.ps1 (--kind scene|dialogue|battle-data|battle-context|fire-effects|item-bitmaps|story)
         status          Show shared T3 asset cache status (reserved interface)

  report                 Render Markdown summary / PR evidence comment from run-summary.json & TRX

Common Options:
  --project <name>       test unit: core|extract; test integration: extract|prepare
  --category <name>      test game: GdUnit [TestCategory] (repeatable)
  --filter <expr>        Test filter expression
  --base <ref>           test affected: git base ref (default merge-base origin/main HEAD)
  --explain              test affected: print file -> rule -> targets without running tests
  --all                  test affected: bypass incremental selection and run full
  --coverage             test unit: enable coverlet.collector XPlat Code Coverage
  --scenario <name>      test e2e: scenario name (default all)
  --gpu                  test e2e: real GPU mode
  --godot-path <path>    Explicit Godot executable path (overrides GODOT_BIN)
  --platform <p>         godot install: auto|win64|linux
  --destination <dir>    godot install: target directory
  --kind <name>          assets prepare: scene|dialogue|battle-data|battle-context|fire-effects|item-bitmaps|story
  --input <path>         assets prepare: input package directory
  --output <path>        assets prepare: output Assets directory
  --from <dir>           report: artifact directory (repeatable)
  --format <fmt>         report: pr-comment|summary (default pr-comment)
  --out <path>           Artifact output directory (or report output file)
  --json                 Print PAL_SUMMARY_JSON=<path> to stdout
  --help, -h             Show this help text

Exit Codes:
  0  Passed (or test affected no-op for pure docs / no changes)
  1  Failed or error (errorMsg written to stderr and run-summary.json)
'@ | Write-Output
}

function Parse-Options {
    param([string[]]$Tokens)
    $positionals = [Collections.Generic.List[string]]::new()
    $flags = @{}
    $multi = @{ category = [Collections.Generic.List[string]]::new(); from = [Collections.Generic.List[string]]::new() }
    $valueOptions = @(
        'project', 'category', 'filter', 'base', 'scenario', 'godot-path',
        'out', 'platform', 'destination', 'kind', 'input', 'output', 'from', 'format', 'repo-root'
    )
    $switchOptions = @('explain', 'all', 'coverage', 'gpu', 'json', 'help', 'include-real-assets')

    $i = 0
    while ($i -lt $Tokens.Count) {
        $tok = [string]$Tokens[$i]
        if ($tok -eq '-h' -or $tok -eq '--help') {
            $flags['help'] = $true
            $i++
            continue
        }
        if ($tok.StartsWith('--')) {
            $name = $tok.Substring(2)
            if ($name -in $switchOptions) {
                $flags[$name] = $true
                $i++
                continue
            }
            if ($name -in $valueOptions) {
                if (($i + 1) -ge $Tokens.Count -or [string]::IsNullOrWhiteSpace($Tokens[$i + 1])) {
                    throw "Option '--$name' requires a non-empty value."
                }
                $val = [string]$Tokens[$i + 1]
                if ($multi.ContainsKey($name)) {
                    $multi[$name].Add($val)
                } else {
                    $flags[$name] = $val
                }
                $i += 2
                continue
            }
            throw "Unknown option: $tok"
        }
        $positionals.Add($tok)
        $i++
    }
    return @{
        Positionals = @($positionals)
        Flags       = $flags
        Multi       = $multi
    }
}

function Resolve-GodotBinary {
    param([string]$ExplicitPath)
    $candidate = $ExplicitPath
    if (-not $candidate) { $candidate = $env:GODOT_BIN }
    if (-not $candidate) { $candidate = $env:PAL_TEST_GODOT }
    if ($candidate) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [IO.Path]::GetFullPath($candidate)
        }
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
        throw "Configured Godot executable not found: $candidate"
    }
    foreach ($name in @('godot-mono.console.exe', 'godot-mono', 'godot')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    throw 'Godot executable not found. Pass --godot-path, set GODOT_BIN, or run `./pal.ps1 godot install`.'
}

function Assert-NoRunningGodotProcess {
    # Cross-platform process check: strictly PID and ProcessName only.
    $procs = @(Get-Process -Name 'godot*' -ErrorAction SilentlyContinue | Select-Object Id, ProcessName)
    if ($procs.Count -gt 0) {
        $list = ($procs | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ', '
        throw "Existing Godot process detected ($list); stop and confirm ownership before starting Godot."
    }
}

function Assert-PinnedGodotVersion {
    param([Parameter(Mandatory)][string]$GodotExe)
    $ver = (& $GodotExe --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $ver -ne $script:PinnedGodotVersion) {
        throw "Godot version mismatch: expected '$($script:PinnedGodotVersion)', got '$ver' (exit $LASTEXITCODE)."
    }
    return $ver
}

function Get-CurrentCommitSha {
    param([string]$Root)
    Push-Location $Root
    try {
        $sha = (& git rev-parse --short HEAD 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $sha) { return $sha }
        return ''
    } finally { Pop-Location }
}

function New-ArtifactsDirectory {
    param(
        [string]$Root,
        [string]$ExplicitOut,
        [string]$Label
    )
    if (-not [string]::IsNullOrWhiteSpace($ExplicitOut)) {
        $dir = if ([IO.Path]::IsPathRooted($ExplicitOut)) { $ExplicitOut } else { Join-Path $Root $ExplicitOut }
        $full = [IO.Path]::GetFullPath($dir)
        [void][IO.Directory]::CreateDirectory($full)
        return $full
    }
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $safe = ($Label -replace '[^a-zA-Z0-9_-]', '-')
    $dir = Join-Path $Root ".artifacts/pal/$stamp-$safe"
    [void][IO.Directory]::CreateDirectory($dir)
    return [IO.Path]::GetFullPath($dir)
}

function Get-RelativeArtifactPath {
    param([string]$BaseDir, [string]$TargetPath)
    return ([IO.Path]::GetRelativePath($BaseDir, $TargetPath) -replace '\\', '/')
}

function Invoke-DotnetTestStep {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ProjectRelPath,
        [Parameter(Mandatory)][string]$ArtifactsDir,
        [string]$RunSettingsRelPath = '',
        [string]$Filter = '',
        [switch]$CollectCoverage,
        [string]$CoveragePackage = '',
        [switch]$EmitJunit,
        [string]$GodotExe = '',
        [Parameter(Mandatory)][hashtable]$State
    )
    $projFull = Join-Path $script:RepoRoot $ProjectRelPath
    $rawDir = Join-Path $ArtifactsDir "raw/$Name"
    if (Test-Path -LiteralPath $rawDir) { Remove-Item -LiteralPath $rawDir -Recurse -Force }
    [void][IO.Directory]::CreateDirectory($rawDir)
    $trxDir = Join-Path $ArtifactsDir 'trx'
    [void][IO.Directory]::CreateDirectory($trxDir)
    $trxName = "$Name.trx"
    $rawTrxPath = Join-Path $rawDir $trxName
    $trxPath = Join-Path $trxDir $trxName

    $argsList = [Collections.Generic.List[string]]::new()
    $argsList.Add('test')
    $argsList.Add($projFull)
    $argsList.Add('--configuration')
    $argsList.Add('Debug')
    if ($RunSettingsRelPath) {
        $argsList.Add('--settings')
        $argsList.Add((Join-Path $script:RepoRoot $RunSettingsRelPath))
    }
    if ($Filter) {
        $argsList.Add('--filter')
        $argsList.Add($Filter)
    }
    $argsList.Add('--logger')
    $argsList.Add("trx;LogFileName=$trxName")
    $argsList.Add('--results-directory')
    $argsList.Add($rawDir)
    if ($CollectCoverage) {
        $argsList.Add('--collect:XPlat Code Coverage')
    }
    if (-not $RunSettingsRelPath) {
        $argsList.Add('--')
        $argsList.Add('RunConfiguration.TreatNoTestsAsError=true')
    }

    $prevGodot = $env:GODOT_BIN
    $prevPalGodot = $env:PAL_TEST_GODOT
    try {
        if ($GodotExe) {
            $env:GODOT_BIN = $GodotExe
            $env:PAL_TEST_GODOT = $GodotExe
        }
        Write-Host "==> dotnet $($argsList -join ' ')"
        & dotnet @argsList
        $exitCode = $LASTEXITCODE
    } finally {
        $env:GODOT_BIN = $prevGodot
        $env:PAL_TEST_GODOT = $prevPalGodot
    }

    if (Test-Path -LiteralPath $rawTrxPath -PathType Leaf) {
        Copy-Item -LiteralPath $rawTrxPath -Destination $trxPath -Force
        $State.TrxFiles.Add((Get-RelativeArtifactPath -BaseDir $ArtifactsDir -TargetPath $trxPath))
        $counts = Read-TrxSummary -TrxPath $trxPath
        $State.Total += $counts.total
        $State.Passed += $counts.passed
        $State.Failed += $counts.failed
        $State.Skipped += $counts.skipped
        if ($EmitJunit) {
            $junitPath = Join-Path (Join-Path $ArtifactsDir 'junit') "$Name.xml"
            Convert-TrxToJunit -TrxPath $trxPath -JunitPath $junitPath
        }
    } elseif ($exitCode -eq 0) {
        throw "Test step '$Name' exited 0 but did not produce expected TRX: $rawTrxPath"
    }

    if ($CollectCoverage) {
        $covFiles = @(Get-ChildItem -LiteralPath $rawDir -Recurse -File -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue)
        $covOutDir = Join-Path $ArtifactsDir "coverage/$Name"
        [void][IO.Directory]::CreateDirectory($covOutDir)
        foreach ($cf in $covFiles) {
            $dest = Join-Path $covOutDir 'coverage.cobertura.xml'
            Copy-Item -LiteralPath $cf.FullName -Destination $dest -Force
            $relCov = Get-RelativeArtifactPath -BaseDir $ArtifactsDir -TargetPath $dest
            if (-not $State.CoverageFiles.Contains($relCov)) { $State.CoverageFiles.Add($relCov) }
            foreach ($entry in @(Read-CoberturaCoverage -CoveragePath $dest)) {
                if (-not $CoveragePackage -or ([string]$entry.package -ieq $CoveragePackage)) {
                    $State.Coverage.Add($entry)
                }
            }
        }
    }

    if ($exitCode -ne 0) {
        $State.Status = 'failed'
        throw "Test step '$Name' failed with exit code $exitCode."
    }
}

function Invoke-UnitTarget {
    param(
        [string]$ProjectScope,
        [string]$Filter,
        [switch]$Coverage,
        [switch]$EmitJunit,
        [string]$ArtifactsDir,
        [hashtable]$State
    )
    $selected = switch ($ProjectScope) {
        'core'    { @(@{ Name = 'Pal.Core.UnitTests'; Path = 'tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj'; Package = 'Pal.Core' }) }
        'extract' { @(@{ Name = 'Pal.Extract.UnitTests'; Path = 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj'; Package = 'Pal.Extract' }) }
        ''        { @(
            @{ Name = 'Pal.Core.UnitTests'; Path = 'tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj'; Package = 'Pal.Core' },
            @{ Name = 'Pal.Extract.UnitTests'; Path = 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj'; Package = 'Pal.Extract' }
        ) }
        default   { throw "Unknown unit project '$ProjectScope'. Allowed: core, extract." }
    }
    foreach ($item in $selected) {
        Invoke-DotnetTestStep -Name $item.Name -ProjectRelPath $item.Path -ArtifactsDir $ArtifactsDir `
            -RunSettingsRelPath 'tests/unit.runsettings' -Filter $Filter -CollectCoverage:$Coverage `
            -CoveragePackage $item.Package -EmitJunit:$EmitJunit -State $State
    }
}

function Invoke-IntegrationTarget {
    param(
        [string]$ProjectScope,
        [string]$Filter,
        [string]$GodotPathOpt,
        [switch]$EmitJunit,
        [string]$ArtifactsDir,
        [hashtable]$State
    )
    $selected = switch ($ProjectScope) {
        'extract' { @('extract') }
        'prepare' { @('prepare') }
        ''        { @('extract', 'prepare') }
        default   { throw "Unknown integration project '$ProjectScope'. Allowed: extract, prepare." }
    }
    if ('extract' -in $selected) {
        Invoke-DotnetTestStep -Name 'Pal.Extract.IntegrationTests' `
            -ProjectRelPath 'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj' `
            -ArtifactsDir $ArtifactsDir -Filter $Filter -EmitJunit:$EmitJunit -State $State
    }
    if ('prepare' -in $selected) {
        Assert-NoRunningGodotProcess
        $godot = Resolve-GodotBinary -ExplicitPath $GodotPathOpt
        [void](Assert-PinnedGodotVersion -GodotExe $godot)
        Invoke-DotnetTestStep -Name 'Pal.Prepare.Tests' `
            -ProjectRelPath 'tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj' `
            -ArtifactsDir $ArtifactsDir -Filter $Filter -EmitJunit:$EmitJunit -GodotExe $godot -State $State
    }
}

function Invoke-GameTarget {
    param(
        [string[]]$Categories,
        [string]$Filter,
        [string]$GodotPathOpt,
        [switch]$IncludeRealAssets,
        [switch]$EmitJunit,
        [string]$ArtifactsDir,
        [hashtable]$State
    )
    Assert-NoRunningGodotProcess
    $godot = Resolve-GodotBinary -ExplicitPath $GodotPathOpt
    [void](Assert-PinnedGodotVersion -GodotExe $godot)

    $parts = [Collections.Generic.List[string]]::new()
    $explicitRealAssets = $IncludeRealAssets -or (@($Categories | Where-Object { $_ -ieq 'RealAssets' }).Count -gt 0)
    if ($Categories -and $Categories.Count -gt 0) {
        $catExpr = (@($Categories | ForEach-Object { "TestCategory=$_" }) -join '|')
        $parts.Add("($catExpr)")
    }
    if (-not $explicitRealAssets) {
        $parts.Add('TestCategory!=RealAssets')
    }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $parts.Add("($Filter)")
    }
    $combinedFilter = ($parts -join '&')

    Invoke-DotnetTestStep -Name 'Pal.Game.Tests' `
        -ProjectRelPath 'tests/Pal.Game.Tests/Pal.Game.Tests.csproj' `
        -ArtifactsDir $ArtifactsDir -RunSettingsRelPath 'tests/Pal.Game.Tests/game.runsettings' `
        -Filter $combinedFilter -EmitJunit:$EmitJunit -GodotExe $godot -State $State
}

function Invoke-LraTarget {
    $lraDir = Join-Path $script:RepoRoot 'tools/long-running-agent'
    if (-not (Test-Path -LiteralPath (Join-Path $lraDir 'package.json') -PathType Leaf)) {
        throw "LRA package.json not found under $lraDir"
    }
    Push-Location $lraDir
    try {
        Write-Host '==> npm test (tools/long-running-agent)'
        & npm test
        if ($LASTEXITCODE -ne 0) {
            throw "LRA npm test failed with exit code $LASTEXITCODE."
        }
    } finally { Pop-Location }
}

function Invoke-MapSelfCheck {
    Write-Host '==> Checking MSBuild dependency graph and tests/affected-map.psd1'
    $result = Test-AffectedMapIntegrity -RepositoryRoot $script:RepoRoot
    if (-not $result.Passed) {
        throw ("Affected map self-check failed:`n - " + ($result.Errors -join "`n - "))
    }
    Write-Host "PASS map self-check: $($result.CheckedProjects) solution projects verified."
}

function Invoke-GodotImportWarmup {
    param([string]$GodotPathOpt)
    Assert-NoRunningGodotProcess
    $godot = Resolve-GodotBinary -ExplicitPath $GodotPathOpt
    [void](Assert-PinnedGodotVersion -GodotExe $godot)
    foreach ($relProj in @('src/Pal.Game', 'tests/Pal.Game.Tests')) {
        $projDir = Join-Path $script:RepoRoot $relProj
        if (-not (Test-Path -LiteralPath (Join-Path $projDir 'project.godot') -PathType Leaf)) { continue }
        Write-Host "==> Godot import warmup: $relProj"
        & $godot --headless --path $projDir --editor --import
        if ($LASTEXITCODE -ne 0) {
            throw "Godot import warmup failed for $relProj (exit $LASTEXITCODE)."
        }
    }
}

$parsed = $null
try {
    $parsed = Parse-Options -Tokens $CliArgs
} catch {
    $errMsg = $_.Exception.Message
    [Console]::Error.WriteLine("pal.ps1 error: $errMsg")
    $art = New-ArtifactsDirectory -Root $script:RepoRoot -ExplicitOut '' -Label 'error'
    $sum = [ordered]@{
        command       = ($CliArgs -join ' ')
        status        = 'error'
        errorMsg      = $errMsg
        durationSec   = 0.0
        counts        = [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0 }
        trxFiles      = @()
        coverageFiles = @()
        coverage      = @()
        artifactsDir  = ConvertTo-RepoRelativePath -RepositoryRoot $script:RepoRoot -Path $art
    }
    [void](Write-RunSummaryJson -ArtifactsDir $art -Summary $sum)
    exit 1
}

if ($parsed.Flags.ContainsKey('repo-root')) {
    $script:RepoRoot = [IO.Path]::GetFullPath([string]$parsed.Flags['repo-root'])
}

if ($parsed.Flags.ContainsKey('help') -or $parsed.Positionals.Count -eq 0) {
    Show-Usage
    exit 0
}

$cmd = $parsed.Positionals[0].ToLowerInvariant()
$sub = if ($parsed.Positionals.Count -ge 2) { $parsed.Positionals[1].ToLowerInvariant() } else { '' }
$commandLabel = if ($sub) { "$cmd $sub" } else { $cmd }

# report writes a markdown file (not an artifacts directory) when --out is a file path
if ($cmd -eq 'report') {
    try {
        $fromDirs = @($parsed.Multi['from'])
        if ($fromDirs.Count -eq 0) { throw 'report requires at least one --from <artifacts-dir>.' }
        $summaries = [Collections.Generic.List[object]]::new()
        foreach ($d in $fromDirs) {
            $fullDir = if ([IO.Path]::IsPathRooted($d)) { $d } else { Join-Path $script:RepoRoot $d }
            $sumFile = Join-Path $fullDir 'run-summary.json'
            if (-not (Test-Path -LiteralPath $sumFile -PathType Leaf)) {
                throw "run-summary.json not found in $fullDir"
            }
            $summaries.Add(([IO.File]::ReadAllText($sumFile, [Text.Encoding]::UTF8) | ConvertFrom-Json))
        }
        $fmt = if ($parsed.Flags.ContainsKey('format')) { [string]$parsed.Flags['format'] } else { 'pr-comment' }
        if ($fmt -notin @('pr-comment', 'summary')) { throw "Unsupported --format '$fmt'. Allowed: pr-comment, summary." }
        $sha = Get-CurrentCommitSha -Root $script:RepoRoot
        $md = Format-MarkdownReport -Summaries $summaries.ToArray() -CommitSha $sha -Format $fmt
        if ($parsed.Flags.ContainsKey('out')) {
            $outFile = if ([IO.Path]::IsPathRooted($parsed.Flags['out'])) { $parsed.Flags['out'] } else { Join-Path $script:RepoRoot $parsed.Flags['out'] }
            $parent = Split-Path $outFile -Parent
            if ($parent) { [void][IO.Directory]::CreateDirectory($parent) }
            [IO.File]::WriteAllText($outFile, $md, [Text.UTF8Encoding]::new($false))
            Write-Host "Report written to $outFile"
        } else {
            Write-Output $md
        }
        exit 0
    } catch {
        [Console]::Error.WriteLine("pal.ps1 report error: $($_.Exception.Message)")
        exit 1
    }
}

$sw = [Diagnostics.Stopwatch]::StartNew()
$explicitOut = if ($parsed.Flags.ContainsKey('out')) { [string]$parsed.Flags['out'] } else { '' }
$artifactsDir = New-ArtifactsDirectory -Root $script:RepoRoot -ExplicitOut $explicitOut -Label ($commandLabel -replace '\s+', '-')
$state = @{
    Status        = 'passed'
    ErrorMsg      = $null
    Total         = 0
    Passed        = 0
    Failed        = 0
    Skipped       = 0
    TrxFiles      = [Collections.Generic.List[string]]::new()
    CoverageFiles = [Collections.Generic.List[string]]::new()
    Coverage      = [Collections.Generic.List[object]]::new()
    Notes         = [Collections.Generic.List[string]]::new()
}

try {
    $godotOpt = if ($parsed.Flags.ContainsKey('godot-path')) { [string]$parsed.Flags['godot-path'] } else { '' }
    $filterOpt = if ($parsed.Flags.ContainsKey('filter')) { [string]$parsed.Flags['filter'] } else { '' }
    $projectOpt = if ($parsed.Flags.ContainsKey('project')) { [string]$parsed.Flags['project'] } else { '' }

    switch ($cmd) {
        'test' {
            $testSub = if ($sub) { $sub } else { 'affected' }
            $commandLabel = "test $testSub"
            switch ($testSub) {
                'affected' {
                    $baseOpt = if ($parsed.Flags.ContainsKey('base')) { [string]$parsed.Flags['base'] } else { '' }
                    $forceAll = $parsed.Flags.ContainsKey('all')
                    $explainOnly = $parsed.Flags.ContainsKey('explain')
                    $gitInfo = Get-ChangedFilesFromGit -RepositoryRoot $script:RepoRoot -BaseRef $baseOpt
                    $sel = Resolve-AffectedSelection -RepositoryRoot $script:RepoRoot -ChangedFiles $gitInfo.Files -ForceAll:$forceAll

                    Write-Host "Affected selection (map v$($sel.MapVersion), base $($gitInfo.BaseCommit)): mode=$($sel.Mode)"
                    Write-Host "Reason: $($sel.Reason)"
                    foreach ($dec in $sel.FileDecisions) {
                        $rText = if ($dec.rules.Count -gt 0) { $dec.rules -join ',' } else { '<unmatched>' }
                        $tText = if ($dec.targets.Count -gt 0) { $dec.targets -join ',' } else { 'none' }
                        Write-Host "  $($dec.file) -> [$rText] -> $tText"
                    }
                    Write-Host "Selected targets: $(if ($sel.Targets.Count -gt 0) { $sel.Targets -join ', ' } else { '<none (no-op)>' })"

                    $state.Notes.Add("mode=$($sel.Mode); targets=$($sel.Targets -join ',')")
                    if ($explainOnly -or $sel.Mode -eq 'no-op') {
                        if ($sel.Mode -eq 'no-op') { Write-Host 'NO-OP: No tests required for this change set.' }
                        break
                    }

                    if ($sel.Mode -eq 'full') {
                        Invoke-MapSelfCheck
                        Invoke-UnitTarget -ProjectScope '' -Filter '' -Coverage:$parsed.Flags.ContainsKey('coverage') -ArtifactsDir $artifactsDir -State $state
                        Invoke-IntegrationTarget -ProjectScope '' -Filter '' -GodotPathOpt $godotOpt -ArtifactsDir $artifactsDir -State $state
                        Invoke-GameTarget -Categories @() -Filter '' -GodotPathOpt $godotOpt -IncludeRealAssets:$parsed.Flags.ContainsKey('include-real-assets') -ArtifactsDir $artifactsDir -State $state
                    } else {
                        if ('core-unit' -in $sel.Targets) {
                            Invoke-UnitTarget -ProjectScope 'core' -Filter $filterOpt -Coverage:$parsed.Flags.ContainsKey('coverage') -ArtifactsDir $artifactsDir -State $state
                        }
                        if ('extract-unit' -in $sel.Targets) {
                            Invoke-UnitTarget -ProjectScope 'extract' -Filter $filterOpt -Coverage:$parsed.Flags.ContainsKey('coverage') -ArtifactsDir $artifactsDir -State $state
                        }
                        if ('extract-integration' -in $sel.Targets) {
                            Invoke-IntegrationTarget -ProjectScope 'extract' -Filter $filterOpt -GodotPathOpt $godotOpt -ArtifactsDir $artifactsDir -State $state
                        }
                        if ('prepare' -in $sel.Targets) {
                            Invoke-IntegrationTarget -ProjectScope 'prepare' -Filter $filterOpt -GodotPathOpt $godotOpt -ArtifactsDir $artifactsDir -State $state
                        }
                        $gameTargets = @($sel.Targets | Where-Object { $_.StartsWith('game:', [StringComparison]::OrdinalIgnoreCase) })
                        if ($gameTargets.Count -gt 0) {
                            $cats = if ('game:*' -in $gameTargets) { @() } else { @($gameTargets | ForEach-Object { $_.Substring(5) }) }
                            Invoke-GameTarget -Categories $cats -Filter $filterOpt -GodotPathOpt $godotOpt -IncludeRealAssets:$parsed.Flags.ContainsKey('include-real-assets') -ArtifactsDir $artifactsDir -State $state
                        }
                        if ('lra' -in $sel.Targets) {
                            Invoke-LraTarget
                        }
                    }
                }
                'unit' {
                    Invoke-UnitTarget -ProjectScope $projectOpt -Filter $filterOpt -Coverage:$parsed.Flags.ContainsKey('coverage') -ArtifactsDir $artifactsDir -State $state
                }
                'integration' {
                    Invoke-IntegrationTarget -ProjectScope $projectOpt -Filter $filterOpt -GodotPathOpt $godotOpt -ArtifactsDir $artifactsDir -State $state
                }
                'game' {
                    Invoke-GameTarget -Categories $parsed.Multi['category'].ToArray() -Filter $filterOpt -GodotPathOpt $godotOpt -IncludeRealAssets:$parsed.Flags.ContainsKey('include-real-assets') -ArtifactsDir $artifactsDir -State $state
                }
                'lra' {
                    Invoke-LraTarget
                }
                'check-map' {
                    Invoke-MapSelfCheck
                }
                'e2e' {
                    $state.Status = 'error'
                    throw 'T3 E2E GPU scenarios are reserved for M7-5 and not yet implemented in this branch.'
                }
                { $_ -in @('full', 'all') } {
                    Invoke-MapSelfCheck
                    Invoke-UnitTarget -ProjectScope '' -Filter '' -Coverage:$parsed.Flags.ContainsKey('coverage') -ArtifactsDir $artifactsDir -State $state
                    Invoke-IntegrationTarget -ProjectScope '' -Filter '' -GodotPathOpt $godotOpt -ArtifactsDir $artifactsDir -State $state
                    Invoke-GameTarget -Categories @() -Filter '' -GodotPathOpt $godotOpt -IncludeRealAssets:$parsed.Flags.ContainsKey('include-real-assets') -ArtifactsDir $artifactsDir -State $state
                }
                default {
                    $state.Status = 'error'
                    throw "Unknown test subcommand '$testSub'. Expected: affected, unit, integration, game, e2e, lra, full, all, check-map."
                }
            }
        }
        'ci' {
            switch ($sub) {
                'dotnet' {
                    Write-Host '==> Guard: PowerShell syntax'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'scripts/Test-PowerShellSyntax.ps1') -RepositoryRoot $script:RepoRoot
                    if ($LASTEXITCODE -ne 0) { throw "Test-PowerShellSyntax.ps1 failed (exit $LASTEXITCODE)." }

                    Write-Host '==> Guard: Unit boundary'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'scripts/Test-UnitBoundary.ps1') -RepositoryRoot $script:RepoRoot
                    if ($LASTEXITCODE -ne 0) { throw "Test-UnitBoundary.ps1 failed (exit $LASTEXITCODE)." }

                    Invoke-MapSelfCheck
                    Invoke-UnitTarget -ProjectScope '' -Filter '' -Coverage -EmitJunit -ArtifactsDir $artifactsDir -State $state
                    Invoke-IntegrationTarget -ProjectScope 'extract' -Filter '' -GodotPathOpt '' -EmitJunit -ArtifactsDir $artifactsDir -State $state
                }
                'prepare' {
                    Invoke-IntegrationTarget -ProjectScope 'prepare' -Filter '' -GodotPathOpt $godotOpt -EmitJunit -ArtifactsDir $artifactsDir -State $state
                }
                'game' {
                    Invoke-GameTarget -Categories @() -Filter '' -GodotPathOpt $godotOpt -EmitJunit -ArtifactsDir $artifactsDir -State $state
                }
                default {
                    $state.Status = 'error'
                    throw "Unknown ci subcommand '$sub'. Expected: dotnet, prepare, game."
                }
            }
        }
        'godot' {
            switch ($sub) {
                'install' {
                    $dest = if ($parsed.Flags.ContainsKey('destination')) { [string]$parsed.Flags['destination'] } else { Join-Path ([IO.Path]::GetTempPath()) ('godot-4.7.2-' + [guid]::NewGuid().ToString('N')) }
                    $plat = if ($parsed.Flags.ContainsKey('platform')) { [string]$parsed.Flags['platform'] } else { 'auto' }
                    $exe = & (Join-Path $script:RepoRoot 'scripts/Get-Godot.ps1') -Destination $dest -Platform $plat
                    if ($LASTEXITCODE -ne 0 -or -not $exe) { throw 'scripts/Get-Godot.ps1 failed.' }
                    Write-Output $exe
                    if ($env:GITHUB_ENV) {
                        [IO.File]::AppendAllText($env:GITHUB_ENV, "GODOT_BIN=$exe`n")
                    }
                }
                'locate' {
                    $exe = Resolve-GodotBinary -ExplicitPath $godotOpt
                    $ver = Assert-PinnedGodotVersion -GodotExe $exe
                    Write-Output "$exe ($ver)"
                }
                'import' {
                    Invoke-GodotImportWarmup -GodotPathOpt $godotOpt
                }
                default {
                    $state.Status = 'error'
                    throw "Unknown godot subcommand '$sub'. Expected: install, locate, import."
                }
            }
        }
        'assets' {
            switch ($sub) {
                'prepare' {
                    $kind = if ($parsed.Flags.ContainsKey('kind')) { [string]$parsed.Flags['kind'] } else { '' }
                    $scriptMap = @{
                        'scene'          = 'scripts/Prepare-GameScene.ps1'
                        'dialogue'       = 'scripts/Prepare-DialoguePackage.ps1'
                        'battle-data'    = 'scripts/Prepare-BattleData.ps1'
                        'battle-context' = 'scripts/Prepare-BattleContext.ps1'
                        'fire-effects'   = 'scripts/Prepare-FireEffects.ps1'
                        'item-bitmaps'   = 'scripts/Prepare-ItemBitmaps.ps1'
                        'story'          = 'scripts/Prepare-StoryPackage.ps1'
                    }
                    if (-not $kind -or -not $scriptMap.ContainsKey($kind)) {
                        $state.Status = 'error'
                        throw "assets prepare requires --kind <$($scriptMap.Keys -join '|')>."
                    }
                    if (-not $parsed.Flags.ContainsKey('input') -or -not $parsed.Flags.ContainsKey('output')) {
                        $state.Status = 'error'
                        throw 'assets prepare requires --input <dir> and --output <dir>.'
                    }
                    $prepArgs = @('-InputPath', [string]$parsed.Flags['input'], '-OutputPath', [string]$parsed.Flags['output'])
                    if ($godotOpt) { $prepArgs += @('-GodotPath', $godotOpt) }
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot $scriptMap[$kind]) @prepArgs
                    if ($LASTEXITCODE -ne 0) { throw "Asset preparation '$kind' failed with exit code $LASTEXITCODE." }
                }
                'status' {
                    $cacheRoot = $env:PAL_ASSET_CACHE_DIR
                    if ($cacheRoot -and (Test-Path -LiteralPath $cacheRoot -PathType Container)) {
                        Write-Output 'T3 shared asset cache: configured (directory exists; private path omitted).'
                    } else {
                        Write-Output 'T3 shared asset cache: not configured (reserved for M7-5).'
                    }
                }
                default {
                    $state.Status = 'error'
                    throw "Unknown assets subcommand '$sub'. Expected: prepare, status."
                }
            }
        }
        default {
            $state.Status = 'error'
            throw "Unknown command '$cmd'. Expected: test, ci, godot, assets, report."
        }
    }
} catch {
    if ($state.Status -eq 'passed') { $state.Status = 'error' }
    $state.ErrorMsg = $_.Exception.Message
    [Console]::Error.WriteLine("pal.ps1 ($commandLabel) failed: $($state.ErrorMsg)")
} finally {
    $sw.Stop()
}

$summaryObj = [ordered]@{
    command       = $commandLabel
    status        = $state.Status
    errorMsg      = $state.ErrorMsg
    durationSec   = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
    counts        = [ordered]@{
        total   = $state.Total
        passed  = $state.Passed
        failed  = $state.Failed
        skipped = $state.Skipped
    }
    trxFiles      = @($state.TrxFiles)
    coverageFiles = @($state.CoverageFiles)
    coverage      = @($state.Coverage)
    artifactsDir  = ConvertTo-RepoRelativePath -RepositoryRoot $script:RepoRoot -Path $artifactsDir
}

$summaryPath = Write-RunSummaryJson -ArtifactsDir $artifactsDir -Summary $summaryObj

if ($cmd -eq 'ci') {
    $sha = Get-CurrentCommitSha -Root $script:RepoRoot
    $md = Format-MarkdownReport -Summaries @(([PSCustomObject]$summaryObj)) -CommitSha $sha -Format 'summary'
    $mdPath = Join-Path $artifactsDir 'summary.md'
    [IO.File]::WriteAllText($mdPath, $md, [Text.UTF8Encoding]::new($false))
    if ($env:GITHUB_STEP_SUMMARY) {
        [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $md, [Text.UTF8Encoding]::new($false))
    }
}

if ($parsed.Flags.ContainsKey('json')) {
    Write-Output "PAL_SUMMARY_JSON=$summaryPath"
}

if ($state.Status -eq 'passed') { exit 0 }
exit 1
