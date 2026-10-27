# Explicit authored battle-context -> Godot native Resource. No runtime hook, overwrite, or original-archive access.
$ErrorActionPreference = 'Stop'
$options = @{}
for ($i = 0; $i -lt $args.Count; $i += 2) {
    $key = [string]$args[$i]
    if ($key -notin @('-InputPath','-OutputPath','-StaticResources','-StaticProfile','-GodotPath','-TimeoutSeconds') -or
        $options.ContainsKey($key) -or $i + 1 -ge $args.Count -or [string]::IsNullOrWhiteSpace([string]$args[$i + 1]) -or ([string]$args[$i + 1]).StartsWith('-')) {
        [Console]::Error.WriteLine('Prepare-BattleContext argument error: expected unique -InputPath/-OutputPath/-StaticResources/-StaticProfile and optional -GodotPath/-TimeoutSeconds, each with a value.')
        exit 2
    }
    $options[$key] = [string]$args[$i + 1]
}
foreach ($required in @('-InputPath','-OutputPath','-StaticResources','-StaticProfile')) {
    if (-not $options.ContainsKey($required)) { [Console]::Error.WriteLine("Prepare-BattleContext argument error: $required is required."); exit 2 }
}
$timeout = 60
if ($options.ContainsKey('-TimeoutSeconds') -and (-not [int]::TryParse($options['-TimeoutSeconds'], [ref]$timeout) -or $timeout -lt 1 -or $timeout -gt 600)) {
    [Console]::Error.WriteLine('Prepare-BattleContext argument error: -TimeoutSeconds must be 1..600.'); exit 2
}
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
function Test-Inside([string]$Child,[string]$Parent) { $Child.Equals($Parent,$comparison) -or $Child.StartsWith($Parent.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar,$comparison) }
function Assert-NoLink([string]$Path) {
    $cursor=$Path
    while ($cursor) { try {$a=[IO.File]::GetAttributes($cursor)} catch [IO.FileNotFoundException] {$a=0} catch [IO.DirectoryNotFoundException] {$a=0}; if ($a -band [IO.FileAttributes]::ReparsePoint) { throw "Link/reparse point is not allowed: $cursor" }; $cursor=[IO.Path]::GetDirectoryName($cursor) }
}
function Assert-TreeNoLink([string]$Directory) { foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($Directory)) { $a=[IO.File]::GetAttributes($entry); if($a -band [IO.FileAttributes]::ReparsePoint){throw "Package contains a link/reparse point: $entry"}; if($a -band [IO.FileAttributes]::Directory){Assert-TreeNoLink $entry} } }
function Resolve-StaticAssetUri([string]$Uri,[string]$Name,[bool]$Directory) {
    if(-not ($Uri -ceq 'res://Assets' -or $Uri.StartsWith('res://Assets/',[StringComparison]::Ordinal))){throw "$Name must be a normalized res://Assets resource URI."}
    foreach($segment in $Uri.Substring(6).Split('/')){if(-not $segment-or$segment-in @('.','..')-or$segment.EndsWith('.')-or$segment-ne$segment.Trim()-or$segment.IndexOfAny([char[]]'\:%?#*"<>|')-ge 0-or@($segment.ToCharArray()|Where-Object{[char]::IsControl($_)}).Count-ne 0){throw "$Name must be a normalized res://Assets resource URI."}}
    $path=[IO.Path]::GetFullPath((Join-Path $project $Uri.Substring(6).Replace('/',[IO.Path]::DirectorySeparatorChar)))
    $assets=[IO.Path]::GetFullPath((Join-Path $project 'Assets'))
    if(-not (Test-Inside $path $assets)){throw "$Name must remain inside project Assets."}
    Assert-NoLink $path
    if($Directory){if(-not [IO.Directory]::Exists($path)){throw "$Name must select an existing directory."}}
    elseif(-not [IO.File]::Exists($path)){throw "$Name must select an existing file."}
    return $path
}
function Invoke-Checked([string]$File,[string[]]$Arguments,[string]$Marker) {
    $info=[Diagnostics.ProcessStartInfo]::new(); $info.FileName=$File; $info.WorkingDirectory=$project; $info.UseShellExecute=$false; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new(); $process.StartInfo=$info
    try { $null=$process.Start(); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync(); if(-not $process.WaitForExit($timeout*1000)){$process.Kill($true);$process.WaitForExit();throw "Preparation process timed out after $timeout seconds (process tree killed)."}; $text=$stdout.GetAwaiter().GetResult()+"`n"+$stderr.GetAwaiter().GetResult(); $plain=$text-replace '\x1B\[[0-?]*[ -/]*[@-~]',''; if($process.ExitCode-ne 0-or $plain-match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)'){throw "Preparation process failed (exit $($process.ExitCode)):`n$plain"}; if(-not $plain.Contains($Marker)){throw "Preparation process did not confirm $Marker`n$plain"} } finally {$process.Dispose()}
}
try {
    $inputRoot=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($options['-InputPath'])); $outputRoot=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($options['-OutputPath'])); $assets=Join-Path $project 'Assets'
    $staticRoot=Resolve-StaticAssetUri $options['-StaticResources'] 'StaticResources' $true
    $staticProfile=Resolve-StaticAssetUri $options['-StaticProfile'] 'StaticProfile' $false
    $staticScene=Join-Path $staticRoot 'Scenes/Scene.tscn'
    Assert-NoLink $staticScene; Assert-TreeNoLink $staticRoot; if(-not [IO.File]::Exists($staticScene)){throw 'StaticResources selected scene is missing.'}
    if(-not (Test-Inside $staticProfile $staticRoot)){throw 'StaticProfile must be inside StaticResources.'}
    if(-not (Test-Inside $outputRoot $assets)){throw 'OutputPath must be the project Assets directory or a directory inside it.'}
    if((Test-Inside $outputRoot $staticRoot)-or(Test-Inside $staticRoot $outputRoot)){throw 'OutputPath and StaticResources must not overlap in either direction.'}
    if((Test-Inside $outputRoot $inputRoot)-or(Test-Inside $inputRoot $outputRoot)){throw 'InputPath and OutputPath must not overlap in either direction.'}
    if(Test-Inside $inputRoot $project){throw 'InputPath must be outside src/Pal.Game.'}
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot
    if(-not [IO.Directory]::Exists($inputRoot)){throw 'InputPath must be an existing authored battle-context directory.'}
    Assert-TreeNoLink $inputRoot
    $files=@([IO.Directory]::EnumerateFiles($inputRoot,'*',[IO.SearchOption]::AllDirectories)|ForEach-Object{[IO.Path]::GetRelativePath($inputRoot,$_).Replace('\','/')})
    if(($files|Sort-Object)-join ',' -cne 'battle-context.json,index.json'){throw 'InputPath must contain exactly index.json and battle-context.json.'}
    if([IO.File]::Exists($outputRoot)){throw 'OutputPath is an existing file.'}
    if([IO.Directory]::Exists($outputRoot)-and @([IO.Directory]::EnumerateFileSystemEntries($outputRoot)).Count-ne 0){throw 'OutputPath must be new or empty; no overwrite is supported.'}
    $godot=$options['-GodotPath']; if(-not $godot){$godot=$env:GODOT_BIN}; if(-not $godot){$godot=(Get-Command godot -ErrorAction Stop).Source}; $godot=(Get-Command $godot -ErrorAction Stop).Source
    Invoke-Checked $godot @('--version') '4.7.2.stable.mono.official.ed1daf0bf'
    Invoke-Checked 'dotnet' @('build',(Join-Path $project 'Pal.Game.csproj'),'--nologo','--verbosity','quiet') ''
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot; Assert-TreeNoLink $inputRoot
    $null=Resolve-StaticAssetUri $options['-StaticResources'] 'StaticResources' $true; $null=Resolve-StaticAssetUri $options['-StaticProfile'] 'StaticProfile' $false; Assert-NoLink $staticScene; Assert-TreeNoLink $staticRoot
    Invoke-Checked $godot @('--headless','--path',$project,'res://Editor/PrepareBattleContext.tscn','--','--battle-input',$inputRoot,'--battle-output',$outputRoot,'--static-resources',$options['-StaticResources'],'--static-profile',$options['-StaticProfile']) 'PAL_PREPARE_BATTLE_CONTEXT:first-mandatory-battle-pre-trigger-v1'
    Assert-NoLink $inputRoot; Assert-NoLink $outputRoot; Assert-TreeNoLink $inputRoot; Assert-TreeNoLink $outputRoot
    $null=Resolve-StaticAssetUri $options['-StaticResources'] 'StaticResources' $true; $null=Resolve-StaticAssetUri $options['-StaticProfile'] 'StaticProfile' $false; Assert-NoLink $staticScene; Assert-TreeNoLink $staticRoot
    Write-Host 'Prepared native Godot battle context: Battle/Profile.tres'; exit 0
} catch { [Console]::Error.WriteLine("Prepare-BattleContext failed: $($_.Exception.Message)`nNo existing output is overwritten; temporary partial resources are not retained."); exit 1 }
