$ErrorActionPreference = 'Stop'
$options = @{}
for ($i = 0; $i -lt $args.Count; $i += 2) {
    $key = [string]$args[$i]
    if ($key -notin @('-InputPath', '-OutputPath', '-GodotPath') -or $options.ContainsKey($key) -or $i + 1 -ge $args.Count -or [string]::IsNullOrWhiteSpace([string]$args[$i + 1])) {
        [Console]::Error.WriteLine('Usage: Prepare-DialoguePackage.ps1 -InputPath <extract-output> -OutputPath <new-or-empty-Assets-path> [-GodotPath <godot-mono.console.exe>]')
        exit 2
    }
    $options[$key] = [string]$args[$i + 1]
}
if (-not $options.ContainsKey('-InputPath') -or -not $options.ContainsKey('-OutputPath')) { [Console]::Error.WriteLine('InputPath and OutputPath are required.'); exit 2 }
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$inputRoot = [IO.Path]::GetFullPath($options['-InputPath'])
$outputRoot = [IO.Path]::GetFullPath($options['-OutputPath'])
$assets = [IO.Path]::GetFullPath((Join-Path $project 'Assets'))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
function Inside($child, $parent) { $child.Equals($parent, $comparison) -or $child.StartsWith($parent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $comparison) }
function Assert-NoLink([string]$Path) {
    $cursor = $Path
    while ($cursor) {
        try { $attributes = [IO.File]::GetAttributes($cursor) }
        catch [IO.FileNotFoundException] { $attributes = 0 }
        catch [IO.DirectoryNotFoundException] { $attributes = 0 }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link/reparse point is not allowed: $cursor" }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
try {
    if (-not (Inside $outputRoot $assets)) { throw 'OutputPath must be under src/Pal.Game/Assets.' }
    if ((Inside $inputRoot $project) -or (Inside $outputRoot $inputRoot) -or (Inside $inputRoot $outputRoot)) { throw 'Input and output must be external/separate from the Godot project and each other.' }
    if (-not [IO.Directory]::Exists($inputRoot)) { throw 'InputPath must be an existing extracted dialogue package.' }
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot
    if ([IO.File]::Exists($outputRoot)) { throw 'OutputPath is an existing file.' }
    if ([IO.Directory]::Exists($outputRoot) -and @([IO.Directory]::EnumerateFileSystemEntries($outputRoot)).Count -ne 0) { throw 'OutputPath must be new or empty; no overwrite is supported.' }
    $godot = $options['-GodotPath']; if (-not $godot) { $godot = $env:GODOT_BIN }
    if (-not $godot) { $godot = 'C:\Users\xiaofan\scoop\apps\godot-mono\4.7.2\godot-mono.console.exe' }
    & pwsh -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Godot process check failed.' }
    $processes = Get-Process -Name 'godot*' -ErrorAction SilentlyContinue
    if ($processes) { $processes | Select-Object Id, ProcessName; throw 'Godot process already exists; stop and confirm ownership before preparation.' }
    $version = (& $godot --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -ne '4.7.2.stable.mono.official.ed1daf0bf') { throw "Pinned Godot version check failed: $version" }
    dotnet build (Join-Path $project 'Pal.Game.csproj') --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Pal.Game build failed.' }
    & $godot --headless --path $project 'res://Editor/PrepareDialogue.tscn' -- '--prepare-input' $inputRoot '--prepare-output' $outputRoot
    if ($LASTEXITCODE -ne 0) { throw "Godot dialogue preparation failed with exit $LASTEXITCODE; incomplete output is retained and not rolled back." }
} catch { [Console]::Error.WriteLine("Prepare-DialoguePackage failed: $($_.Exception.Message)"); exit 1 }
