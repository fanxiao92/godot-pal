# Idempotent cross-platform bridge from .claude/skills -> .agents/skills (#277).
# Uses NTFS Junction on Windows (no admin/dev-mode needed) and SymbolicLink on Linux/macOS.
# Never commits Git symlinks (120000); /.claude/ is gitignored.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$target = Join-Path $root '.agents/skills'
if (-not [IO.Directory]::Exists($target)) {
    throw "Canonical skills directory not found: $target"
}

$claudeDir = Join-Path $root '.claude'
$linkPath = Join-Path $claudeDir 'skills'
[void][IO.Directory]::CreateDirectory($claudeDir)

if (Test-Path -LiteralPath $linkPath) {
    $item = Get-Item -LiteralPath $linkPath -Force
    if ($item.LinkType) {
        $resolvedTarget = [IO.Path]::GetFullPath(($item.Target | Select-Object -First 1))
        if ($resolvedTarget -ieq [IO.Path]::GetFullPath($target)) {
            Write-Output "OK agent-skills link already up-to-date: .claude/skills -> .agents/skills ($($item.LinkType))"
            exit 0
        }
        Remove-Item -LiteralPath $linkPath -Force
    } else {
        throw "Refusing to overwrite non-link directory/file at $linkPath. Remove it manually and re-run."
    }
}

$linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
New-Item -ItemType $linkType -Path $linkPath -Target $target | Out-Null
Write-Output "CREATED agent-skills link: .claude/skills -> .agents/skills ($linkType)"
exit 0
