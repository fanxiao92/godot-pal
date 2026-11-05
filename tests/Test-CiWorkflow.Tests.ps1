# Explicit maintenance regression: actual workflow blocks, real dotnet/xUnit, no remote Actions or Godot.
[CmdletBinding()]
param([string]$EvidenceDirectory)
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
Assert ($unit.Contains("git grep --cached -l -e 'version https://git-lfs'")) 'Unit job must guard committable trees against LFS pointers (M5 retro: #76 CI diagnosis).'
# #76 informational committable-scope full gate: the same Test-M0 entry as local acceptance.
Assert ($full -cmatch [regex]::Escape("if: github.event_name == 'push' || github.event_name == 'workflow_dispatch' || (github.event_name == 'pull_request' && github.event.action == 'ready_for_review')")) 'Full gate must run only on main push, PR readiness or manual dispatch.'
Assert (-not [regex]::IsMatch($full, "github\.event\.action == 'synchronize'")) 'Full gate must never be scheduled per synchronize.'
Assert ($full -cmatch '(?m)^    runs-on: windows-2022$' -and $full -cmatch '(?m)^    timeout-minutes: 35$') 'Full gate runner or its 35-minute envelope changed.'
Assert ($full -cmatch '(?m)^    if:' -and $full -notmatch '(?m)^    needs:') 'Full gate must stay scheduled independently, not behind unit-check.'
foreach ($fragment in @('persist-credentials: false','fetch-depth: 2','lfs: false','global-json-file: global.json','if: always()','if-no-files-found: error','name: ci-full-evidence-${{ github.run_id }}-${{ github.run_attempt }}','m0-evidence/**/*.trx','m0-evidence/PASS.txt')) { Assert ($full.Contains($fragment)) "Missing full gate CI policy: $fragment" }
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
