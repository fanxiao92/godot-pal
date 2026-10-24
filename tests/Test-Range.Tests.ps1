# Public range CLI against real, minimal SDK/xUnit fixtures; no engine or package bootstrap.
[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$entry = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../scripts/Test-Range.ps1'))
$hostPath = [Environment]::ProcessPath
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$evidence = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('range-evidence-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new range regression evidence directory.' }
$null = [IO.Directory]::CreateDirectory($evidence)
$root = Join-Path ([IO.Path]::GetTempPath()) ('range 人工 空格 ' + [guid]::NewGuid().ToString('N'))
$records = [Collections.Generic.List[object]]::new(); $completed = $false
$oldPath = $env:PATH; $oldDotnetRoot = $env:DOTNET_ROOT
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Put([string]$Relative, [string]$Text) { $file = Join-Path $root $Relative; $null = [IO.Directory]::CreateDirectory((Split-Path $file -Parent)); [IO.File]::WriteAllText($file, $Text, $utf8) }
function Q([string]$Text) { "'" + $Text.Replace("'", "''") + "'" }
function Check([string]$Name, [int]$ExpectedExit, [string]$Diagnostic, [string[]]$Selection = @('cli-integration'), [string]$Filter, [string[]]$Batches = @(), [string]$ExistingOutput, [string]$Probe, [switch]$EngineFixture) {
    $number = $records.Count + 1
    $output = if ($ExistingOutput) { $ExistingOutput } else { Join-Path $evidence "run-$number" }
    $scopeArray = @($Selection | ForEach-Object { Q $_ }) -join ','
    $code = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(`$false); "
    if ($Probe) {
        $code += '$global:fixtureProbeMode=' + (Q $Probe) + '; '
        $code += @'
function Get-CimInstance { param([string]$Query)
    if ($Query -cne "SELECT ProcessId, Name FROM Win32_Process WHERE Name LIKE 'godot%.exe'") { throw 'Probe requested more than approved PID/Name query' }
    if ($global:fixtureProbeMode -in @('unavailable','forbidden')) { throw 'Synthetic unavailable/forbidden probe' }
    if ($global:fixtureProbeMode -eq 'conflict') { [pscustomobject]@{ ProcessId=1234; Name='godot-owned-fixture.exe' } }
}
function Get-Process { param([string]$Name)
    if ($Name -cne '*godot*') { throw 'Probe requested an unexpected name' }
    if ($global:fixtureProbeMode -in @('unavailable','forbidden')) { throw 'Synthetic unavailable/forbidden probe' }
    if ($global:fixtureProbeMode -eq 'conflict') { [pscustomobject]@{ Id=1234; Name='godot-owned-fixture' } }
}
'@
        $code += '; '
    }
    $code += "& $(Q $entry) -RepositoryRoot $(Q $root) -OutputDirectory $(Q $output) -Scope @($scopeArray)"
    if ($EngineFixture) { $code += ' -GodotPath ' + (Q (Join-Path $root 'fake-godot.exe')) + ' -FixedPackageDirectory ' + (Q (Join-Path $root 'fake-bundle')) }
    if ($PSBoundParameters.ContainsKey('Filter')) { $code += ' -Filter ' + (Q $Filter) }
    if ($Batches.Count) { $code += ' -GameBatch @(' + (@($Batches | ForEach-Object { Q $_ }) -join ',') + ')' }
    $code += '; exit $LASTEXITCODE'
    $tokens = $null; $errors = $null; $null = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    Assert (-not $errors.Count) 'Generated public invocation must parse before execution.'
    # -EncodedCommand serializes host streams as CLIXML; mixing those with a
    # target's raw Console.Error corrupts XML decoding in the receiving pwsh.
    # A pre-parsed owned -File wrapper transports the same real array, as text.
    $wrapper = Join-Path $evidence "$number.invoke.ps1"
    [IO.File]::WriteAllText($wrapper, $code, $utf8)
    $null = [Management.Automation.Language.Parser]::ParseFile($wrapper, [ref]$tokens, [ref]$errors)
    Assert (-not $errors.Count) 'Generated invocation file must parse before execution.'
    $stdout = Join-Path $evidence "$number.stdout.log"; $stderr = Join-Path $evidence "$number.stderr.log"
    Push-Location ([IO.Path]::GetTempPath())
    try { & $hostPath -NoProfile -File $wrapper 1> $stdout 2> $stderr; $actual = $LASTEXITCODE } finally { Pop-Location }
    [IO.File]::WriteAllText((Join-Path $evidence "$number.exit"), [string]$actual, $utf8)
    $text = [IO.File]::ReadAllText($stdout, [Text.UTF8Encoding]::new($false, $true)) + [IO.File]::ReadAllText($stderr, [Text.UTF8Encoding]::new($false, $true))
    $records.Add(@{ name = $Name; executable = $hostPath; arguments = @('-NoProfile','-File',$wrapper); code = $code; exitCode = $actual; expectedExit = $ExpectedExit; outputDirectory = $output })
    [IO.File]::WriteAllText((Join-Path $evidence 'commands.json'), (ConvertTo-Json -InputObject $records.ToArray() -Depth 8), $utf8)
    Assert ($actual -eq $ExpectedExit -and $text.Contains($Diagnostic)) "${Name}: expected $ExpectedExit/$Diagnostic, got $actual/$text"
    Write-Host "PASS $Name"
    return $output
}
function Report([string]$Output) { Get-Content -LiteralPath (Join-Path $Output 'range-run.json') -Raw -Encoding utf8 | ConvertFrom-Json }
try {
    Put 'global.json' '{"sdk":{"version":"10.0.402","rollForward":"latestPatch","allowPrerelease":false}}'
    Put 'NuGet.Config' '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>'
    Put 'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1"/><PackageReference Include="xunit" Version="2.9.3"/><PackageReference Include="xunit.runner.visualstudio" Version="3.1.4"/></ItemGroup></Project>'
    $source = @'
using Xunit;
namespace RangeFixture {
public class Cases {
    [Theory][InlineData("甲")][InlineData("乙")]
    public void SameMethod(string text) { Assert.NotEmpty(text); }
    [Fact] public void Other() { Assert.True(true); }
    public static System.Collections.Generic.IEnumerable<object[]> Values => new[] {
        new object[] { new byte[] {1,2,3,4,5,6,7,8,9,10} },
        new object[] { new byte[] {1,2,3,4,5,6,7,8,9,11} }
    };
    [Theory][MemberData(nameof(Values))] public void Bytes(byte[] data) { Assert.Equal(10, data.Length); }
}}
namespace NotRangeFixture {
public class Cases {
    [Fact] public void Other() { System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory,"unselected.marker"),"executed"); }
}}
'@
    Put 'tests/Pal.Extract.IntegrationTests/Cases.cs' $source
    Put 'tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj' '<Project/>'
    Put 'fake-godot.exe' 'Owned non-executable prerequisite fixture: must never be launched.'
    $null = [IO.Directory]::CreateDirectory((Join-Path $root 'fake-bundle'))
    $null = Check 'empty scope rejects before SDK' 1 '[range:selection]' -Selection @()
    $null = Check 'unknown scope rejects before SDK' 1 '[range:selection]' -Selection @('unit')
    $null = Check 'duplicate scope rejects' 1 '[range:selection]' -Selection @('cli-integration','cli-integration')
    $null = Check 'empty scope name rejects' 1 '[range:selection]' -Selection @('')
    $null = Check 'explicit empty filter rejects' 1 '[range:filter-syntax]' -Filter ''
    $null = Check 'Boolean filter grammar rejects' 1 '[range:filter-syntax]' -Filter 'FullyQualifiedName=A|FullyQualifiedName=B'
    $noPrepare = Check 'Prepare filter rejects for its own reason before prerequisites/build/output' 1 '[range:filter-scope]' -Selection @('prepare') -Filter 'FullyQualifiedName=Nope'
    Assert (-not (Test-Path -LiteralPath $noPrepare)) 'Selection error created an output directory.'
    $null = Check 'Game requires selected batches' 1 '[range:batch-scope]' -Selection @('game')
    $null = Check 'unknown Game batch rejects' 1 '[range:selection]' -Selection @('game') -Batches @('unknown')
    $null = Check 'duplicate Game batch rejects' 1 '[range:selection]' -Selection @('game') -Batches @('process','process')
    $null = Check 'batch on CLI rejects' 1 '[range:batch-scope]' -Batches @('process')
    $blockedProbe = Check 'existing Godot stops before bootstrap SDK or engine' 1 '[range:process-conflict]' -Selection @('prepare') -EngineFixture -Probe conflict
    $probeReport = Report $blockedProbe
    Assert ($probeReport.commands.Count -eq 0 -and $null -eq $probeReport.preparation -and $probeReport.godotPreflight.processes.Count -eq 1) 'Conflict did not stop work or retain PID/Name-only evidence.'
    $unknownProbe = Check 'unknown global process inventory fails closed before work' 1 '[range:process-preflight]' -Selection @('prepare') -EngineFixture -Probe unavailable
    Assert ((Report $unknownProbe).commands.Count -eq 0) 'Unknown inventory executed SDK.'
    $clearProbe = Check 'empty probe proceeds only to real next prerequisite' 1 '[range:prerequisite]' -Selection @('prepare') -EngineFixture -Probe clear
    Assert ((Report $clearProbe).godotPreflight.processes.Count -eq 0 -and (Report $clearProbe).commands.Count -eq 0) 'Clear inventory did not record before missing real bootstrap.'
    $output = Check 'exact method preserves two parameter cases from foreign cwd without global probe' 0 'PASS range-regression' -Filter 'FullyQualifiedName=RangeFixture.Cases.SameMethod' -Probe forbidden
    $r = Report $output
    Assert ($r.status -ceq 'completed' -and $r.projects.Count -eq 1 -and $r.projects[0].expected.Count -eq 2 -and $r.projects[0].executed.Count -eq 2) 'Expected literal two parameter cases.'
    Assert ((@($r.commands.name) -join ',') -ceq 'sdk,cli-integration.build,cli-integration.discover,cli-integration.test') 'Unexpected scope/phase ordering.'
    Assert ($r.sourceIdentity.Count -gt 0 -and $r.toolIdentity.Count -gt 0 -and $r.projects[0].artifacts.Count -gt 0) 'Source/compiled identity missing.'
    foreach ($c in $r.commands) { Assert ($c.exitKnown -and $c.exitCode -eq 0 -and $c.rootExited -and $c.stdoutComplete -and $c.stderrComplete -and $c.cleanupComplete) 'Missing actual completion flags.' }
    $snapshot = Get-Content -LiteralPath (Join-Path $output 'cli-integration.discovery.json') -Raw | ConvertFrom-Json
    Assert ($snapshot.all.total -eq 6 -and $snapshot.selected.Count -eq 2 -and $snapshot.selected[0].Id -cne $snapshot.selected[1].Id) 'Independent full inventory/selected case IDs lost.'
    $selectedBytes = @($snapshot.selected | ForEach-Object { @($_.Properties | Where-Object { $_.Key.Id -ceq 'XunitTestCaseSerialization' })[0].Value })
    Assert ($selectedBytes[0] -cne $selectedBytes[1]) 'Distinct parameter serialization lost.'
    $assemblyDir = Split-Path $r.projects[0].assembly -Parent
    Assert (-not (Test-Path -LiteralPath (Join-Path $assemblyDir 'unselected.marker'))) 'Unselected namespace executed.'
    $normal = Check 'literal class prefix does not use contains semantics' 0 'PASS range-regression' -Filter 'FullyQualifiedName~RangeFixture.Cases.'
    $r = Report $normal
    Assert ($r.projects[0].executed.Count -eq 5 -and -not (Test-Path -LiteralPath (Join-Path $assemblyDir 'unselected.marker'))) 'Prefix ran the wrong namespace.'
    $sameDisplay = Check 'same display preserves different case IDs and byte parameters' 0 'PASS range-regression' -Filter 'FullyQualifiedName=RangeFixture.Cases.Bytes'
    $r = Report $sameDisplay; $s = Get-Content -LiteralPath (Join-Path $sameDisplay 'cli-integration.discovery.json') -Raw | ConvertFrom-Json
    Assert ($r.projects[0].executed.Count -eq 2 -and $s.selected[0].DisplayName -ceq $s.selected[1].DisplayName -and $s.selected[0].Id -cne $s.selected[1].Id) 'Duplicate-display parameter identities were merged.'
    $noMatch = Check 'zero matches rejects before test execution' 1 '[range:filter-empty]' -Filter 'FullyQualifiedName=RangeFixture.Missing'
    Assert (-not (@((Report $noMatch).commands.name) -contains 'cli-integration.test')) 'No-match selection executed tests.'
    $null = Check 'prefix is ordinal case-sensitive' 1 '[range:filter-empty]' -Filter 'FullyQualifiedName~rangefixture.Cases.'
    $hash = (Get-FileHash -LiteralPath (Join-Path $output 'range-run.json')).Hash
    $null = Check 'existing successful output is never overwritten' 1 '[range:output-exists]' -ExistingOutput $output
    Assert ((Get-FileHash -LiteralPath (Join-Path $output 'range-run.json')).Hash -ceq $hash) 'Existing result was overwritten.'
    # Private artificial apphost controls the native boundary, not production logic.
    Put 'shim/Proxy.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>'
    Put 'shim/Program.cs' 'System.Console.OutputEncoding=new System.Text.UTF8Encoding(false); System.Console.WriteLine("{\"状态\":\"中文失败\"}"); System.Console.Error.WriteLine("原始退出 7"); return 7;'
    & $dotnet build (Join-Path $root 'shim/Proxy.csproj') --nologo *> (Join-Path $evidence 'shim-build.log'); $shimExit = $LASTEXITCODE
    Assert ($shimExit -eq 0) 'Artificial native failure fixture failed to build.'
    $shim = Join-Path $root 'shim/bin/Debug/net10.0'
    Copy-Item -LiteralPath (Join-Path $shim 'Proxy.exe') -Destination (Join-Path $shim 'dotnet.exe')
    try {
        $env:DOTNET_ROOT = Split-Path $dotnet -Parent; $env:PATH = $shim + [IO.Path]::PathSeparator + $oldPath
        $failed = Check 'native 7 and literal Chinese raw bytes survive the public runner' 7 '[range:command-exit]'
        $r = Report $failed; Assert ($r.commands[0].exitCode -eq 7 -and $r.commands[0].exitKnown) 'Original numeric exit was replaced.'
        $raw = [IO.File]::ReadAllText((Join-Path $failed $r.commands[0].stdout), [Text.UTF8Encoding]::new($false, $true))
        Assert ($raw.TrimEnd("`r", "`n") -ceq '{"状态":"中文失败"}') 'Raw UTF-8 JSON changed.'
        Assert ([IO.File]::ReadAllText((Join-Path $failed $r.commands[0].stderr), [Text.UTF8Encoding]::new($false, $true)).Contains('原始退出 7')) 'Original stderr lost.'
    } finally { $env:PATH = $oldPath; $env:DOTNET_ROOT = $oldDotnetRoot }
    $null = Check 'normal invocation after native failure recovers' 0 'PASS range-regression' -Filter 'FullyQualifiedName=RangeFixture.Cases.SameMethod'
    $blockedOutput = Join-Path $evidence ('run-' + ($records.Count + 1))
    $blocked = (Join-Path $blockedOutput 'cli-integration/results.trx').Replace('"', '""')
    Put 'tests/Pal.Extract.IntegrationTests/Fault.cs' ('using Xunit; namespace RangeFixture; public class Fault { [Fact] public void BlockTrx() { System.IO.Directory.CreateDirectory(@"' + $blocked + '"); Assert.True(true); } }')
    $null = Check 'missing current TRX cannot be success' 1 '[range:results-missing]' -Filter 'FullyQualifiedName=RangeFixture.Fault.BlockTrx' -ExistingOutput $blockedOutput
    $completed = $true
    Write-Host "PASS range regression: $($records.Count) public invocations; real artificial SDK/xUnit only."
} finally {
    $env:PATH = $oldPath; $env:DOTNET_ROOT = $oldDotnetRoot
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    if ($completed -and -not $EvidenceDirectory) { Remove-Item -LiteralPath $evidence -Recurse -Force }
    elseif (-not $completed) { Write-Host "Range regression failure retained: $evidence" }
}
