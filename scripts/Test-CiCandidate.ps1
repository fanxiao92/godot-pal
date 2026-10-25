[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EventPath,
    [Parameter(Mandatory)][string]$MetadataPath,
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$ServerSnapshotPath,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
function Fail([int]$Code, [string]$Message) { throw "[candidate:$Code] $Message" }
function Read-Json([string]$Path, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail 2 "$Name file is missing." }
    try { $text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($Path), $utf8); $value = $text | ConvertFrom-Json -Depth 20 -NoEnumerate }
    catch { Fail 2 "$Name is not valid strict UTF-8 JSON: $($_.Exception.Message)" }
    if ($null -eq $value -or $value -is [array] -or $value -isnot [pscustomobject]) { Fail 2 "$Name root must be a JSON object." }
    return $value
}
function Required-String($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($property.Value)) { Fail 2 "$Name must be a nonempty string." }
    return $property.Value
}
function Sha([string]$Value, [string]$Name) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { Fail 2 "$Name must be a 40-character SHA-1." }
    return $Value.ToLowerInvariant()
}
function Required-Bool($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) { Fail 2 "$Name must be boolean." }
    return [bool]$property.Value
}
function Required-PositiveInteger($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -is [bool] -or $property.Value -isnot [ValueType] -or $property.Value -lt 1 -or $property.Value -ne [Math]::Truncate([double]$property.Value)) { Fail 2 "$Name must be a positive integer." }
    return [long]$property.Value
}
function Git([string[]]$Arguments) {
    $gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $info = [Diagnostics.ProcessStartInfo]::new($gitPath)
    $info.WorkingDirectory = [IO.Path]::GetFullPath($RepositoryRoot); $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $utf8; $info.StandardErrorEncoding = $utf8
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    $stdout = $null; $stderr = $null
    try {
        if (-not $process.Start()) { Fail 2 'Could not start git.' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit(); $stdout = $stdoutTask.GetAwaiter().GetResult(); $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { Fail 2 "git failed ($($process.ExitCode)) for '$($Arguments -join ' ')': $stderr" }
        return $stdout.Trim()
    } finally { $process.Dispose() }
}
try {
    if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) { Fail 2 'RepositoryRoot is missing.' }
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    if (-not (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)) { $null = New-Item -ItemType Directory -Path $EvidenceDirectory }
    $evidence = [IO.Path]::GetFullPath($EvidenceDirectory); $output = Join-Path $evidence 'candidate-identity.json'
    $event = Read-Json $EventPath 'event'; $metadata = Read-Json $MetadataPath 'metadata'; $snapshot = Read-Json $ServerSnapshotPath 'server snapshot'
    $eventName = Required-String $event 'eventName'; $action = Required-String $event 'action'; $mode = Required-String $event 'mode'
    $githubSha = Sha (Required-String $metadata 'githubSha') 'githubSha'; $runId = Required-String $metadata 'runId'; $workflowRef = Required-String $metadata 'workflowRef'
    $runAttempt = Required-PositiveInteger $metadata 'runAttempt'; $draft = Required-Bool $metadata 'draft'
    $actualHead = Sha (Git @('rev-parse','HEAD^{commit}')) 'actual checkout'
    $tree = Sha (Git @('show','-s','--format=%T','HEAD^{commit}')) 'actual tree'
    $parentText = Git @('show','-s','--format=%P','HEAD^{commit}'); $parents = @($parentText -split '\s+' | Where-Object { $_ })
    foreach ($parent in $parents) { $null = Sha $parent 'actual parent' }
    $head = $null; $base = $null; $candidate = $githubSha
    if ($mode -in @('draft-quick','ready-full')) {
        if ($eventName -cne 'pull_request') { Fail 2 "$mode requires pull_request event." }
        $head = Sha (Required-String $metadata 'prHeadSha') 'prHeadSha'; $base = Sha (Required-String $metadata 'prBaseSha') 'prBaseSha'
        if ($mode -eq 'draft-quick' -and -not $draft) { Fail 2 'draft-quick requires draft=true.' }
        if ($mode -eq 'ready-full' -and $draft) { Fail 2 'ready-full requires draft=false.' }
        if ($actualHead -cne $githubSha -or $parents.Count -ne 2 -or $parents[0] -cne $base -or $parents[1] -cne $head) { Fail 2 'PR checkout/head/base/merge candidate identity does not match ordered git history.' }
    } elseif ($mode -in @('push-main','manual-full')) {
        if ($draft) { Fail 2 'Non-PR mode cannot carry draft=true.' }
        if ($mode -eq 'push-main' -and ($eventName -cne 'push' -or $action -cne 'main')) { Fail 2 'push-main requires push/main event.' }
        if ($mode -eq 'manual-full' -and $eventName -cne 'workflow_dispatch') { Fail 2 'manual-full requires workflow_dispatch event.' }
        if ($actualHead -cne $githubSha -or $parents.Count -gt 2) { Fail 2 'Non-PR checkout must be the GitHub SHA and have a bounded commit parent list.' }
    } else { Fail 2 "Unsupported mode: $mode" }
    $initial = [ordered]@{ eventName=$eventName; action=$action; mode=$mode; runId=$runId; runAttempt=$runAttempt; workflowRef=$workflowRef; draft=$draft; headSha=$head; baseSha=$base; candidateSha=$candidate; actualCheckout=$actualHead; tree=$tree; parents=@($parents) }
    foreach ($name in @('candidateSha','actualCheckout','tree')) { $null = Sha $initial[$name] $name }
    foreach ($name in @('eventName','action','mode','runId','workflowRef','candidateSha','actualCheckout','tree')) {
        $property = $snapshot.PSObject.Properties[$name]
        if ($null -eq $property -or $property.Value -isnot [string] -or $property.Value -cne $initial[$name]) { Fail 3 "Server snapshot stale or mismatched: $name (API identity must match current run context)." }
    }
    foreach ($name in @('runAttempt','draft')) {
        $property = $snapshot.PSObject.Properties[$name]
        if ($null -eq $property -or $property.Value.GetType() -ne $initial[$name].GetType() -or $property.Value -ne $initial[$name]) { Fail 3 "Server snapshot stale or mismatched: $name." }
    }
    foreach ($name in @('headSha','baseSha')) {
        if ($null -ne $initial[$name]) { $property = $snapshot.PSObject.Properties[$name]; if ($null -eq $property -or $property.Value -isnot [string] -or $property.Value -cne $initial[$name]) { Fail 3 "Server snapshot stale or mismatched: $name." } }
    }
    $snapshotParents = $snapshot.PSObject.Properties['parents']; if ($null -eq $snapshotParents -or $snapshotParents.Value -isnot [array] -or @($snapshotParents.Value).Count -ne $parents.Count) { Fail 3 'Server snapshot stale or mismatched: parents.' }
    for ($index=0; $index -lt $parents.Count; $index++) { if ($snapshotParents.Value[$index] -cne $parents[$index]) { Fail 3 'Server snapshot stale or mismatched: parents.' } }
    if (Test-Path -LiteralPath $output) { Fail 4 'Candidate evidence already exists; refusing overwrite.' }
    $json = $initial | ConvertTo-Json -Depth 10
    try { $stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None); try { $bytes = $utf8.GetBytes($json); $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() } } catch { Fail 4 "Cannot atomically create candidate evidence: $($_.Exception.Message)" }
    Write-Output "PASS candidate identity: $mode; evidence $output"; exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message); $failureExit = if ($_.Exception.Message -match '^\[candidate:([0-9]+)\]') { [int]$Matches[1] } else { 4 }; exit $failureExit
}
