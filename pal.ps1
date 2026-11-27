#!/usr/bin/env pwsh
# Unified cross-platform (pwsh 7) front-door CLI for godot-pal.
# Pure 0/1 exit codes, no auto-retry, no quarantine.
# Intentionally omits [CmdletBinding()] so POSIX-style flags like --out never collide with PowerShell common parameters.
$CliArgs = @($args | ForEach-Object { [string]$_ })

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$script:RepoRoot = [IO.Path]::GetFullPath($PSScriptRoot)

Import-Module (Join-Path $script:RepoRoot 'scripts/pal/PalCommon.psm1') -Force -Global
Import-Module (Join-Path $script:RepoRoot 'scripts/pal/AffectedSelection.psm1') -Force
Import-Module (Join-Path $script:RepoRoot 'scripts/pal/TestReport.psm1') -Force
Import-Module (Join-Path $script:RepoRoot 'scripts/pal/T3Assets.psm1') -Force
Import-Module (Join-Path $script:RepoRoot 'scripts/pal/CapabilityLedger.psm1') -Force

function Show-Usage {
    @'
Usage:
  pwsh -NoProfile -NonInteractive -File ./pal.ps1 <command> [subcommand] [options]

Commands:
  test affected          [T0] Incremental test selection (default when subcommand omitted)
       unit              Run Pal.Core.UnitTests and/or Pal.Extract.UnitTests
       integration       Run Pal.Extract.IntegrationTests and/or Pal.Prepare.Tests
       game              Run tests/Pal.Game.Tests (Godot headless + GdUnit)
       e2e               [T3] End-to-end GPU YAML regression scenarios (--gpu required)
       lra               Run tools/long-running-agent npm test
       full | all        Run committable full suite (map self-check + unit + integration + game)
       check-map         Run MSBuild dependency graph & tests/affected-map.psd1 self-check
       check-ledger      Verify code-derived docs/generated/capabilities-ledger.md zero-drift (--update to regenerate)

  ci   dotnet            [T1/T2 Job 1] Syntax + boundary + map self-check + Unit (coverlet) + CLI integration
       prepare           [T1/T2 Job 2] Prepare integration tests (requires Godot)
       game              [T1/T2 Job 3] Pal.Game.Tests headless single pass (requires Godot)

  godot install          Download pinned official Godot 4.7.2-stable mono (win64/linux)
        locate           Resolve & verify pinned Godot executable version
        import           Warm Godot --headless --editor --import cache for Pal.Game & Pal.Game.Tests

  assets prepare         Prepare T3 shared real-asset cache (default) or wrap scripts/Prepare-*.ps1 (--kind)
         status          Show shared T3 asset & golden cache status

  report                 Render Markdown summary / PR evidence comment from run-summary.json & TRX

Common Options:
  --project <name>       test unit: core|extract; test integration: extract|prepare
  --category <name>      test game: GdUnit [TestCategory] (repeatable)
  --filter <expr>        Test filter expression
  --base <ref>           test affected: git base ref (default merge-base origin/main HEAD)
  --explain              test affected: print file -> rule -> targets without running tests
  --all                  test affected: bypass incremental selection and run full
  --coverage             test unit: enable coverlet.collector XPlat Code Coverage
  --blame-hang           test game: enable --blame-hang-timeout 2m and normal console logger
  --scenario <name|path> test e2e: scenario name, YAML file path, or all (default all)
  --gpu                  test e2e: real GPU mode (required for T3 E2E)
  --update-golden        test e2e: explicitly create/refresh local golden screenshots & manifest
  --godot-path <path>    Explicit Godot executable path (overrides GODOT_BIN)
  --platform <p>         godot install: auto|win64|linux
  --destination <dir>    godot install: target directory
  --kind <name>          assets prepare: scene|dialogue|battle-data|battle-context|fire-effects|item-bitmaps|story
  --input <path>         assets prepare: input package directory
  --output <path>        assets prepare: output Assets directory
  --static-resources <p> assets prepare (battle-context): static resources directory
  --static-profile <p>   assets prepare (battle-context): static profile .tres path
  --battle-resources <p> assets prepare (battle-context): optional battle resources directory
  --from <dir>           report: artifact directory (repeatable)
  --format <fmt>         report: pr-comment|summary (default pr-comment)
  --out <path>           Artifact output directory (or report output file)
  --repo-root <dir>      Override repository root directory
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
        'out', 'platform', 'destination', 'kind', 'input', 'output',
        'static-resources', 'static-profile', 'battle-resources',
        'from', 'format', 'repo-root'
    )
    $switchOptions = @('explain', 'all', 'coverage', 'blame-hang', 'gpu', 'update-golden', 'update', 'json', 'help', 'include-real-assets')

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
        $full = Resolve-PathUnderRoot -Root $Root -Path $ExplicitOut
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
        [switch]$BlameHang,
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
    if ($BlameHang) {
        $argsList.Add('--logger')
        $argsList.Add('console;verbosity=normal')
        $argsList.Add('--blame-hang')
        $argsList.Add('--blame-hang-timeout')
        $argsList.Add('2m')
        $argsList.Add('--blame-hang-dump-type')
        $argsList.Add('mini')
    }
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
    $stepSw = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($GodotExe) {
            $env:GODOT_BIN = $GodotExe
            $env:PAL_TEST_GODOT = $GodotExe
        }
        Write-Host "==> dotnet $($argsList -join ' ')"
        & dotnet @argsList
        $exitCode = $LASTEXITCODE
    } finally {
        $stepSw.Stop()
        $env:GODOT_BIN = $prevGodot
        $env:PAL_TEST_GODOT = $prevPalGodot
    }

    $trxCounts = $null
    if (Test-Path -LiteralPath $rawTrxPath -PathType Leaf) {
        Copy-Item -LiteralPath $rawTrxPath -Destination $trxPath -Force
        $State.TrxFiles.Add((Get-RelativeArtifactPath -BaseDir $ArtifactsDir -TargetPath $trxPath))
        $trxCounts = Read-TrxSummary -TrxPath $trxPath
        $State.Total += $trxCounts.total
        $State.Passed += $trxCounts.passed
        $State.Failed += $trxCounts.failed
        $State.Skipped += $trxCounts.skipped
        $State.Steps.Add([ordered]@{
            name        = $Name
            total       = $trxCounts.total
            passed      = $trxCounts.passed
            failed      = $trxCounts.failed
            skipped     = $trxCounts.skipped
            durationSec = [Math]::Round($stepSw.Elapsed.TotalSeconds, 2)
        })
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

    if ($BlameHang) {
        $blameFiles = @(Get-ChildItem -LiteralPath $rawDir -Recurse -File -Include 'Sequence*.xml', '*.dmp' -ErrorAction SilentlyContinue)
        if ($blameFiles.Count -gt 0) {
            $blameDir = Join-Path $ArtifactsDir 'blame'
            [void][IO.Directory]::CreateDirectory($blameDir)
            foreach ($bf in $blameFiles) {
                Copy-Item -LiteralPath $bf.FullName -Destination (Join-Path $blameDir $bf.Name) -Force
            }
        }
    }

    if ($exitCode -ne 0) {
        $State.Status = 'failed'
        throw "Test step '$Name' failed with exit code $exitCode."
    }
    if ($trxCounts -and ($trxCounts.failed -gt 0 -or ($trxCounts.outcome -notin @('Completed', 'Passed')))) {
        $State.Status = 'failed'
        throw "Test step '$Name' TRX reported non-passing outcome '$($trxCounts.outcome)' (failed=$($trxCounts.failed))."
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
        $godot = Resolve-ReadyGodotBinary -ExplicitPath $GodotPathOpt
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
        [switch]$BlameHang,
        [string]$ArtifactsDir,
        [hashtable]$State
    )
    $godot = Resolve-ReadyGodotBinary -ExplicitPath $GodotPathOpt

    $parts = [Collections.Generic.List[string]]::new()
    $explicitRealAssets = $IncludeRealAssets -or (@($Categories | Where-Object { $_ -ieq 'RealAssets' }).Count -gt 0)
    $explicitE2E = (@($Categories | Where-Object { $_ -ieq 'E2E' }).Count -gt 0)
    if ($Categories -and $Categories.Count -gt 0) {
        $catExpr = (@($Categories | ForEach-Object { "TestCategory=$_" }) -join '|')
        $parts.Add("($catExpr)")
    }
    if (-not $explicitRealAssets) {
        $parts.Add('TestCategory!=RealAssets')
    }
    if (-not $explicitE2E) {
        $parts.Add('TestCategory!=E2E')
    }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $parts.Add("($Filter)")
    }
    $combinedFilter = ($parts -join '&')

    Invoke-DotnetTestStep -Name 'Pal.Game.Tests' `
        -ProjectRelPath 'tests/Pal.Game.Tests/Pal.Game.Tests.csproj' `
        -ArtifactsDir $ArtifactsDir -RunSettingsRelPath 'tests/Pal.Game.Tests/game.runsettings' `
        -Filter $combinedFilter -EmitJunit:$EmitJunit -BlameHang:($EmitJunit -or $BlameHang) -GodotExe $godot -State $State
}

function Invoke-LraTarget {
    $lraDir = Join-Path $script:RepoRoot 'tools/long-running-agent'
    if (-not (Test-Path -LiteralPath (Join-Path $lraDir 'package.json') -PathType Leaf)) {
        throw "LRA package.json not found under $lraDir"
    }
    $prevSdkRoot = $env:PAL_PI_SDK_ROOT
    $localSdkRoot = Join-Path $lraDir 'node_modules/@earendil-works/pi-coding-agent'
    Push-Location $lraDir
    try {
        if (-not $env:PAL_PI_SDK_ROOT -and (Test-Path -LiteralPath $localSdkRoot -PathType Container)) {
            $env:PAL_PI_SDK_ROOT = $localSdkRoot
        }
        Write-Host '==> npm run typecheck (tools/long-running-agent)'
        & npm run typecheck
        if ($LASTEXITCODE -ne 0) {
            throw "LRA npm run typecheck failed with exit code $LASTEXITCODE."
        }
        Write-Host '==> npm test (tools/long-running-agent)'
        & npm test
        if ($LASTEXITCODE -ne 0) {
            throw "LRA npm test failed with exit code $LASTEXITCODE."
        }
    } finally {
        $env:PAL_PI_SDK_ROOT = $prevSdkRoot
        Pop-Location
    }
}

function Invoke-MapSelfCheck {
    Write-Host '==> Checking MSBuild dependency graph and tests/affected-map.psd1'
    $result = Test-AffectedMapIntegrity -RepositoryRoot $script:RepoRoot
    if (-not $result.Passed) {
        throw ("Affected map self-check failed:`n - " + ($result.Errors -join "`n - "))
    }
    Write-Host "PASS map self-check: $($result.CheckedProjects) solution projects verified."
}

function Invoke-FullTestSuite {
    param(
        [switch]$Coverage,
        [string]$GodotPathOpt,
        [switch]$IncludeRealAssets,
        [string]$ArtifactsDir,
        [hashtable]$State
    )
    Invoke-MapSelfCheck
    Invoke-UnitTarget -ProjectScope '' -Filter '' -Coverage:$Coverage -ArtifactsDir $ArtifactsDir -State $State
    Invoke-IntegrationTarget -ProjectScope '' -Filter '' -GodotPathOpt $GodotPathOpt -ArtifactsDir $ArtifactsDir -State $State
    Invoke-GameTarget -Categories @() -Filter '' -GodotPathOpt $GodotPathOpt -IncludeRealAssets:$IncludeRealAssets -ArtifactsDir $ArtifactsDir -State $State
}

function Get-YamlPhaseCount {
    param([Parameter(Mandatory)][string]$YamlPath)
    $lines = [IO.File]::ReadAllLines($YamlPath, [Text.Encoding]::UTF8)
    $inPhases = $false
    $count = 0
    foreach ($line in $lines) {
        if ($line -match '^phases:\s*$') {
            $inPhases = $true
            continue
        }
        if ($inPhases) {
            if ($line -match '^[a-zA-Z0-9_-]+:') { break }
            if ($line -match '^\s{2}-\s+') { $count++ }
        }
    }
    return [Math]::Max(1, $count)
}

function Resolve-E2EScenarioFiles {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string]$ScenarioOpt = ''
    )
    $scenariosDir = Join-Path $RepositoryRoot 'tests/Pal.Game.Tests/E2E/Scenarios'
    $canonical = @(
        'boot-and-explore',
        'real-dialogue-bilingual',
        'battle-closure',
        'save-load-restore',
        'dynamic-scene'
    )
    if ([string]::IsNullOrWhiteSpace($ScenarioOpt) -or $ScenarioOpt -ieq 'all') {
        $list = [Collections.Generic.List[string]]::new()
        foreach ($name in $canonical) {
            $p = Join-Path $scenariosDir "$name.yaml"
            if (-not (Test-Path -LiteralPath $p -PathType Leaf)) {
                throw "Canonical E2E scenario file not found: $p"
            }
            $list.Add([IO.Path]::GetFullPath($p))
        }
        return @($list)
    }
    if (Test-Path -LiteralPath $ScenarioOpt -PathType Leaf) {
        return @([IO.Path]::GetFullPath($ScenarioOpt))
    }
    $resolvedPath = Resolve-PathUnderRoot -Root $RepositoryRoot -Path $ScenarioOpt
    if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
        return @($resolvedPath)
    }
    $byName = Join-Path $scenariosDir "$ScenarioOpt.yaml"
    if (Test-Path -LiteralPath $byName -PathType Leaf) {
        return @([IO.Path]::GetFullPath($byName))
    }
    throw "E2E scenario not found: '$ScenarioOpt'. Expected one of ($($canonical -join ', ')) or an existing .yaml file path."
}

function Invoke-E2ETarget {
    param(
        [switch]$Gpu,
        [switch]$UpdateGolden,
        [string]$ScenarioOpt,
        [string]$GodotPathOpt,
        [Parameter(Mandatory)][string]$ArtifactsDir,
        [Parameter(Mandatory)][hashtable]$State
    )
    if (-not $Gpu) {
        $State.Status = 'error'
        throw 'T3 E2E scenarios require real GPU rendering: pass --gpu (T3 E2E is local GPU-only and never runs headless).'
    }
    $cacheInfo = Initialize-T3Assets -RepositoryRoot $script:RepoRoot -GodotPath $GodotPathOpt
    $godot = Resolve-ReadyGodotBinary -ExplicitPath $GodotPathOpt
    $scenarioFiles = Resolve-E2EScenarioFiles -RepositoryRoot $script:RepoRoot -ScenarioOpt $ScenarioOpt
    $commitSha = Get-CurrentCommitSha -Root $script:RepoRoot

    $e2eScenarios = [Collections.Generic.List[object]]::new()
    $checksumEntries = [Collections.Generic.List[object]]::new()
    $firstFailure = $null

    foreach ($yamlFile in $scenarioFiles) {
        $scenarioName = [IO.Path]::GetFileNameWithoutExtension($yamlFile)
        $phaseCount = Get-YamlPhaseCount -YamlPath $yamlFile
        $mergedFrames = [Collections.Generic.List[object]]::new()
        $mergedDuration = 0.0
        $mergedProbe = $true
        $mergedStatus = 'passed'
        $mergedError = $null

        for ($phaseIdx = 0; $phaseIdx -lt $phaseCount; $phaseIdx++) {
            $stepName = if ($phaseCount -gt 1) { "e2e-$scenarioName-phase$phaseIdx" } else { "e2e-$scenarioName" }
            $prevEnv = @{
                PAL_E2E_SCENARIO_PATH = $env:PAL_E2E_SCENARIO_PATH
                PAL_E2E_PHASE_INDEX   = $env:PAL_E2E_PHASE_INDEX
                PAL_E2E_ARTIFACTS_DIR = $env:PAL_E2E_ARTIFACTS_DIR
                PAL_E2E_GOLDEN_DIR    = $env:PAL_E2E_GOLDEN_DIR
                PAL_E2E_UPDATE_GOLDEN = $env:PAL_E2E_UPDATE_GOLDEN
                PAL_E2E_COMMIT_SHA    = $env:PAL_E2E_COMMIT_SHA
            }
            $stepFailed = $false
            try {
                $env:PAL_E2E_SCENARIO_PATH = $yamlFile
                $env:PAL_E2E_PHASE_INDEX = [string]$phaseIdx
                $env:PAL_E2E_ARTIFACTS_DIR = $ArtifactsDir
                $env:PAL_E2E_GOLDEN_DIR = $cacheInfo.GoldensDir
                $env:PAL_E2E_UPDATE_GOLDEN = if ($UpdateGolden) { '1' } else { '0' }
                $env:PAL_E2E_COMMIT_SHA = $commitSha

                Invoke-DotnetTestStep -Name $stepName `
                    -ProjectRelPath 'tests/Pal.Game.Tests/Pal.Game.Tests.csproj' `
                    -ArtifactsDir $ArtifactsDir `
                    -RunSettingsRelPath 'tests/Pal.Game.Tests/e2e-gpu.runsettings' `
                    -Filter 'FullyQualifiedName=Pal.Game.Tests.E2E.E2EScenarioSuite.Run_configured_yaml_scenario' `
                    -GodotExe $godot -State $State
            } catch {
                $stepFailed = $true
                $mergedStatus = 'failed'
                $mergedError = $_.Exception.Message
                if (-not $firstFailure) { $firstFailure = $mergedError }
            } finally {
                foreach ($k in $prevEnv.Keys) {
                    if ($null -eq $prevEnv[$k]) { Remove-Item -Path "Env:$k" -ErrorAction SilentlyContinue }
                    else { Set-Item -Path "Env:$k" -Value $prevEnv[$k] }
                }
            }

            $resFile = Join-Path $ArtifactsDir "e2e-results/$scenarioName.phase$phaseIdx.json"
            if (Test-Path -LiteralPath $resFile -PathType Leaf) {
                $phaseRes = [IO.File]::ReadAllText($resFile, [Text.Encoding]::UTF8) | ConvertFrom-Json
                $mergedDuration += [double]$phaseRes.DurationSec
                if (-not $phaseRes.ProbePassed) { $mergedProbe = $false }
                if ($phaseRes.Status -ne 'passed') {
                    $mergedStatus = 'failed'
                    if ($phaseRes.Error) { $mergedError = [string]$phaseRes.Error }
                }
                foreach ($fr in @($phaseRes.Frames)) {
                    $frameObj = [ordered]@{
                        frameName        = [string]$fr.FrameName
                        status           = [string]$fr.Status
                        error            = $fr.Error
                        actualPath       = [string]$fr.ActualPath
                        actualSha256     = [string]$fr.ActualSha256
                        goldenSha256     = $fr.GoldenSha256
                        diffPath         = $fr.DiffPath
                        diffPercent      = [double]$fr.DiffPercent
                        tolerancePercent = [double]$fr.TolerancePercent
                    }
                    $mergedFrames.Add($frameObj)
                    $checksumEntries.Add([ordered]@{
                        scenario     = $scenarioName
                        frame        = [string]$fr.FrameName
                        actualPath   = [string]$fr.ActualPath
                        actualSha256 = [string]$fr.ActualSha256
                        goldenSha256 = $fr.GoldenSha256
                        diffPercent  = [double]$fr.DiffPercent
                        status       = [string]$fr.Status
                    })
                }
            } elseif ($stepFailed) {
                $mergedProbe = $false
            }
            if ($stepFailed) { break }
        }

        $e2eScenarios.Add([ordered]@{
            scenario    = $scenarioName
            phases      = $phaseCount
            status      = $mergedStatus
            probePassed = $mergedProbe
            durationSec = [Math]::Round($mergedDuration, 2)
            error       = $mergedError
            frames      = @($mergedFrames)
        })
    }

    $checksumPath = Join-Path $ArtifactsDir 'checksums.json'
    [IO.File]::WriteAllText(
        $checksumPath,
        ((ConvertTo-Json -InputObject @($checksumEntries) -Depth 6) + "`n"),
        [Text.UTF8Encoding]::new($false)
    )
    $contactSheetRel = if (Test-Path -LiteralPath (Join-Path $ArtifactsDir 'contact-sheet.png') -PathType Leaf) { 'contact-sheet.png' } else { $null }
    $State.E2E = [ordered]@{
        cacheKey     = $cacheInfo.CacheKey
        updateGolden = [bool]$UpdateGolden
        contactSheet = $contactSheetRel
        checksums    = 'checksums.json'
        scenarios    = @($e2eScenarios)
    }
    if ($firstFailure) {
        $State.Status = 'failed'
        throw $firstFailure
    }
}

function Invoke-GodotImportWarmup {
    param([string]$GodotPathOpt)
    $godot = Resolve-ReadyGodotBinary -ExplicitPath $GodotPathOpt
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
    $sum = New-RunSummaryObject -Command ($CliArgs -join ' ') -Status 'error' -ErrorMsg $errMsg `
        -ArtifactsDir (ConvertTo-RepoRelativePath -RepositoryRoot $script:RepoRoot -Path $art)
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
        $trxFailures = [Collections.Generic.List[object]]::new()
        foreach ($d in $fromDirs) {
            $fullDir = Resolve-PathUnderRoot -Root $script:RepoRoot -Path $d
            $sumFile = Join-Path $fullDir 'run-summary.json'
            if (-not (Test-Path -LiteralPath $sumFile -PathType Leaf)) {
                throw "run-summary.json not found in $fullDir"
            }
            $sumObj = [IO.File]::ReadAllText($sumFile, [Text.Encoding]::UTF8) | ConvertFrom-Json
            $summaries.Add($sumObj)
            if ($sumObj.PSObject.Properties['trxFiles'] -and $sumObj.trxFiles) {
                foreach ($relTrx in @($sumObj.trxFiles)) {
                    $trxFull = Resolve-PathUnderRoot -Root $fullDir -Path ([string]$relTrx)
                    foreach ($failItem in @(Read-TrxFailures -TrxPath $trxFull)) {
                        $trxFailures.Add($failItem)
                    }
                }
            }
        }
        $fmt = if ($parsed.Flags.ContainsKey('format')) { [string]$parsed.Flags['format'] } else { 'pr-comment' }
        if ($fmt -notin @('pr-comment', 'summary')) { throw "Unsupported --format '$fmt'. Allowed: pr-comment, summary." }
        $sha = Get-CurrentCommitSha -Root $script:RepoRoot
        $md = Format-MarkdownReport -Summaries $summaries.ToArray() -CommitSha $sha -Format $fmt -TrxFailures $trxFailures.ToArray()
        if ($parsed.Flags.ContainsKey('out')) {
            $outFile = Resolve-PathUnderRoot -Root $script:RepoRoot -Path ([string]$parsed.Flags['out'])
            $parent = Split-Path $outFile -Parent
            if ($parent) { [void][IO.Directory]::CreateDirectory($parent) }
            [IO.File]::WriteAllText($outFile, $md, [Text.UTF8Encoding]::new($false))
            Write-Host "Report written to $outFile"
        } else {
            Write-Output $md
        }
        exit 0
    } catch {
        $errMsg = $_.Exception.Message
        [Console]::Error.WriteLine("pal.ps1 report error: $errMsg")
        $art = New-ArtifactsDirectory -Root $script:RepoRoot -ExplicitOut '' -Label 'report-error'
        $sum = New-RunSummaryObject -Command 'report' -Status 'error' -ErrorMsg $errMsg `
            -ArtifactsDir (ConvertTo-RepoRelativePath -RepositoryRoot $script:RepoRoot -Path $art)
        $sumPath = Write-RunSummaryJson -ArtifactsDir $art -Summary $sum
        if ($parsed.Flags.ContainsKey('json')) { Write-Output "PAL_SUMMARY_JSON=$sumPath" }
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
    Steps         = [Collections.Generic.List[object]]::new()
    TrxFiles      = [Collections.Generic.List[string]]::new()
    CoverageFiles = [Collections.Generic.List[string]]::new()
    Coverage      = [Collections.Generic.List[object]]::new()
    E2E           = $null
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

                    if ($explainOnly -or $sel.Mode -eq 'no-op') {
                        if ($sel.Mode -eq 'no-op') { Write-Host 'NO-OP: No tests required for this change set.' }
                        break
                    }

                    if ($sel.Mode -eq 'full') {
                        Invoke-FullTestSuite -Coverage:$parsed.Flags.ContainsKey('coverage') -GodotPathOpt $godotOpt -IncludeRealAssets:$parsed.Flags.ContainsKey('include-real-assets') -ArtifactsDir $artifactsDir -State $state
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
                    Invoke-GameTarget -Categories $parsed.Multi['category'].ToArray() -Filter $filterOpt -GodotPathOpt $godotOpt -IncludeRealAssets:$parsed.Flags.ContainsKey('include-real-assets') -BlameHang:$parsed.Flags.ContainsKey('blame-hang') -ArtifactsDir $artifactsDir -State $state
                }
                'lra' {
                    Invoke-LraTarget
                }
                'check-map' {
                    Invoke-MapSelfCheck
                }
                'check-ledger' {
                    Invoke-CapabilityLedgerCheck -RepositoryRoot $script:RepoRoot -Update:$parsed.Flags.ContainsKey('update')
                }
                'e2e' {
                    $scenarioOpt = if ($parsed.Flags.ContainsKey('scenario')) { [string]$parsed.Flags['scenario'] } else { 'all' }
                    Invoke-E2ETarget -Gpu:$parsed.Flags.ContainsKey('gpu') `
                        -UpdateGolden:$parsed.Flags.ContainsKey('update-golden') `
                        -ScenarioOpt $scenarioOpt -GodotPathOpt $godotOpt `
                        -ArtifactsDir $artifactsDir -State $state
                }
                { $_ -in @('full', 'all') } {
                    Invoke-FullTestSuite -Coverage:$parsed.Flags.ContainsKey('coverage') -GodotPathOpt $godotOpt -IncludeRealAssets:$parsed.Flags.ContainsKey('include-real-assets') -ArtifactsDir $artifactsDir -State $state
                }
                default {
                    $state.Status = 'error'
                    throw "Unknown test subcommand '$testSub'. Expected: affected, unit, integration, game, e2e, lra, full, all, check-map, check-ledger."
                }
            }
        }
        'ci' {
            switch ($sub) {
                'dotnet' {
                    Write-Host '==> Guard: No Git LFS pointers in committable trees (src/tests/tools)'
                    Push-Location $script:RepoRoot
                    try {
                        $pointers = @(& git grep --cached -l -e 'version https://git-lfs' -- src tests tools)
                        $grepExit = $LASTEXITCODE
                        if ($grepExit -gt 1) { throw "git grep failed with exit $grepExit." }
                        if ($grepExit -eq 0 -and $pointers.Count -gt 0) {
                            throw "LFS pointers inside committable trees (src/tests/tools): $($pointers -join ', ')"
                        }
                    } finally { Pop-Location }

                    Write-Host '==> Guard: PowerShell syntax'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'scripts/Test-PowerShellSyntax.ps1') -RepositoryRoot $script:RepoRoot
                    if ($LASTEXITCODE -ne 0) { throw "Test-PowerShellSyntax.ps1 failed (exit $LASTEXITCODE)." }

                    Write-Host '==> Guard: Unit boundary'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'scripts/Test-UnitBoundary.ps1') -RepositoryRoot $script:RepoRoot
                    if ($LASTEXITCODE -ne 0) { throw "Test-UnitBoundary.ps1 failed (exit $LASTEXITCODE)." }

                    Write-Host '==> Guard: Repo hygiene'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'scripts/Test-RepoHygiene.ps1') -RepositoryRoot $script:RepoRoot -AllDocs
                    if ($LASTEXITCODE -ne 0) { throw "Test-RepoHygiene.ps1 failed (exit $LASTEXITCODE)." }

                    Write-Host '==> Self-test: PalFrontDoor.Tests.ps1'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'tests/PalFrontDoor.Tests.ps1')
                    if ($LASTEXITCODE -ne 0) { throw "PalFrontDoor.Tests.ps1 failed (exit $LASTEXITCODE)." }

                    Write-Host '==> Self-test: Test-PowerShellSyntax.Tests.ps1'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'tests/Test-PowerShellSyntax.Tests.ps1')
                    if ($LASTEXITCODE -ne 0) { throw "Test-PowerShellSyntax.Tests.ps1 failed (exit $LASTEXITCODE)." }

                    Write-Host '==> Self-test: UnitBoundary.Tests.ps1'
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot 'tests/UnitBoundary.Tests.ps1')
                    if ($LASTEXITCODE -ne 0) { throw "UnitBoundary.Tests.ps1 failed (exit $LASTEXITCODE)." }
                    Invoke-MapSelfCheck
                    Invoke-CapabilityLedgerCheck -RepositoryRoot $script:RepoRoot
                    Invoke-UnitTarget -ProjectScope '' -Filter '' -Coverage -EmitJunit -ArtifactsDir $artifactsDir -State $state
                    Invoke-IntegrationTarget -ProjectScope 'extract' -Filter '' -GodotPathOpt '' -EmitJunit -ArtifactsDir $artifactsDir -State $state
                }
                'prepare' {
                    Invoke-IntegrationTarget -ProjectScope 'prepare' -Filter '' -GodotPathOpt $godotOpt -EmitJunit -ArtifactsDir $artifactsDir -State $state
                }
                'game' {
                    # Partition into two disjoint Godot sessions: Profiles+Prepare (775 tests) vs Runtime/Scene/Process (321 tests),
                    # so neither process accumulates 930+ tests of RefCounted Gen2 wrappers (avoids Godot 4.7.2 mono_object_disposed_baseref GC crash).
                    $godot = Resolve-ReadyGodotBinary -ExplicitPath $godotOpt
                    Invoke-DotnetTestStep -Name 'Pal.Game.Tests.ProfilesPrepare' `
                        -ProjectRelPath 'tests/Pal.Game.Tests/Pal.Game.Tests.csproj' `
                        -ArtifactsDir $artifactsDir -RunSettingsRelPath 'tests/Pal.Game.Tests/game.runsettings' `
                        -Filter '(TestCategory=Profiles|TestCategory=Prepare)&TestCategory!=RealAssets&TestCategory!=E2E' -EmitJunit -BlameHang -GodotExe $godot -State $state
                    Invoke-DotnetTestStep -Name 'Pal.Game.Tests.Runtime' `
                        -ProjectRelPath 'tests/Pal.Game.Tests/Pal.Game.Tests.csproj' `
                        -ArtifactsDir $artifactsDir -RunSettingsRelPath 'tests/Pal.Game.Tests/game.runsettings' `
                        -Filter 'TestCategory!=Profiles&TestCategory!=Prepare&TestCategory!=RealAssets&TestCategory!=E2E' -EmitJunit -BlameHang -GodotExe $godot -State $state
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
                    if (-not $kind) {
                        $info = Initialize-T3Assets -RepositoryRoot $script:RepoRoot -GodotPath $godotOpt
                        $hitText = if ($info.PreparedNow) { 'built and cached' } else { 'cache hit' }
                        Write-Output "T3 shared asset cache ready: $($info.CacheKey) ($hitText, worktree=$($info.LinkMode))"
                        break
                    }
                    $scriptMap = @{
                        'scene'          = 'scripts/Prepare-GameScene.ps1'
                        'dialogue'       = 'scripts/Prepare-DialoguePackage.ps1'
                        'battle-data'    = 'scripts/Prepare-BattleData.ps1'
                        'battle-context' = 'scripts/Prepare-BattleContext.ps1'
                        'fire-effects'   = 'scripts/Prepare-FireEffects.ps1'
                        'item-bitmaps'   = 'scripts/Prepare-ItemBitmaps.ps1'
                        'story'          = 'scripts/Prepare-StoryPackage.ps1'
                    }
                    if (-not $scriptMap.ContainsKey($kind)) {
                        $state.Status = 'error'
                        throw "assets prepare --kind must be one of <$($scriptMap.Keys -join '|')>."
                    }
                    if (-not $parsed.Flags.ContainsKey('input') -or -not $parsed.Flags.ContainsKey('output')) {
                        $state.Status = 'error'
                        throw 'assets prepare requires --input <dir> and --output <dir>.'
                    }
                    $prepArgs = @('-InputPath', [string]$parsed.Flags['input'], '-OutputPath', [string]$parsed.Flags['output'])
                    if ($kind -eq 'battle-context') {
                        if (-not $parsed.Flags.ContainsKey('static-resources') -or -not $parsed.Flags.ContainsKey('static-profile')) {
                            $state.Status = 'error'
                            throw 'assets prepare --kind battle-context requires --static-resources <dir> and --static-profile <path>.'
                        }
                        $prepArgs += @('-StaticResources', [string]$parsed.Flags['static-resources'], '-StaticProfile', [string]$parsed.Flags['static-profile'])
                        if ($parsed.Flags.ContainsKey('battle-resources')) {
                            $prepArgs += @('-BattleResources', [string]$parsed.Flags['battle-resources'])
                        }
                    }
                    if ($godotOpt) { $prepArgs += @('-GodotPath', $godotOpt) }
                    & pwsh -NoProfile -NonInteractive -File (Join-Path $script:RepoRoot $scriptMap[$kind]) @prepArgs
                    if ($LASTEXITCODE -ne 0) { throw "Asset preparation '$kind' failed with exit code $LASTEXITCODE." }
                }
                'status' {
                    $info = Get-T3CacheInfo -RepositoryRoot $script:RepoRoot
                    if (-not $info.Configured) {
                        Write-Output 'T3 shared asset cache: PAL_RAW_DATA_DIR not configured (set PAL_RAW_DATA_DIR to enable T3).'
                    } else {
                        $assetState = if ($info.AssetsHit) { 'HIT' } else { 'MISS' }
                        $goldenState = if ($info.GoldensHit) { 'HIT' } else { 'MISS' }
                        Write-Output "T3 shared asset cache: key=$($info.CacheKey) assets=$assetState goldens=$goldenState (private paths omitted)"
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

$summaryObj = New-RunSummaryObject -Command $commandLabel -Status $state.Status -ErrorMsg $state.ErrorMsg `
    -DurationSec $sw.Elapsed.TotalSeconds -Total $state.Total -Passed $state.Passed -Failed $state.Failed `
    -Skipped $state.Skipped -Steps $state.Steps.ToArray() -TrxFiles $state.TrxFiles.ToArray() -CoverageFiles $state.CoverageFiles.ToArray() `
    -Coverage $state.Coverage.ToArray() -E2E $state.E2E `
    -ArtifactsDir (ConvertTo-RepoRelativePath -RepositoryRoot $script:RepoRoot -Path $artifactsDir)

$summaryPath = Write-RunSummaryJson -ArtifactsDir $artifactsDir -Summary $summaryObj

if ($cmd -eq 'ci') {
    $sha = Get-CurrentCommitSha -Root $script:RepoRoot
    $ciFailures = [Collections.Generic.List[object]]::new()
    foreach ($relTrx in $state.TrxFiles) {
        $trxFull = Resolve-PathUnderRoot -Root $artifactsDir -Path $relTrx
        foreach ($failItem in @(Read-TrxFailures -TrxPath $trxFull)) {
            $ciFailures.Add($failItem)
        }
    }
    $md = Format-MarkdownReport -Summaries @(([PSCustomObject]$summaryObj)) -CommitSha $sha -Format 'summary' -TrxFailures $ciFailures.ToArray()
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
