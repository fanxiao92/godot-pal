# Public-entry regression with real, minimal SDK/xUnit projects; not part of the unit selection.
[CmdletBinding()]
param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot ('TestResults/unit-entry-regression-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$entry = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../scripts/Test-Unit.ps1'))
$hostPath = (Get-Process -Id $PID).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Regression evidence directory must be new' }
$null = [IO.Directory]::CreateDirectory($evidence)
$root = Join-Path ([IO.Path]::GetTempPath()) ('unit 人工 空格 ' + [guid]::NewGuid().ToString('N'))
$records = [Collections.Generic.List[object]]::new()
function Put([string]$Relative, [string]$Text) {
    $path = Join-Path $root $Relative
    $null = [IO.Directory]::CreateDirectory((Split-Path $path -Parent))
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
}
function Reset-Fixture {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    Put 'global.json' '{"sdk":{"version":"10.0.402","rollForward":"latestPatch","allowPrerelease":false}}'
    Put 'NuGet.Config' '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>'
    foreach ($p in @(@('src/Pal.Core/Pal.Core', ''), @('tools/Pal.Extract/Pal.Extract', '<OutputType>Exe</OutputType>'))) {
        Put ($p[0] + '.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework>$($p[1])</PropertyGroup></Project>"
        Put ((Split-Path $p[0] -Parent) + '/Program.cs') 'public class Program { public static void Main() {} }'
    }
    foreach ($p in @(@('Pal.Core.Tests', '../../src/Pal.Core/Pal.Core.csproj'), @('Pal.Extract.UnitTests', '../../tools/Pal.Extract/Pal.Extract.csproj'))) {
        Put "tests/$($p[0])/$($p[0]).csproj" @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1"/><PackageReference Include="xunit" Version="2.9.3"/><PackageReference Include="xunit.runner.visualstudio" Version="3.1.4"/><ProjectReference Include="$($p[1])"/></ItemGroup></Project>
"@
    }
    Put 'tests/Pal.Core.Tests/Cases.cs' 'using Xunit; namespace Artificial; public class CoreCases { [Fact] public void One() { Assert.Equal(2, 1 + 1); } }'
    Put 'tests/Pal.Extract.UnitTests/Cases.cs' 'using Xunit; namespace Artificial; public class CliCases { [Theory][InlineData(1)][InlineData(2)] public void Value(int n) { Assert.InRange(n, 1, 2); } }'
}
function Check([string]$Name, [int]$ExpectedExit, [string]$Diagnostic, [string[]]$Selection = @('core','cli-unit'), [string]$Output = '') {
    $number = $records.Count + 1
    if (-not $Output) { $Output = Join-Path $evidence "run-$number" }
    $quote = { param($s) "'" + $s.Replace("'", "''") + "'" }
    $array = (@($Selection | ForEach-Object { & $quote $_ }) -join ',')
    $code = "& $(& $quote $entry) -RepositoryRoot $(& $quote $root) -OutputDirectory $(& $quote $Output) -Project @($array)"
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Push-Location ([IO.Path]::GetTempPath())
    try {
        $text = (& $hostPath -NoProfile -EncodedCommand ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))) 2>&1 | Out-String)
        $actual = $LASTEXITCODE
    } finally { Pop-Location }
    $watch.Stop()
    [IO.File]::WriteAllText((Join-Path $evidence "$number.output.log"), $text, [Text.UTF8Encoding]::new($false))
    $records.Add(@{ name = $Name; command = $code; exitCode = $actual; elapsedMs = $watch.Elapsed.TotalMilliseconds; outputDirectory = $Output })
    $records | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'commands.json') -Encoding utf8
    if ($actual -ne $ExpectedExit -or -not $text.Contains($Diagnostic)) { throw "${Name}: expected $ExpectedExit / $Diagnostic, got $actual / $text" }
    Write-Host "PASS $Name (exit $actual; $Diagnostic)"
    return $Output
}
try {
    Reset-Fixture
    $output = Check 'two real projects / foreign cwd / Chinese spaces' 0 'PASS unit: 2 projects'
    $report = Get-Content -LiteralPath (Join-Path $output 'unit-run.json') -Raw | ConvertFrom-Json
    if ($report.status -cne 'completed' -or $report.projects.Count -ne 2 -or $report.projects[0].executed -ne 1 -or $report.projects[1].executed -ne 2) { throw 'Expected literal 1 Core + 2 CLI passed cases' }
    $expectedPhases = @('boundary','sdk','core.build','core.discover','cli-unit.build','cli-unit.discover','core.test','cli-unit.test')
    if (($report.commands.name -join '|') -cne ($expectedPhases -join '|')) { throw 'Both explicit discoveries must be frozen before either execution; no solution or heavy phase permitted' }
    foreach ($c in $report.commands) {
        if ($c.exitCode -ne 0 -or -not $c.exitKnown -or -not $c.stdoutComplete -or -not $c.stderrComplete -or $null -eq $c.elapsedMs -or $c.elapsedMs -le 0 -or $c.status -cne 'completed') { throw 'Incomplete command / invented measurement' }
    }
    if (-not $report.sdk -or -not $report.sourceIdentity.Count -or $report.toolIdentity.Count -ne 3 -or $report.elapsedMs -le 0) { throw 'Missing code/environment/timing identity' }
    $core = Get-Content -LiteralPath (Join-Path $output 'core.discovery.json') -Raw | ConvertFrom-Json
    $cli = Get-Content -LiteralPath (Join-Path $output 'cli-unit.discovery.json') -Raw | ConvertFrom-Json
    if (@($core.cases.FullyQualifiedName) -cne 'Artificial.CoreCases.One' -or @($cli.cases | Where-Object FullyQualifiedName -CNE 'Artificial.CliCases.Value').Count -or $cli.cases.Count -ne 2) { throw 'Wrong independent method selection' }
    $null = Check 'empty selection cannot be green' 1 '[unit:selection]' @()
    $null = Check 'unknown project rejected' 1 '[unit:selection]' @('integration')
    $null = Check 'empty name rejected' 1 '[unit:selection]' @('')
    $null = Check 'duplicate project rejected' 1 '[unit:selection]' @('core','core')
    $null = Check 'old output directory rejected unchanged' 1 '[unit:output-exists]' @('core') $output
    if ((Get-Content -LiteralPath (Join-Path $output 'unit-run.json') -Raw | ConvertFrom-Json).status -cne 'completed') { throw 'Old success was overwritten' }
    Remove-Item -LiteralPath (Join-Path $root 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj')
    $null = Check 'missing applicable project before SDK' 1 '[boundary:project-missing]'
    Reset-Fixture
    Put 'tools/Pal.Extract/Pal.Extract.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup><Target Name="Deploy"><Copy SourceFiles="a" DestinationFolder="b"/></Target></Project>'
    $null = Check 'boundary deployment rejected before SDK' 1 '[boundary:build-target]'
    Reset-Fixture
    Put 'tests/Pal.Core.Tests/Cases.cs' 'namespace Artificial; public class EmptyCases { }'
    $zero = Check 'zero discovered/executed tests rejected' 1 '[unit:zero-discovery]' @('core')
    Reset-Fixture
    Put 'tests/Pal.Core.Tests/Cases.cs' 'using Xunit; namespace Artificial; public class SkippedCases { [Fact(Skip="unexpected skip")] public void One() {} }'
    $skip = Check 'nonzero discovery but zero execution / skip rejected' 1 '[unit:results]' @('core')
    Reset-Fixture
    Put 'tests/Pal.Core.Tests/Cases.cs' 'using Xunit; namespace Artificial; public class FailingCases { [Fact] public void One() { Assert.True(false, "中文失败原文"); } }'
    $failed = Check 'real test failure preserves original exit and UTF8 logs' 1 '[unit:command-exit]' @('core')
    $failedReport = Get-Content -LiteralPath (Join-Path $failed 'unit-run.json') -Raw | ConvertFrom-Json
    $testCommand = @($failedReport.commands | Where-Object name -CEQ 'core.test')
    if ($testCommand.Count -ne 1 -or $testCommand[0].exitCode -ne 1 -or -not $testCommand[0].stdoutComplete -or -not $testCommand[0].stderrComplete -or $failedReport.status -cne 'failed') { throw 'Original known test failure was lost' }
    $originalLogs = [IO.File]::ReadAllText((Join-Path $failed $testCommand[0].stdout)) + [IO.File]::ReadAllText((Join-Path $failed $testCommand[0].stderr))
    if (-not $originalLogs.Contains('中文失败原文')) { throw 'Original UTF8 failure log lost Chinese text' }
    Reset-Fixture
    $null = Check 'normal Core subset after failures recovers' 0 'PASS unit: 1 projects' @('core')
    Reset-Fixture
    $missingOutput = Join-Path $evidence ('run-' + ($records.Count + 1))
    $blocked = (Join-Path $missingOutput 'core/results.trx').Replace('"', '""')
    # Deliberate artifact fault in an artificial production dependency; no SDK/tool mocking seam.
    Put 'src/Pal.Core/Program.cs' "public class Program { public static void BlockResult() { System.IO.Directory.CreateDirectory(@`"$blocked`" ); } }"
    Put 'tests/Pal.Core.Tests/Cases.cs' 'using Xunit; namespace Artificial; public class CoreCases { [Fact] public void One() { Program.BlockResult(); Assert.True(true); } }'
    $null = Check 'result path blocked / missing TRX cannot count as success' 1 '[unit:results-missing]' @('core') $missingOutput
    Reset-Fixture
    Put 'tests/Pal.Extract.UnitTests/Cases.cs' @'
using Xunit;
namespace Artificial;
public class CliCases {
    public static System.Collections.Generic.IEnumerable<object[]> Values => new[] {
        new object[] { new byte[] { 1,2,3,4,5,6,7,8,9,10 } },
        new object[] { new byte[] { 1,2,3,4,5,6,7,8,9,11 } }
    };
    [Theory][MemberData(nameof(Values))] public void Bytes(byte[] data) { Assert.Equal(10, data.Length); }
}
'@
    $duplicates = Check 'coincident displays preserve two full parameter identities' 0 'PASS unit: 1 projects' @('cli-unit')
    $duplicateCases = (Get-Content -LiteralPath (Join-Path $duplicates 'cli-unit.discovery.json') -Raw | ConvertFrom-Json).cases
    if ($duplicateCases.Count -ne 2 -or $duplicateCases[0].DisplayName -cne $duplicateCases[1].DisplayName -or $duplicateCases[0].Id -ceq $duplicateCases[1].Id -or
        @($duplicateCases | Where-Object FullyQualifiedName -CNE 'Artificial.CliCases.Bytes').Count) { throw 'Expected literal two long-array cases sharing display but not identity' }
    Reset-Fixture
    $changedOutput = Join-Path $evidence ('run-' + ($records.Count + 1))
    $frozenPath = (Join-Path $changedOutput 'core.discovery.json').Replace('"', '""')
    Put 'src/Pal.Core/Program.cs' "public class Program { public static int Value() => System.IO.File.Exists(@`"$frozenPath`") ? 2 : 1; }"
    Put 'tests/Pal.Core.Tests/Cases.cs' 'using Xunit; namespace Artificial; public class CoreCases { public static System.Collections.Generic.IEnumerable<object[]> Values => new[] { new object[] { Program.Value() } }; [Theory][MemberData(nameof(Values))] public void One(int n) { Assert.InRange(n, 1, 2); } }'
    $changed = Check 'same count but substituted parameter case rejected' 1 '[unit:identity]' @('core') $changedOutput
    $frozenCase = (Get-Content -LiteralPath (Join-Path $changed 'core.discovery.json') -Raw | ConvertFrom-Json).cases
    [xml]$changedTrx = Get-Content -LiteralPath (Join-Path $changed 'core/results.trx') -Raw
    if ($frozenCase.Count -ne 1 -or $frozenCase[0].DisplayName -cne 'Artificial.CoreCases.One(n: 1)' -or $changedTrx.TestRun.Results.UnitTestResult.testName -cne 'Artificial.CoreCases.One(n: 2)') { throw 'Substitution must be precisely case 1 -> case 2, not a count/build/crash failure' }
    Reset-Fixture
    $null = Check 'final normal two-project recovery' 0 'PASS unit: 2 projects'
    Write-Host "PASS regression: $($records.Count) public invocations; $evidence"
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    if (Test-Path -LiteralPath $root) { throw 'Owned artificial project was not cleaned' }
    Write-Host "CLEAN owned fixture: $root (evidence retained: $evidence)"
}
