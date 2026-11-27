# Download pinned official Godot 4.7.2-stable .NET archive into a new directory; does not modify PATH.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [ValidateSet('auto', 'win64', 'linux')][string]$Platform = 'auto'
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Destination) {
    throw 'Use a new destination directory to avoid mixing engine versions.'
}

$resolvedPlatform = if ($Platform -ne 'auto') {
    $Platform
} elseif ($IsLinux) {
    'linux'
} elseif ($IsWindows) {
    'win64'
} else {
    throw 'Unsupported OS for automatic Godot download; pass -Platform win64 or linux explicitly.'
}

# Pinned from the official 4.7.2-stable SHA512-SUMS.txt, never fetched alongside each run.
$packages = @{
    win64 = @{
        Asset = 'Godot_v4.7.2-stable_mono_win64.zip'
        Sha512 = '79229fd112b0c9cbeab82363a4ef7be18ea70f1caf86bf912789335b136fbe7e01db0053a33461438c5da1c680c17bbb10040bd09bedc51221cd4423d0367757'
        ExecutablePattern = '*console.exe'
        NeedsChmod = $false
    }
    linux = @{
        Asset = 'Godot_v4.7.2-stable_mono_linux_x86_64.zip'
        Sha512 = '1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13'
        ExecutablePattern = 'Godot_v4.7.2-stable_mono_linux.x86_64'
        NeedsChmod = $true
    }
}

$pkg = $packages[$resolvedPlatform]
New-Item -ItemType Directory -Path $Destination | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path
$url = "https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/$($pkg.Asset)"
$archive = Join-Path $Destination $pkg.Asset

Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $archive
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
if ($actualHash -ne $pkg.Sha512) {
    throw "Godot archive SHA512 does not match the pinned official checksum ($resolvedPlatform)."
}

$engineDir = Join-Path $Destination 'engine'
Expand-Archive -LiteralPath $archive -DestinationPath $engineDir
$executables = @(Get-ChildItem -LiteralPath $engineDir -Recurse -File -Filter $pkg.ExecutablePattern)
if ($executables.Count -ne 1) {
    throw "Expected exactly one Godot executable matching '$($pkg.ExecutablePattern)', found $($executables.Count)."
}

$executablePath = $executables[0].FullName
if ($pkg.NeedsChmod -and -not $IsWindows) {
    & chmod +x $executablePath
    if ($LASTEXITCODE -ne 0) {
        throw "chmod +x failed for $executablePath (exit $LASTEXITCODE)."
    }
}

Write-Host "Verified official Godot .NET archive ($resolvedPlatform): $($pkg.Sha512)"
$executablePath
