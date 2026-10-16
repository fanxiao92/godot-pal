# Download into a new temporary directory; this does not install or modify PATH.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
if (Test-Path $Destination) { throw 'Use a new destination directory to avoid mixing engine versions.' }
New-Item -ItemType Directory -Path $Destination | Out-Null
$Destination = (Resolve-Path $Destination).Path
$asset = 'Godot_v4.7.2-stable_mono_win64.zip'
$url = "https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/$asset"
# Pinned from the official 4.7.2-stable SHA512-SUMS.txt, not downloaded alongside each run.
$expectedHash = '79229fd112b0c9cbeab82363a4ef7be18ea70f1caf86bf912789335b136fbe7e01db0053a33461438c5da1c680c17bbb10040bd09bedc51221cd4423d0367757'
$archive = Join-Path $Destination $asset
Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA512).Hash -ne $expectedHash) {
    throw 'Godot archive SHA512 does not match the pinned official checksum.'
}
Expand-Archive -Path $archive -DestinationPath (Join-Path $Destination 'engine')
$console = @(Get-ChildItem (Join-Path $Destination 'engine') -Recurse -Filter '*console.exe')
if ($console.Count -ne 1) { throw 'Expected exactly one Godot console executable.' }
Write-Host "Verified official Godot .NET archive: $expectedHash"
$console[0].FullName
