# Run from PowerShell 7 on Windows. All generated projects stay outside the repository.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GodotPath
)
$ErrorActionPreference = 'Stop'
$GodotPath = (Resolve-Path $GodotPath).Path
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$root = Join-Path ([IO.Path]::GetTempPath()) ('pal-compat-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
Write-Host "Probe directory: $root"
if ($env:DOTNET_ROLL_FORWARD) { throw 'Run without a global DOTNET_ROLL_FORWARD override.' }

function Invoke-ProbeProcess {
    param([string]$Name, [string]$File, [string[]]$Arguments, [string]$Directory, [int]$TimeoutSeconds = 120)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $File
    $info.WorkingDirectory = $Directory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    } finally { $process.Dispose() }
    $text | Set-Content (Join-Path $root "$Name.log") -Encoding utf8
    Write-Host "$Name : exit=$exitCode timeout=$timedOut"
    # Keep raw logs, but remove console color escapes before evaluating diagnostics.
    $plainText = $text -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
    [pscustomobject]@{ Name = $Name; ExitCode = $exitCode; TimedOut = $timedOut; Text = $plainText }
}

function Assert-ProbeSuccess {
    param($Result, [switch]$RequireMarker)
    if ($Result.TimedOut) { throw "$($Result.Name): timed out" }
    if ($Result.ExitCode -ne 0) { throw "$($Result.Name): exit $($Result.ExitCode); see probe logs" }
    if ($Result.Text -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)') {
        throw "$($Result.Name): error in log; see probe logs"
    }
    if ($RequireMarker -and $Result.Text -notmatch '(?m)^PAL_COMPAT_OK:42\s*$') {
        throw "$($Result.Name): C# success marker missing"
    }
}

$version = Invoke-ProbeProcess 'godot-version' $GodotPath @('--version') $root
Assert-ProbeSuccess $version
if ($version.Text.Trim() -ne '4.7.2.stable.mono.official.ed1daf0bf') {
    throw 'This probe targets Godot .NET 4.7.2; re-evaluate other versions separately.'
}
$info = Invoke-ProbeProcess 'dotnet-info' $dotnet @('--info') $root
Assert-ProbeSuccess $info
$sdks = Invoke-ProbeProcess 'dotnet-sdks' $dotnet @('--list-sdks') $root
Assert-ProbeSuccess $sdks
$sdkLine = ($sdks.Text -split "`n" | Where-Object { $_ -match '^10\.0\.402 \[' } | Select-Object -First 1)
if (-not $sdkLine) { throw 'Install/approve SDK 10.0.402 before running this baseline probe.' }
$sdkBase = [regex]::Match($sdkLine, '\[(.+)\]').Groups[1].Value
$sdkDirectory = Join-Path $sdkBase '10.0.402'
@{ sdk = @{ version = '10.0.402'; rollForward = 'latestPatch'; allowPrerelease = $false } } |
    ConvertTo-Json -Depth 3 | Set-Content (Join-Path $root 'global.json')

# Ask the installed editor's generator, not a hand-written template, for its defaults.
$generator = Join-Path $root 'generator'
$game = Join-Path $root 'game'
New-Item -ItemType Directory $generator, $game | Out-Null
Set-Content (Join-Path $generator 'Generator.csproj') @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
Set-Content (Join-Path $generator 'Program.cs') @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
AssemblyLoadContext.Default.Resolving += (context, name) => {
    foreach (var directory in new[] { args[0], args[1] }) {
        var path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
var assembly = Assembly.LoadFrom(Path.Combine(args[0], "GodotTools.ProjectEditor.dll"));
var method = assembly.GetType("GodotTools.ProjectEditor.ProjectGenerator")!
    .GetMethod("GenAndSaveGameProject")!;
Console.WriteLine(method.Invoke(null, new object[] { args[2], "Probe" }));
'@
$tools = Join-Path (Split-Path $GodotPath) 'GodotSharp/Tools'
$generated = Invoke-ProbeProcess 'generate-default' $dotnet @('run', '--project', (Join-Path $generator 'Generator.csproj'), '--', $tools, $sdkDirectory, $game) $root
Assert-ProbeSuccess $generated
$project = Join-Path $game 'Probe.csproj'
Copy-Item $project (Join-Path $root 'Probe.default.csproj')
[xml]$default = Get-Content $project -Raw
$defaultFramework = $default.SelectSingleNode('/Project/PropertyGroup/TargetFramework[not(@Condition)]').InnerText
Write-Host "Generated default framework: $defaultFramework"
if ($defaultFramework -ne 'net8.0' -or $default.Project.Sdk -ne 'Godot.NET.Sdk/4.7.2') {
    throw 'Generator output differs from this probe baseline; inspect the saved default before proceeding.'
}

# Only the isolated candidate is changed. Keep the generated default as evidence.
$content = Get-Content $project -Raw
$content.Replace('net8.0', 'net10.0') | Set-Content $project
Set-Content (Join-Path $game 'project.godot') @'
config_version=5
[application]
config/name="Pal Compatibility Probe"
run/main_scene="res://Main.tscn"
[dotnet]
project/assembly_name="Probe"
[rendering]
renderer/rendering_method="gl_compatibility"
'@
Set-Content (Join-Path $game 'Main.tscn') @'
[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://Main.cs" id="1"]
[node name="Main" type="Node"]
script = ExtResource("1")
'@
Set-Content (Join-Path $game 'Main.cs') @'
using Godot;
public partial class Main : Node
{
    public override void _Ready()
    {
        var mode = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(mode, "missing-marker") >= 0) { GetTree().Quit(); return; }
        // Runtime calculation proves the C# entrypoint actually executes.
        int answer = System.Linq.Enumerable.Sum(new[] { 19, 23 });
        GD.Print($"PAL_COMPAT_OK:{answer}");
        GD.Print($"PAL_RUNTIME:{System.Environment.Version}");
        if (System.Array.IndexOf(mode, "exit-error") >= 0) { GetTree().Quit(17); return; }
        if (System.Array.IndexOf(mode, "runtime-error") >= 0) GD.PushError("PAL_EXPECTED_FAILURE");
        if (System.Array.IndexOf(mode, "timeout") >= 0) return;
        GetTree().Quit();
    }
}
'@
$restore = Invoke-ProbeProcess 'restore' $dotnet @('restore', $project) $root
Assert-ProbeSuccess $restore
$build = Invoke-ProbeProcess 'build' $dotnet @('build', $project, '--no-restore') $root
Assert-ProbeSuccess $build
$import = Invoke-ProbeProcess 'import' $GodotPath @('--headless', '--path', $game, '--editor', '--import') $root
Assert-ProbeSuccess $import

# Negative controls at the real process seam precede the positive run.
foreach ($mode in @('missing-marker', 'runtime-error', 'exit-error', 'timeout')) {
    $result = Invoke-ProbeProcess $mode $GodotPath @('--headless', '--path', $game, '--', $mode) $root -TimeoutSeconds 10
    $rejected = $false
    try { Assert-ProbeSuccess $result -RequireMarker } catch { $rejected = $true; Write-Host "Rejected $mode as expected: $_" }
    if (-not $rejected) { throw "Negative control $mode was incorrectly accepted." }
    if ($mode -eq 'missing-marker' -and ($result.TimedOut -or $result.ExitCode -ne 0)) { throw 'Missing-marker control did not exit cleanly.' }
    if ($mode -eq 'runtime-error' -and ($result.TimedOut -or $result.Text -notmatch 'ERROR: PAL_EXPECTED_FAILURE' -or $result.Text -notmatch 'PAL_COMPAT_OK:42')) { throw 'Runtime-error control did not exercise marker plus error.' }
    if ($mode -eq 'exit-error' -and ($result.TimedOut -or $result.ExitCode -ne 17 -or $result.Text -notmatch 'PAL_COMPAT_OK:42')) { throw 'Exit-error control did not exercise a nonzero exit after C# execution.' }
    if ($mode -eq 'timeout' -and (-not $result.TimedOut -or $result.Text -notmatch 'PAL_COMPAT_OK:42')) { throw 'Timeout control did not reach C# before timing out.' }
}
$run = Invoke-ProbeProcess 'run' $GodotPath @('--headless', '--path', $game) $root -TimeoutSeconds 30
Assert-ProbeSuccess $run -RequireMarker
Write-Host "PASS: Godot 4.7.2 + SDK 10.0.402 + net10.0. Logs and generated sources: $root"
