# Syntax only: Parser never executes, imports or dot-sources a selected file.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [AllowEmptyCollection()][AllowEmptyString()][string[]]$Path
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$failed = $false
function Diagnostic([string]$Code, [string]$Message) {
    $script:failed = $true
    [Console]::Error.WriteLine("[syntax:$Code] $Message")
}
try {
    if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) { throw '[syntax:missing-root] RepositoryRoot must exist.' }
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $explicit = $PSBoundParameters.ContainsKey('Path')
    if ($explicit -and (-not $Path -or @($Path | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count)) {
        throw '[syntax:empty] Explicit Path must contain nonblank objects.'
    }
    if (-not $explicit) { $Path = @('scripts', 'tests') }
    $excluded = @('bin', 'obj', '.godot', '.packages', 'TestResults', 'reference', '.pi', '.git')
    $files = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    function Is-Excluded([string]$File) {
        $relative = [IO.Path]::GetRelativePath($root, $File)
        if ($relative -eq '..' -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or [IO.Path]::IsPathRooted($relative)) { return $false }
        foreach ($part in ($relative -split '[/\\]')) { if ($part -in $excluded) { return $true } }
        return $false
    }
    foreach ($item in $Path) {
        $full = [IO.Path]::GetFullPath($item, $root)
        if (-not (Test-Path -LiteralPath $full)) { Diagnostic 'missing' $full; continue }
        $entry = Get-Item -LiteralPath $full
        if ((Is-Excluded $full) -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Diagnostic 'range' "Excluded or linked target: $full"; continue
        }
        if (-not $entry.PSIsContainer) {
            if ($entry.Extension -notin '.ps1', '.psm1', '.psd1') { Diagnostic 'range' "Unsupported source extension: $full" }
            else { [void]$files.Add($full) }
            continue
        }
        $relative = [IO.Path]::GetRelativePath($root, $full)
        if ($relative -eq '..' -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or [IO.Path]::IsPathRooted($relative)) {
            Diagnostic 'range' 'Directory traversal must remain inside declared RepositoryRoot; select generated external files individually.'
            continue
        }
        $pending = [Collections.Generic.Stack[string]]::new()
        $pending.Push($full)
        while ($pending.Count) {
            foreach ($child in (Get-ChildItem -LiteralPath $pending.Pop() -Force)) {
                if ((Is-Excluded $child.FullName) -or ($child.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
                if ($child.PSIsContainer) { $pending.Push($child.FullName) }
                elseif ($child.Extension -in '.ps1', '.psm1', '.psd1') { [void]$files.Add($child.FullName) }
            }
        }
    }
    if (-not $files.Count) { Diagnostic 'empty' 'No PowerShell source objects selected.' }
    foreach ($file in $files) {
        try {
            $tokens = $null; $errors = $null
            $null = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
            foreach ($errorRecord in $errors) {
                Diagnostic 'error' ("{0}:{1}:{2}: {3}" -f $file, $errorRecord.Extent.StartLineNumber, $errorRecord.Extent.StartColumnNumber, $errorRecord.Message)
            }
        } catch { Diagnostic 'read' "${file}: $($_.Exception.Message)" }
    }
    if ($failed) { exit 1 }
    Write-Output "PASS syntax: $($files.Count) file(s)"
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
