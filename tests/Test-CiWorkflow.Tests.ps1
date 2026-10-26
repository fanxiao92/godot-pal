[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workflow = Join-Path $root '.github/workflows/ci.yml'
$lines = [IO.File]::ReadAllLines($workflow, $utf8)
$base = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('ci-workflow-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $base) { throw "EvidenceDirectory must be new: $base" }
$null = New-Item -ItemType Directory -Path $base

function Write-Utf8([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Write-Json([string]$Path, $Value) { Write-Utf8 $Path (ConvertTo-Json -InputObject $Value -Depth 50) }
function Assert-That([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Parse([string]$Text) {
    $tokens = $null; $errors = $null
    [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw "PowerShell AST failed: $($errors -join '; ')" }
}
# This is deliberately a fail-closed extractor for this repository's fixed block style,
# not a YAML parser or a GitHub expression/needs evaluator. YAML is checked separately.
function Read-ActualBlock([string]$Name) {
    $matches = @(for ($i = 0; $i -lt $lines.Length; $i++) { if ($lines[$i] -ceq "      - name: $Name") { $i } })
    if ($matches.Count -ne 1) { throw "Expected exactly one actual workflow step: $Name" }
    $i = $matches[0] + 1
    while ($i -lt $lines.Length -and $lines[$i] -cne '        run: |') {
        if ($lines[$i].StartsWith('      - name:')) { throw "Step has no block run: $Name" }
        $i++
    }
    if ($i -ge $lines.Length) { throw "Step has no block run: $Name" }
    $body = [Collections.Generic.List[string]]::new()
    for ($i++; $i -lt $lines.Length; $i++) {
        if ([string]::IsNullOrWhiteSpace($lines[$i])) { $body.Add(''); continue }
        if (-not $lines[$i].StartsWith('          ')) { break }
        $body.Add($lines[$i].Substring(10))
    }
    $text = $body -join "`n"
    if ($text.Contains('${{')) { throw "Unrendered GitHub expression in actual block: $Name" }
    Assert-Parse $text
    return $text
}
$blocks = @{
    quick = Read-ActualBlock 'Record and validate candidate identity'
    start = Read-ActualBlock 'Record and validate start candidate identity'
    end = Read-ActualBlock 'Validate end candidate freshness'
    m0 = Read-ActualBlock 'Run shared M0, M2 and M3 acceptance checks'
    cost = Read-ActualBlock 'Generate current-run cost report'
    proof = Read-ActualBlock 'Validate current-run full proof'
    final = Read-ActualBlock 'Fail closed unless the current full gate completed successfully'
    offlineStart = Read-ActualBlock 'Record and validate offline start candidate identity'
    offlineEnd = Read-ActualBlock 'Validate offline end candidate freshness'
    offlineProof = Read-ActualBlock 'Validate current-run offline proof'
}
foreach ($name in $blocks.Keys) { Write-Utf8 (Join-Path $base "$name.extracted.ps1") $blocks[$name] }
Write-Json (Join-Path $base 'source.json') @{ workflow = $workflow; sha256 = (Get-FileHash $workflow -Algorithm SHA256).Hash; synthetic = $true }
$pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source

$fakeM0 = @'
param([string]$GodotPath, [string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$u = [Text.UTF8Encoding]::new($false)
function Put([string]$Name, [string]$Value) { [IO.File]::WriteAllText((Join-Path $EvidenceDirectory $Name), $Value, $u) }
# Match the actual public M0 contract: the callee, not its caller, creates this root.
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a new evidence directory; this run requires an empty package cache.' }
$null = New-Item -ItemType Directory -Path $EvidenceDirectory
$marker = Join-Path $EvidenceDirectory 'fake-call-count.txt'
if (Test-Path -LiteralPath $marker) { throw 'Fake M0 was called more than once.' }
Put 'fake-call-count.txt' '1'
$started = [DateTimeOffset]::UtcNow.ToString('O')
$null = New-Item -ItemType Directory -Path (Join-Path $EvidenceDirectory 'results')
Put 'test.log' 'explicit synthetic command log'
Put 'test.stdout.log' 'explicit synthetic stdout'
Put 'test.stderr.log' ''
$finished = [DateTimeOffset]::UtcNow.ToString('O')
$trx = '<TestRun id="fixture-run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Times creation="START" queuing="START" start="START" finish="FINISH"/><TestDefinitions><UnitTest name="Fixture.Pass" storage="fixture.dll" id="test-1"><Execution id="exec-1"/><TestMethod codeBase="fixture.dll" adapterTypeName="executor://fixture/" className="Fixture.Tests" name="Pass"/></UnitTest></TestDefinitions><TestEntries><TestEntry testId="test-1" executionId="exec-1" testListId="list-1"/></TestEntries><TestLists><TestList name="All Loaded Tests" id="list-1"/></TestLists><Results><UnitTestResult executionId="exec-1" testId="test-1" testName="Fixture.Pass" outcome="Passed" duration="00:00:00" startTime="START" endTime="START"/></Results><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" inProgress="0" pending="0"/><RunInfos/></ResultSummary></TestRun>'
Put 'results/one.trx' ($trx.Replace('START', $started).Replace('FINISH', $finished))
$record = [ordered]@{command='dotnet';arguments=@('test','fixture.csproj','--logger','trx;LogFileName=one.trx','--results-directory','results');log='test.log';stdout='test.stdout.log';stderr='test.stderr.log';phase='completed';rootExited=$true;stdoutComplete=$true;stderrComplete=$true;outputComplete=$true;cleanupComplete=$true;exitKnown=$true;cancellationRequested=$false;timedOut=$false;exitCode=0;expectedExit=0;startedUtc=$started;finishedUtc=$finished;elapsedMs=([DateTimeOffset]::Parse($finished)-[DateTimeOffset]::Parse($started)).TotalMilliseconds}
Put 'commands.json' (ConvertTo-Json -InputObject @($record) -Depth 15)
Put 'PASS.txt' 'explicit synthetic PASS'
switch ($env:FAKE_M0_SCENARIO) {
    'seven' { [Console]::Error.WriteLine('中文 fake M0 stderr'); exit 7 }
    'invalid-utf8-seven' { [Console]::OpenStandardError().WriteByte(255); exit 7 }
    'throw-after-native-zero' { & pwsh -NoProfile -Command 'exit 0'; throw 'Controlled M0 throw after a successful inner native command.' }
    'measurement-write-seven' { $null=New-Item -ItemType Directory -Path (Join-Path $EvidenceDirectory 'task-measurement.json'); exit 7 }
    'taskexit-write-seven' { $null=New-Item -ItemType Directory -Path (Join-Path $EvidenceDirectory 'task-exit.txt'); exit 7 }
    'measurement-write-zero' { $null=New-Item -ItemType Directory -Path (Join-Path $EvidenceDirectory 'task-measurement.json'); exit 0 }
    default { exit 0 }
}
'@
Assert-Parse $fakeM0

function New-Case([string]$Name, [string]$Scenario = 'success', [bool]$CandidateFixture = $true) {
    $dir = Join-Path $base $Name
    $null = New-Item -ItemType Directory -Path $dir
    $scripts = Join-Path $dir 'scripts'
    $null = New-Item -ItemType Directory -Path $scripts
    $hashes = @()
    foreach ($script in @('New-CiCostInput.ps1','Get-TestCostReport.ps1','TestCostReport.psm1','GameTestBatches.ps1','Test-CiFullProof.ps1','New-CiCandidateInputs.ps1','Test-CiCandidate.ps1')) {
        $source = Join-Path $root "scripts/$script"; $target = Join-Path $scripts $script
        [IO.File]::Copy($source, $target)
        $sha = (Get-FileHash $source -Algorithm SHA256).Hash
        Assert-That ($sha -ceq (Get-FileHash $target -Algorithm SHA256).Hash) "Copied production script differs: $script"
        $hashes += @{name=$script;sha256=$sha}
    }
    Write-Json (Join-Path $dir 'copied-source.json') $hashes
    Write-Utf8 (Join-Path $scripts 'Test-M0.ps1') $fakeM0
    $sha = 'a' * 40
    $identity = @{sha=$sha;checkout=$sha;runId='workflow-fixture';attempt=1;candidate=$sha;workflowRef="fixture/.github/workflows/ci.yml@refs/heads/含'引号";eventName='workflow_dispatch';action='manual'}
    if ($CandidateFixture) { Write-Json (Join-Path $dir 'ci-identity.json') $identity }
    $candidate = @{eventName='workflow_dispatch';action='manual';mode='manual-full';runId=$identity.runId;runAttempt=1;workflowRef=$identity.workflowRef;draft=$false;headSha=$null;baseSha=$null;candidateSha=$sha;actualCheckout=$sha;tree=('c'*40);parents=@()}
    if ($CandidateFixture) { foreach ($sub in @('candidate-proof','candidate-end-proof','offline-start-proof','offline-end-proof')) { $null=New-Item -ItemType Directory -Path (Join-Path $dir $sub); Write-Json (Join-Path $dir "$sub/candidate-identity.json") $candidate } }
    return @{dir=$dir;env=@{RUNNER_TEMP=$dir;GITHUB_WORKSPACE=$dir;GITHUB_OUTPUT=(Join-Path $dir 'github-output.txt');CI_RUN_ID=$identity.runId;CI_RUN_ATTEMPT='1';CI_SHA=$sha;CI_WORKFLOW_REF=$identity.workflowRef;CI_EVENT_NAME='workflow_dispatch';GODOT_CONSOLE='explicit-fake-no-engine';FAKE_M0_SCENARIO=$Scenario}}
}
function Invoke-Block($Case, [string]$Block, [string]$Prefix = '') {
    $script = Join-Path $Case.dir "$Block.runner.ps1"
    $text = "`$ErrorActionPreference = 'Stop'`n[Console]::OutputEncoding = [Text.UTF8Encoding]::new(`$false)`n$Prefix`n$($blocks[$Block])"
    Assert-Parse $text
    Write-Utf8 $script $text
    $psi = [Diagnostics.ProcessStartInfo]::new($pwsh)
    $psi.WorkingDirectory=$Case.dir; $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    $psi.StandardOutputEncoding=$utf8; $psi.StandardErrorEncoding=$utf8
    foreach ($arg in @('-NoProfile','-File',$script)) { $psi.ArgumentList.Add($arg) }
    foreach ($key in $Case.env.Keys) { $psi.Environment[$key]=$Case.env[$key] }
    $process = [Diagnostics.Process]::new(); $process.StartInfo=$psi
    if (-not $process.Start()) { throw "Could not start actual block: $Block" }
    $stdoutPath=Join-Path $Case.dir "$Block.stdout"; $stderrPath=Join-Path $Case.dir "$Block.stderr"
    $stdoutFile=[IO.File]::Open($stdoutPath,[IO.FileMode]::CreateNew); $stderrFile=[IO.File]::Open($stderrPath,[IO.FileMode]::CreateNew)
    try {
        $stdout=$process.StandardOutput.BaseStream.CopyToAsync($stdoutFile); $stderr=$process.StandardError.BaseStream.CopyToAsync($stderrFile)
        $process.WaitForExit(); $stdout.GetAwaiter().GetResult(); $stderr.GetAwaiter().GetResult(); $exit=$process.ExitCode
    } finally { $stdoutFile.Dispose(); $stderrFile.Dispose(); $process.Dispose() }
    Write-Json (Join-Path $Case.dir "$Block.command.json") @{argv=@($pwsh,'-NoProfile','-File',$script);exit=$exit;stdoutComplete=$true;stderrComplete=$true;env=$Case.env}
    return @{Exit=$exit;Out=[IO.File]::ReadAllText($stdoutPath,$utf8);Err=[IO.File]::ReadAllText($stderrPath,$utf8)}
}
$results = [Collections.Generic.List[object]]::new(); $failures = [Collections.Generic.List[string]]::new()
function Check-Case([string]$Name, [scriptblock]$Check) {
    try { & $Check; $results.Add(@{name=$Name;passed=$true}) }
    catch { $failures.Add("${Name}: $($_.Exception.Message)"); $results.Add(@{name=$Name;passed=$false;error=$_.Exception.Message}) }
}
Check-Case 'actual-shared-m0-entry-contract-without-sdk' {
    $case=New-Case 'actual-entry-contract'
    $sourcePath=Join-Path $root 'scripts/Test-M0.ps1'; $source=[IO.File]::ReadAllText($sourcePath,$utf8)
    $tokens=$null; $errors=$null
    $ast=[System.Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
    Assert-That ($errors.Count -eq 0) 'Actual shared M0 source does not parse.'
    $boundary=@($ast.EndBlock.Statements | Where-Object { $_.Extent.Text -ceq 'New-Item -ItemType Directory $evidence | Out-Null' })
    Assert-That ($boundary.Count -eq 1) 'Actual M0 prelude boundary changed; do not execute an unbounded source prefix.'
    # Exact actual prelude only: stop before process queries, SDK, engine or the acceptance body.
    $prelude=$source.Substring(0,$boundary[0].Extent.EndOffset)
    $pTokens=$null; $pErrors=$null
    $pAst=[System.Management.Automation.Language.Parser]::ParseInput($prelude,[ref]$pTokens,[ref]$pErrors)
    $commands=@($pAst.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true))
    Assert-That (@($commands | Where-Object { $_.GetCommandName() -notin @('Resolve-Path','Join-Path','Test-Path','New-Item','Out-Null') }).Count -eq 0) 'Actual M0 prelude gained a non-filesystem command.'
    $contractScript=$prelude+"`n[IO.File]::WriteAllText((Join-Path `$EvidenceDirectory 'entry-contract-accepted.txt'),'new-root')`nexit 0"
    Assert-Parse $contractScript
    Write-Utf8 (Join-Path $case.dir 'scripts/Test-M0.ps1') $contractScript
    Write-Json (Join-Path $case.dir 'entry-source.json') @{source=$sourcePath;sha256=(Get-FileHash $sourcePath -Algorithm SHA256).Hash;scope='Actual initial entry only, no SDK/process/engine execution'}
    $case.env.GODOT_CONSOLE=$pwsh # Existing file for Resolve-Path only; never executed as an engine.
    $result=Invoke-Block $case 'm0'
    Assert-That ($result.Exit -eq 0) "Actual shared M0 entry precondition failed: $($result.Err)"
    Assert-That (Test-Path -LiteralPath (Join-Path $case.dir 'm0-evidence/entry-contract-accepted.txt')) 'Actual shared M0 did not accept the fresh directory contract.'
}
Check-Case 'actual-m0-cost-proof-positive' {
    $case=New-Case 'positive'; $m=Invoke-Block $case 'm0'
    Assert-That ($m.Exit -eq 0) "Actual M0 wrapper failed: $($m.Err)"
    $measurement=Get-Content (Join-Path $case.dir 'm0-evidence/task-measurement.json') -Raw | ConvertFrom-Json -DateKind String
    Assert-That ($measurement.exitKnown -ceq $true -and $measurement.exitCode -eq 0 -and $measurement.wallClockMs -gt 0) 'Incorrect original successful measurement.'
    Assert-That ([IO.File]::ReadAllText((Join-Path $case.dir 'm0-evidence/fake-call-count.txt')) -ceq '1') 'M0 was not called exactly once.'
    $cost=Invoke-Block $case 'cost'; Assert-That ($cost.Exit -eq 0) "Actual cost block failed: $($cost.Err)"
    $proof=Invoke-Block $case 'proof'; Assert-That ($proof.Exit -eq 0) "Actual proof block failed: $($proof.Err)"
    $output=[IO.File]::ReadAllText((Join-Path $case.dir 'github-output.txt'))
    Assert-That ($output -match '(?m)^status=complete\r?$') 'Actual proof block did not publish complete.'
}
Check-Case 'known-seven-byte-exact-stderr' {
    $case=New-Case 'seven' 'seven'; $r=Invoke-Block $case 'm0'
    Assert-That ($r.Exit -eq 7) "Original native exit 7 was changed: $($r.Exit)"
    $actual=[IO.File]::ReadAllBytes((Join-Path $case.dir 'm0-evidence/task-stderr.log'))
    $expected=[Text.UTF8Encoding]::new($false).GetBytes("中文 fake M0 stderr$([Environment]::NewLine)")
    Assert-That ([Convert]::ToBase64String($actual) -ceq [Convert]::ToBase64String($expected)) 'Original native UTF8 stderr bytes changed.'
}
Check-Case 'invalid-utf8-output-preserves-original-seven' {
    $case=New-Case 'invalid-utf8-seven' 'invalid-utf8-seven';$r=Invoke-Block $case 'm0'
    Assert-That ($r.Exit -eq 7) 'Output replay decoding replaced the original native 7.'
    Assert-That ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $case.dir 'm0-evidence/task-stderr.log'))) -ceq '/w==') 'Invalid original bytes were rewritten.'
    $m=Get-Content (Join-Path $case.dir 'm0-evidence/task-measurement.json') -Raw|ConvertFrom-Json
    Assert-That ($m.exitCode -eq 7 -and $m.captureErrors.Count -gt 0) 'Output replay error was not separately retained.'
}
Check-Case 'throw-after-successful-inner-command' {
    $case=New-Case 'throw' 'throw-after-native-zero'; $r=Invoke-Block $case 'm0'
    Assert-That ($r.Exit -eq 1) 'An inner native zero replaced the failed original task.'
    $m=Get-Content (Join-Path $case.dir 'm0-evidence/task-measurement.json') -Raw | ConvertFrom-Json
    Assert-That ($m.exitKnown -ceq $true -and $m.exitCode -eq 1) 'Failed original native host was not recorded as exit 1.'
}
Check-Case 'unknown-launch-does-not-reuse-stale-zero' {
    $case=New-Case 'unknown'; $r=Invoke-Block $case 'm0' '$global:LASTEXITCODE=0; function Get-Command { param($Name,$CommandType,$ErrorAction); [pscustomobject]@{Source=(Join-Path $env:RUNNER_TEMP "controlled-missing-host.exe")} }'
    Assert-That ($r.Exit -ne 0) 'Unknown launch was incorrectly a successful task.'
    $m=Get-Content (Join-Path $case.dir 'm0-evidence/task-measurement.json') -Raw | ConvertFrom-Json
    Assert-That ($m.exitKnown -ceq $false -and $null -eq $m.exitCode) 'Unknown exit was replaced by stale numeric zero.'
    Assert-That ($r.Err.Contains('controlled-missing-host.exe')) 'Unknown outcome failed for the wrong reason.'
}
foreach ($scenario in @('measurement-write-seven','taskexit-write-seven')) {
    Check-Case $scenario {
        $case=New-Case $scenario $scenario; $r=Invoke-Block $case 'm0'
        Assert-That ($r.Exit -eq 7) "Evidence failure replaced original 7 with $($r.Exit)."
        Assert-That ($r.Err.Contains('M0 evidence capture failed:')) 'Expected controlled evidence-write failure was not observed.'
    }
}
Check-Case 'capture-error-is-not-an-original-task-failure' {
    $case=New-Case 'capture-zero' 'measurement-write-zero'; $r=Invoke-Block $case 'm0'
    Assert-That ($r.Exit -ne 0 -and $r.Err.Contains('M0 evidence capture failed:')) 'Evidence failure did not fail the wrapper closed.'
    Assert-That ([IO.File]::ReadAllText((Join-Path $case.dir 'm0-evidence/task-exit.txt')) -ceq '0') 'Engineering error overwrote original task zero.'
}
Check-Case 'immediate-snapshot-rejects-post-m0-tamper' {
    $case=New-Case 'tamper'; $m=Invoke-Block $case 'm0'; Assert-That ($m.Exit -eq 0) 'Tamper control M0 failed.'
    $cost=Invoke-Block $case 'cost'; Assert-That ($cost.Exit -eq 0) 'Tamper control cost generation failed.'
    $pass=Join-Path $case.dir 'm0-evidence/PASS.txt'
    [IO.File]::Copy($pass,(Join-Path $case.dir 'PASS.before-tamper.txt'))
    Write-Utf8 $pass 'controlled post-M0 tamper'
    $proof=Invoke-Block $case 'proof'
    Assert-That ($proof.Exit -ne 0 -and $proof.Err.Contains('[full-proof:artifact]')) 'Actual FullProof did not reject the original artifact tamper.'
    Assert-That (-not(Test-Path -LiteralPath $case.env.GITHUB_OUTPUT)) 'Rejected proof published a credential.'
}
Check-Case 'actual-manual-candidate-to-final-proof-chain' {
    $case=New-Case 'actual-candidate-chain' 'success' $false
    $git=(Get-Command git -CommandType Application | Select-Object -First 1).Source
    $gitCommands=@(@('init','--initial-branch=main'),@('add','scripts'),@('-c','user.name=Fixture','-c','user.email=fixture@example.invalid','commit','-m','Owned synthetic CI fixture'),@('-c','user.name=Fixture','-c','user.email=fixture@example.invalid','commit','--allow-empty','-m','Owned single-parent CI fixture'))
    $number=0
    foreach($argv in $gitCommands){
        $number++;$out=Join-Path $case.dir "git-$number.stdout";$err=Join-Path $case.dir "git-$number.stderr"
        & $git -C $case.dir @argv 1> $out 2> $err
        $exit=$LASTEXITCODE;Write-Json (Join-Path $case.dir "git-$number.command.json") @{argv=@($git,'-C',$case.dir)+$argv;exit=$exit}
        Assert-That ($exit -eq 0) "Owned fixture Git failed at $number."
    }
    $sha=(& $git -C $case.dir rev-parse HEAD).Trim();Assert-That ($LASTEXITCODE -eq 0 -and $sha -match '^[0-9a-f]{40}$') 'Owned fixture head is missing.'
    $event=Join-Path $case.dir 'dispatch-event.json';Write-Json $event @{inputs=$null;ref='refs/heads/main'}
    $case.env.CI_EVENT_PATH=$event;$case.env.CI_SHA=$sha
    foreach($block in @('start','m0','end','cost','proof')){
        $r=Invoke-Block $case $block;Assert-That ($r.Exit -eq 0) "Actual $block block failed in complete owned candidate chain: $($r.Err)"
    }
    $produced=Get-Content (Join-Path $case.dir 'candidate-proof/candidate-identity.json') -Raw|ConvertFrom-Json -NoEnumerate
    Assert-That ($produced.parents -is [array] -and $produced.parents.Count -eq 1) 'Actual Git chain must exercise a single-parent JSON array.'
    $id=Get-Content (Join-Path $case.dir 'ci-identity.json') -Raw|ConvertFrom-Json
    Assert-That ($id.action -ceq 'manual' -and $id.candidate -ceq $sha) 'Actual producer action/candidate was not preserved in CI identity.'
    Assert-That ([IO.File]::ReadAllText($case.env.GITHUB_OUTPUT).Contains('status=complete')) 'Actual candidate-to-FullProof chain published no complete output.'
}
Check-Case 'full-gate-upload-includes-boundary-evidence' {
    $starts=@(for($i=0;$i -lt $lines.Length;$i++){if($lines[$i] -ceq '  full-gate:'){$i}}); Assert-That ($starts.Count -eq 1) 'Expected exactly one full-gate job.'
    $end=$lines.Length; for($i=$starts[0]+1;$i -lt $lines.Length;$i++){if($lines[$i] -match '^  \S'){$end=$i;break}}
    $stepStarts=@(for($i=0;$i -lt $lines.Length;$i++){if($lines[$i] -ceq '      - name: Upload acceptance evidence'){$i}})
    $stepStarts=@($stepStarts | Where-Object {$_ -ge $starts[0] -and $_ -lt $end})
    Assert-That ($stepStarts.Count -eq 1) 'Expected exactly one full-gate Upload acceptance evidence step.'
    $stepEnd=$end; for($i=$stepStarts[0]+1;$i -lt $end;$i++){if($lines[$i] -match '^      - name:'){$stepEnd=$i;break}}
    $pathStarts=@(for($i=$stepStarts[0]+1;$i -lt $stepEnd;$i++){if($lines[$i] -ceq '          path: |'){$i}})
    Assert-That ($pathStarts.Count -eq 1) 'Expected exactly one acceptance upload path block.'
    $pathEnd=$stepEnd; for($i=$pathStarts[0]+1;$i -lt $stepEnd;$i++){if($lines[$i] -match '^ {0,10}\S'){$pathEnd=$i;break}}
    $paths=@($lines[($pathStarts[0]+1)..($pathEnd-1)] | ForEach-Object { $_.Trim() })
    Assert-That ($paths -ccontains '${{ runner.temp }}/m0-evidence/boundary-tests/**') 'Full-gate upload must include the boundary-tests evidence subtree.'
    Assert-That (@($paths | Where-Object { $_ -match '^\$\{\{ runner\.temp \}\}/m0-evidence/(?:packages|http-cache)(?:/|\*|$)' }).Count -eq 0) 'Boundary upload must not include package or HTTP cache paths.'
    Assert-That (@($paths | Where-Object { $_ -match '^\$\{\{ runner\.temp \}\}/m0-evidence/?(?:\*+(?:/\*+)*)?$' }).Count -eq 0) 'Acceptance upload must not expand to the entire M0 root.'
}
Check-Case 'offline-is-independent-and-full-gate-does-not-repeat-offline-regressions' {
    $text=$lines -join "`n"
    $job=[regex]::Match($text,'(?ms)^  engineering-offline:\n(?<body>.*?)(?=^  full-gate:)').Groups['body'].Value
    Assert-That ($job -and $job -notmatch '(?m)^    needs:' -and $job -notmatch 'setup-dotnet@|Get-Godot.ps1|Test-M0.ps1') 'Offline job must run independently without SDK/engine setup or execution.'
    $full=[regex]::Match($text,'(?ms)^  full-gate:\n(?<body>.*?)(?=^  full-check:)').Groups['body'].Value
    Assert-That ($full -notmatch 'Test-Engineering.ps1 -Mode Offline') 'Full gate must not repeat offline regressions.'
    Assert-That ($job.Contains("if: github.event_name != 'pull_request' || github.event.pull_request.draft != true")) 'Offline must follow the full-path Draft condition.'
    Assert-That ($job.Contains('ci-offline-evidence-${{ github.run_id }}-${{ github.run_attempt }}')) 'Offline upload must distinguish run and attempt.'
    Assert-That ($job.Contains('./tests/Test-CiWorkflow.Tests.ps1 -EvidenceDirectory') -and $job.Contains('/workflow-evidence/**')) 'Workflow contract regressions and their evidence must be wired into CI.'
    $final=($lines[[Array]::IndexOf($lines,'  full-check:')..($lines.Length-1)]) -join "`n"
    $bindings=@{OFFLINE_RESULT='result';OFFLINE_STATUS='outputs.proof_status';OFFLINE_RUN_ID='outputs.proof_run_id';OFFLINE_ATTEMPT='outputs.proof_attempt';OFFLINE_CANDIDATE='outputs.proof_candidate';OFFLINE_CODE='outputs.proof_code'}
    foreach($name in $bindings.Keys) {
        $expected='          '+$name+': ${{ needs.engineering-offline.'+$bindings[$name]+' }}'
        Assert-That ([regex]::Matches($final,'(?m)^'+[regex]::Escape($expected)+'$').Count -eq 1) "Final offline env must consume the actual needs output: $name"
    }
    foreach($field in @('status','run_id','attempt','candidate','code')) {
        Assert-That ($job.Contains('steps.offline-proof.outputs.'+$field+' }}')) "Offline job output binding is missing: $field"
    }
    Assert-That ($job.Contains('      - name: Download and verify fixed GdUnit bundle for offline checks') -and $job.Contains('      - name: Run offline engineering regressions')) 'Download and regressions must have separate step names.'
}
function Write-OfflineJournal($Case) {
    $dir=Join-Path $Case.env.RUNNER_TEMP 'offline-evidence'; $null=New-Item -ItemType Directory -Path $dir
    $steps=@(foreach($name in @('ast','unit-boundary','syntax-regression','engineering-regression','unit-boundary-regression','game-batches-regression','process-snapshot-regression','gdunit-packages-regression','cost-report-regression')) {
        @{name=$name;exitCode=0;exitKnown=$true;rootExited=$true;stdoutComplete=$true;stderrComplete=$true;cleanupComplete=$true;status='completed'}
    })
    Write-Json (Join-Path $dir 'engineering-run.json') @{schemaVersion=1;kind='engineering';mode='Offline';status='complete';steps=$steps}
}
foreach($shape in @('positive','journal-failed','journal-array','journal-status-array','journal-mode-array','missing-step','duplicate-step','step-seven','step-unknown-exit','step-exit-string','step-status-array','step-status-bool','step-name-array','step-no-eof','step-cleanup-incomplete','candidate-array','candidate-stale-run','candidate-stale-attempt','candidate-stale-code','candidate-stale-workflow','candidate-draft','candidate-end-mismatch','missing-journal')) {
    Check-Case "offline-proof-$shape" {
        $case=New-Case "offline-proof-$shape"; Write-OfflineJournal $case
        $journalPath=Join-Path $case.dir 'offline-evidence/engineering-run.json'
        $candidatePath=Join-Path $case.dir 'offline-start-proof/candidate-identity.json'
        $journal=Get-Content $journalPath -Raw | ConvertFrom-Json
        $candidate=Get-Content $candidatePath -Raw | ConvertFrom-Json
        switch($shape) {
            'journal-failed' { $journal.status='failed' }
            'journal-status-array' { $journal.status=@('complete') }
            'journal-mode-array' { $journal.mode=@('Offline') }
            'step-status-array' { $journal.steps[0].status=@('completed') }
            'step-status-bool' { $journal.steps[0].status=$true }
            'step-name-array' { $journal.steps[0].name=@('ast') }
            'missing-step' { $journal.steps=@($journal.steps | Select-Object -SkipLast 1) }
            'duplicate-step' { $journal.steps[8].name=$journal.steps[0].name }
            'step-seven' { $journal.steps[0].exitCode=7 }
            'step-unknown-exit' { $journal.steps[0].exitKnown=$false }
            'step-exit-string' { $journal.steps[0].exitCode='0' }
            'step-no-eof' { $journal.steps[0].stdoutComplete=$false }
            'step-cleanup-incomplete' { $journal.steps[0].cleanupComplete=$false }
            'candidate-stale-run' { $candidate.runId='old' }
            'candidate-stale-attempt' { $candidate.runAttempt=2 }
            'candidate-stale-code' { $candidate.actualCheckout=('f'*40) }
            'candidate-stale-workflow' { $candidate.workflowRef='old' }
            'candidate-draft' { $candidate.draft=$true }
        }
        Write-Json $journalPath $journal
        Write-Json $candidatePath $candidate
        Write-Json (Join-Path $case.dir 'offline-end-proof/candidate-identity.json') $candidate
        if($shape -eq 'candidate-end-mismatch') { $candidate.tree=('f'*40); Write-Json (Join-Path $case.dir 'offline-end-proof/candidate-identity.json') $candidate }
        if($shape -eq 'candidate-array') { Write-Json $candidatePath @($candidate) }
        if($shape -eq 'journal-array') { Write-Json $journalPath @($journal) }
        if($shape -eq 'missing-journal') { Remove-Item -LiteralPath $journalPath }
        $r=Invoke-Block $case 'offlineProof'
        if($shape -eq 'positive') {
            Assert-That ($r.Exit -eq 0) "Valid offline proof failed: $($r.Err)"
            $proof=Get-Content (Join-Path $case.dir 'offline-proof.json') -Raw | ConvertFrom-Json
            Assert-That ($proof.status -ceq 'complete' -and $proof.runId -ceq $case.env.CI_RUN_ID -and $proof.codeIdentity -ceq $case.env.CI_SHA) 'Offline proof changed current identity.'
        } else {
            Assert-That ($r.Exit -ne 0) "Invalid offline proof was accepted: $shape"
            Assert-That (-not(Test-Path -LiteralPath $case.env.GITHUB_OUTPUT)) "Rejected offline proof published outputs: $shape"
        }
    }
}
Check-Case 'final-needs-and-evidence-upload-contract' {
    $tail=($lines[[Array]::IndexOf($lines,'  full-check:')..($lines.Length-1)]) -join "`n"
    Assert-That ($tail -match '(?m)^    needs: \[quick-unit, engineering-offline, full-gate\]$' -and $tail -match '(?m)^    if: always\(\)$') 'Final check must always consume quick-unit, engineering-offline AND full-gate.'
    foreach($name in @('ci-quick-evidence-${{ github.run_id }}-${{ github.run_attempt }}','m0-windows-evidence-${{ github.run_id }}-${{ github.run_attempt }}')) {
        Assert-That (($lines -join "`n").Contains($name)) 'Artifact name must distinguish both run and attempt without overwriting older evidence.'
    }
    foreach($path in @('original-event.json','quick-candidate-input/**','quick-candidate-proof/**','candidate-start/**','candidate-end/**','m0-streams/**','m0-evidence/task-exit.txt','full-proof.json')) {
        Assert-That (($lines -join "`n").Contains('/'+$path)) "Original upload path missing: $path"
    }
}
$guardCases=@(
    @{name='ready-success';patch=@{};pass=$true},
    @{name='main-quick-skipped';patch=@{CURRENT_EVENT_NAME='push';QUICK_RESULT='skipped'};pass=$true},
    @{name='manual-quick-skipped';patch=@{CURRENT_EVENT_NAME='workflow_dispatch';QUICK_RESULT='skipped'};pass=$true},
    @{name='ready-quick-failed';patch=@{QUICK_RESULT='failure'};reason='quick-unit'},
    @{name='ready-quick-skipped';patch=@{QUICK_RESULT='skipped'};reason='quick-unit'},
    @{name='ready-quick-cancelled';patch=@{QUICK_RESULT='cancelled'};reason='quick-unit'},
    @{name='ready-quick-unknown';patch=@{QUICK_RESULT='unknown'};reason='quick-unit'},
    @{name='draft-not-a-full-credential';patch=@{CURRENT_DRAFT='true'};reason='Draft'},
    @{name='full-failed';patch=@{NEEDS_RESULT='failure'};reason='full-gate'},
    @{name='full-cancelled';patch=@{NEEDS_RESULT='cancelled'};reason='full-gate'},
    @{name='full-skipped';patch=@{NEEDS_RESULT='skipped'};reason='full-gate'},
    @{name='full-unknown';patch=@{NEEDS_RESULT='unknown'};reason='full-gate'},
    @{name='offline-failed';patch=@{OFFLINE_RESULT='failure'};reason='engineering-offline'},
    @{name='offline-cancelled';patch=@{OFFLINE_RESULT='cancelled'};reason='engineering-offline'},
    @{name='offline-skipped';patch=@{OFFLINE_RESULT='skipped'};reason='engineering-offline'},
    @{name='offline-unknown';patch=@{OFFLINE_RESULT='unknown'};reason='engineering-offline'},
    @{name='offline-missing-result';patch=@{OFFLINE_RESULT=''};reason='engineering-offline'},
    @{name='offline-stale-run';patch=@{OFFLINE_RUN_ID='old'};reason='Offline proof'},
    @{name='offline-stale-attempt';patch=@{OFFLINE_ATTEMPT='2'};reason='Offline proof'},
    @{name='offline-stale-candidate';patch=@{OFFLINE_CANDIDATE=('f'*40)};reason='Offline proof'},
    @{name='offline-stale-code';patch=@{OFFLINE_CODE=('f'*40)};reason='Offline proof'},
    @{name='offline-missing-proof';patch=@{OFFLINE_STATUS=''};reason='Offline proof'},
    @{name='stale-proof-run';patch=@{PROOF_RUN_ID='old'};reason='Full proof'},
    @{name='stale-proof-attempt';patch=@{PROOF_ATTEMPT='2'};reason='Full proof'},
    @{name='stale-proof-candidate';patch=@{PROOF_CANDIDATE=('f'*40)};reason='Full proof'},
    @{name='stale-proof-code';patch=@{PROOF_CODE=('f'*40)};reason='Full proof'},
    @{name='missing-proof';patch=@{PROOF_STATUS=''};reason='Full proof'},
    @{name='both-identities-empty';patch=@{PROOF_RUN_ID='';CURRENT_RUN_ID=''};reason='context'},
    @{name='unsupported-event';patch=@{CURRENT_EVENT_NAME='unhandled'};reason='event'}
)
foreach($g in $guardCases) {
    Check-Case ('final-'+$g.name) {
        $dir=Join-Path $base ('guard-'+$g.name);$null=New-Item -ItemType Directory -Path $dir
        $case=@{dir=$dir;env=@{OFFLINE_RESULT='success';OFFLINE_STATUS='complete';OFFLINE_RUN_ID='fixture';OFFLINE_ATTEMPT='1';OFFLINE_CANDIDATE=('a'*40);OFFLINE_CODE=('a'*40);NEEDS_RESULT='success';QUICK_RESULT='success';PROOF_STATUS='complete';PROOF_RUN_ID='fixture';PROOF_ATTEMPT='1';PROOF_CANDIDATE=('a'*40);PROOF_CODE=('a'*40);CURRENT_RUN_ID='fixture';CURRENT_ATTEMPT='1';CURRENT_SHA=('a'*40);CURRENT_EVENT_NAME='pull_request';CURRENT_DRAFT='false'}}
        foreach($key in $g.patch.Keys){$case.env[$key]=$g.patch[$key]}
        $r=Invoke-Block $case 'final'
        if($g.pass){Assert-That ($r.Exit -eq 0) "Valid final guard failed: $($r.Err)"}
        else{Assert-That ($r.Exit -ne 0 -and $r.Err.Contains($g.reason)) "Final guard should reject $($g.name) for '$($g.reason)', got $($r.Exit): $($r.Err)"}
    }
}
# Fixed-style checkout extraction, like Read-ActualBlock; not a YAML evaluator.
function Read-ActualCheckoutDepth([string]$Job) {
    $starts = @(for ($i=0; $i -lt $lines.Length; $i++) { if ($lines[$i] -ceq "  ${Job}:") { $i } })
    Assert-That ($starts.Count -eq 1) "Expected one actual job: $Job"
    $end=$lines.Length
    for ($i=$starts[0]+1; $i -lt $lines.Length; $i++) { if ($lines[$i] -match '^  \S') { $end=$i; break } }
    $body=$lines[$starts[0]..($end-1)] -join "`n"
    $checkouts=[regex]::Matches($body,'(?ms)^        uses: actions/checkout@(?<sha>[0-9a-f]{40})[^\n]*\n        with:\n(?<with>(?:          [^\n]*\n)+)')
    Assert-That ($checkouts.Count -eq 1) "Expected one fixed-style checkout in $Job"
    Assert-That ($checkouts[0].Groups['sha'].Value -ceq '3d3c42e5aac5ba805825da76410c181273ba90b1') 'Checkout action changed; recheck its default-depth contract.'
    $with=$checkouts[0].Groups['with'].Value
    foreach ($policy in @('persist-credentials','lfs')) {
        Assert-That ([regex]::Matches($with,"(?m)^          ${policy}:").Count -eq 1 -and $with -cmatch "(?m)^          ${policy}: false`$") "Checkout $policy policy changed in $Job"
    }
    $declared=[regex]::Matches($with,'(?m)^          fetch-depth:')
    $depth=[regex]::Matches($with,'(?m)^          fetch-depth: ([0-9]+)$')
    Assert-That ($declared.Count -le 1 -and $declared.Count -eq $depth.Count) "Unsupported checkout depth syntax in $Job"
    # The pinned action.yml defaults to 1. Exercise that actual bug, not an early grep rejection.
    if (-not $depth.Count) { return 1 }
    return [int]$depth[0].Groups[1].Value
}
function Invoke-OwnedGit($Case, [string]$Repository, [string[]]$Arguments) {
    $Case.gitCount=[int]$Case.gitCount+1
    $prefix=Join-Path $Case.env.RUNNER_TEMP ('owned-git-'+$Case.gitCount)
    $git=(Get-Command git -CommandType Application | Select-Object -First 1).Source
    & $git -C $Repository @Arguments 1> "$prefix.stdout" 2> "$prefix.stderr"
    $exit=$LASTEXITCODE
    Write-Json "$prefix.command.json" @{argv=@($git,'-C',$Repository)+$Arguments;exit=$exit}
    Assert-That ($exit -eq 0) "Owned fixture Git failed: $($Arguments -join ' '); see $prefix.stderr"
    return [IO.File]::ReadAllText("$prefix.stdout",$utf8).Trim()
}
function New-ShallowCandidateCase([string]$Name, [int]$Depth, [bool]$Draft) {
    $case=New-Case $Name 'success' $false; $case.gitCount=0; $seed=$case.dir
    foreach ($argv in @(@('init','--initial-branch=base'),@('config','user.name','Fixture'),@('config','user.email','fixture@example.invalid'),@('add','scripts'),@('commit','-m','Owned base'),@('checkout','-b','feature'))) { $null=Invoke-OwnedGit $case $seed $argv }
    Write-Utf8 (Join-Path $seed 'head.txt') 'owned head'
    foreach ($argv in @(@('add','head.txt'),@('commit','-m','Owned head'))) { $null=Invoke-OwnedGit $case $seed $argv }
    $head=Invoke-OwnedGit $case $seed @('rev-parse','HEAD')
    $null=Invoke-OwnedGit $case $seed @('checkout','base'); Write-Utf8 (Join-Path $seed 'base.txt') 'owned base update'
    foreach ($argv in @(@('add','base.txt'),@('commit','-m','Owned base update'))) { $null=Invoke-OwnedGit $case $seed $argv }
    $parent=Invoke-OwnedGit $case $seed @('rev-parse','HEAD')
    $null=Invoke-OwnedGit $case $seed @('merge','--no-ff',$head,'-m','Owned merge')
    $merge=Invoke-OwnedGit $case $seed @('rev-parse','HEAD'); $tree=Invoke-OwnedGit $case $seed @('show','-s','--format=%T','HEAD')
    $sourceParents=@((Invoke-OwnedGit $case $seed @('show','-s','--format=%P','HEAD')) -split '\s+' | Where-Object { $_ })
    Assert-That ($sourceParents.Count -eq 2 -and $sourceParents[0] -ceq $parent -and $sourceParents[1] -ceq $head) 'Owned source must be a real ordered base/head merge before testing shallow checkout.'
    $clone=Join-Path $seed 'checkout'; $uri=([uri]($seed+[IO.Path]::DirectorySeparatorChar)).AbsoluteUri
    $null=Invoke-OwnedGit $case $seed @('clone','--no-local','--depth',([string]$Depth),'--branch','base',$uri,$clone)
    Assert-That ((Invoke-OwnedGit $case $clone @('rev-parse','HEAD')) -ceq $merge) 'Shallow clone changed the candidate.'
    Assert-That ((Invoke-OwnedGit $case $clone @('rev-parse','--is-shallow-repository')) -ceq 'true') 'Fixture must stay shallow, not silently fetch all history.'
    Write-Json (Join-Path $seed 'expected-history.json') @{checkout=$merge;tree=$tree;base=$parent;head=$head;parents=@($parent,$head);depth=$Depth}
    $event=Join-Path $seed 'fixture-event.json'; Write-Json $event @{action='synchronize';pull_request=@{number=39;draft=$Draft;head=@{sha=$head};base=@{sha=$parent};merge_commit_sha=$merge}}
    $case.dir=$clone; $case.env.GITHUB_WORKSPACE=$clone; $case.env.CI_EVENT_PATH=$event
    $case.env.CI_SHA=$merge; $case.env.CI_EVENT_NAME='pull_request'; $case.env.CI_PR_NUMBER='39'
    $case.env.CI_WORKFLOW_REF='fixture/repo/.github/workflows/ci.yml@refs/pull/39/merge'
    $case.env.GITHUB_REPOSITORY='fixture/repo'; $case.env.CI_API_URL='https://fixture.invalid'; $case.env.CI_TOKEN='explicit-nonsecret-fixture'
    $case.env.FIXTURE_HEAD=$head; $case.env.FIXTURE_BASE=$parent; $case.env.FIXTURE_DRAFT=$Draft.ToString()
    return @{case=$case;checkout=$merge;tree=$tree;parents=@($parent,$head)}
}
$ownedApi=@'
function Invoke-RestMethod {
    param($Method,$Uri,$Headers)
    if ($Method -cne 'Get' -or $Uri -cne 'https://fixture.invalid/repos/fixture/repo/pulls/39' -or $Headers.Authorization -cne 'Bearer explicit-nonsecret-fixture') { throw 'Only the owned API fixture may be called.' }
    return [pscustomobject]@{head=[pscustomobject]@{sha=$env:FIXTURE_HEAD};base=[pscustomobject]@{sha=$env:FIXTURE_BASE};merge_commit_sha=$env:CI_SHA;draft=[bool]::Parse($env:FIXTURE_DRAFT)}
}
'@
Assert-Parse $ownedApi
foreach ($job in @('quick-unit','engineering-offline','full-gate')) {
    Check-Case "actual-$job-shallow-candidate-chain" {
        $depth=Read-ActualCheckoutDepth $job
        $fixture=New-ShallowCandidateCase "shallow-$job" $depth ($job -ceq 'quick-unit')
        $block=if ($job -ceq 'quick-unit') { 'quick' } elseif ($job -ceq 'engineering-offline') { 'offlineStart' } else { 'start' }
        $result=Invoke-Block $fixture.case $block $ownedApi
        Assert-That ($result.Exit -eq 0) "Actual $job candidate failed at configured depth ${depth}: $($result.Err)"
        $proof=if ($job -ceq 'quick-unit') { 'quick-candidate-proof' } elseif ($job -ceq 'engineering-offline') { 'offline-start-proof' } else { 'candidate-proof' }
        $identity=Get-Content (Join-Path $fixture.case.env.RUNNER_TEMP "$proof/candidate-identity.json") -Raw | ConvertFrom-Json -NoEnumerate
        Assert-That ($identity.parents -is [array] -and $identity.parents.Count -eq 2 -and $identity.parents[0] -ceq $fixture.parents[0] -and $identity.parents[1] -ceq $fixture.parents[1]) 'Actual public producer/consumer did not retain the ordered base/head parents.'
        Assert-That ($identity.actualCheckout -ceq $fixture.checkout -and $identity.candidateSha -ceq $fixture.checkout -and $identity.tree -ceq $fixture.tree) 'Actual public identity changed checkout/candidate/tree.'
        Assert-That ($depth -eq 2) 'Checkout must fetch only the required direct parent layer.'
        if ($job -ceq 'engineering-offline') {
            Write-OfflineJournal $fixture.case
            $end=Invoke-Block $fixture.case 'offlineEnd' $ownedApi
            Assert-That ($end.Exit -eq 0) "Actual offline end candidate failed: $($end.Err)"
            $proofResult=Invoke-Block $fixture.case 'offlineProof'
            Assert-That ($proofResult.Exit -eq 0) "Actual offline candidate/journal/proof chain failed: $($proofResult.Err)"
        }
    }
}
Check-Case 'depth-one-candidate-rejects-for-ordered-parent-loss' {
    $fixture=New-ShallowCandidateCase 'shallow-negative-depth-one' 1 $true
    $result=Invoke-Block $fixture.case 'quick' $ownedApi
    Assert-That ($result.Exit -ne 0 -and $result.Err.Contains('[candidate:2] PR checkout/head/base/merge candidate identity does not match ordered git history.')) 'Depth-one control did not fail for the actual remote parent-loss reason.'
    $snapshot=Get-Content (Join-Path $fixture.case.env.RUNNER_TEMP 'quick-candidate-input/snapshot.json') -Raw | ConvertFrom-Json -NoEnumerate
    Assert-That ($snapshot.parents -is [array] -and $snapshot.parents.Count -eq 0 -and $snapshot.actualCheckout -ceq $fixture.checkout) 'Depth-one control did not preserve the original empty-parent snapshot.'
    Assert-That (-not(Test-Path -LiteralPath (Join-Path $fixture.case.env.RUNNER_TEMP 'quick-candidate-proof/candidate-identity.json'))) 'Rejected shallow checkout emitted a success identity.'
}
Write-Json (Join-Path $base 'summary.json') @{synthetic=$true;results=@($results);count=$results.Count;failed=$failures.Count;noSdkOrEngine=$true}
if ($failures.Count) { throw "Actual workflow regression failed ($($failures.Count)):`n$($failures -join "`n")`nEvidence: $base" }
Write-Output "PASS actual workflow M0/cost/FullProof blocks: $($results.Count) cases; synthetic fixtures only; evidence=$base"
