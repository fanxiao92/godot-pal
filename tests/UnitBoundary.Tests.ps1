# Offline public-command regression; artificial UTF-8 projects only, no SDK/engine required.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$command = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../scripts/Test-UnitBoundary.ps1'))
$root = Join-Path ([IO.Path]::GetTempPath()) ('unit 边界 godot-pal ' + [guid]::NewGuid().ToString('N'))
$hostPath = (Get-Process -Id $PID).Path
$count = 0
function Put([string]$Relative, [string]$Text) {
    $path = Join-Path $root $Relative
    [IO.Directory]::CreateDirectory((Split-Path $path -Parent)) | Out-Null
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
}
function Reset-Fixture {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    Put 'src/Pal.Core/Pal.Core.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
    Put 'tools/Pal.Extract/Pal.Extract.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
    Put 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include="../../src/Pal.Core/Pal.Core.csproj" /></ItemGroup></Project>'
    Put 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include="../../tools/Pal.Extract/Pal.Extract.csproj" /><Compile Include="../Pal.Extract.Tests/Fixtures/Yj2TestData.cs" Link="Fixtures/Yj2TestData.cs" /></ItemGroup></Project>'
    Put 'tests/Pal.Core.Tests/人工 Tests.cs' 'using System.IO; class Pure { string 中文 = "File.ReadAllText 不是调用"; /* Directory.CreateDirectory */ }'
    Put 'tests/Pal.Extract.UnitTests/PngTests.cs' 'using System.IO; using System.IO.Compression; class Png { void Read() { using var m = new MemoryStream(); using var r = new BinaryReader(m); using var z = new ZLibStream(m, CompressionMode.Decompress); } }'
    Put 'tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs' 'class Yj2TestData { static byte[] Data() => new byte[] { 1, 2 }; }'
    Put 'tools/Pal.Extract/Program.cs' 'class Program { static void Main() { System.IO.File.ReadAllText("production I/O is not a unit fixture"); } }'
    Put 'NuGet.Config' '<configuration><packageSources><add key="gdunit" value=".packages/gdunit" /></packageSources></configuration>'
}
function Check([string]$Name, [int]$Exit, [string]$Diagnostic, [string[]]$Selection = @('core', 'cli-unit')) {
    # -Command argument transport permits explicit empty arrays, unlike native -File string[] binding.
    $escapedCommand = $command.Replace("'", "''")
    $escapedRoot = $root.Replace("'", "''")
    $literal = (@($Selection | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ',')
    $invocation = "& '$escapedCommand' -RepositoryRoot '$escapedRoot' -Project @($literal)"
    Push-Location ([IO.Path]::GetTempPath())
    try {
        $output = (& $hostPath -NoProfile -Command $invocation 2>&1 | Out-String)
        $actual = $LASTEXITCODE
    } finally { Pop-Location }
    if ($actual -ne $Exit -or $output -notmatch [regex]::Escape($Diagnostic)) {
        throw "${Name}: expected exit $Exit / $Diagnostic; got exit $actual / $output"
    }
    $script:count++
    Write-Host "PASS $Name (exit $actual; $Diagnostic)"
    Write-Host ($output.TrimEnd())
}
try {
    Reset-Fixture
    Check 'pure projects / Chinese and spaces / foreign cwd / production Exe I/O allowed' 0 'PASS unit-boundary: cli-unit'
    $caseFailures = [Collections.Generic.List[string]]::new()
    Put 'tests/forbidden.cs' 'class Forbidden { static string Leak = File.ReadAllText("outside unit sources"); }'
    Put 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include="../../src/Pal.Core/Pal.Core.csproj" /><compile Include="../forbidden.cs" Link="Leak.cs" /></ItemGroup></Project>'
    try { Check 'noncanonical compile cannot bypass explicit helper selection' 1 '[boundary:project-shape]' } catch { $caseFailures.Add($_.Exception.Message); Write-Host $_.Exception.Message }
    Reset-Fixture
    Put 'tools/Pal.Extract/Pal.Extract.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><projectreference Include="../../src/Pal.Game/Pal.Game.csproj" /></ItemGroup></Project>'
    try { Check 'noncanonical production projectreference cannot hide dependency' 1 '[boundary:project-shape]' } catch { $caseFailures.Add($_.Exception.Message); Write-Host $_.Exception.Message }
    if ($caseFailures.Count) { throw ($caseFailures -join "`n") }
    Reset-Fixture
    Put 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include="../Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj" /></ItemGroup></Project>'
    Check 'integration reference rejected' 1 '[boundary:project-reference]'
    foreach ($include in @('../Pal.Prepare.Tests/Pal.Prepare.Tests.csproj', '../../src/Pal.Game/Pal.Game.csproj')) {
        Reset-Fixture
        Put 'src/Pal.Core/Pal.Core.csproj' "<Project Sdk=`"Microsoft.NET.Sdk`"><ItemGroup><ProjectReference Include=`"$include`" /></ItemGroup></Project>"
        Check "transitive dependency $include rejected" 1 '[boundary:project-reference]'
    }
    foreach ($task in @('<Copy SourceFiles="a" DestinationFolder="cli" />', '<Exec Command="echo 部署" />')) {
        Reset-Fixture
        Put 'tools/Pal.Extract/Pal.Extract.csproj' "<Project Sdk=`"Microsoft.NET.Sdk`"><Target Name=`"Deploy`" AfterTargets=`"Build`">$task</Target></Project>"
        Check "transitive build deployment $task rejected" 1 '[boundary:build-target]'
    }
    foreach ($source in @(
        'class C { C() { File.ReadAllText("constructor"); } }',
        'class C { static string x = Directory.GetCurrentDirectory(); }',
        'class C { [CaseSensitiveFileSystemFact] void Test() {} }',
        'class C { static object Data => File.ReadAllBytes("provider"); }',
        'class C { static object Data() { return new ProcessStartInfo(); } }',
        'class C { CliProcess helper; }',
        'class C { DirectoryLinkFixture helper; }',
        'class C { MkfFixture helper; }'
    )) {
        Reset-Fixture
        Put 'tests/Pal.Core.Tests/人工 Tests.cs' $source
        Check "whole-source side effect rejected: $source" 1 '[boundary:source-forbidden]'
    }
    Reset-Fixture
    Put 'tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs' 'class Yj2TestData { static string Leak = File.ReadAllText("linked initializer"); }'
    Check 'explicit linked source inspected' 1 '[boundary:source-forbidden]'
    Reset-Fixture
    Check 'empty selection rejected' 1 '[boundary:selection]' @()
    Check 'unknown selection rejected' 1 '[boundary:selection]' @('integration')
    Check 'empty name rejected' 1 '[boundary:selection]' @('')
    Check 'duplicate selection rejected' 1 '[boundary:selection]' @('core', 'core')
    Remove-Item -LiteralPath (Join-Path $root 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj')
    Check 'missing applicable project rejected' 1 '[boundary:project-missing]'
    Reset-Fixture
    Remove-Item -LiteralPath (Join-Path $root 'tests/Pal.Core.Tests/人工 Tests.cs')
    Check 'empty source selection rejected' 1 '[boundary:source-selection]'
    Reset-Fixture
    Check 'normal invocation after deterministic failures remains clean' 0 'PASS unit-boundary: cli-unit'
    Check 'explicit single Core selection' 0 'PASS unit-boundary: core' @('core')
    foreach ($xml in @(
        '<Project Sdk="Godot.NET.Sdk/4.7.2" />',
        '<Project Sdk="Microsoft.NET.Sdk"><Import Project="heavy.targets" /></Project>',
        '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="gdUnit4.api" Version="1" /></ItemGroup></Project>',
        '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Reference Include="Pal.Extract.IntegrationTests" /></ItemGroup></Project>',
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><PostBuildEvent>echo 部署</PostBuildEvent></PropertyGroup></Project>',
        '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><None Include="cli.exe" CopyToOutputDirectory="Always" /></ItemGroup></Project>',
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup Condition="true"><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
    )) {
        Reset-Fixture
        Put 'tools/Pal.Extract/Pal.Extract.csproj' $xml
        Check "unsupported/heavy project shape rejected: $xml" 1 '[boundary:project-shape]'
    }
    Reset-Fixture
    Put 'Directory.Build.targets' '<Project><Target Name="Hidden"><Exec Command="echo deployment" /></Target></Project>'
    Check 'implicit repository build import rejected' 1 '[boundary:project-shape]'
    Reset-Fixture
    Put 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/Pal.Core/Pal.Core.csproj" /></ItemGroup></Project>'
    Check 'missing test identity rejected' 1 '[boundary:test-project]'
    Reset-Fixture
    Put 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include="../../tools/Pal.Extract/Pal.Extract.csproj" /><Compile Include="../Pal.Extract.Tests/**/*.cs" /></ItemGroup></Project>'
    Check 'wildcard shared source rejected' 1 '[boundary:source-selection]'
    Reset-Fixture
    Put 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include="../../tools/Pal.Extract/Pal.Extract.csproj" /><Compile Include="../Pal.Extract.IntegrationTests/CliProcess.cs" Link="Helper.cs" /></ItemGroup></Project>'
    Put 'tests/Pal.Extract.IntegrationTests/CliProcess.cs' '// Even an empty prohibited helper file must not be shared.'
    Check 'prohibited helper source link rejected' 1 '[boundary:source-selection]'
    Reset-Fixture
    Put 'tests/Pal.Core.Tests/人工 Tests.cs' 'class C { string Pure = """"safe""""; string Leak = File.ReadAllText("初始化"); }'
    Check 'raw-string delimiter must not hide subsequent initializer / UTF-8 diagnostic path' 1 '人工 Tests.cs:1 forbidden unit fixture token: File'
    foreach ($source in @(
        'using F = System.IO.File; class C { string Data = F.ReadAllText("alias"); }',
        'class C { string Data = $"{File.ReadAllText("interpolated")}"; }',
        'class C { string Data = @"verbatim ""File.ReadAllText"""; string Raw = """File.ReadAllText"""; }'
    )) {
        Reset-Fixture
        Put 'tests/Pal.Core.Tests/人工 Tests.cs' $source
        if ($source.StartsWith('class C { string Data = @')) {
            Check 'ordinary/verbatim/raw literal text is not I/O' 0 'PASS unit-boundary: core'
        } else { Check "recognizable alias/interpolation: $source" 1 '[boundary:source-forbidden]' }
    }
    Reset-Fixture
    Put 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj' '<Project Sdk="Microsoft.NET.Sdk"><Target Name="CopyCliForProcessTests"><Copy SourceFiles="cli" DestinationFolder="out" /></Target></Project>'
    Check 'unit deployment target rejected directly' 1 '[boundary:build-target]'
    Reset-Fixture
    Remove-Item -LiteralPath (Join-Path $root 'tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs')
    Check 'missing explicitly linked source rejected' 1 '[boundary:source-selection]'
    Reset-Fixture
    Put 'src/Pal.Core/Pal.Core.csproj' '<Project>'
    Check 'malformed project XML rejected' 1 '[boundary:project-read]'
    Reset-Fixture
    Check 'final clean invocation after all failures' 0 'PASS unit-boundary: cli-unit'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Host "PASS UnitBoundary public-command regression: $count cases; fixture cleaned."
