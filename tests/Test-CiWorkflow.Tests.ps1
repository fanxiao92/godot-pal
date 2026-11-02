[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$root = Split-Path $PSScriptRoot -Parent
$workflow = Join-Path $root '.github/workflows/ci.yml'
$text = [IO.File]::ReadAllText($workflow, $utf8).Replace("`r`n", "`n")
$base = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('unit-workflow-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path $base) { throw 'EvidenceDirectory must be new.' }
$null = New-Item -ItemType Directory $base
function Assert([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Put([string]$Path, [string]$Value) { [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false)) }
function Json([string]$Path, $Value) { Put $Path (ConvertTo-Json -InputObject $Value -Depth 40) }
# Fail-closed repository block extractor, not a YAML/expression/Actions scheduler.
function Block([string]$Name) {
    $matches = [regex]::Matches($text, '(?ms)^      - name: ' + [regex]::Escape($Name) + '\n(?:(?!^      - name: ).)*?^        run: \|\n(?<code>(?:          [^\n]*\n|\n)+)')
    Assert ($matches.Count -eq 1) "Expected one actual workflow block: $Name"
    $code = ($matches[0].Groups['code'].Value -split "`n" | ForEach-Object { if ($_.StartsWith('          ')) { $_.Substring(10) } else { $_ } }) -join "`n"
    $tokens=$null; $errors=$null; $null=[Management.Automation.Language.Parser]::ParseInput($code,[ref]$tokens,[ref]$errors)
    Assert ($errors.Count -eq 0 -and -not $code.Contains('${{')) "Actual block cannot parse: $Name"
    Put (Join-Path $base ($Name.Replace(' ','-')+'.ps1')) $code
    return $code
}
Assert ($text -match '(?m)^  unit-check:$') 'Missing independent unit-check job'
$start = Block 'Validate Unit start candidate'
$run = Block 'Run all approved Unit projects'
$end = Block 'Validate Unit end candidate and evidence'
$legacy = Block 'Block legacy full acceptance'
Json (Join-Path $base 'source.json') @{workflow=$workflow;sha256=(Get-FileHash $workflow).Hash;synthetic=$true}
Assert ($text -match 'types: \[opened, synchronize, reopened, ready_for_review, converted_to_draft, edited\]') 'PR event matrix changed'
Assert ($text -match 'branches: \[main\]' -and $text -match '(?m)^  workflow_dispatch:') 'Missing main/manual events'
Assert ($text -notmatch 'paths:|paths-ignore:|--filter|continue-on-error|Get-Godot|Get-GdUnit|Test-Engineering|Test-M0|Test-Unit.ps1|quick-unit:|engineering-offline:|full-gate:') 'Unit workflow includes narrowing or heavy execution'
$unit = [regex]::Match($text,'(?ms)^  unit-check:\n(?<body>.*?)(?=^  full-check:)').Groups['body'].Value
Assert ($unit -and $unit -notmatch '(?m)^    if:|(?m)^    needs:' -and $unit -match 'timeout-minutes: 20') 'Unit job must run for every declared event within existing timeout'
Assert ($unit -match 'fetch-depth: 2' -and $unit -match 'persist-credentials: false' -and $unit -match 'lfs: false' -and $unit -match 'global-json-file: global.json') 'Pinned checkout/SDK policy changed'
Assert ($unit.Contains('name: ci-unit-evidence-${{ github.run_id }}-${{ github.run_attempt }}') -and $unit.Contains('if: always()') -and $unit.Contains('if-no-files-found: error')) 'Evidence must upload by run/attempt and fail if absent'
foreach ($path in @('unit-evidence/**','unit-start/**','unit-end/**','original-event.json')) { Assert ($unit.Contains('/'+$path)) "Missing evidence upload: $path" }
foreach ($binding in @('CI_EVENT_PATH: ${{ github.event_path }}','CI_SHA: ${{ github.sha }}','CI_RUN_ID: ${{ github.run_id }}','CI_RUN_ATTEMPT: ${{ github.run_attempt }}','CI_WORKFLOW_REF: ${{ github.workflow_ref }}')) { Assert ($unit.Contains($binding)) "Missing actual context binding: $binding" }
# A native executable at the SDK process boundary, not an alternate business/Unit implementation.
# It is built once with the approved SDK, no test packages or Godot. Its outputs are explicitly synthetic.
$shim = Join-Path $base 'sdk-fixture'; $null=New-Item -ItemType Directory $shim
[IO.File]::Copy((Join-Path $root 'global.json'),(Join-Path $shim 'global.json'))
Put (Join-Path $shim 'Fixture.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
Put (Join-Path $shim 'Program.cs') @'
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
class Program {
 static int Main(string[] args) {
  if (args.SequenceEqual(new[]{"--version"})) { Console.WriteLine("10.0.403"); return 0; }
  // Reject discovery/filter/no-build/solution variants at the native process boundary.
  if(args.Length!=8 || args[0]!="test" || args[2]!="--configuration" || args[3]!="Debug" || args[4]!="--logger" || args[5]!="trx;LogFileName=results.trx" || args[6]!="--results-directory") { Console.Error.WriteLine("unexpected argv"); return 91; }
  var name=args[1].Contains("Pal.Core.")?"core":"cli-unit";
  File.AppendAllText(Environment.GetEnvironmentVariable("FIXTURE_CALLS"),name+"\n");
  var mode=Environment.GetEnvironmentVariable("FIXTURE_TARGET")==name?Environment.GetEnvironmentVariable("FIXTURE_MODE"):"pass";
  if(mode=="seven") { Console.Error.WriteLine("中文 original exit seven"); return 7; }
  if(mode=="logged-error") { Console.Error.WriteLine("error MSB1000: controlled"); }
  if(mode=="missing") return 0;
  var ns=(XNamespace)"http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
  var stamp=DateTimeOffset.UtcNow.ToString("O"); var execution=Guid.NewGuid().ToString();
  var assembly=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[1]),"bin/Debug/net10.0",Path.GetFileNameWithoutExtension(args[1])+".dll"));
  var counters=new XElement(ns+"Counters");
  foreach(var c in new[]{"total","executed","passed","failed","error","timeout","aborted","inconclusive","passedButRunAborted","notRunnable","notExecuted","disconnected","warning","completed","inProgress","pending"}) counters.SetAttributeValue(c,new[]{"total","executed","passed"}.Contains(c)?1:0);
  var doc=new XElement(ns+"TestRun",new XAttribute("id",Guid.NewGuid()),
   new XElement(ns+"Times",new[]{"creation","queuing","start","finish"}.Select(t=>new XAttribute(t,stamp))),
   new XElement(ns+"Results",new XElement(ns+"UnitTestResult",new XAttribute("executionId",execution),new XAttribute("testId","case"),new XAttribute("testName","Fixture.Pass"),new XAttribute("outcome",mode=="skip"?"NotExecuted":"Passed"))),
   new XElement(ns+"TestDefinitions",new XElement(ns+"UnitTest",new XAttribute("id","case"),new XAttribute("name","Fixture.Pass"),new XElement(ns+"Execution",new XAttribute("id",execution)),new XElement(ns+"TestMethod",new XAttribute("codeBase",assembly),new XAttribute("className","Fixture"),new XAttribute("name","Pass")))),
   new XElement(ns+"TestEntries",new XElement(ns+"TestEntry",new XAttribute("testId","case"),new XAttribute("executionId",execution))),
   new XElement(ns+"ResultSummary",new XAttribute("outcome",mode=="aborted"?"Aborted":"Completed"),counters,new XElement(ns+"RunInfos")));
  if(mode=="zero") { doc.Element(ns+"Results").RemoveNodes(); doc.Element(ns+"TestDefinitions").RemoveNodes(); doc.Element(ns+"TestEntries").RemoveNodes(); }
  Directory.CreateDirectory(args[7]); doc.Save(Path.Combine(args[7],"results.trx"));
  Console.WriteLine("Synthetic project output"); return 0;
 }
}
'@
$dotnet=(Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
& $dotnet build (Join-Path $shim 'Fixture.csproj') --configuration Debug --nologo 1> (Join-Path $base 'shim-build.stdout') 2> (Join-Path $base 'shim-build.stderr')
$buildExit=$LASTEXITCODE; Json (Join-Path $base 'shim-build.command.json') @{executable=$dotnet;exitCode=$buildExit;project='sdk-fixture/Fixture.csproj'}
Assert ($buildExit -eq 0) 'Synthetic native SDK fixture did not build; stop without retry.'
$shimExe=Join-Path $shim 'bin/Debug/net10.0/Fixture.exe'
Assert (Test-Path $shimExe) 'Native SDK fixture requires the approved Windows environment.'
$pwsh=(Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
$git=(Get-Command git -CommandType Application | Select-Object -First 1).Source
function G([string]$Repo,[string[]]$Argv) {
    $output=@(& $git -C $Repo @Argv) -join "`n"; Assert ($LASTEXITCODE -eq 0) "Owned Git failed: $Argv"; return $output.Trim()
}
$fixture=Join-Path $base 'owned-repository'; $null=New-Item -ItemType Directory $fixture
$null=New-Item -ItemType Directory (Join-Path $fixture 'scripts')
foreach($file in @('Invoke-CiUnit.ps1','GameTestBatches.ps1','TestRunDefinitions.ps1','New-CiCandidateInputs.ps1','Test-CiCandidate.ps1')) {
    [IO.File]::Copy((Join-Path $root "scripts/$file"),(Join-Path $fixture "scripts/$file"))
    Assert ((Get-FileHash "$root/scripts/$file").Hash -ceq (Get-FileHash "$fixture/scripts/$file").Hash) 'Fixture must use exact production bytes'
}
$null=G $fixture @('init','--quiet','--initial-branch=main'); $null=G $fixture @('config','user.name','Owned fixture'); $null=G $fixture @('config','user.email','fixture@example.invalid')
$null=G $fixture @('add','.'); $null=G $fixture @('commit','--quiet','-m','base'); $parent=G $fixture @('rev-parse','HEAD')
$null=G $fixture @('checkout','--quiet','-b','topic'); Put "$fixture/head.txt" 'owned head'; $null=G $fixture @('add','.'); $null=G $fixture @('commit','--quiet','-m','head'); $head=G $fixture @('rev-parse','HEAD')
$null=G $fixture @('checkout','--quiet','main'); Put "$fixture/base.txt" 'owned base'; $null=G $fixture @('add','.'); $null=G $fixture @('commit','--quiet','-m','base update'); $parent=G $fixture @('rev-parse','HEAD')
$null=G $fixture @('merge','--quiet','--no-ff','topic','-m','merge candidate'); $sha=G $fixture @('rev-parse','HEAD')
$api=@'
function Invoke-RestMethod {
 param($Method,$Uri,$Headers)
 if ($Method -cne 'Get' -or $Uri -cne 'https://fixture.invalid/repos/fixture/repo/pulls/42') { throw 'Unexpected external API request' }
 [pscustomobject]@{head=[pscustomobject]@{sha=$env:FIXTURE_HEAD};base=[pscustomobject]@{sha=$env:FIXTURE_BASE};merge_commit_sha=$env:FIXTURE_SHA;draft=[bool]::Parse($env:FIXTURE_DRAFT)}
}
function Get-Command {
 param($Name,$CommandType,$ErrorAction)
 if ($Name -ceq 'dotnet') { [pscustomobject]@{Source=$env:FIXTURE_EXE} } else { Microsoft.PowerShell.Core\Get-Command -Name $Name -CommandType $CommandType -ErrorAction Stop }
}
'@
function Invoke-Block($Case,[string]$Name,[string]$Code) {
    $file=Join-Path $Case.dir "$Name.ps1"; Put $file ($api+"`n"+$Code)
    $tokens=$null; $errors=$null; $null=[Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)
    Assert ($errors.Count -eq 0) 'Generated block host must parse before execution'
    $psi=[Diagnostics.ProcessStartInfo]::new($pwsh); $psi.WorkingDirectory=$fixture; $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    foreach($arg in @('-NoProfile','-File',$file)) { $psi.ArgumentList.Add($arg) }
    foreach($key in $Case.env.Keys) { $psi.Environment[$key]=[string]$Case.env[$key] }
    $p=[Diagnostics.Process]::new(); $p.StartInfo=$psi
    $stdout=[IO.File]::Open((Join-Path $Case.dir "$Name.stdout"),[IO.FileMode]::CreateNew); $stderr=[IO.File]::Open((Join-Path $Case.dir "$Name.stderr"),[IO.FileMode]::CreateNew)
    try {
        Assert ($p.Start()) 'Could not start owned block host'
        $a=$p.StandardOutput.BaseStream.CopyToAsync($stdout); $b=$p.StandardError.BaseStream.CopyToAsync($stderr)
        $all=[Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($p.WaitForExitAsync(),$a,$b))
        Assert ($all.Wait(600000)) 'Owned regression timed out; retain evidence and stop'
        $nativeExit=$p.ExitCode
    } finally { $stdout.Dispose(); $stderr.Dispose(); $p.Dispose() }
    Assert ($nativeExit -is [int]) 'Native block exit must remain a numeric integer, not the string Code parameter'
    $commandPath=Join-Path $Case.dir "$Name.command.json"
    Json $commandPath @{argv=@($pwsh,'-NoProfile','-File',$file);exitCode=$nativeExit;synthetic=$true}
    $record=Get-Content -LiteralPath $commandPath -Raw | ConvertFrom-Json
    Assert ($record.exitCode -is [long] -and $record.exitCode -eq $nativeExit) 'Saved block exit must be a JSON number'
    return @{exit=$nativeExit;err=[IO.File]::ReadAllText((Join-Path $Case.dir "$Name.stderr"),$utf8)}
}
function New-Case([string]$Name,[string]$Event='workflow_dispatch',[bool]$Draft=$false) {
    $dir=Join-Path $base $Name; $null=New-Item -ItemType Directory $dir
    $payload=if($Event -eq 'pull_request') { @{action='synchronize';pull_request=@{number=42;draft=$Draft}} } elseif($Event -eq 'push') { @{ref='refs/heads/main'} } else { @{inputs=$null} }
    Json "$dir/event.json" $payload
    return @{dir=$dir;env=@{RUNNER_TEMP=$dir;GITHUB_WORKSPACE=$fixture;CI_EVENT_PATH="$dir/event.json";CI_EVENT_NAME=$Event;CI_SHA=$sha;CI_RUN_ID='owned-unit';CI_RUN_ATTEMPT='1';CI_WORKFLOW_REF='fixture/.github/workflows/ci.yml@refs/heads/main';CI_API_URL='https://fixture.invalid';CI_PR_NUMBER='42';GITHUB_REPOSITORY='fixture/repo';CI_TOKEN='nonsecret-owned-fixture';FIXTURE_HEAD=$head;FIXTURE_BASE=$parent;FIXTURE_SHA=$sha;FIXTURE_DRAFT=$Draft.ToString();FIXTURE_EXE=$shimExe;FIXTURE_CALLS="$dir/calls.txt";FIXTURE_TARGET='core';FIXTURE_MODE='pass'}}
}
$count=0
foreach($entry in @(@{name='draft';event='pull_request';draft=$true},@{name='ready';event='pull_request';draft=$false},@{name='push';event='push';draft=$false},@{name='manual';event='workflow_dispatch';draft=$false})) {
    $case=New-Case $entry.name $entry.event $entry.draft
    foreach($b in @(@{n='start';c=$start},@{n='run';c=$run},@{n='end';c=$end})) {
        $r=Invoke-Block $case $b.n $b.c; Assert ($r.exit -eq 0) "Actual $($entry.name)/$($b.n) failed: $($r.err)"
    }
    Assert ([IO.File]::ReadAllText("$($case.dir)/calls.txt").Replace("`r",'') -ceq "core`ncli-unit`n") 'Must execute exactly both full projects in order'
    $report=Get-Content "$($case.dir)/unit-evidence/unit-run.json" -Raw | ConvertFrom-Json
    # Regression for native Git resolution: the actual public executor must get past initial Git,
    # bind real source bytes, and reach the native SDK commands without same-name recursion.
    Assert ($report.source.Count -ge 5 -and $report.commands.Count -eq 3 -and $report.commands[0].name -ceq 'sdk') 'Executor did not complete native Git/source-to-SDK transition'
    Assert (($report.artifacts.path -ccontains 'core/results.trx') -and ($report.artifacts.path -ccontains 'cli-unit/results.trx')) 'Cross-platform artifact keys must use forward slash'
    Assert ($report.warningProjectMs -eq 300000 -and $report.warningRoundMs -eq 600000 -and $report.commandTimeoutMs -eq 600000 -and $null -eq $report.restoreMs -and $null -eq $report.buildMs -and $report.wallClockMs -gt 0) 'Cost/budget fields changed or unknown phases became zero'
    $count++
}
foreach($mode in @('seven','missing','skip','zero','aborted','logged-error','unknown-launch')) {
    $case=New-Case "stop-$mode"; $case.env.FIXTURE_MODE=$mode
    $r=Invoke-Block $case 'start' $start; Assert ($r.exit -eq 0) 'Control start failed'
    if($mode -eq 'unknown-launch') { $case.env.FIXTURE_EXE="$base/missing-owned.exe" }
    $r=Invoke-Block $case 'run' $run
    $expected=if($mode -eq 'seven'){7}else{1}
    Assert ($r.exit -eq $expected) "Wrong original failure for $mode : $($r.exit): $($r.err)"
    $report=Get-Content "$($case.dir)/unit-evidence/unit-run.json" -Raw | ConvertFrom-Json
    Assert ($report.status -cne 'completed' -and $report.projects[1].status -ceq 'not-run') "Failure did not stop remaining project: $mode"
    if($mode -eq 'unknown-launch') { Assert ($report.commands[0].exitKnown -ceq $false -and $null -eq $report.commands[0].exitCode) 'Unknown launch reused stale exit zero' }
    elseif($mode -eq 'seven') { Assert ($r.err.Contains('original exit 7') -and ([IO.File]::ReadAllText("$($case.dir)/unit-evidence/core.stderr.log").Contains('中文 original exit seven'))) 'Original numeric/UTF8 failure lost' }
    elseif($mode -eq 'missing') { Assert ($r.err -match 'results.trx') 'Missing TRX rejected for unrelated failure' }
    elseif($mode -eq 'logged-error') { Assert ($r.err.Contains('Failure diagnostic')) 'Logged-error rejection changed' }
    else { Assert ($r.err -match '\[unit-trx\]|Incomplete TRX identity graph') 'Invalid TRX rejected for unrelated failure' }
    $count++
}
# A later failure cannot be spliced with the earlier project's PASS.
$case=New-Case 'second-project-seven'; $case.env.FIXTURE_TARGET='cli-unit'; $case.env.FIXTURE_MODE='seven'
$r=Invoke-Block $case 'start' $start; Assert ($r.exit -eq 0) 'Control start failed'
$r=Invoke-Block $case 'run' $run; Assert ($r.exit -eq 7) 'Second project failure hidden'
$report=Get-Content "$($case.dir)/unit-evidence/unit-run.json" -Raw|ConvertFrom-Json
Assert ($report.projects[0].status -ceq 'completed' -and $report.status -cne 'completed') 'Partial project pass became whole success'; $count++
foreach($fault in @('tamper','missing-log','stale-run','stale-attempt','stale-candidate','partial-report','missing-report','changed-api','missing-source','source-bytes','typed-completion')) {
    $case=New-Case "end-$fault" 'pull_request' $false
    foreach($b in @(@{n='start';c=$start},@{n='run';c=$run})) { $r=Invoke-Block $case $b.n $b.c; Assert ($r.exit -eq 0) 'End control setup failed' }
    $rp="$($case.dir)/unit-evidence/unit-run.json"; $report=Get-Content $rp -Raw|ConvertFrom-Json -DateKind String
    switch($fault) {
        'tamper' { Add-Content "$($case.dir)/unit-evidence/core/results.trx" 'tamper' }
        'missing-log' { Remove-Item "$($case.dir)/unit-evidence/core.stdout.log" }
        'stale-run' { $case.env.CI_RUN_ID='old-run' }
        'stale-attempt' { $case.env.CI_RUN_ATTEMPT='2' }
        'stale-candidate' { $case.env.CI_SHA=('f'*40) }
        'partial-report' { $report.projects=@($report.projects[0]); Json $rp $report }
        'missing-report' { Remove-Item $rp }
        'changed-api' { $case.env.FIXTURE_SHA=('e'*40) }
        'missing-source' { $report.PSObject.Properties.Remove('source'); Json $rp $report }
        'source-bytes' { Put "$fixture/head.txt" 'controlled source change' }
        'typed-completion' { $report.commands[0].exitKnown='True'; Json $rp $report }
    }
    $r=Invoke-Block $case 'end' $end
    Assert ($r.exit -ne 0) "Actual end accepted $fault"
    $reason=switch($fault) {
        'missing-log' { 'Unit original artifact missing: core.stdout.log' }
        'missing-report' { 'Unit original report missing.' }
        default { 'Unit candidate/evidence|Missing complete approved Unit|Unit original artifact|candidate:|Unit source|Missing Unit source|Incomplete Unit command' }
    }
    if($fault -in @('missing-log','missing-report')) { Assert ($r.err.Contains($reason)) "End rejected $fault without its specific missing-evidence diagnostic: $($r.err)" }
    else { Assert ($r.err -match $reason) "End rejected $fault for unexpected diagnostic: $($r.err)" }
    if($fault -eq 'source-bytes') { Put "$fixture/head.txt" 'owned head' }
    $count++
}
$case=New-Case 'legacy-block'; $r=Invoke-Block $case 'legacy' $legacy
Assert ($r.exit -ne 0 -and $r.err.Contains('Unit-only is not full-check')) 'Legacy full-check must explicitly refuse'; $count++
Json (Join-Path $base 'summary.json') @{count=$count;failed=0;synthetic=$true;realGit=$true;nativeSdkFixture=$true;remoteActions=$false}
Write-Output "PASS actual Unit workflow: $count owned contract cases; native SDK fixture, no product/Godot/full acceptance."
