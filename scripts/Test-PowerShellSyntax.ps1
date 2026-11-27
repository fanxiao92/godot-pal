# Parse only (with optional AST rule): never executes, imports or dot-sources a selected file.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [AllowEmptyCollection()][AllowEmptyString()][string[]]$Path,
    [switch]$RejectParameterlessWaitForExit
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
    if (-not $explicit) {
        $Path = @('scripts', 'tests')
        if (Test-Path -LiteralPath (Join-Path $root 'pal.ps1') -PathType Leaf) { $Path = @('pal.ps1') + $Path }
    }
    $excluded = @('bin', 'obj', '.godot', '.packages', '.artifacts', 'TestResults', 'reference', '.pi', '.git')
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
            $ast = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
            foreach ($errorRecord in $errors) {
                Diagnostic 'error' ("{0}:{1}:{2}: {3}" -f $file, $errorRecord.Extent.StartLineNumber, $errorRecord.Extent.StartColumnNumber, $errorRecord.Message)
            }
            $trimCalls = $ast.FindAll({ param($node)
                $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Member -is [Management.Automation.Language.StringConstantExpressionAst] -and
                $node.Member.Value -ieq 'TrimStart' -and $node.Arguments.Count -eq 1 -and
                $node.Arguments[0] -is [Management.Automation.Language.StringConstantExpressionAst] -and
                $node.Arguments[0].Value -in @('./', '.\')
            }, $true)
            foreach ($call in $trimCalls) {
                Diagnostic 'trimstart-path-prefix' ("{0}:{1}:{2}: TrimStart('{3}') strips leading dots from dotfiles; use -replace '^\./', '' instead." -f $file, $call.Extent.StartLineNumber, $call.Extent.StartColumnNumber, $call.Arguments[0].Value)
            }
            if ([IO.Path]::GetExtension($file) -ieq '.psm1') {
                $importCalls = $ast.FindAll({ param($node)
                    if ($node -isnot [Management.Automation.Language.CommandAst] -or ($node.GetCommandName() -ine 'Import-Module')) { return $false }
                    $params = @($node.CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] } | ForEach-Object ParameterName)
                    return ($params -contains 'Force') -and ($params -notcontains 'Global')
                }, $true)
                foreach ($call in $importCalls) {
                    Diagnostic 'nested-force-import' ("{0}:{1}:{2}: Import-Module -Force inside a .psm1 module without -Global shadows caller exports." -f $file, $call.Extent.StartLineNumber, $call.Extent.StartColumnNumber)
                }
            }
            if ($RejectParameterlessWaitForExit) {
                $calls = $ast.FindAll({ param($node)
                    $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
                    $node.Member -is [Management.Automation.Language.StringConstantExpressionAst] -and
                    $node.Member.Value -ieq 'WaitForExit' -and $node.Arguments.Count -eq 0
                }, $true)
                foreach ($call in $calls) {
                    Diagnostic 'parameterless-wait-for-exit' ("{0}:{1}:{2}: WaitForExit() requires an explicit timeout argument." -f $file, $call.Extent.StartLineNumber, $call.Extent.StartColumnNumber)
                }
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
