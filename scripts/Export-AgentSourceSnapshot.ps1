# Read-only Git + raw-byte hashes. No patch reconstruction, checkout, SDK or acceptance.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$GitExecutable,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$BaselinePath,
    [string[]]$AllowedChangedPath = @()
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Import-Module (Join-Path $PSScriptRoot 'AgentEvidenceJson.psm1')
$records = [Collections.Generic.List[object]]::new()
function Git-Read([string[]]$Arguments) {
    $n = $records.Count + 1
    $info = [Diagnostics.ProcessStartInfo]::new($git)
    $info.WorkingDirectory = $root; $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.Environment['GIT_OPTIONAL_LOCKS'] = '0'
    foreach ($arg in @('--no-optional-locks', '-C', $root) + $Arguments) { $info.ArgumentList.Add($arg) }
    $record = [ordered]@{ executable = $git; arguments = @($info.ArgumentList); cwd = $root; startedUtc = [DateTimeOffset]::UtcNow.ToString('o'); exitCode = $null; rootExited = $false; stdoutComplete = $false; stderrComplete = $false; cleanupComplete = $false; timedOut = $false }
    $records.Add($record)
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    $stdoutPath = Join-Path $out "$n.stdout"; $stderrPath = Join-Path $out "$n.stderr"
    $stdout = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew)
    $stderr = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew)
    $started = $false
    try {
        $started = $process.Start()
        if (-not $started) { throw 'Git did not start.' }
        $record.pid = $process.Id
        $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
        $all = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(), $copyOut, $copyErr))
        if (-not $all.Wait(30000)) { $record.timedOut = $true; throw 'Git read incomplete; retain evidence and stop.' }
        $record.exitCode = $process.ExitCode; $record.rootExited = $true
        $record.stdoutComplete = $copyOut.IsCompletedSuccessfully; $record.stderrComplete = $copyErr.IsCompletedSuccessfully
        $record.cleanupComplete = $true
    } finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true); $record.cleanupComplete = $process.WaitForExit(5000)
        }
        $stdout.Dispose(); $stderr.Dispose(); $process.Dispose()
        $record.endedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Write-AgentNewJson (Join-Path $out "$n.command.json") $record
    }
    if ($record.exitCode -ne 0 -or -not $record.rootExited -or -not $record.stdoutComplete -or -not $record.stderrComplete -or -not $record.cleanupComplete) { throw "Git read failed: $($Arguments -join ' '); see $n.command.json" }
    return [IO.File]::ReadAllText($stdoutPath, [Text.UTF8Encoding]::new($false, $true))
}
function Source-Files {
    $paths = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in ((Git-Read @('ls-files', '-z', '--cached', '--others', '--exclude-standard')) -split "`0")) {
        if ($path) { [void]$paths.Add($path) }
    }
    $files = [ordered]@{}
    $verifiedParents = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$verifiedParents.Add($root)
    foreach ($path in $paths) {
        $full = [IO.Path]::GetFullPath($path, $root)
        if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Off-tree source path: $path" }
        $parent = $full
        while ($parent -and -not $verifiedParents.Contains($parent)) {
            if ([IO.File]::Exists($parent) -or [IO.Directory]::Exists($parent)) {
                if ([IO.File]::GetAttributes($parent) -band [IO.FileAttributes]::ReparsePoint) { throw "Linked source path is not supported: $path" }
            }
            [void]$verifiedParents.Add($parent)
            $parent = [IO.Path]::GetDirectoryName($parent)
        }
        if ([IO.Directory]::Exists($full)) { throw "Source path is not a regular file: $path" }
        if (-not [IO.File]::Exists($full)) { $files[$path] = $null; continue }
        $stream = [IO.File]::OpenRead($full)
        try { $files[$path] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    return ,$files
}
try {
    $root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
    $git = [IO.Path]::GetFullPath($GitExecutable)
    if (-not (Test-Path -LiteralPath $root -PathType Container) -or -not (Test-Path -LiteralPath $git -PathType Leaf)) { throw 'Explicit repository and Git executable must exist.' }
    # Reject ordinary subdirectories before even creating output under a false root.
    if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { throw 'RepositoryRoot must be the Git worktree root, not a subdirectory.' }
    $out = [IO.Path]::GetFullPath($EvidenceDirectory)
    if (Test-Path -LiteralPath $out) { throw 'EvidenceDirectory must be new.' }
    $relativeOutput = [IO.Path]::GetRelativePath($root, $out)
    if ($relativeOutput -ne '..' -and -not $relativeOutput.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -and -not [IO.Path]::IsPathRooted($relativeOutput)) { throw 'Keep snapshot output outside the repository.' }
    # Claim a new directory by rename; CreateDirectory alone can merge a racing output.
    $staging = $out + '.staging-' + [guid]::NewGuid().ToString('N')
    $null = [IO.Directory]::CreateDirectory($staging)
    [IO.Directory]::Move($staging, $out)
    $topLevel = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath((Git-Read @('rev-parse', '--show-toplevel')).Trim()))
    if ($topLevel -ine $root) { throw 'RepositoryRoot must be the Git worktree root, not a subdirectory.' }
    $head = (Git-Read @('rev-parse', 'HEAD')).Trim()
    $tree = (Git-Read @('rev-parse', 'HEAD^{tree}')).Trim()
    $branch = (Git-Read @('rev-parse', '--abbrev-ref', 'HEAD')).Trim()
    $null = Git-Read @('diff', '--no-ext-diff', '--no-textconv', '--cached', '--quiet')
    $files = Source-Files
    $second = Source-Files
    if ((ConvertTo-Json -InputObject $files -Compress) -cne (ConvertTo-Json -InputObject $second -Compress) -or (Git-Read @('rev-parse', 'HEAD')).Trim() -cne $head) { throw 'Source changed during snapshot; no stable binding produced.' }
    $null = Git-Read @('diff', '--no-ext-diff', '--no-textconv', '--cached', '--quiet')
    if ((Git-Read @('rev-parse', '--abbrev-ref', 'HEAD')).Trim() -cne $branch) { throw 'Branch changed during snapshot.' }
    $changed = [Collections.Generic.List[string]]::new()
    if ($BaselinePath) {
        $baselineFile = [IO.Path]::GetFullPath($BaselinePath)
        $baselineRaw = [IO.File]::ReadAllBytes($baselineFile)
        $baselineSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($baselineRaw)).ToLowerInvariant()
        Write-AgentNewFile (Join-Path $out 'baseline.raw.json') $baselineRaw
        $base = Read-AgentJson (Join-Path $out 'baseline.raw.json')
        if ($base['head'] -cne $head -or $base['tree'] -cne $tree -or $base['branch'] -cne $branch -or [IO.Path]::GetFullPath($base['worktree']) -ine $root) { throw 'Baseline Git/worktree identity differs.' }
        if (-not $base['files'] -or -not ($base['files'] -is [Collections.IDictionary])) { throw 'Baseline requires a nonempty files map.' }
        $allPaths = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
        foreach ($path in $base['files'].Keys) { [void]$allPaths.Add($path) }
        foreach ($path in $files.Keys) { [void]$allPaths.Add($path) }
        foreach ($path in $allPaths) {
            if (-not $base['files'].ContainsKey($path) -or -not $files.Contains($path) -or $base['files'][$path] -cne $files[$path]) { $changed.Add($path) }
        }
        foreach ($path in $changed) { if ($path -cnotin $AllowedChangedPath) { throw "Out-of-scope source delta: $path" } }
    } elseif ($AllowedChangedPath.Count) { throw 'AllowedChangedPath requires BaselinePath.' }
    if ($BaselinePath -and (Get-FileHash -LiteralPath $baselineFile).Hash.ToLowerInvariant() -cne $baselineSha) { throw 'Baseline changed during comparison.' }
    $binding = [ordered]@{ kind = 'agent-source-snapshot'; worktree = $root; head = $head; tree = $tree; branch = $branch; files = $files; comparedBaseline = $BaselinePath; baselineSha256 = $baselineSha; changedPaths = $changed.ToArray(); indexEmpty = $true; quiescentWriterRequired = $true; runtimeVerified = $false; acceptance = $false }
    Write-AgentNewJson (Join-Path $out 'binding.json') $binding
    [ordered]@{ status = 'complete'; head = $head; sourceFiles = $files.Count; changedPaths = $changed.ToArray(); binding = (Join-Path $out 'binding.json'); runtimeVerified = $false } | ConvertTo-Json -Depth 5
    exit 0
} catch {
    [Console]::Error.WriteLine("[agent-source] $($_.Exception.Message)")
    exit 1
}
