[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('real', 'synthetic-won', 'synthetic-lost')][string]$Mode,
    [Parameter()][ValidateSet('zh-Hans', 'en')][string]$Language = 'zh-Hans',
    [string]$ProfileUri,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$GodotPath
)
$ErrorActionPreference = 'Stop'
# One explicitly selected mode/language per invocation. No build, prepare, cleanup or retry.
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$godot = (Resolve-Path -LiteralPath $GodotPath).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($evidence.Equals($repo, $comparison) -or $evidence.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw 'GPU evidence must be outside the repository.'
}
if (Test-Path -LiteralPath $evidence) { throw 'EvidenceDirectory must be a new directory; existing evidence is never overwritten.' }
if ($Mode -eq 'real' -and $ProfileUri -notmatch '^res://.+\.tres$') { throw 'Real mode requires a res:// .tres ProfileUri.' }
function Snapshot([string[]]$roots) {
    $entries = @()
    foreach ($root in $roots) {
        $full = ProjectSettingsGlobalize($root)
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $entries += Get-ChildItem -LiteralPath $full -Recurse -File | Sort-Object FullName | ForEach-Object {
            @{ Path = $_.FullName.Substring($project.Length); Length = $_.Length;
               Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        }
    }
    $entries
}
function ProjectSettingsGlobalize([string]$uri) {
    [IO.Path]::GetFullPath((Join-Path $project ($uri.Substring(6) -replace '/', [IO.Path]::DirectorySeparatorChar)))
}
$protectedRoots = if ($Mode -eq 'real') {
    @('res://Assets/M4RealBattle53', 'res://Assets/M4RealBattle53Closure', 'res://Assets/M4RealBattle53Events', 'res://Assets/M4RealStatic_20261102T163721Z')
} else {
    @('res://Samples/AuthoredBattle', 'res://Assets/RuntimeTests', 'res://Tests/RuntimeProfiles')
}
$before = Snapshot $protectedRoots
$conflicts = @(& (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1'))
if ($conflicts.Count -ne 0) { $conflicts | Format-List; throw 'Existing Godot/repository test processes require ownership confirmation. No GPU run started.' }
$version = (& $godot --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -ne '4.7.2.stable.mono.official.ed1daf0bf') { throw "Pinned Godot version mismatch: $version" }
New-Item -ItemType Directory -Path $evidence | Out-Null
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'resources-before.json') -Encoding utf8
$stdout = Join-Path $evidence 'stdout.log'
$stderr = Join-Path $evidence 'stderr.log'
$arguments = @('--path', $project, '--audio-driver', 'Dummy', 'res://Tests/M4FirstBattleGpuFixture.tscn')
$environment = @{
    PAL_M4_GPU_EVIDENCE = $evidence; PAL_M4_GPU_MODE = $Mode
    PAL_M4_GPU_LANGUAGE = $Language; PAL_M4_GPU_PROFILE = $ProfileUri
}
foreach ($key in $environment.Keys) { Set-Item -Path "Env:$key" -Value $environment[$key] }
$startedAt = [DateTime]::UtcNow
$p = Start-Process -FilePath $godot -ArgumentList $arguments -WorkingDirectory $project `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
foreach ($key in @($environment.Keys)) { Remove-Item -Path "Env:$key" -ErrorAction SilentlyContinue }
$ownedPid = $p.Id
$waitMs = if ($Mode -eq 'synthetic-won') { 660000 } else { 360000 }
$timedOut = -not $p.WaitForExit($waitMs)
if ($timedOut) { $p.Kill($true); $p.WaitForExit() }
$code = $p.ExitCode
$after = Snapshot $protectedRoots
$after | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'resources-after.json') -Encoding utf8
$unchanged = (ConvertTo-Json $before -Depth 4 -Compress) -ceq (ConvertTo-Json $after -Depth 4 -Compress)
$output = if (Test-Path $stdout) { Get-Content $stdout -Raw } else { '' }
$errors = if (Test-Path $stderr) { Get-Content $stderr -Raw } else { '' }
$engineError = ($output + "`n" + $errors) -match '(?im)(ERROR:|SCRIPT ERROR:|Unhandled|PAL_M4_GPU_FAILED)'
$resultPath = Join-Path $evidence 'fixture-result.json'
$fixture = if (Test-Path $resultPath) { Get-Content $resultPath -Raw | ConvertFrom-Json } else { $null }
$glyphCheck = "dialogue-glyphs-$Language"
$required = if ($Mode -eq 'real') {
    @('static-scene-rendered', $glyphCheck, 'dialogue-portrait-zone', 'battle-two-live-enemies',
        'battle-background-independent', 'battle-enemy1-sprite', 'battle-enemy2-sprite', 'battle-player1-sprite',
        'battle-player2-sprite', 'battle-hp-hud-text', 'battle-hud-panel', 'battle-feedback-visible-result', 'real-damage-trajectory')
} elseif ($Mode -eq 'synthetic-won') {
    @('battle-two-live-enemies', 'battle-background-independent', 'battle-enemy1-sprite', 'battle-enemy2-sprite',
        'battle-player1-sprite', 'battle-player2-sprite', 'battle-hp-hud-text', 'battle-hud-panel',
        'settlement-line-present', 'settlement-box-dark', 'settlement-box-text', 'post-battle-map-restored')
} else {
    @('battle-two-live-enemies', 'battle-background-independent', 'battle-enemy1-sprite', 'battle-enemy2-sprite',
        'battle-player1-sprite', 'battle-player2-sprite', 'battle-hp-hud-text', 'battle-hud-panel',
        'red-fade-pre-sampled', 'red-fade-convergence-formula')
}
$presentNames = @($fixture.checks | ForEach-Object { $_.name })
$missing = @($required | Where-Object { $presentNames -notcontains $_ })
$passed = -not $timedOut -and $code -eq 0 -and $unchanged -and -not $engineError -and
    $null -ne $fixture -and $fixture.completed -eq $true -and $fixture.failures -eq 0 -and $missing.Count -eq 0 -and
    $output.Contains("PAL_M4_GPU_END:mode=${Mode}:language=${Language}:failures=0:completed=true")
[ordered]@{
    Pid = $ownedPid; StartedAtUtc = $startedAt.ToString('O'); EndedAtUtc = [DateTime]::UtcNow.ToString('O')
    Mode = $Mode; Language = $Language; ExitCode = $code; TimedOut = $timedOut; ResourcesUnchanged = $unchanged
    EngineError = $engineError; FixtureCompleted = ($null -ne $fixture -and $fixture.completed -eq $true)
    FixtureFailures = $fixture.failures; MissingChecks = $missing; Passed = $passed
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'process-result.json') -Encoding utf8
Write-Output "GPU_EVIDENCE=$evidence"
Write-Output "GPU_RESULT:mode=$Mode language=$Language exit=$code timeout=$timedOut resourcesUnchanged=$unchanged engineError=$engineError missingChecks=$($missing.Count) passed=$passed"
if (-not $passed) { exit 1 }
