# Actual workflow blocks: Offline uses synthetic tools; default OwnedProcess also runs real dotnet/xUnit.
[CmdletBinding()]
param(
    [string]$EvidenceDirectory,
    [ValidateSet('Offline', 'OwnedProcess')][string]$Mode = 'OwnedProcess'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$root = Split-Path $PSScriptRoot -Parent
$utf8 = [Text.UTF8Encoding]::new($false)
$text = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/ci.yml')).Replace("`r`n", "`n")
$base = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('native-unit-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $base) { throw 'EvidenceDirectory must be new.' }
$null = New-Item -ItemType Directory $base
function Assert([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Put([string]$Path, [string]$Value) { [IO.File]::WriteAllText($Path, $Value, $utf8) }
function Block([string]$Name) {
    # This repository's fixed indentation only; not an Actions/YAML scheduler.
    $matches = [regex]::Matches($text, '(?ms)^      - name: ' + [regex]::Escape($Name) + '\n(?:(?!^      - name: ).)*?^        run: \|\n(?<code>(?:          [^\n]*\n|\n)+)')
    Assert ($matches.Count -eq 1) "Expected one actual workflow block: $Name"
    $code = ($matches[0].Groups['code'].Value -split "`n" | ForEach-Object { if ($_.StartsWith('          ')) { $_.Substring(10) } else { $_ } }) -join "`n"
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    Assert ($errors.Count -eq 0 -and -not $code.Contains('${{')) "Actual block does not parse: $Name"
    return $code
}
$settings = Join-Path $root 'tests/unit.runsettings'
Assert (Test-Path -LiteralPath $settings -PathType Leaf) 'Native Unit settings missing.'
[xml]$settingsXml = [IO.File]::ReadAllText($settings)
Assert ($settingsXml.RunSettings.RunConfiguration.TreatNoTestsAsError -ceq 'true' -and $settingsXml.RunSettings.xUnit.FailSkips -ceq 'true') 'Native zero/skip failure policy missing.'
Assert ($text -match 'types: \[opened, synchronize, reopened, ready_for_review, converted_to_draft, edited\]' -and $text -match 'branches: \[main\]' -and $text -match '(?m)^  workflow_dispatch:') 'Event coverage changed.'
$unit = [regex]::Match($text, '(?ms)^  unit-check:\n(?<body>.*?)(?=^  full-check:)').Groups['body'].Value
$full = [regex]::Match($text, '(?ms)^  full-check:\n(?<body>.*\z)').Groups['body'].Value
Assert ($unit -and $full) 'Both the native Unit job and the #76 full-check job must stay present.'
Assert ($text -notmatch 'paths:|paths-ignore:|continue-on-error|CI_TOKEN|unit-run\.json') 'Workflow-level narrowing or audit artifacts are not allowed.'
Assert ($unit -notmatch '--filter|--no-build|--no-restore|Invoke-CiUnit|New-CiCandidateInputs|Test-CiCandidate|Test-Engineering|Test-M0|Test-Unit\.ps1|Get-Godot|Get-GdUnit') 'Native Unit job acquired narrowing, audit orchestration or heavy dependencies.'
Assert ($unit -notmatch '(?m)^    (if|needs):' -and $unit -match 'timeout-minutes: 20') 'Unit must run independently within the existing job budget.'
foreach ($fragment in @('persist-credentials: false','fetch-depth: 2','lfs: false','global-json-file: global.json','if: always()','if-no-files-found: error','name: ci-unit-evidence-${{ github.run_id }}-${{ github.run_attempt }}')) { Assert ($unit.Contains($fragment)) "Missing native CI policy: $fragment" }
Assert ($unit.Contains('Test-UnitBoundary.ps1')) 'Unit job must run the source-boundary guard (M5 retro: #113 shipped a unit File-I/O violation CI could not see).'
Assert ($unit.Contains('git grep --cached -l -e') -and $unit.Contains('https://git-lfs')) 'Unit job must guard committable trees against LFS pointers (M5 retro: #76 CI diagnosis).'
# #76 informational committable-scope full gate: the same Test-M0 entry as local acceptance.
Assert ($full -cmatch [regex]::Escape("if: github.event_name == 'push' || github.event_name == 'workflow_dispatch' || (github.event_name == 'pull_request' && github.event.action == 'ready_for_review')")) 'Full gate must run only on main push, PR readiness or manual dispatch.'
Assert (-not [regex]::IsMatch($full, "github\.event\.action == 'synchronize'")) 'Full gate must never be scheduled per synchronize.'
Assert ($full -cmatch '(?m)^    runs-on: windows-2022$' -and $full -cmatch '(?m)^    timeout-minutes: 35$') 'Full gate runner or its 35-minute envelope changed.'
Assert ($full -cmatch '(?m)^    if:' -and $full -notmatch '(?m)^    needs:') 'Full gate must stay scheduled independently, not behind unit-check.'
foreach ($fragment in @('persist-credentials: false','fetch-depth: 2','lfs: false','global-json-file: global.json','if: always()','if-no-files-found: error','name: ci-full-evidence-${{ github.run_id }}-${{ github.run_attempt }}','m0-evidence/**/*.trx','m0-evidence/PASS.txt')) { Assert ($full.Contains($fragment)) "Missing full gate CI policy: $fragment" }
$agentEvidence = Block 'Guard agent evidence tooling (offline)'
Assert ($agentEvidence.Contains('scripts/Test-PowerShellSyntax.ps1') -and $agentEvidence.Contains('tests/AgentEvidence.Tests.ps1')) 'Agent evidence guard must run syntax and public offline regression.'
Assert ($agentEvidence -notmatch '\bdotnet\b|Godot|GdUnit|GPU|CIM|Download|Invoke-WebRequest|https?://|Test-Unit\.ps1|Test-M0|Test-Engineering') 'Agent evidence guard acquired SDK/engine/GPU/download execution.'
$guardTools = @(
    @{script='scripts/Test-PowerShellSyntax.ps1'; evidence=$null},
    @{script='tests/AgentEvidence.Tests.ps1'; evidence='agent-evidence-regression'},
    @{script='tests/AgentWorkflow.Tests.ps1'; evidence='agent-workflow-regression'},
    @{script='tests/Test-PowerShellSyntax.Tests.ps1'; evidence='syntax-regression'},
    @{script='tests/Pal.Game.Tests/GameBatches.Tests.ps1'; evidence='game-batches-regression'},
    @{script='tests/Test-CiWorkflow.Tests.ps1'; evidence='ci-workflow-regression'}
)
$upload = [regex]::Match($unit, '(?ms)^      - name: Upload Unit evidence\n.*\z').Value
Assert ($upload -match 'if: always\(\)' -and $upload -match 'if-no-files-found: error' -and $upload -match 'actions/upload-artifact@') 'Offline originals need unconditional artifact upload.'
$tokens = $null; $errors = $null
$guardAst = [Management.Automation.Language.Parser]::ParseInput($agentEvidence, [ref]$tokens, [ref]$errors)
$toolCalls = @($guardAst.FindAll({ param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.InvocationOperator -eq 'Ampersand' }, $true))
Assert ($toolCalls.Count -eq $guardTools.Count) 'Offline guard tool count changed.'
for ($i = 0; $i -lt $guardTools.Count; $i++) {
    $tool = $guardTools[$i]; $call = $toolCalls[$i].Extent.Text
    Assert ($toolCalls[$i].GetCommandName() -eq 'pwsh' -and $call.StartsWith('& pwsh -NoProfile -File ')) 'Each regression needs an isolated native script exit, not its last expected-failure child exit.'
    Assert ($call.Contains("'" + $tool.script + "'")) "Offline tool missing/reordered: $($tool.script)"
    if ($tool.evidence) {
        Assert ($call.Contains("-EvidenceDirectory (Join-Path `$env:RUNNER_TEMP '$($tool.evidence)')")) 'Regression originals must use their own fresh directory.'
        Assert ($upload.Contains('${{ runner.temp }}/' + $tool.evidence + '/**')) "Missing uploaded originals: $($tool.evidence)"
    }
}
foreach ($command in $guardAst.FindAll({ param($n) $n -is [Management.Automation.Language.CommandAst] }, $true)) {
    Assert ($command.GetCommandName() -eq 'Join-Path' -or $command -in $toolCalls) 'Unexpected command in offline guard.'
}
Assert ($toolCalls[-1].Extent.Text.Contains('-Mode Offline')) 'CI regression must not select its SDK mode in CI guard.'
Assert ([regex]::Matches($agentEvidence, [regex]::Escape('if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }')).Count -eq $guardTools.Count) 'Every offline tool must propagate numeric failure.'
$checkout = Block 'Verify Unit checkout'
$core = Block 'Run Core Unit'
$cli = Block 'Run CLI Unit'
$fullCheckout = Block 'Verify full gate checkout'
$warmup = Block 'Install pinned Godot and warm the project import cache'
Assert ($warmup.Contains('Get-Godot.ps1')) 'Pinned engine download must stay in the CI warm-up step.'
Assert ($warmup.Contains('--editor') -and $warmup.Contains('--import')) 'Import-cache warm-up command missing.'
Assert ($warmup -notmatch 'Test-M0') 'Warm-up must not run the gate itself.'
$gate = Block 'Committable-scope full gate (Test-M0)'
Assert ($gate.Contains('-Scope committable')) 'Full gate lost its committable scope selection.'
Assert ($gate.Contains('Test-M0.ps1') -and $gate.Contains('GODOT_ENGINE')) 'Full gate must use the shared Test-M0 entry with the warm-up step pinned engine.'
Assert ($gate.Contains('PASS.txt')) 'Full gate must surface its PASS summary in the job output.'
foreach ($entry in @(@{code=$core;project='Pal.Core.UnitTests';name='core'},@{code=$cli;project='Pal.Extract.UnitTests';name='cli-unit'})) {
    Assert ($entry.code.Contains("dotnet test tests/$($entry.project)/$($entry.project).csproj --configuration Debug --settings tests/unit.runsettings --logger 'trx;LogFileName=results.trx'")) 'Expected a direct full-project native command.'
    Assert ($entry.code.Contains('if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }') -and $entry.code.Contains('Test-Path')) 'Original exit or missing-report guard missing.'
    Assert ($unit.Contains("/unit-results/$($entry.name)/results.trx")) 'Expected TRX is not uploaded.'
}
Assert ($unit.IndexOf('name: Run Core Unit') -lt $unit.IndexOf('name: Run CLI Unit')) 'Project order changed.'
Assert ([regex]::Matches($unit, 'timeout-minutes: 10').Count -eq 2) 'Each direct project must keep a ten-minute step budget.'
# Execute the actual offline shell block against synthetic public tools, never the SDK/engine.
$offlineFixture = Join-Path $base 'offline-repository'
$null = [IO.Directory]::CreateDirectory($offlineFixture)
$pwsh = (Get-Process -Id $PID).Path
$offlineRecords = [Collections.Generic.List[object]]::new()
$nestedNative = Join-Path $base 'expected-native-failure.ps1'; Put $nestedNative 'exit 9'
function Run-OfflineGuard([string]$Name, [int]$FailAt = -1) {
    $dir = Join-Path $base $Name; $null = [IO.Directory]::CreateDirectory($dir)
    for ($i = 0; $i -lt $guardTools.Count; $i++) {
        $file = Join-Path $offlineFixture $guardTools[$i].script
        $null = [IO.Directory]::CreateDirectory((Split-Path $file -Parent))
        $nativeExit = if ($i -eq $FailAt) { 7 } else { 0 }
        $fake = @"
param([string]`$EvidenceDirectory,[string]`$Mode)
[IO.File]::AppendAllText(`$env:GUARD_CALLS, '$i' + [Environment]::NewLine)
if (`$EvidenceDirectory) {
    `$null = [IO.Directory]::CreateDirectory(`$EvidenceDirectory)
    [IO.File]::WriteAllText((Join-Path `$EvidenceDirectory 'original.txt'), 'fixture $i exit $nativeExit')
}
& (Get-Process -Id `$PID).Path -NoProfile -File '$($nestedNative.Replace("'", "''"))'
if ($nativeExit -ne 0) { exit $nativeExit }
Write-Output 'normal-after-expected-native-failure'
"@
        Put $file $fake
        $tokens = $null; $errors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
        Assert (-not $errors.Count) 'Synthetic guard tool must parse before execution.'
    }
    $blockFile = Join-Path $dir 'block.ps1'; Put $blockFile $agentEvidence
    $psi = [Diagnostics.ProcessStartInfo]::new($pwsh)
    $psi.UseShellExecute = $false; $psi.WorkingDirectory = $offlineFixture
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile','-File',$blockFile)) { $psi.ArgumentList.Add($arg) }
    $psi.Environment['GITHUB_WORKSPACE'] = $offlineFixture; $psi.Environment['RUNNER_TEMP'] = $dir
    $callsFile = Join-Path $dir 'calls.txt'; $psi.Environment['GUARD_CALLS'] = $callsFile
    $record = @{name=$Name; executable=$pwsh; arguments=@('-NoProfile','-File',$blockFile); cwd=$offlineFixture; exitCode=$null; status='incomplete'}
    $offlineRecords.Add($record)
    Put (Join-Path $base 'offline-commands.json') (ConvertTo-Json -InputObject $offlineRecords.ToArray() -Depth 6)
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $psi
    $stdout = [IO.File]::Create((Join-Path $dir 'stdout.log'))
    $stderr = [IO.File]::Create((Join-Path $dir 'stderr.log'))
    $started = $false
    try {
        $started = $process.Start(); Assert $started 'Offline block did not start.'
        $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
        $all = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(), $copyOut, $copyErr))
        Assert ($all.Wait(60000)) 'Offline fixture completion unknown; retain evidence and stop.'
        $actual = $process.ExitCode; $record.exitCode = $actual; $record.status = 'completed'
    } finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            $record.cleanupComplete = $process.WaitForExit(5000)
        }
        $stdout.Dispose(); $stderr.Dispose(); $process.Dispose()
        Put (Join-Path $base 'offline-commands.json') (ConvertTo-Json -InputObject $offlineRecords.ToArray() -Depth 6)
    }
    Put (Join-Path $dir 'exit') ([string]$actual)
    Assert ($actual -eq $(if ($FailAt -ge 0) { 7 } else { 0 })) 'Offline block lost original numeric exit.'
    $calls = @([IO.File]::ReadAllLines($callsFile))
    $last = if ($FailAt -ge 0) { $FailAt } else { $guardTools.Count - 1 }
    Assert (($calls -join ',') -ceq ((0..$last) -join ',')) 'Offline failure did not stop later tools.'
    foreach ($i in 0..$last) {
        if ($guardTools[$i].evidence) {
            Assert (Test-Path -LiteralPath (Join-Path $dir ($guardTools[$i].evidence + '/original.txt'))) 'Offline original lost on success/failure.'
        }
    }
}
Run-OfflineGuard 'offline-green'
for ($i = 0; $i -lt $guardTools.Count; $i++) { Run-OfflineGuard "offline-exit7-$i" $i }
Put (Join-Path $base 'offline-summary.json') (@{cases=$offlineRecords.Count; failed=0; realSdk=$false; realEngine=$false; realGpu=$false; network=$false; actualShellBlock=$true} | ConvertTo-Json)
Write-Output "PASS offline CI workflow: $($offlineRecords.Count) controls; actual AST/shell block, synthetic tools, no SDK/engine/GPU/download."
if ($Mode -eq 'Offline') { exit 0 }
# Reuse approved package versions from the actual csproj, not another handwritten dependency policy.
$fixture = Join-Path $base 'owned-repository'; $null = New-Item -ItemType Directory $fixture
foreach ($file in @('global.json','NuGet.Config')) { [IO.File]::Copy((Join-Path $root $file), (Join-Path $fixture $file)) }
$null = New-Item -ItemType Directory (Join-Path $fixture 'tests')
[IO.File]::Copy($settings, (Join-Path $fixture 'tests/unit.runsettings'))
$pass = 'using Xunit; public class NativeUnitCase { [Fact] public void Pass() { Assert.Equal(4, 2 + 2); } }'
foreach ($name in @('Pal.Core.UnitTests','Pal.Extract.UnitTests')) {
    $dir = Join-Path $fixture "tests/$name"; $null = New-Item -ItemType Directory $dir
    [xml]$project = [IO.File]::ReadAllText((Join-Path $root "tests/$name/$name.csproj"))
    foreach ($node in @($project.SelectNodes('//ProjectReference|//Compile'))) { $null = $node.ParentNode.RemoveChild($node) }
    $project.Save((Join-Path $dir "$name.csproj")); Put (Join-Path $dir 'NativeUnitCase.cs') $pass
}
Put (Join-Path $fixture '.gitignore') "bin/`nobj/`n"
$git = (Get-Command git -CommandType Application | Select-Object -First 1).Source
foreach ($arguments in @(@('init','--quiet'),@('config','user.name','Owned fixture'),@('config','user.email','fixture@example.invalid'),@('add','.'),@('commit','--quiet','-m','Native Unit fixture'))) {
    & $git -C $fixture @arguments
    Assert ($LASTEXITCODE -eq 0) 'Owned fixture Git setup failed.'
}
$sha = & $git -C $fixture rev-parse HEAD; Assert ($LASTEXITCODE -eq 0) 'Owned fixture has no HEAD.'
$pwsh = (Get-Process -Id $PID).Path
function Run([string]$Name, [string]$Code, [string]$ExpectedSha = $sha) {
    $dir = Join-Path $base $Name; $null = New-Item -ItemType Directory $dir
    $script = Join-Path $dir 'block.ps1'; Put $script ("`$ErrorActionPreference = 'Stop'`n" + $Code + "`nexit 0`n")
    $tokens = $null; $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$parseErrors)
    $diagnostics = @($parseErrors | ForEach-Object { '{0}:{1}:{2}: {3}' -f $script, $_.Extent.StartLineNumber, $_.Extent.StartColumnNumber, $_.Message })
    Assert ($parseErrors.Count -eq 0) ("Generated workflow block does not parse: {0}`n{1}" -f $Name, ($diagnostics -join "`n"))
    $psi = [Diagnostics.ProcessStartInfo]::new($pwsh)
    $psi.WorkingDirectory = $fixture; $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile','-File',$script)) { $psi.ArgumentList.Add($arg) }
    $psi.Environment['RUNNER_TEMP'] = $dir; $psi.Environment['GITHUB_SHA'] = $ExpectedSha
    $psi.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'; $psi.Environment['DOTNET_NOLOGO'] = '1'
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $psi
    try {
        Assert ($process.Start()) 'Owned block did not start.'
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit(); $nativeExit = $process.ExitCode
        $outText = $stdout.GetAwaiter().GetResult(); $errText = $stderr.GetAwaiter().GetResult()
    } finally { $process.Dispose() }
    Put "$dir/stdout.log" $outText; Put "$dir/stderr.log" $errText
    Put "$dir/command.json" (@{executable=$pwsh;arguments=@('-NoProfile','-File',$script);cwd=$fixture;exitCode=$nativeExit} | ConvertTo-Json)
    return @{exit=$nativeExit;text=$outText+"`n"+$errText;dir=$dir}
}
$parseRejected = $false
try { $null = Run 'invalid-generated' 'if (' }
catch {
    Put "$base/invalid-generated/parse-error.txt" $_.Exception.Message
    $parseRejected = $_.Exception.Message.Contains('Generated workflow block does not parse:')
}
Assert $parseRejected 'Malformed generated block was not rejected before launch.'
Assert (-not (Test-Path "$base/invalid-generated/command.json")) 'Malformed generated block reached process execution.'
$r = Run 'checkout' $checkout; Assert ($r.exit -eq 0) 'Actual checkout guard rejected matching SHA.'
$r = Run 'wrong-checkout' $checkout ('f' * 40)
Assert ($r.exit -ne 0 -and $r.text.Contains('Unit checkout does not match GITHUB_SHA.')) 'Wrong checkout was not refused for its designated reason.'
$r = Run 'normal' ($core + "`n" + $cli)
Assert ($r.exit -eq 0) "Actual native project commands failed: $($r.text)"
foreach ($name in @('core','cli-unit')) { Assert (Test-Path "$($r.dir)/unit-results/$name/results.trx") 'Normal direct command did not produce both reports.' }
$caseFile = Join-Path $fixture 'tests/Pal.Core.UnitTests/NativeUnitCase.cs'
foreach ($control in @(
    @{name='failure';source='using Xunit; public class NativeUnitCase { [Fact] public void Fail() { Assert.True(false, "NATIVE_UNIT_EXPECTED_FAILURE"); } }';reason='NATIVE_UNIT_EXPECTED_FAILURE'},
    @{name='skip';source='using Xunit; public class NativeUnitCase { [Fact(Skip="NATIVE_UNIT_EXPECTED_SKIP")] public void Skip() { } }';reason='NATIVE_UNIT_EXPECTED_SKIP'},
    @{name='zero';source='public class NativeUnitCase { }';reason='No test is available|No tests (were|are|found|available)'}
)) {
    Put $caseFile $control.source
    $r = Run $control.name ($core + "`n" + $cli)
    Assert ($r.exit -ne 0 -and $r.text -match $control.reason) "Expected native $($control.name) refusal missing: $($r.text)"
    Assert (-not (Test-Path "$($r.dir)/unit-results/cli-unit")) 'A failed first project did not stop the second.'
}
Put $caseFile $pass
# Suppress only the logger to reach the actual missing-report guard after a real successful test.
$withoutTrx = $core.Replace("--logger 'trx;LogFileName=results.trx'", "--logger 'console;verbosity=minimal'")
$r = Run 'missing-report' $withoutTrx
Assert ($r.exit -ne 0 -and $r.text.Contains('Missing current Core Unit TRX.')) 'Missing TRX did not fail for its designated reason.'
$r = Run 'existing-results' ("New-Item -ItemType Directory -Path `"`$env:RUNNER_TEMP/unit-results/core`" -Force | Out-Null`n" + $core)
Assert ($r.exit -ne 0 -and $r.text.Contains('Core Unit results directory already exists.')) 'Existing results were allowed to substitute for a new run.'
$r = Run 'full-checkout' $fullCheckout; Assert ($r.exit -eq 0) 'Full gate checkout guard rejected matching SHA.'
$r = Run 'full-wrong-checkout' $fullCheckout ('f' * 40)
Assert ($r.exit -ne 0 -and $r.text.Contains('Full gate checkout does not match GITHUB_SHA.')) 'Full gate wrong checkout was not refused for its designated reason.'
Put (Join-Path $base 'summary.json') (@{cases=11;failed=0;realDotnet=$true;realGit=$true;remoteActions=$false;productUnit=$false} | ConvertTo-Json)
Write-Output 'PASS native Unit workflow and committable full-gate wiring: 11 controls; actual blocks and real dotnet/xUnit; no remote Actions/Godot execution.'
