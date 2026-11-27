# Cross-platform offline regression tests for ./pal.ps1, T0 affected selection, report, and script hygiene.
[CmdletBinding()]
param([string]$EvidenceDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$palEntry = Join-Path $repoRoot 'pal.ps1'
$hostPath = (Get-Process -Id $PID).Path
$utf8 = [Text.UTF8Encoding]::new($false)

Import-Module (Join-Path $repoRoot 'scripts/pal/AffectedSelection.psm1') -Force
Import-Module (Join-Path $repoRoot 'scripts/pal/TestReport.psm1') -Force

$workDir = if ($EvidenceDirectory) {
    [IO.Path]::GetFullPath($EvidenceDirectory)
} else {
    Join-Path ([IO.Path]::GetTempPath()) ('pal-front-door-test-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $workDir) { throw "EvidenceDirectory must be new: $workDir" }
[void][IO.Directory]::CreateDirectory($workDir)

$passedCount = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}
function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "ASSERTION FAILED: $Message (expected '$Expected', got '$Actual')" }
}
function Assert-SetEqual([string[]]$Expected, [string[]]$Actual, [string]$Message) {
    $e = (@($Expected | Sort-Object -Unique) -join ',')
    $a = (@($Actual | Sort-Object -Unique) -join ',')
    if ($e -ine $a) { throw "ASSERTION FAILED: $Message (expected [$e], got [$a])" }
}
function Pass([string]$Name) {
    $script:passedCount++
    Write-Host "PASS $Name"
}

function Invoke-Pal {
    param([string[]]$Arguments)
    $outFile = Join-Path $workDir ("cmd-$script:passedCount-" + [guid]::NewGuid().ToString('N') + '.out')
    $errFile = Join-Path $workDir ("cmd-$script:passedCount-" + [guid]::NewGuid().ToString('N') + '.err')
    & $hostPath -NoProfile -NonInteractive -File $palEntry @Arguments 1> $outFile 2> $errFile
    $code = $LASTEXITCODE
    return @{
        ExitCode = $code
        StdOut   = [IO.File]::ReadAllText($outFile, $utf8)
        StdErr   = [IO.File]::ReadAllText($errFile, $utf8)
    }
}

$completed = $false
try {
    # 1. Help & CLI routing
    $helpRes = Invoke-Pal @('--help')
    Assert-Equal 0 $helpRes.ExitCode 'pal --help should exit 0'
    Assert-True ($helpRes.StdOut -match 'test affected' -and $helpRes.StdOut -match 'ci\s+dotnet' -and $helpRes.StdOut -match 'godot install' -and $helpRes.StdOut -match 'assets prepare' -and $helpRes.StdOut -match 'report') 'help output must list all 5 top-level commands'
    Pass 'help lists all 5 top-level commands'

    # 2. Unknown command -> exit 1 + run-summary.json with status=error and errorMsg
    $badOut = Join-Path $workDir 'bad-cmd-out'
    $badRes = Invoke-Pal @('bogus', '--out', $badOut, '--json')
    Assert-Equal 1 $badRes.ExitCode 'unknown command must exit 1'
    Assert-True ($badRes.StdErr -match "Unknown command 'bogus'") 'stderr must contain clear errorMsg'
    $badSumPath = Join-Path $badOut 'run-summary.json'
    Assert-True (Test-Path -LiteralPath $badSumPath -PathType Leaf) 'run-summary.json must be written on error'
    $badSum = [IO.File]::ReadAllText($badSumPath, $utf8) | ConvertFrom-Json
    Assert-Equal 'error' $badSum.status 'bad command status must be error'
    Assert-True ([string]$badSum.errorMsg -match "Unknown command 'bogus'") 'run-summary.json must persist errorMsg'
    Assert-True ($badRes.StdOut -match 'PAL_SUMMARY_JSON=') '--json must emit PAL_SUMMARY_JSON'
    Pass 'unknown command returns exit 1 with errorMsg in stderr and run-summary.json'

    # 3. test e2e without --gpu -> exit 1 with clear --gpu requirement
    $e2eOut = Join-Path $workDir 'e2e-out'
    $e2eRes = Invoke-Pal @('test', 'e2e', '--out', $e2eOut)
    Assert-Equal 1 $e2eRes.ExitCode 'test e2e without --gpu must exit 1'
    $e2eSum = [IO.File]::ReadAllText((Join-Path $e2eOut 'run-summary.json'), $utf8) | ConvertFrom-Json
    Assert-Equal 'error' $e2eSum.status 'test e2e without --gpu status must be error'
    Assert-True ([string]$e2eSum.errorMsg -match '--gpu') 'test e2e without --gpu errorMsg must mention --gpu'
    Pass 'test e2e without --gpu fails cleanly with exit 1'

    # 4. Unit tests for T0 Resolve-AffectedSelection against real repository graph & map
    $selNone = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @()
    Assert-Equal 'no-op' $selNone.Mode 'empty change set -> no-op'
    Assert-SetEqual @() $selNone.Targets 'empty change set -> no targets'
    Pass 'affected selection: empty changes -> no-op'

    $selDocs = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('docs/README.md', 'README.md', 'AGENTS.md')
    Assert-Equal 'no-op' $selDocs.Mode 'pure docs -> no-op'
    Assert-SetEqual @() $selDocs.Targets 'pure docs -> no targets'
    Pass 'affected selection: pure docs -> no-op'

    $selCore = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('src/Pal.Core/Battle/BattleEngine.cs')
    Assert-Equal 'targeted' $selCore.Mode 'Pal.Core only -> targeted'
    Assert-SetEqual @('core-unit', 'extract-unit', 'extract-integration', 'prepare', 'game:Smoke') $selCore.Targets 'Pal.Core only -> Core/Extract/Prepare + game:Smoke'
    Pass 'affected selection: Pal.Core only -> Core/Extract/Prepare + game:Smoke'

    $selExtract = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('tools/Pal.Extract/Program.cs')
    Assert-Equal 'targeted' $selExtract.Mode 'Pal.Extract -> targeted'
    Assert-SetEqual @('extract-unit', 'extract-integration', 'prepare') $selExtract.Targets 'Pal.Extract -> extract-unit, extract-integration, prepare'
    Pass 'affected selection: Pal.Extract -> extract-unit, extract-integration, prepare'

    $selLra = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('tools/long-running-agent/policy.ts')
    Assert-Equal 'targeted' $selLra.Mode 'LRA -> targeted'
    Assert-SetEqual @('lra') $selLra.Targets 'LRA -> lra'
    Pass 'affected selection: LRA -> lra target'

    $selLinked = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('tests/Fixtures/BattleContextTestFixture.cs')
    Assert-Equal 'targeted' $selLinked.Mode 'BattleContextTestFixture -> targeted'
    Assert-SetEqual @('core-unit', 'prepare', 'game:*') $selLinked.Targets 'BattleContextTestFixture -> core-unit, prepare, game:*'
    Pass 'affected selection: linked BattleContextTestFixture -> core-unit, prepare, game:*'

    $selProfiles = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('src/Pal.Game/Scripts/Profiles/PalExplorationProfile.cs')
    Assert-Equal 'targeted' $selProfiles.Mode 'Game Profiles -> targeted'
    Assert-SetEqual @('game:*', 'prepare') $selProfiles.Targets 'Game Profiles (also under src/Pal.Game/**) -> game:*, prepare'
    Pass 'affected selection: Game Profiles -> game:*, prepare'

    $selMenuTest = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('tests/Pal.Game.Tests/Tests/MenuSceneTests.cs')
    Assert-Equal 'targeted' $selMenuTest.Mode 'single Game test file -> targeted'
    Assert-SetEqual @('game:Menu') $selMenuTest.Targets 'MenuSceneTests.cs -> game:Menu'
    $selMultiCat = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('tests/Pal.Game.Tests/Tests/ExplorationSceneTests.cs')
    Assert-Equal 'targeted' $selMultiCat.Mode 'multi-category Game test file -> targeted'
    Assert-SetEqual @('game:Exploration', 'game:Smoke') $selMultiCat.Targets 'comma-separated [TestCategory] must preserve all categories'
    Pass 'affected selection: single Game test file narrows to all its TestCategories'

    foreach ($fullPath in @('GodotPal.sln', 'src/Pal.Core/Pal.Core.csproj', 'tests/unit.runsettings', 'src/Pal.Game/project.godot', 'pal.ps1', 'tests/affected-map.psd1', '.github/workflows/ci.yml', '.gitignore')) {
        $selFull = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @($fullPath)
        Assert-Equal 'full' $selFull.Mode "infra path '$fullPath' must trigger full fallback"
        Assert-Equal $fullPath $selFull.FileDecisions[0].file "dotfile/infra path '$fullPath' must not lose leading dot"
    }
    Pass 'affected selection: build/infra/front-door paths trigger full fallback'

    $selUnknown = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('unmapped/random.txt')
    Assert-Equal 'full' $selUnknown.Mode 'unmatched path must safely fallback to full'
    Assert-SetEqual @('unmapped/random.txt') $selUnknown.UnmatchedFiles 'unmatched path must be reported'
    Pass 'affected selection: unknown path safely falls back to full'

    $selForceAll = Resolve-AffectedSelection -RepositoryRoot $repoRoot -ChangedFiles @('docs/README.md') -ForceAll
    Assert-Equal 'full' $selForceAll.Mode '--all must override pure docs to full'
    Pass 'affected selection: --all forces full'

    # 5. End-to-end git repo test for `./pal.ps1 test affected` (pure docs no-op + rename/delete)
    $fakeRepo = Join-Path $workDir 'fake-git-repo'
    [void][IO.Directory]::CreateDirectory($fakeRepo)
    [void][IO.Directory]::CreateDirectory((Join-Path $fakeRepo 'docs'))
    [void][IO.Directory]::CreateDirectory((Join-Path $fakeRepo 'src/Pal.Core'))
    [void][IO.Directory]::CreateDirectory((Join-Path $fakeRepo 'tests'))
    Copy-Item -LiteralPath (Join-Path $repoRoot 'GodotPal.sln') -Destination (Join-Path $fakeRepo 'GodotPal.sln')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tests/affected-map.psd1') -Destination (Join-Path $fakeRepo 'tests/affected-map.psd1')
    foreach ($projRel in (Get-SolutionProjects -RepositoryRoot $repoRoot)) {
        $destProj = Join-Path $fakeRepo $projRel
        [void][IO.Directory]::CreateDirectory((Split-Path $destProj -Parent))
        Copy-Item -LiteralPath (Join-Path $repoRoot $projRel) -Destination $destProj
    }
    [IO.File]::WriteAllText((Join-Path $fakeRepo 'docs/note.md'), "# note`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $fakeRepo 'src/Pal.Core/Old.cs'), "class Old {}`n", $utf8)

    Push-Location $fakeRepo
    try {
        & git init -b main | Out-Null
        & git config user.email 'test@example.com'
        & git config user.name 'Test'
        & git add -A | Out-Null
        & git commit -m 'base' | Out-Null

        # Pure docs change -> exit 0 no-op
        [IO.File]::WriteAllText((Join-Path $fakeRepo 'docs/note.md'), "# updated note`n", $utf8)
        $noopOut = Join-Path $workDir 'noop-out'
        $noopRes = Invoke-Pal @('test', 'affected', '--repo-root', $fakeRepo, '--base', 'main', '--out', $noopOut)
        Assert-Equal 0 $noopRes.ExitCode 'pure docs test affected must exit 0'
        Assert-True ($noopRes.StdOut -match 'NO-OP') 'pure docs test affected must print NO-OP'
        $noopSum = [IO.File]::ReadAllText((Join-Path $noopOut 'run-summary.json'), $utf8) | ConvertFrom-Json
        Assert-Equal 'passed' $noopSum.status 'pure docs run-summary status must be passed'
        Assert-Equal 0 ([int]$noopSum.counts.total) 'pure docs counts.total must be 0'
        Pass 'end-to-end pal.ps1 test affected: pure docs returns exit 0 no-op'

        # Rename Pal.Core file -> --explain shows Pal.Core targets
        & git mv src/Pal.Core/Old.cs src/Pal.Core/New.cs | Out-Null
        $expOut = Join-Path $workDir 'explain-out'
        $expRes = Invoke-Pal @('test', 'affected', '--explain', '--repo-root', $fakeRepo, '--base', 'main', '--out', $expOut)
        Assert-Equal 0 $expRes.ExitCode 'test affected --explain must exit 0'
        Assert-True ($expRes.StdOut -match 'game:Smoke' -and $expRes.StdOut -match 'core-unit') '--explain must list core-unit and game:Smoke'
        Pass 'end-to-end pal.ps1 test affected --explain handles renamed Pal.Core file'
    } finally {
        Pop-Location
    }

    # 6. TRX -> JUnit + Cobertura + report subcommand
    $repDir = Join-Path $workDir 'rep-source'
    [void][IO.Directory]::CreateDirectory($repDir)
    $trxSample = Join-Path $repDir 'sample.trx'
    [IO.File]::WriteAllText($trxSample, @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="PassCase" outcome="Passed" duration="00:00:00.1230000" />
    <UnitTestResult testName="FailCase" outcome="Failed" duration="00:00:00.0500000">
      <Output><ErrorInfo><Message>Expected 1 got 2</Message></ErrorInfo></Output>
    </UnitTestResult>
  </Results>
  <ResultSummary outcome="Failed">
    <Counters total="2" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" />
  </ResultSummary>
</TestRun>
'@, $utf8)
    $trxCounts = Read-TrxSummary -TrxPath $trxSample
    Assert-Equal 2 $trxCounts.total 'TRX total'
    Assert-Equal 1 $trxCounts.passed 'TRX passed'
    Assert-Equal 1 $trxCounts.failed 'TRX failed'
    $junitOut = Join-Path $repDir 'sample.junit.xml'
    Convert-TrxToJunit -TrxPath $trxSample -JunitPath $junitOut
    Assert-True (Test-Path -LiteralPath $junitOut -PathType Leaf) 'JUnit XML must be created'
    [xml]$junitXml = [IO.File]::ReadAllText($junitOut, $utf8)
    Assert-Equal '2' $junitXml.testsuites.testsuite.tests 'JUnit tests attribute'
    Assert-Equal '1' $junitXml.testsuites.testsuite.failures 'JUnit failures attribute'

    $covSample = Join-Path $repDir 'coverage.cobertura.xml'
    [IO.File]::WriteAllText($covSample, @'
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.842" branch-rate="0.765">
  <packages>
    <package name="Pal.Core" line-rate="0.842" branch-rate="0.765" />
  </packages>
</coverage>
'@, $utf8)
    $covEntries = @(Read-CoberturaCoverage -CoveragePath $covSample)
    Assert-Equal 1 $covEntries.Count 'coverage package count'
    Assert-Equal 'Pal.Core' $covEntries[0].package 'coverage package name'
    Assert-Equal 84.2 $covEntries[0].lineRate 'coverage lineRate'

    $repSum = [ordered]@{
        command = 'ci dotnet'
        status = 'failed'
        errorMsg = "Test step 'sample' failed with exit code 1."
        durationSec = 12.5
        counts = [ordered]@{ total = 2; passed = 1; failed = 1; skipped = 0 }
        steps = @(
            [ordered]@{ name = 'Pal.Game.Tests.ProfilesPrepare'; total = 775; passed = 775; failed = 0; skipped = 0; durationSec = 91.2 }
            [ordered]@{ name = 'Pal.Game.Tests.Runtime'; total = 321; passed = 320; failed = 1; skipped = 0; durationSec = 240.4 }
        )
        trxFiles = @('sample.trx')
        coverageFiles = @('coverage/Pal.Core.UnitTests/coverage.cobertura.xml')
        coverage = @($covEntries) + @([ordered]@{ package = 'Pal.Core'; lineRate = 15.1; branchRate = 15.4 })
        artifactsDir = '.artifacts/pal/sample'
    }
    [void](Write-RunSummaryJson -ArtifactsDir $repDir -Summary $repSum)
    $mdOut = Join-Path $workDir 'pr-comment.md'
    $repRes = Invoke-Pal @('report', '--from', $repDir, '--out', $mdOut)
    Assert-Equal 0 $repRes.ExitCode 'pal.ps1 report must exit 0'
    $mdText = [IO.File]::ReadAllText($mdOut, $utf8)
    Assert-True ($mdText -match 'ci dotnet' -and $mdText -match 'Pal\.Game\.Tests\.ProfilesPrepare' -and $mdText -match '775' -and $mdText -match 'Pal\.Game\.Tests\.Runtime' -and $mdText -match '84\.2%' -and $mdText -notmatch '15\.1%' -and $mdText -match 'FailCase' -and $mdText -match 'Expected 1 got 2' -and $mdText -match 'T3') 'report markdown must include per-step breakdown, TRX failure details, highest package coverage, command, and T3 placeholder'
    Pass 'TRX/JUnit/Cobertura parsing and pal.ps1 report generation'

    # 7. Script hygiene: Get-Godot.ps1 Linux support & Prepare-DialoguePackage.ps1 cleanup
    $getGodotText = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/Get-Godot.ps1'), $utf8)
    Assert-True ($getGodotText -match 'Godot_v4\.7\.2-stable_mono_linux_x86_64\.zip') 'Get-Godot.ps1 must support official Linux zip'
    Assert-True ($getGodotText -match '1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13') 'Get-Godot.ps1 must pin official Linux SHA512'
    Assert-True ($getGodotText -match 'chmod \+x') 'Get-Godot.ps1 must chmod +x on Linux'
    Pass 'Get-Godot.ps1 cross-platform Linux contract verified'

    $prepDlgText = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/Prepare-DialoguePackage.ps1'), $utf8)
    Assert-True ($prepDlgText -notmatch 'xiaofan') 'Prepare-DialoguePackage.ps1 must not contain personal paths'
    Assert-True ($prepDlgText -notmatch 'Get-TestProcesses\.ps1') 'Prepare-DialoguePackage.ps1 must not depend on Get-TestProcesses.ps1'
    Assert-True ($prepDlgText -match 'Resolve-ReadyGodotBinary') 'Prepare-DialoguePackage.ps1 must call shared PID/Name-only Godot preflight guard'
    Pass 'Prepare-DialoguePackage.ps1 personal path and legacy process-script coupling removed'

    # 8. report error path writes run-summary.json with errorMsg
    $repErrRes = Invoke-Pal @('report', '--repo-root', $workDir, '--json')
    Assert-Equal 1 $repErrRes.ExitCode 'report without --from must exit 1'
    Assert-True ($repErrRes.StdOut -match 'PAL_SUMMARY_JSON=(.+)') 'report error with --json must emit summary path'
    $repErrSumPath = $Matches[1].Trim()
    Assert-True (Test-Path -LiteralPath $repErrSumPath -PathType Leaf) 'report error must write run-summary.json'
    $repErrSum = [IO.File]::ReadAllText($repErrSumPath, $utf8) | ConvertFrom-Json
    Assert-Equal 'error' $repErrSum.status 'report error summary status must be error'
    Assert-True ([string]$repErrSum.errorMsg -match '--from') 'report error summary must contain errorMsg'
    Pass 'report error persists errorMsg to run-summary.json'

    # 9. assets status, missing PAL_RAW_DATA_DIR, battle-context flag validation, and T3 report rendering
    $prevRaw = $env:PAL_RAW_DATA_DIR
    try {
        Remove-Item Env:PAL_RAW_DATA_DIR -ErrorAction SilentlyContinue
        $assetsOut = Join-Path $workDir 'assets-status-out'
        $assetsRes = Invoke-Pal @('assets', 'status', '--out', $assetsOut)
        Assert-Equal 0 $assetsRes.ExitCode 'assets status must exit 0'
        $missingRawOut = Join-Path $workDir 'assets-missing-raw-out'
        $missingRawRes = Invoke-Pal @('assets', 'prepare', '--out', $missingRawOut)
        Assert-Equal 1 $missingRawRes.ExitCode 'assets prepare without PAL_RAW_DATA_DIR must exit 1'
        Assert-True ($missingRawRes.StdErr -match 'PAL_RAW_DATA_DIR') 'missing PAL_RAW_DATA_DIR error must name PAL_RAW_DATA_DIR'
    } finally {
        if ($null -ne $prevRaw) { $env:PAL_RAW_DATA_DIR = $prevRaw }
    }
    $bcOut = Join-Path $workDir 'assets-bc-out'
    $bcRes = Invoke-Pal @('assets', 'prepare', '--kind', 'battle-context', '--input', 'a', '--output', 'b', '--out', $bcOut)
    Assert-Equal 1 $bcRes.ExitCode 'battle-context without --static-resources/--static-profile must exit 1'
    Assert-True ($bcRes.StdErr -match '--static-resources' -and $bcRes.StdErr -match '--static-profile') 'battle-context error must name required static flags'

    $t3Dir = Join-Path $workDir 't3-rep-source'
    [void][IO.Directory]::CreateDirectory($t3Dir)
    $t3Sum = [ordered]@{
        command = 'test e2e'
        status = 'passed'
        errorMsg = $null
        durationSec = 42.1
        counts = [ordered]@{ total = 5; passed = 5; failed = 0; skipped = 0 }
        trxFiles = @()
        coverageFiles = @()
        coverage = @()
        artifactsDir = '.artifacts/pal/sample-t3'
        e2e = [ordered]@{
            cacheKey = 'raw:sha256-0123456789abcdef + prepare:v1'
            updateGolden = $false
            contactSheet = 'contact-sheet.png'
            scenarios = @(
                [ordered]@{
                    scenario = 'boot-and-explore'
                    status = 'passed'
                    probePassed = $true
                    durationSec = 1.2
                    frames = @(
                        [ordered]@{
                            frameName = '01-boot-scene4'
                            status = 'passed'
                            actualSha256 = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
                            goldenSha256 = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
                            diffPercent = 0.0
                            tolerancePercent = 1.0
                            diffPath = $null
                        }
                    )
                }
            )
        }
    }
    [void](Write-RunSummaryJson -ArtifactsDir $t3Dir -Summary $t3Sum)
    $t3MdOut = Join-Path $workDir 't3-pr-comment.md'
    $t3RepRes = Invoke-Pal @('report', '--from', $t3Dir, '--out', $t3MdOut)
    Assert-Equal 0 $t3RepRes.ExitCode 'T3 report must exit 0'
    $t3Md = [IO.File]::ReadAllText($t3MdOut, $utf8)
    Assert-True ($t3Md -match 'raw:sha256-0123456789abcdef \+ prepare:v1' -and $t3Md -match 'boot-and-explore/01-boot-scene4' -and $t3Md -match 'contact-sheet\.png' -and $t3Md -match '人工 5 分钟审阅清单') 'T3 report must render cacheKey, frame SHA256/diff table, contact sheet, and manual review checklist'
    Pass 'assets T3 validation and T3 PR evidence report rendering succeed'

    # Case 22: check-ledger detects drift (exit 1 with regenerate command) and passes when synced
    $ledgerRes = Invoke-Pal @('test', 'check-ledger')
    Assert-Equal 0 $ledgerRes.ExitCode "check-ledger on synced repo must exit 0; stderr=$($ledgerRes.Stderr)"
    $driftDir = Join-Path $workDir 'ledger-drift-test'
    [void][IO.Directory]::CreateDirectory((Join-Path $driftDir 'docs/generated'))
    # Copy real ledger and tamper one line, then point check-ledger via a lightweight junction/link to tools/src?
    # Even cleaner: call Invoke-CapabilityLedgerCheck directly on a synthetic repo copy or tamper check via temp file.
    # Let's test Pal.Extract export-capability-ledger -> tamper -> verify check fails when ledger differs.
    Import-Module (Join-Path $repoRoot 'scripts/pal/CapabilityLedger.psm1') -Force
    $realLedger = Join-Path $repoRoot 'docs/generated/capabilities-ledger.md'
    $origBytes = [IO.File]::ReadAllBytes($realLedger)
    try {
        [IO.File]::WriteAllText($realLedger, ([Text.Encoding]::UTF8.GetString($origBytes) + "`n<!-- tampered -->`n"), $utf8)
        $driftRes = Invoke-Pal @('test', 'check-ledger')
        Assert-Equal 1 $driftRes.ExitCode 'tampered capabilities-ledger.md must cause check-ledger to exit 1'
        Assert-True ($driftRes.Stderr -match 'check-ledger --update') "drift stderr must include regeneration command; got: $($driftRes.Stderr)"
    } finally {
        [IO.File]::WriteAllBytes($realLedger, $origBytes)
    }
    Pass 'test check-ledger passes when synced and fails with exit 1 on drift'

    # Case 23: assets inspect fails cleanly when PAL_RAW_DATA_DIR is unset, and outputs redacted JSON on synthetic MKF fixture
    $prevRaw = $env:PAL_RAW_DATA_DIR
    try {
        Remove-Item Env:PAL_RAW_DATA_DIR -ErrorAction SilentlyContinue
        $unsetRes = Invoke-Pal @('assets', 'inspect')
        Assert-Equal 1 $unsetRes.ExitCode 'assets inspect without PAL_RAW_DATA_DIR must exit 1'
        Assert-True ($unsetRes.Stderr -match 'PAL_RAW_DATA_DIR') "unset stderr must mention PAL_RAW_DATA_DIR; got: $($unsetRes.Stderr)"

        $synthRaw = Join-Path $workDir 'secret-private-raw-dir'
        [void][IO.Directory]::CreateDirectory($synthRaw)
        # Minimal valid 1-chunk MKF (offset table: [8, 12] + 4-byte payload)
        [byte[]]$mkfBytes = @(
            0x08, 0x00, 0x00, 0x00,
            0x0C, 0x00, 0x00, 0x00,
            0x01, 0x02, 0x03, 0x04
        )
        foreach ($mkfName in @('data.mkf', 'gop.mkf', 'map.mkf', 'mgo.mkf', 'pat.mkf', 'sss.mkf')) {
            [IO.File]::WriteAllBytes((Join-Path $synthRaw $mkfName), $mkfBytes)
        }
        $env:PAL_RAW_DATA_DIR = $synthRaw
        $insRes = Invoke-Pal @('assets', 'inspect', '--json')
        Assert-Equal 0 $insRes.ExitCode "assets inspect --json on synthetic MKF must exit 0; stderr=$($insRes.Stderr)"
        Assert-True ($insRes.Stdout -notmatch 'secret-private-raw-dir') "assets inspect output MUST NOT leak private directory path; got: $($insRes.Stdout)"
        Assert-True ($insRes.Stdout -match '<redacted:PAL_RAW_DATA_DIR>') 'assets inspect output must mark redacted source'
        Assert-True ($insRes.Stdout -match 'data\.mkf') 'assets inspect output must list MKF filename'
    } finally {
        if ($null -eq $prevRaw) { Remove-Item Env:PAL_RAW_DATA_DIR -ErrorAction SilentlyContinue }
        else { $env:PAL_RAW_DATA_DIR = $prevRaw }
    }
    Pass 'assets inspect handles unset env and redacts private paths on synthetic MKF'

    # Case 24: scripts/Sync-AgentSkills.ps1 is idempotent in a temp repo
    $syncRepo = Join-Path $workDir 'sync-skills-repo'
    [void][IO.Directory]::CreateDirectory((Join-Path $syncRepo '.agents/skills/verify'))
    [IO.File]::WriteAllText((Join-Path $syncRepo '.agents/skills/verify/SKILL.md'), "# verify`n", $utf8)
    $syncScript = Join-Path $repoRoot 'scripts/Sync-AgentSkills.ps1'
    & pwsh -NoProfile -NonInteractive -File $syncScript -RepositoryRoot $syncRepo | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'first Sync-AgentSkills.ps1 run must exit 0'
    Assert-True (Test-Path -LiteralPath (Join-Path $syncRepo '.claude/skills/verify/SKILL.md') -PathType Leaf) 'bridged .claude/skills/verify/SKILL.md must be readable'
    & pwsh -NoProfile -NonInteractive -File $syncScript -RepositoryRoot $syncRepo | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'second Sync-AgentSkills.ps1 run must be idempotent and exit 0'
    Pass 'scripts/Sync-AgentSkills.ps1 bridges .claude/skills idempotently'

    $completed = $true
    Write-Host "PASS PalFrontDoor regression: $passedCount cases."
} finally {
    if ($completed -and -not $EvidenceDirectory -and (Test-Path -LiteralPath $workDir)) {
        Remove-Item -LiteralPath $workDir -Recurse -Force
    } elseif (-not $completed) {
        Write-Host "PalFrontDoor regression failed; evidence retained at $workDir"
    }
}
