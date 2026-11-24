# Public offline maintenance regression: synthetic state and read-only Git, no SDK/engine/network.
[CmdletBinding()]
param([string]$EvidenceDirectory, [string]$GitExecutable = (Get-Command git -ErrorAction Stop).Source)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../scripts/AgentEvidenceJson.psm1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hostPath = (Get-Process -Id $PID).Path
$out = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('agent-workflow-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $out) { throw 'EvidenceDirectory must be new.' }
$null = [IO.Directory]::CreateDirectory($out)
$foreign = Join-Path $out '中文 foreign cwd'; $null = [IO.Directory]::CreateDirectory($foreign)
$commands = [Collections.Generic.List[object]]::new(); $completed = $false
function Assert([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Check([string]$Name, [string]$Entry, [string[]]$Arguments, [int]$Expected = 0, [string]$Reason = '') {
    $n = $commands.Count + 1
    $argv = @('-NoProfile', '-File', (Join-Path $root "scripts/$Entry")) + $Arguments
    Push-Location $foreign
    try { & $hostPath @argv 1> (Join-Path $out "$n.stdout") 2> (Join-Path $out "$n.stderr"); $actual = $LASTEXITCODE } finally { Pop-Location }
    $commands.Add(@{ name = $Name; executable = $hostPath; arguments = $argv; cwd = $foreign; exitCode = $actual; expectedExit = $Expected })
    # Journal is a live result, not a preserved input.
    [IO.File]::WriteAllText((Join-Path $out 'commands.json'), (ConvertTo-Json -InputObject $commands.ToArray() -Depth 8))
    Assert ($actual -eq $Expected) "${Name}: expected $Expected, got $actual; see $out/$n.stderr"
    if ($Reason) { Assert ([IO.File]::ReadAllText((Join-Path $out "$n.stderr")) -match $Reason) "${Name}: wrong failure reason" }
    Write-Host "PASS $Name"
}
try {
    $history = Join-Path $out 'history.json'; Write-AgentNewJson $history @{ original = $true }
    $inputFile = Join-Path $out 'current.json'
    $receipts = [ordered]@{}; foreach ($n in 1..200) { $receipts["old$n"] = @{ receipt = "archive/unit$n/original.json"; detail = 'Historical detail, not an active authorization.' } }
    $state = @{
        schemaVersion = 1; kind = 'derived_execution_index_not_issue_tracker'; scope = @{ stories = 164; fullM6 = $false }; integration = @{ acceptedLeaves = 14 }
        active_ticket = @{ number = 161; state = 'candidate'; source = @{ worktree = $root; branch = 'fixture'; head = 'a' * 40; tree = 'b' * 40; dirty = $true; binding = 'binding.json' }; authority = @{ sdkRemaining = 0; writer = $null; runId = $null; deadlineUtc = $null }; lastAttempt = @{ outcome = 'stopped'; receipt = 'stop.json' }; checks = @(); openContracts = @(@{ kind = 'unclosed' }); limits = @('No automatic retry.'); inputs = @{ brief = 'historical.md' } }
        receipts = $receipts; history = @{ snapshot = $history; sha256 = (Get-FileHash $history).Hash.ToLowerInvariant() }; nextAction = @{ action = 'plan'; reason = 'fresh planning only'; plan = 'plan.md' }; updatedUtc = '2026-11-09T02:00:00.1234567Z'
    }
    # BOM and CRLF must survive archival exactly; parsed dates must remain strings.
    $text = (ConvertTo-Json -InputObject $state -Depth 30) -replace '\r?\n', "`r`n"
    [IO.File]::WriteAllText($inputFile, $text, [Text.UTF8Encoding]::new($true))
    $beforeSha = (Get-FileHash $inputFile).Hash.ToLowerInvariant()
    $packet = Join-Path $out 'packet'
    Check 'idle export preserves raw bytes and reduces receipts' 'Export-AgentHandoff.ps1' @('-InputPath', $inputFile, '-OutputDirectory', $packet, '-ReceiptKey', 'old200')
    Assert ((Get-FileHash $inputFile).Hash.ToLowerInvariant() -ceq $beforeSha) 'Read-only export modified current.'
    Assert ((Get-FileHash (Join-Path $packet 'before.raw.json')).Hash.ToLowerInvariant() -ceq $beforeSha) 'BOM/CRLF raw bytes changed.'
    $compact = Read-AgentJson (Join-Path $packet 'current.compact.json')
    Assert ($compact['receipts'].Count -eq 1 -and $compact['updatedUtc'] -ceq $state.updatedUtc) 'Receipt selection/date precision changed.'
    foreach ($key in @('scope', 'integration', 'active_ticket', 'nextAction')) {
        $original = Read-AgentJson $inputFile
        Assert ((ConvertTo-Json -InputObject $compact[$key] -Depth 30 -Compress) -ceq (ConvertTo-Json -InputObject $original[$key] -Depth 30 -Compress)) "Lost current $key"
    }
    Assert ((Get-Item (Join-Path $packet 'current.compact.json')).Length -lt (Get-Item $inputFile).Length / 4) 'History still dominates context.'
    Check 'existing packet is never overwritten' 'Export-AgentHandoff.ps1' @('-InputPath', $inputFile, '-OutputDirectory', $packet, '-ReceiptKey', 'old200') 1 'must be new'
    Check 'missing receipt fails before publication' 'Export-AgentHandoff.ps1' @('-InputPath', $inputFile, '-OutputDirectory', (Join-Path $out 'missing'), '-ReceiptKey', 'absent') 1 'receipt key'
    Check 'stale hash cannot apply' 'Export-AgentHandoff.ps1' @('-InputPath', $inputFile, '-OutputDirectory', (Join-Path $out 'stale'), '-ReceiptKey', 'old200', '-Apply', '-ExpectedSha256', ('0' * 64)) 1 'ExpectedSha256'
    Check 'Apply requires an expected original hash' 'Export-AgentHandoff.ps1' @('-InputPath', $inputFile, '-OutputDirectory', (Join-Path $out 'unguarded'), '-ReceiptKey', 'old200', '-Apply') 1 'ExpectedSha256'
    $state.history.sha256 = '0' * 64
    $badHistory = Join-Path $out 'bad-history.json'; Write-AgentNewJson $badHistory $state
    Check 'changed history blocks export before publication' 'Export-AgentHandoff.ps1' @('-InputPath', $badHistory, '-OutputDirectory', (Join-Path $out 'bad-history-packet'), '-ReceiptKey', 'old200') 1 'History snapshot hash differs'
    Assert (-not (Test-Path (Join-Path $out 'bad-history-packet'))) 'Invalid history published a packet.'
    $state.history.sha256 = (Get-FileHash $history).Hash.ToLowerInvariant()
    $state.active_ticket.authority.writer = 'retained-worker'
    $retained = Join-Path $out 'retained-writer.json'; Write-AgentNewJson $retained $state
    Check 'idle state cannot retain writer authority' 'Export-AgentHandoff.ps1' @('-InputPath', $retained, '-OutputDirectory', (Join-Path $out 'retained-writer-packet'), '-ReceiptKey', 'old200') 1 'agent-evidence:schema'
    $state.active_ticket.authority.writer = $null
    $state.updatedUtc = 'not-a-time'
    $invalidTime = Join-Path $out 'invalid-time.json'; Write-AgentNewJson $invalidTime $state
    $invalidTimeSha = (Get-FileHash $invalidTime).Hash.ToLowerInvariant()
    Check 'classifier rejection never applies a compact candidate' 'Export-AgentHandoff.ps1' @('-InputPath', $invalidTime, '-OutputDirectory', (Join-Path $out 'invalid-time-packet'), '-ReceiptKey', 'old200', '-Apply', '-ExpectedSha256', $invalidTimeSha) 1 'Compact candidate rejected'
    Assert ((Get-FileHash $invalidTime).Hash.ToLowerInvariant() -ceq $invalidTimeSha) 'Rejected candidate replaced current.'
    Assert (Test-Path (Join-Path $out 'invalid-time-packet/before.raw.json')) 'Rejected candidate lost its raw archive.'
    $state.updatedUtc = '2026-11-09T02:00:00.1234567Z'
    $state.active_ticket.state = 'running'; $state.active_ticket.authority = @{ sdkRemaining = 1; writer = 'worker'; runId = 'active'; deadlineUtc = '2026-11-09T03:00:00Z' }
    $running = Join-Path $out 'running.json'; Write-AgentNewJson $running $state
    Check 'running writer cannot export a session handoff' 'Export-AgentHandoff.ps1' @('-InputPath', $running, '-OutputDirectory', (Join-Path $out 'active'), '-ReceiptKey', 'old200') 1 'Return all execution authority'
    $state.active_ticket.authority.sdkRemaining = 0; $state.active_ticket.authority.writer = $null
    $runningIdle = Join-Path $out 'running-idle.json'; Write-AgentNewJson $runningIdle $state
    Check 'running state alone prevents handoff' 'Export-AgentHandoff.ps1' @('-InputPath', $runningIdle, '-OutputDirectory', (Join-Path $out 'running-idle-packet'), '-ReceiptKey', 'old200') 1 'Return all execution authority'
    $duplicate = Join-Path $out 'duplicate.json'
    [IO.File]::WriteAllText($duplicate, '{"schemaVersion":1,"schemaVersion":1}')
    Check 'duplicate JSON keys rejected' 'Export-AgentHandoff.ps1' @('-InputPath', $duplicate, '-OutputDirectory', (Join-Path $out 'duplicate-packet'), '-ReceiptKey', 'old200') 1 'Duplicate property'
    $applied = Join-Path $out 'applied'
    Check 'explicit Apply atomically replaces only the compact candidate' 'Export-AgentHandoff.ps1' @('-InputPath', $inputFile, '-OutputDirectory', $applied, '-ReceiptKey', 'old200', '-Apply', '-ExpectedSha256', $beforeSha)
    Assert ((Get-FileHash (Join-Path $applied 'before.raw.json')).Hash.ToLowerInvariant() -ceq $beforeSha) 'Apply lost original.'
    Assert ((Get-FileHash $inputFile).Hash -ceq (Get-FileHash (Join-Path $applied 'current.compact.json')).Hash) 'Applied bytes differ from validated candidate.'
    $export = Read-AgentJson (Join-Path $applied 'export.json')
    Assert ((Get-FileHash -LiteralPath $export['replaceBackup']).Hash.ToLowerInvariant() -ceq $beforeSha) 'Atomic replacement backup lost original bytes.'
    Assert (-not $export['runtimeOrSchedulerVerified'] -and -not $export['authorityGranted']) 'Export falsely grants authority.'
    Check 'repository subdirectory cannot evade outside-output rule' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', (Join-Path $root 'scripts'), '-GitExecutable', $GitExecutable, '-EvidenceDirectory', (Join-Path $out 'subdirectory')) 1 'worktree root'
    Assert (-not (Test-Path (Join-Path $out 'subdirectory'))) 'Subdirectory root created output before rejection.'
    Check 'in-tree snapshot output rejected without creating files' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', (Join-Path $root ('forbidden-' + [guid]::NewGuid().ToString('N')))) 1 'outside the repository'
    $snapshot = Join-Path $out 'snapshot'
    Check 'source snapshot hashes real repo read-only with empty index' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', $snapshot)
    $baseline = Join-Path $snapshot 'binding.json'
    $binding = Read-AgentJson $baseline
    Assert ($binding['files'].Count -gt 0 -and -not $binding['runtimeVerified'] -and -not $binding['acceptance']) 'Snapshot falsely grants runtime acceptance.'
    $rawPath = 'scripts/Export-AgentHandoff.ps1'
    Assert ($binding['files'][$rawPath] -ceq (Get-FileHash -LiteralPath (Join-Path $root $rawPath)).Hash.ToLowerInvariant()) 'Snapshot did not hash raw source bytes.'
    $snapshotSha = (Get-FileHash $baseline).Hash
    Check 'existing snapshot output is never merged or overwritten' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', $snapshot) 1 'must be new'
    Assert ((Get-FileHash $baseline).Hash -ceq $snapshotSha) 'Existing snapshot binding changed.'
    Check 'unchanged source compares without parsing a patch' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', (Join-Path $out 'same'), '-BaselinePath', $baseline)
    Assert ((Get-FileHash (Join-Path $out 'same/baseline.raw.json')).Hash -ceq $snapshotSha) 'Comparison rewrote baseline bytes.'
    $binding['files']['missing-null-source.txt'] = $null
    $nullBaseline = Join-Path $out 'null-baseline.json'; Write-AgentNewJson $nullBaseline $binding
    Check 'missing path differs from a baseline null deletion' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', (Join-Path $out 'missing-null'), '-BaselinePath', $nullBaseline) 1 'Out-of-scope source delta: missing-null-source.txt'
    Check 'allowed removal of null entry stays in path union' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', (Join-Path $out 'allowed-null'), '-BaselinePath', $nullBaseline, '-AllowedChangedPath', 'missing-null-source.txt')
    $removed = Read-AgentJson (Join-Path $out 'allowed-null/binding.json')
    Assert (@($removed['changedPaths']).Count -eq 1 -and $removed['changedPaths'][0] -ceq 'missing-null-source.txt' -and -not $removed['files'].ContainsKey('missing-null-source.txt')) 'Null/missing delta disappeared.'
    $null = $binding['files'].Remove('missing-null-source.txt')
    $path = @($binding['files'].Keys)[0]; $binding['files'][$path] = '0' * 64
    $bad = Join-Path $out 'bad-baseline.json'; Write-AgentNewJson $bad $binding
    Check 'unapproved byte delta blocks binding publication' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', (Join-Path $out 'bad'), '-BaselinePath', $bad) 1 'Out-of-scope source delta'
    Assert (-not (Test-Path (Join-Path $out 'bad/binding.json'))) 'Failed comparison published a valid binding.'
    Check 'explicit allowed delta remains visible' 'Export-AgentSourceSnapshot.ps1' @('-RepositoryRoot', $root, '-GitExecutable', $GitExecutable, '-EvidenceDirectory', (Join-Path $out 'allowed'), '-BaselinePath', $bad, '-AllowedChangedPath', $path)
    $allowed = Read-AgentJson (Join-Path $out 'allowed/binding.json')
    Assert (@($allowed['changedPaths']).Count -eq 1 -and $allowed['changedPaths'][0] -ceq $path) 'Allowed delta disappeared.'
    $completed = $true
    Write-Host "PASS agent workflow regression: $($commands.Count) public invocations; synthetic state/read-only Git, no SDK/engine/network."
} finally {
    if ($completed -and -not $EvidenceDirectory) { Remove-Item -LiteralPath $out -Recurse -Force }
    elseif (-not $completed) { Write-Host "Regression failed; originals retained: $out" }
}
