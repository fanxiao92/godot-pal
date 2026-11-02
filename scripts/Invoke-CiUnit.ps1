# CI-only direct-project executor. Not the agent default or a full-acceptance gate.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$CandidateIdentityPath,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'GameTestBatches.ps1') # Definitions only.
. (Join-Path $PSScriptRoot 'TestRunDefinitions.ps1')
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$out = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $out) { throw '[ci-unit] EvidenceDirectory must be new.' }
$null = New-Item -ItemType Directory $out
$exitCode = 1
$whole = $null
$report = [ordered]@{
    schemaVersion=1; kind='unit-only'; status='incomplete'; candidate=$null; source=@(); commands=@(); projects=@(
        [ordered]@{name='core';project='tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj';status='not-run'},
        [ordered]@{name='cli-unit';project='tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj';status='not-run'}
    ); startedUtc=$null; finishedUtc=$null; wallClockMs=$null; sdk=$null; failure=$null
    cache='Ordinary NuGet cache and implicit restore/build; not empty-cache, offline or performance proof.'
    restoreMs=$null; buildMs=$null; warningProjectMs=300000; warningRoundMs=600000; commandTimeoutMs=600000; cleanupTimeoutMs=5000
}
function Save { [IO.File]::WriteAllText((Join-Path $out 'unit-run.json'), ($report | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false)) }
function Git([string[]]$Arguments) {
    # PowerShell names are case-insensitive: invoking bare git here recurses into Git.
    $nativeGit = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $value = @(& $nativeGit -C $root @Arguments)
    if ($LASTEXITCODE -ne 0) { throw '[ci-unit] git identity command failed.' }
    return ($value -join "`n").Trim()
}
function Invoke-Recorded([string]$Name, [string]$Executable, [string[]]$Arguments) {
    $record = [ordered]@{name=$Name;executable=$Executable;arguments=$Arguments;cwd=$root;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');finishedUtc=$null;wallClockMs=$null;exitKnown=$false;exitCode=$null;rootExited=$false;stdoutComplete=$false;stderrComplete=$false;status='incomplete';stdout="$Name.stdout.log";stderr="$Name.stderr.log"}
    $report.commands += $record; Save
    $psi = [Diagnostics.ProcessStartInfo]::new($Executable)
    $psi.WorkingDirectory=$root; $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    $psi.Environment['DOTNET_CLI_UI_LANGUAGE']='en-US'; $psi.Environment['DOTNET_NOLOGO']='1'
    foreach ($arg in $Arguments) { $psi.ArgumentList.Add($arg) }
    $process=[Diagnostics.Process]::new(); $process.StartInfo=$psi
    $stdout=[IO.File]::Open((Join-Path $out $record.stdout),[IO.FileMode]::CreateNew)
    $stderr=[IO.File]::Open((Join-Path $out $record.stderr),[IO.FileMode]::CreateNew)
    $watch=[Diagnostics.Stopwatch]::StartNew(); $started=$false; $copyOut=$null; $copyErr=$null
    try {
        $started=$process.Start()
        if (-not $started) { throw '[ci-unit] Process launch unknown.' }
        $copyOut=$process.StandardOutput.BaseStream.CopyToAsync($stdout); $copyErr=$process.StandardError.BaseStream.CopyToAsync($stderr)
        $all=[Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(),$copyOut,$copyErr))
        if (-not $all.Wait(600000)) { throw '[ci-unit] Command exit/EOF timeout; stop without retry.' }
        $record.rootExited=$true; $record.exitKnown=$true; $record.exitCode=$process.ExitCode
        $record.stdoutComplete=$copyOut.IsCompletedSuccessfully; $record.stderrComplete=$copyErr.IsCompletedSuccessfully
        $record.status='completed'
    } finally {
        if ($started -and $process.HasExited) { $record.rootExited=$true; $record.exitKnown=$true; $record.exitCode=$process.ExitCode; if ($record.exitCode -ne 0) { $script:exitCode=$record.exitCode } }
        if ($null -ne $copyOut) { $record.stdoutComplete=$copyOut.IsCompletedSuccessfully; $record.stderrComplete=$copyErr.IsCompletedSuccessfully }
        if ($started -and -not $process.HasExited) { try { $process.Kill($true); $record.cleanupComplete=$process.WaitForExit(5000) } catch { $record.cleanupError=$_.Exception.Message } }
        $stdout.Dispose(); $stderr.Dispose(); $process.Dispose()
        $watch.Stop(); $record.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O'); $record.wallClockMs=$watch.Elapsed.TotalMilliseconds
        Save
    }
    if ($record.exitCode -ne 0) { throw "[ci-unit] $Name original exit $($record.exitCode); stop, remaining projects not run." }
    $log=[IO.File]::ReadAllText((Join-Path $out $record.stdout),$utf8)+"`n"+[IO.File]::ReadAllText((Join-Path $out $record.stderr),$utf8)
    if ($log -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)|Aborting test run|Test Run Aborted|test run timeout|test host process crashed|WindowsStdOutHook') { throw '[ci-unit] Failure diagnostic despite exit zero.' }
    return $log.Trim()
}
try {
    $candidate=[IO.File]::ReadAllText([IO.Path]::GetFullPath($CandidateIdentityPath),$utf8) | ConvertFrom-Json -NoEnumerate -DateKind String
    if ($candidate -isnot [pscustomobject] -or $candidate -is [array] -or $candidate.mode -cnotin @('pr-unit','push-unit','manual-unit')) { throw '[ci-unit] Expected an explicit Unit candidate object.' }
    if ((Git @('rev-parse','HEAD')) -cne $candidate.actualCheckout -or $candidate.actualCheckout -cne $candidate.candidateSha -or (Git @('status','--porcelain','--untracked-files=all'))) { throw '[ci-unit] Checkout differs or source tree is dirty.' }
    $report.candidate=$candidate
    $files=(Git @('-c','core.quotepath=false','ls-files')) -split "`n"
    $report.source=@(foreach ($file in $files) { @{path=$file;sha256=(Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash} })
    $report.startedUtc=[DateTimeOffset]::UtcNow.ToString('O'); $whole=[Diagnostics.Stopwatch]::StartNew(); Save
    $dotnet=(Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $report.sdk=Invoke-Recorded 'sdk' $dotnet @('--version')
    $runIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executionIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $report.projects) {
        $project.status='incomplete'; Save
        $results=Join-Path $out $project.name
        # New evidence root and per-project logger path: never reuse artifacts or skip build/restore.
        $null=Invoke-Recorded $project.name $dotnet @('test',$project.project,'--configuration','Debug','--logger','trx;LogFileName=results.trx','--results-directory',$results)
        $assembly=Join-Path $root ((Split-Path $project.project -Parent)+'/bin/Debug/net10.0/'+[IO.Path]::GetFileNameWithoutExtension($project.project)+'.dll')
        $trx=Join-Path $results 'results.trx'
        $identity=Assert-DirectUnitTrxRun -Path $trx -Assembly $assembly
        if (-not $runIds.Add($identity.runId)) { throw '[ci-unit] Reused TRX run ID.' }
        foreach ($id in $identity.executionIds) { if (-not $executionIds.Add($id)) { throw '[ci-unit] Reused execution ID.' } }
        $command=$report.commands[-1]
        $times=$identity.document.SelectSingleNode('/t:TestRun/t:Times',$identity.ns)
        if ([DateTimeOffset]::Parse($times.start) -lt [DateTimeOffset]::Parse($command.startedUtc).AddSeconds(-2) -or [DateTimeOffset]::Parse($times.finish) -gt [DateTimeOffset]::Parse($command.finishedUtc).AddSeconds(2)) { throw '[ci-unit] TRX is outside current command time window.' }
        $project.status='completed'; $project.executed=$identity.names.Count; $project.runId=$identity.runId; $project.trx="$($project.name)/results.trx"; $project.wallClockMs=$command.wallClockMs
        if ($command.wallClockMs -gt 300000) { Write-Warning "Unit project $($project.name) exceeded 5 minutes; warning only." }
        Save
    }
    if ((Git @('rev-parse','HEAD')) -cne $candidate.actualCheckout -or (Git @('status','--porcelain','--untracked-files=all'))) { throw '[ci-unit] Checkout/source changed during run.' }
    foreach ($file in $report.source) { if ((Get-FileHash -LiteralPath (Join-Path $root $file.path) -Algorithm SHA256).Hash -cne $file.sha256) { throw '[ci-unit] Source bytes changed during run.' } }
    $report.status='completed'; $exitCode=0
} catch { $report.failure=$_.Exception.Message; [Console]::Error.WriteLine($report.failure) }
finally {
    if ($null -ne $whole) { $whole.Stop(); $report.wallClockMs=$whole.Elapsed.TotalMilliseconds }
    $report.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O')
    if ($report.wallClockMs -gt 600000) { Write-Warning 'Unit round exceeded 10 minutes; warning only.' }
    try {
        # Bind original files before subsequent candidate API calls. Never include caches/resources.
        $report.artifacts=@(foreach ($file in Get-ChildItem -LiteralPath $out -Recurse -File | Where-Object Name -CNE 'unit-run.json') { @{path=[IO.Path]::GetRelativePath($out,$file.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;length=$file.Length} })
        Save
    } catch { [Console]::Error.WriteLine("[ci-unit] Evidence save failed: $($_.Exception.Message)"); if ($exitCode -eq 0) { $exitCode=1 } }
}
if ($exitCode -eq 0) { Write-Output 'PASS CI Unit-only: both approved projects, not full acceptance.' }
exit $exitCode
