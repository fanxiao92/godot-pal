# Public orchestration regression: explicit artificial repository, no SDK/engine/network.
[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$previousEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$utf8 = [Text.UTF8Encoding]::new($false)
$entry = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../scripts/Test-Engineering.ps1'))
$checker = Join-Path (Split-Path $entry -Parent) 'Test-PowerShellSyntax.ps1'
$hostPath = (Get-Process -Id $PID).Path
$evidence = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('engineering-test-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new engineering regression evidence directory.' }
$null = [IO.Directory]::CreateDirectory($evidence)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('engineering 人工 空格 ' + [guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixture)
$foreign = Join-Path $fixture 'foreign cwd'; $null = [IO.Directory]::CreateDirectory($foreign)
$records = [Collections.Generic.List[object]]::new()
$completed = $false
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Put([string]$Name, [string]$Text) {
    $file = Join-Path $fixture $Name
    $null = [IO.Directory]::CreateDirectory((Split-Path $file -Parent))
    $tokens = $null; $errors = $null
    if ([IO.Path]::GetExtension($file) -eq '.ps1') {
        $null = [Management.Automation.Language.Parser]::ParseInput($Text, [ref]$tokens, [ref]$errors)
        Assert (-not $errors.Count) "Generated fake tool must parse: $Name"
    }
    [IO.File]::WriteAllText($file, $Text, $utf8)
}
function Boundary([string]$Body) { Put 'scripts/Test-UnitBoundary.ps1' ("param([string]`$RepositoryRoot)`n" + $Body) }
$healthy = "Write-Output 'PASS unit-boundary: core (fixture)'; Write-Output 'PASS unit-boundary: cli-unit (fixture)'"
function Check([string]$Name, [string]$Mode, [int]$Expected, [string]$Diagnostic = '', [string]$Root = $fixture, [string]$Bundle = '', [switch]$DefaultEvidence, [string]$Existing = '') {
    $number = $records.Count + 1
    $output = if ($Existing) { $Existing } else { Join-Path $evidence "run-$number" }
    $arguments = @('-NoProfile', '-File', $entry, '-RepositoryRoot', $Root, '-Mode', $Mode)
    if ($Bundle) { $arguments += @('-GdUnitInputDirectory', $Bundle) }
    if (-not $DefaultEvidence) { $arguments += @('-EvidenceDirectory', $output) }
    $stdout = Join-Path $evidence "$number.stdout.json"; $stderr = Join-Path $evidence "$number.stderr.log"
    Push-Location $foreign
    try { & $hostPath @arguments 1> $stdout 2> $stderr; $actual = $LASTEXITCODE } finally { Pop-Location }
    [IO.File]::WriteAllText((Join-Path $evidence "$number.exit"), [string]$actual, $utf8)
    $raw = [IO.File]::ReadAllText($stdout, [Text.UTF8Encoding]::new($false, $true))
    $report = $raw | ConvertFrom-Json
    $records.Add(@{ name = $Name; arguments = $arguments; exitCode = $actual; expectedExit = $Expected })
    [IO.File]::WriteAllText((Join-Path $evidence 'commands.json'), (ConvertTo-Json -InputObject $records.ToArray() -Depth 8), $utf8)
    Assert ($actual -eq $Expected) "${Name}: expected exit $Expected, got $actual; $raw"
    if ($Diagnostic) { Assert ($report.failure -match $Diagnostic) "${Name}: wrong reason: $($report.failure)" }
    Assert (($actual -eq 0) -eq ($report.status -eq 'complete')) "${Name}: false completion"
    Write-Host "PASS $Name"
    return $report
}
try {
    $null = [IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts'))
    [IO.File]::Copy($checker, (Join-Path $fixture 'scripts/Test-PowerShellSyntax.ps1'))
    Boundary $healthy
    $fakeSteps = @{
        'tests/Test-PowerShellSyntax.Tests.ps1' = 'PASS syntax regression: 1 public invocations'
        'tests/Test-Engineering.Tests.ps1' = 'PASS engineering regression: 1 public invocations'
        'tests/UnitBoundary.Tests.ps1' = 'PASS UnitBoundary public-command regression: 1 cases; fixture cleaned.'
        'tests/Pal.Game.Tests/GameBatches.Tests.ps1' = 'PASS: 1 offline batch metadata groups;'
        'tests/Pal.Game.Tests/ProcessSnapshot.Tests.ps1' = 'PASS: ProcessSnapshot fake regression; real CIM invocations = 0.'
        'tests/Pal.Game.Tests/GdUnitPackages.Tests.ps1' = 'PASS: 1 offline GdUnit package regression cases;'
        'tests/Pal.Game.Tests/TestCostReport.Tests.ps1' = 'PASS: 1 offline cost report groups; report invocations=1;'
        'tests/Pal.Game.Tests/DotnetBoundary.Tests.ps1' = 'PASS: 1 real dotnet-boundary cases;'
        'tests/UnitEntry.Tests.ps1' = 'PASS regression: 1 public invocations;'
    }
    foreach ($p in $fakeSteps.Keys) { Put $p ("param([string]`$EvidenceDirectory,[string]`$InputDirectory)`nWrite-Output '$($fakeSteps[$p])'") }
    # Four literal synthetic files validate orchestration only, not actual package provenance.
    $bundle = Join-Path $fixture '人工 bundle'; $null = [IO.Directory]::CreateDirectory($bundle)
    $assets = @(foreach ($n in @('a', 'b', 'provenance.json', 'SHA256SUMS')) {
        $p = Join-Path $bundle $n; [IO.File]::WriteAllText($p, 'fixture bytes', $utf8)
        @{ file = $n; size = 13; sha256 = (Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant() }
    })
    Put 'scripts/gdunit-packages.lock.json' (ConvertTo-Json -InputObject @{ schemaVersion = 1; assets = $assets } -Depth 5)
    $inputBefore = @(Get-ChildItem $bundle -File | ForEach-Object { "$($_.Name)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash $_.FullName).Hash)" })
    $r = Check 'missing repository cannot succeed' 'Offline' 1 'missing-root' -Root (Join-Path $fixture 'absent')
    $sentinel = Join-Path $evidence 'existing'; $null = [IO.Directory]::CreateDirectory($sentinel)
    [IO.File]::WriteAllText((Join-Path $sentinel 'user.txt'), 'untouched', $utf8)
    $r = Check 'existing evidence is never overwritten' 'Source' 1 'output' -Existing $sentinel
    Assert ([IO.File]::ReadAllText((Join-Path $sentinel 'user.txt')) -eq 'untouched') 'Input overwritten.'
    $r = Check 'Source in foreign cwd is exactly AST and boundary' 'Source' 0
    Assert ((@($r.steps.name) -join ',') -ceq 'ast,unit-boundary') 'Source scope expanded.'
    $r = Check 'default successful temporary evidence is cleaned' 'Source' 0 -DefaultEvidence
    Assert ($r.temporaryEvidenceCleaned -and -not (Test-Path -LiteralPath $r.evidenceDirectory)) 'Default success leaked its directory.'
    $r = Check 'offline requires explicit package input' 'Offline' 1 'bundle'
    $badBundle = Join-Path $fixture 'corrupt bundle'; $null = [IO.Directory]::CreateDirectory($badBundle)
    foreach ($p in (Get-ChildItem -LiteralPath $bundle -File)) { [IO.File]::Copy($p.FullName, (Join-Path $badBundle $p.Name)) }
    [IO.File]::WriteAllText((Join-Path $badBundle 'a'), 'altered bytes', $utf8)
    $r = Check 'same-size corrupt bundle fails before child steps' 'Offline' 1 'Length/hash mismatch' -Bundle $badBundle
    Assert (@($r.steps).Count -eq 0) 'Invalid package input started child steps.'
    $r = Check 'offline runs the exact fixed fixture sequence' 'Offline' 0 -Bundle $bundle
    Assert ((@($r.steps.name) -join ',') -ceq 'ast,unit-boundary,syntax-regression,engineering-regression,unit-boundary-regression,game-batches-regression,process-snapshot-regression,gdunit-packages-regression,cost-report-regression') 'Offline wiring omitted or reordered a step.'
    $r = Check 'SDK mode is wired but fixtures do not execute SDK' 'OwnedProcess' 0
    Assert ((@($r.steps.name) -join ',') -ceq 'ast,unit-boundary,dotnet-boundary,unit-entry') 'OwnedProcess wiring missing.'
    Boundary "[Console]::Out.WriteLine('{`"状态`":`"中文失败`"}'); [Console]::Error.WriteLine('原始失败日志'); exit 7"
    $r = Check 'real child exit7 and Chinese raw streams are retained' 'Source' 7 'unit-boundary exited 7'
    $failedPath = $r.evidenceDirectory
    Assert ($r.steps[1].exitKnown -and $r.steps[1].exitCode -eq 7) 'Original numeric failure lost.'
    $bytes = [IO.File]::ReadAllBytes((Join-Path $failedPath 'unit-boundary/stdout.log'))
    Assert ([Convert]::ToHexString($bytes) -ceq [Convert]::ToHexString($utf8.GetBytes('{"状态":"中文失败"}' + [Environment]::NewLine))) 'Chinese stdout bytes changed.'
    Assert ([IO.File]::ReadAllText((Join-Path $failedPath 'unit-boundary/stderr.log'), [Text.UTF8Encoding]::new($false, $true)).Contains('原始失败日志')) 'Original failure stderr lost.'
    Boundary "Write-Output 'partial'; exit 0"
    $r = Check 'zero exit without result is incomplete' 'Source' 2 'missing-result'
    Assert ($r.steps[1].status -eq 'incomplete') 'Partial result became completed.'
    Boundary ($healthy + "; `$s=[Console]::OpenStandardError(); `$s.WriteByte(255); `$s.Flush(); exit 0")
    $r = Check 'invalid stderr bytes are unknown rather than complete' 'Source' 2 'encoding'
    $oldFailureTarget = [Environment]::GetEnvironmentVariable('PAL_ENGINEERING_FIXTURE_REPORT')
    try {
        $failureTarget = Join-Path (Join-Path $evidence "run-$($records.Count + 1)") 'engineering-run.json'
        [Environment]::SetEnvironmentVariable('PAL_ENGINEERING_FIXTURE_REPORT', $failureTarget)
        Boundary "Remove-Item -LiteralPath `$env:PAL_ENGINEERING_FIXTURE_REPORT; New-Item -ItemType Directory -Path `$env:PAL_ENGINEERING_FIXTURE_REPORT | Out-Null; [Console]::Error.WriteLine('原始失败 7'); exit 7"
        $r = Check 'recording failure cannot replace original child exit7' 'Source' 7 'unit-boundary exited 7'
        Assert ($r.steps[1].exitCode -eq 7 -and (Test-Path -LiteralPath (Join-Path $r.evidenceDirectory 'unit-boundary/stderr.log'))) 'Evidence failure erased child result.'
    } finally { [Environment]::SetEnvironmentVariable('PAL_ENGINEERING_FIXTURE_REPORT', $oldFailureTarget) }
    Boundary $healthy
    $r = Check 'failure followed by normal call has no residue' 'Source' 0
    Assert (Test-Path -LiteralPath (Join-Path $failedPath 'unit-boundary/stderr.log')) 'Later success erased prior failure.'
    $missingTool = Join-Path $fixture 'tests/Pal.Game.Tests/TestCostReport.Tests.ps1'
    Remove-Item -LiteralPath $missingTool
    $r = Check 'missing child tool cannot complete' 'Offline' 1 'missing-script' -Bundle $bundle
    $inputAfter = @(Get-ChildItem $bundle -File | ForEach-Object { "$($_.Name)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash $_.FullName).Hash)" })
    Assert (-not (Compare-Object $inputBefore $inputAfter)) 'Bundle input was modified.'
    $completed = $true
    Write-Host "PASS engineering regression: $($records.Count) public invocations; artificial tools, no SDK/engine/network."
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
    [Console]::OutputEncoding = $previousEncoding
    if ($completed -and -not $EvidenceDirectory) { Remove-Item -LiteralPath $evidence -Recurse -Force }
    elseif (-not $completed) { Write-Host "Engineering regression failed; raw evidence retained: $evidence" }
}
