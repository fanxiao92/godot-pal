# Explicit offline M1 -> Godot native import. No startup hook, extraction, overwrite or rollback.
# Manual argument parsing keeps all argument errors non-interactive and consistently exit 2.
$ErrorActionPreference = 'Stop'
$options = @{}
for ($i = 0; $i -lt $args.Count; $i += 2) {
    $key = [string]$args[$i]
    if ($key -notin @('-InputPath', '-OutputPath', '-GodotPath', '-TimeoutSeconds') -or
        $options.ContainsKey($key) -or $i + 1 -ge $args.Count -or [string]::IsNullOrWhiteSpace([string]$args[$i + 1]) -or
        ([string]$args[$i + 1]).StartsWith('-')) {
        [Console]::Error.WriteLine("Prepare-GameScene argument error: expected unique -InputPath/-OutputPath, optional -GodotPath/-TimeoutSeconds, each with a value.")
        exit 2
    }
    $options[$key] = [string]$args[$i + 1]
}
if (-not $options.ContainsKey('-InputPath') -or -not $options.ContainsKey('-OutputPath')) {
    [Console]::Error.WriteLine('Prepare-GameScene argument error: -InputPath and -OutputPath are required.')
    exit 2
}
$timeout = 60
if ($options.ContainsKey('-TimeoutSeconds') -and (-not [int]::TryParse($options['-TimeoutSeconds'], [ref]$timeout) -or $timeout -lt 1 -or $timeout -gt 600)) {
    [Console]::Error.WriteLine('Prepare-GameScene argument error: -TimeoutSeconds must be 1..600.')
    exit 2
}
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
function Test-Inside([string]$Child, [string]$Parent) {
    return $Child.Equals($Parent, $comparison) -or $Child.StartsWith($Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $comparison)
}
function Assert-NoLink([string]$Path) {
    $cursor = $Path
    while ($cursor) {
        # GetAttributes also observes a dangling link; do not use Resolve-Path (which follows links).
        try { $attributes = [IO.File]::GetAttributes($cursor) }
        catch [IO.FileNotFoundException] { $attributes = 0 }
        catch [IO.DirectoryNotFoundException] { $attributes = 0 }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link/reparse point is not allowed: $cursor" }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
function Assert-TreeNoLink([string]$Directory) {
    foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($Directory)) {
        $attributes = [IO.File]::GetAttributes($entry)
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Package contains a link/reparse point: $entry" }
        if ($attributes -band [IO.FileAttributes]::Directory) { Assert-TreeNoLink $entry }
    }
}
function Invoke-Checked([string]$File, [string[]]$Arguments, [string]$Marker) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $File; $info.WorkingDirectory = $project; $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($timeout * 1000)) {
            $process.Kill($true); $process.WaitForExit()
            throw "Preparation process timed out after $timeout seconds (process tree killed)."
        }
        $text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult()
        $plain = $text -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
        if ($process.ExitCode -ne 0 -or $plain -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)') { throw "Preparation process failed (exit $($process.ExitCode)):`n$plain" }
        if ($Marker -and -not $plain.Contains($Marker)) { throw "Preparation process did not confirm $Marker`n$plain" }
    } finally { $process.Dispose() }
}
try {
    $inputRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($options['-InputPath']))
    $outputRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($options['-OutputPath']))
    $assets = Join-Path $project 'Assets'
    if (-not (Test-Inside $outputRoot $assets)) { throw 'OutputPath must be the project Assets directory or a directory inside it.' }
    if ((Test-Inside $outputRoot $inputRoot) -or (Test-Inside $inputRoot $outputRoot)) { throw 'InputPath and OutputPath must not overlap in either direction.' }
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot
    if (-not [IO.Directory]::Exists($inputRoot)) { throw 'InputPath must be an existing complete M1 package directory.' }
    Assert-TreeNoLink $inputRoot
    if ([IO.File]::Exists($outputRoot)) { throw 'OutputPath is an existing file.' }
    if ([IO.Directory]::Exists($outputRoot) -and @([IO.Directory]::EnumerateFileSystemEntries($outputRoot)).Count -ne 0) { throw 'OutputPath must be new or empty; no overwrite is supported.' }
    $godot = $options['-GodotPath']
    if (-not $godot) { $godot = $env:GODOT_BIN }
    if (-not $godot) { $godot = (Get-Command godot -ErrorAction Stop).Source }
    $godot = (Get-Command $godot -ErrorAction Stop).Source
    Invoke-Checked $godot @('--version') '4.7.2.stable.mono.official.ed1daf0bf'
    Invoke-Checked 'dotnet' @('build', (Join-Path $project 'Pal.Game.csproj'), '--nologo', '--verbosity', 'quiet') ''
    $common = @('--headless', '--path', $project, 'res://Editor/PrepareScene.tscn', '--', '--prepare-input', $inputRoot, '--prepare-output', $outputRoot, '--prepare-phase')
    Invoke-Checked $godot ($common + 'textures') 'PAL_PREPARE_TEXTURES'
    # Import PNGs through the official Godot importer before saving native references to them.
    Invoke-Checked $godot @('--headless', '--path', $project, '--editor', '--import') ''
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot; Assert-TreeNoLink $inputRoot; Assert-TreeNoLink $outputRoot
    Invoke-Checked $godot ($common + 'resources') 'PAL_PREPARE_NATIVE'
    Write-Host 'Prepared native Godot scene: Scenes/Scene.tscn'
    exit 0
} catch {
    [Console]::Error.WriteLine("Prepare-GameScene failed: $($_.Exception.Message)`nA failed import may leave incomplete output; it is not automatically cleared or rolled back.")
    exit 1
}
