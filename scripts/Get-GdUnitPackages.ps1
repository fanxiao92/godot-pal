# Verify the complete pinned bundle before atomically exposing the repository-local NuGet source.
[CmdletBinding()]
param([string]$InputDirectory)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'gdunit-packages.lock.json') -Raw | ConvertFrom-Json
$destination = Join-Path $repository '.packages/gdunit'
$parent = [IO.Path]::GetDirectoryName($destination)
if (Test-Path -LiteralPath $parent) {
    $folder = Get-Item -LiteralPath $parent -Force
    if (-not $folder.PSIsContainer -or ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected a regular local package directory: $parent"
    }
}

function Assert-BundleFile([string]$Directory, $Asset) {
    $path = Join-Path $Directory $Asset.file
    $file = Get-Item -LiteralPath $path -Force
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected a regular bundle file: $path"
    }
    if ($file.Length -ne $Asset.size) { throw "Pinned length mismatch: $path (expected $($Asset.size), got $($file.Length))." }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Asset.sha256) {
        throw "Pinned SHA256 mismatch: $path"
    }
}
function Assert-Bundle([string]$Directory) {
    $folder = Get-Item -LiteralPath $Directory -Force
    if (-not $folder.PSIsContainer -or ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected a regular bundle directory: $Directory"
    }
    $names = @(Get-ChildItem -LiteralPath $Directory -Force | Select-Object -ExpandProperty Name)
    if (Compare-Object @($lock.assets.file | Sort-Object) @($names | Sort-Object)) {
        throw "Incomplete or unexpected bundle contents: $Directory. No existing files will be replaced."
    }
    foreach ($asset in $lock.assets) { Assert-BundleFile $Directory $asset }
}

# Explicit input is always checked, even when the destination is already ready.
if ($InputDirectory) {
    $InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
    Assert-Bundle $InputDirectory
}
if (Test-Path -LiteralPath $destination) {
    Assert-Bundle $destination
    Write-Host "Verified cached GdUnit bundle: $destination"
    return $destination
}
if (-not (Test-Path -LiteralPath $parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
$staging = Join-Path $parent ('gdunit-stage-' + [guid]::NewGuid().ToString('N'))
# CreateNew semantics: cleanup below owns only this newly-created staging directory.
New-Item -ItemType Directory -Path $staging -ErrorAction Stop | Out-Null
try {
    foreach ($asset in $lock.assets) {
        $target = Join-Path $staging $asset.file
        if ($InputDirectory) {
            [IO.File]::Copy((Join-Path $InputDirectory $asset.file), $target, $false)
        } else {
            $url = $lock.release.baseUrl + $asset.file
            Write-Host "Downloading pinned GdUnit asset: $url"
            Invoke-WebRequest -Uri $url -OutFile $target -ErrorAction Stop
        }
        Assert-BundleFile $staging $asset
    }
    Assert-Bundle $staging
    # Same-volume rename; an existing destination (including a concurrent writer) is never overwritten.
    [IO.Directory]::Move($staging, $destination)
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
Write-Host "Verified GdUnit bundle ready: $destination"
$destination
