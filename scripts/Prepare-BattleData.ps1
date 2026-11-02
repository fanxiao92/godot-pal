# Explicit numeric JSON -> typed Godot Resource. No runtime hook or alternate converter.
$ErrorActionPreference = 'Stop'
$options = @{}
for ($i = 0; $i -lt $args.Count; $i += 2) {
    $key = [string]$args[$i]
    if ($key -notin @('-InputPath','-OutputPath','-GodotPath','-TimeoutSeconds') -or
        $options.ContainsKey($key) -or $i + 1 -ge $args.Count -or [string]::IsNullOrWhiteSpace([string]$args[$i + 1]) -or ([string]$args[$i + 1]).StartsWith('-')) {
        [Console]::Error.WriteLine('Prepare-BattleData argument error: expected unique -InputPath/-OutputPath and optional -GodotPath/-TimeoutSeconds, each with a value.')
        exit 2
    }
    $options[$key] = [string]$args[$i + 1]
}
foreach ($required in @('-InputPath','-OutputPath')) {
    if (-not $options.ContainsKey($required)) { [Console]::Error.WriteLine("Prepare-BattleData argument error: $required is required."); exit 2 }
}
$timeout = 60
if ($options.ContainsKey('-TimeoutSeconds') -and (-not [int]::TryParse($options['-TimeoutSeconds'], [ref]$timeout) -or $timeout -lt 1 -or $timeout -gt 600)) {
    [Console]::Error.WriteLine('Prepare-BattleData argument error: -TimeoutSeconds must be 1..600.'); exit 2
}
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
function Test-Inside([string]$Child,[string]$Parent) { $Child.Equals($Parent,$comparison) -or $Child.StartsWith($Parent.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar,$comparison) }
function Assert-NoLink([string]$Path) {
    $cursor=$Path
    while ($cursor) { try {$a=[IO.File]::GetAttributes($cursor)} catch [IO.FileNotFoundException] {$a=0} catch [IO.DirectoryNotFoundException] {$a=0}; if ($a -band [IO.FileAttributes]::ReparsePoint) { throw "Link/reparse point is not allowed: $cursor" }; $cursor=[IO.Path]::GetDirectoryName($cursor) }
}
function Assert-TreeNoLink([string]$Directory) { foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($Directory)) { $a=[IO.File]::GetAttributes($entry); if($a -band [IO.FileAttributes]::ReparsePoint){throw "Package contains a link/reparse point: $entry"}; if($a -band [IO.FileAttributes]::Directory){Assert-TreeNoLink $entry} } }
function Invoke-Checked([string]$File,[string[]]$Arguments,[string]$Marker) {
    $info=[Diagnostics.ProcessStartInfo]::new(); $info.FileName=$File; $info.WorkingDirectory=$project; $info.UseShellExecute=$false; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new(); $process.StartInfo=$info
    try { $null=$process.Start(); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync(); if(-not $process.WaitForExit($timeout*1000)){$process.Kill($true);$process.WaitForExit();throw "Preparation process timed out after $timeout seconds (process tree killed)."}; $text=$stdout.GetAwaiter().GetResult()+"`n"+$stderr.GetAwaiter().GetResult(); $plain=$text-replace '\x1B\[[0-?]*[ -/]*[@-~]',''; if($process.ExitCode-ne 0-or $plain-match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)'){throw "Preparation process failed (exit $($process.ExitCode)):`n$plain"}; if(-not $plain.Contains($Marker)){throw "Preparation process did not confirm $Marker`n$plain"} ; if($Marker.StartsWith('PAL_PREPARE_')){Write-Host $plain} } finally {$process.Dispose()}
}
try {
    $inputRoot=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($options['-InputPath']))
    $outputRoot=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($options['-OutputPath']))
    $assets=[IO.Path]::GetFullPath((Join-Path $project 'Assets'))
    if(-not (Test-Inside $outputRoot $assets)){throw 'OutputPath must be under project Assets.'}
    if((Test-Inside $outputRoot $inputRoot)-or(Test-Inside $inputRoot $outputRoot)){throw 'InputPath and OutputPath must not overlap in either direction.'}
    if(Test-Inside $inputRoot $project){throw 'InputPath must be outside src/Pal.Game.'}
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot
    if(-not [IO.Directory]::Exists($inputRoot)){throw 'InputPath must be an existing battle-data directory.'}
    Assert-TreeNoLink $inputRoot
    $entries=@([IO.Directory]::EnumerateFileSystemEntries($inputRoot))
    if($entries.Count-ne 1-or [IO.Path]::GetFileName($entries[0])-cne 'battle-data.json'-or -not [IO.File]::Exists($entries[0])){throw 'InputPath must contain exactly battle-data.json and no other entries.'}
    if([IO.File]::Exists($outputRoot)){throw 'OutputPath is an existing file.'}
    if([IO.Directory]::Exists($outputRoot)-and @([IO.Directory]::EnumerateFileSystemEntries($outputRoot)).Count-ne 0){throw 'OutputPath must be new or empty; no overwrite is supported.'}
    $processes=@(Get-Process -Name 'godot*' -ErrorAction SilentlyContinue | Select-Object Id,ProcessName)
    if($processes.Count){$processes | Format-Table | Out-String | Write-Host; throw 'Godot process already exists; stop and confirm ownership.'}
    $godot=$options['-GodotPath']; if(-not $godot){$godot=$env:GODOT_BIN}; if(-not $godot){$godot=(Get-Command godot -ErrorAction Stop).Source}; $godot=(Get-Command $godot -ErrorAction Stop).Source
    Invoke-Checked $godot @('--version') '4.7.2.stable.mono.official.ed1daf0bf'
    Invoke-Checked 'dotnet' @('build',(Join-Path $project 'Pal.Game.csproj'),'--nologo','--verbosity','quiet') ''
    # The public Editor API repeats all checks and owns stable source reading/publication.
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot; Assert-TreeNoLink $inputRoot
    Invoke-Checked $godot @('--headless','--path',$project,'res://Editor/PrepareBattleData.tscn','--','--battle-input',$inputRoot,'--battle-output',$outputRoot) 'PAL_PREPARE_BATTLE_DATA:BattleContent.tres'
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot; Assert-TreeNoLink $inputRoot; Assert-TreeNoLink $outputRoot
    Write-Host 'Prepared native Godot battle data: BattleContent.tres'; exit 0
} catch { [Console]::Error.WriteLine("Prepare-BattleData failed: $($_.Exception.Message)`nNo existing output is overwritten; only owned temporary files and new empty directories are cleaned up."); exit 1 }
