# Compact an explicit idle-state index; preserve the original raw bytes. No SDK or dispatch.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string[]]$ReceiptKey,
    [string]$ExpectedSha256,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Import-Module (Join-Path $PSScriptRoot 'AgentEvidenceJson.psm1')
try {
    $inputFile = [IO.Path]::GetFullPath($InputPath)
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $output) { throw 'OutputDirectory must be new.' }
    $raw = [IO.File]::ReadAllBytes($inputFile)
    $beforeSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw)).ToLowerInvariant()
    if (($Apply -or $ExpectedSha256) -and $ExpectedSha256 -cne $beforeSha) { throw 'ExpectedSha256 is required for Apply and must match the original.' }
    $state = Read-AgentJson $inputFile -SchemaFile (Join-Path $PSScriptRoot 'agent-evidence.schema.json')
    if ($state['kind'] -cne 'derived_execution_index_not_issue_tracker') { throw 'Expected a current-state index.' }
    $ticket = $state['active_ticket']; $authority = $ticket['authority']
    if ($ticket['state'] -ceq 'running' -or $authority['sdkRemaining'] -ne 0 -or $null -ne $authority['writer'] -or $null -ne $authority['runId'] -or $null -ne $authority['deadlineUtc']) { throw 'Return all execution authority before exporting a session handoff.' }
    $history = [IO.Path]::GetFullPath($state['history']['snapshot'], (Split-Path $inputFile -Parent))
    if ((Get-FileHash -LiteralPath $history).Hash.ToLowerInvariant() -cne $state['history']['sha256']) { throw 'History snapshot hash differs.' }
    $selected = [ordered]@{}
    foreach ($key in $ReceiptKey) {
        if ([string]::IsNullOrWhiteSpace($key) -or $selected.Contains($key) -or -not $state['receipts'].ContainsKey($key)) { throw "Missing, blank or duplicate receipt key: $key" }
        $selected.Add($key, $state['receipts'][$key])
    }
    if (-not $selected.Count) { throw 'Select at least one capability receipt; history is archived, not discarded.' }
    $originalCount = $state['receipts'].Count
    $state['receipts'] = $selected
    $state['history'] = @{ snapshot = (Join-Path $output 'before.raw.json'); sha256 = $beforeSha }
    # Private staging + directory rename: never merge into a concurrently created output.
    $staging = $output + '.staging-' + [guid]::NewGuid().ToString('N')
    $null = [IO.Directory]::CreateDirectory($staging)
    Write-AgentNewFile (Join-Path $staging 'before.raw.json') $raw
    Write-AgentNewJson (Join-Path $staging 'current.compact.json') $state
    $handoff = @"
# Current handoff (derived index, not execution authority)

Read current.compact.json beside this file; original index: $inputFile
Original SHA256: $beforeSha
Ticket: $($ticket['number']); state: $($ticket['state']); dirty: $($ticket['source']['dirty'])
Worktree: $($ticket['source']['worktree'])
Branch/HEAD: $($ticket['source']['branch']) / $($ticket['source']['head'])
Latest source binding: $($ticket['source']['binding'])
Next: $($state['nextAction']['action']) — $($state['nextAction']['reason'])
Plan: $($state['nextAction']['plan'])

- Preserved $($selected.Count) explicitly selected capability receipts; all $originalCount original entries remain in before.raw.json.
- Scope, integration, active ticket (including failures/limits/contracts) and next action are unchanged.
- Existing inputs/limits may describe a historical unit. The latest binding and fresh Issue/API checks govern new planning; old briefs do not grant authority.
- Before switching/resuming, independently check scheduler/pending requests, Git/dirty identity and the current index hash. This exporter does not inspect a fleet or cancel a task.
- Start a NEW main session with this file, the compact index and applicable repository instructions. Do not replay accepted checks or import full historical file lists.
- Read archived receipts only for the current acceptance scope or a concrete conflict. This handoff is not ticket, integration, Game or full-spec acceptance.
"@
    Write-AgentNewFile (Join-Path $staging 'handoff.md') ([Text.UTF8Encoding]::new($false).GetBytes($handoff))
    [IO.Directory]::Move($staging, $output)
    # The existing classifier remains the authority for state validation.
    $hostPath = (Get-Process -Id $PID).Path
    & $hostPath -NoProfile -File (Join-Path $PSScriptRoot 'Test-AgentEvidence.ps1') -InputPath (Join-Path $output 'current.compact.json') 1> (Join-Path $output 'validation.stdout.json') 2> (Join-Path $output 'validation.stderr.txt')
    $validationExit = $LASTEXITCODE
    if ($validationExit -ne 0) { throw "Compact candidate rejected (exit $validationExit); original retained." }
    if ((Get-FileHash -LiteralPath $inputFile).Hash.ToLowerInvariant() -cne $beforeSha) { throw 'Concurrent current-state change; original retained.' }
    if ($Apply) {
        # Same-directory replacement, after backup + classifier + unchanged-original check.
        $candidate = $inputFile + '.' + [guid]::NewGuid().ToString('N') + '.candidate'
        Write-AgentNewFile $candidate ([IO.File]::ReadAllBytes((Join-Path $output 'current.compact.json')))
        if ((Get-FileHash -LiteralPath $inputFile).Hash.ToLowerInvariant() -cne $beforeSha) { throw "Concurrent state change; unused candidate retained: $candidate" }
        $replaceBackup = $inputFile + '.' + [guid]::NewGuid().ToString('N') + '.replaced.raw'
        [IO.File]::Replace($candidate, $inputFile, $replaceBackup)
    }
    $report = [ordered]@{
        status = 'complete'; applied = [bool]$Apply; input = $inputFile; output = $output
        beforeSha256 = $beforeSha; compactSha256 = (Get-FileHash -LiteralPath (Join-Path $output 'current.compact.json')).Hash.ToLowerInvariant()
        originalReceipts = $originalCount; selectedReceipts = $selected.Count
        originalBytes = $raw.Length; compactBytes = (Get-Item -LiteralPath (Join-Path $output 'current.compact.json')).Length
        replaceBackup = $(if ($Apply) { $replaceBackup } else { $null })
        runtimeOrSchedulerVerified = $false; authorityGranted = $false
    }
    Write-AgentNewJson (Join-Path $output 'export.json') $report
    $report | ConvertTo-Json -Depth 5
    exit 0
} catch {
    [Console]::Error.WriteLine("[agent-handoff] $($_.Exception.Message)")
    exit 1
}
