[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NativeDialogueDirectory,
    [Parameter(Mandatory)][string]$PalettePath,
    [Parameter(Mandatory)][ValidateSet('zh-Hans', 'en')][string]$Language,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$GodotPath,
    [string]$InteractionProfileUri
)
$ErrorActionPreference = 'Stop'
# One explicitly selected language/process per invocation. No build, prepare, cleanup or retry.
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$native = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath $NativeDialogueDirectory).Path)
$palette = (Resolve-Path -LiteralPath $PalettePath).Path
$godot = (Resolve-Path -LiteralPath $GodotPath).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($evidence.Equals($repo, $comparison) -or $evidence.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw 'Real GPU evidence must be outside the repository.'
}
if (Test-Path -LiteralPath $evidence) { throw 'EvidenceDirectory must be a new directory; existing evidence is never overwritten.' }
$protected = @('Font.tres', 'Language.zh-Hans.tres', 'Language.en.tres', 'Portrait-059.tres', 'Prompt-00.tres', 'Prompt-01.tres', 'Prompt-02.tres') |
    ForEach-Object { Join-Path $native $_ }
$protected += $palette
if ($InteractionProfileUri) {
    if (-not $InteractionProfileUri.StartsWith('res://')) { throw 'InteractionProfileUri must select an existing native res:// profile.' }
    $protected += Join-Path $project $InteractionProfileUri.Substring(6)
}
function Snapshot {
    @($protected | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        [ordered]@{ Path = $item.FullName; Length = $item.Length; LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('O'); Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    }) | ConvertTo-Json -Depth 4
}
$before = Snapshot
$conflicts = @(& (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1'))
if ($conflicts.Count -ne 0) { $conflicts | Format-List; throw 'Existing Godot/repository test processes require ownership confirmation. No GPU run started.' }
$version = (& $godot --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -ne '4.7.2.stable.mono.official.ed1daf0bf') { throw "Pinned Godot version mismatch: $version" }
New-Item -ItemType Directory -Path $evidence | Out-Null
$before | Set-Content -LiteralPath (Join-Path $evidence 'resources-before.json') -Encoding utf8
$start = [Diagnostics.ProcessStartInfo]::new($godot)
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.WorkingDirectory = $project
$arguments = @('--path', $project, '--audio-driver', 'Dummy', 'res://Tests/RealDialogueFixture.tscn')
if ($InteractionProfileUri) { $arguments += @('--', '--m3-dialogue-profile', $InteractionProfileUri, '--m3-dialogue-language', $Language) }
foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
$start.Environment['PAL_DIALOGUE_NATIVE_ROOT'] = $native.Replace('\', '/')
$start.Environment['PAL_DIALOGUE_PALETTE'] = $palette.Replace('\', '/')
$start.Environment['PAL_DIALOGUE_LANGUAGE'] = $Language
$start.Environment['PAL_DIALOGUE_GPU_EVIDENCE'] = $evidence
[ordered]@{
    Executable = $godot; Arguments = $arguments; Language = $Language; NativeDialogueDirectory = $native
    PalettePath = $palette; Version = $version; Head = (git -C $repo rev-parse HEAD)
    InteractionProfileUri = $InteractionProfileUri; WorkingTreeStatus = (git -C $repo status --short | Out-String)
    Scope = if ($InteractionProfileUri) { 'Actual Main interaction + map10, complete events, logical GPU readback and programmatic input. Map prestate is a restoration oracle, not independent full-map pixel proof. No physical/manual acceptance.' } else { 'Native dialogue resources, synthetic indexed background, logical GPU readback, programmatic key input. No physical/manual acceptance.' }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'command.json') -Encoding utf8
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$timedOut = $false
$startedAt = [DateTime]::UtcNow
try {
    if (-not $process.Start()) { throw 'Owned Godot process did not start.' }
    $ownedPid = $process.Id
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit(60000)
    if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
    $output = $stdout.GetAwaiter().GetResult()
    $errors = $stderr.GetAwaiter().GetResult()
    $output | Set-Content -LiteralPath (Join-Path $evidence 'stdout.log') -Encoding utf8
    $errors | Set-Content -LiteralPath (Join-Path $evidence 'stderr.log') -Encoding utf8
    $code = $process.ExitCode
    $after = Snapshot
    $after | Set-Content -LiteralPath (Join-Path $evidence 'resources-after.json') -Encoding utf8
    $unchanged = $before -ceq $after
    $resultPath = Join-Path $evidence 'fixture-result.json'
    $fixture = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } else { $null }
    $identityPath = Join-Path $evidence 'resource-identity.json'
    $identity = if (Test-Path -LiteralPath $identityPath) { Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json } else { $null }
    $identityMatched = -not $InteractionProfileUri -or ($null -ne $identity -and
        $identity.InteractionProfileUri -ceq $InteractionProfileUri -and
        $native.Equals($identity.NativeDialogueDirectory, $comparison) -and $palette.Equals($identity.PalettePath, $comparison))
    $engineError = ($output + "`n" + $errors) -match '(?im)(ERROR:|SCRIPT ERROR:|Unhandled|PAL_DIALOGUE_GPU_FAILED)'
    $passed = -not $timedOut -and $code -eq 0 -and $unchanged -and $identityMatched -and -not $engineError -and
        $null -ne $fixture -and $fixture.completed -eq $true -and $fixture.failures -eq 0 -and $fixture.checks.Count -eq $(if ($InteractionProfileUri) { 10 } else { 6 }) -and
        (-not $InteractionProfileUri -or $output.Contains("PAL_INTERACTION_END:language=${Language}:calls=2:entry=0188:completed=true")) -and
        $output.Contains("PAL_DIALOGUE_GPU_END:language=${Language}:failures=0:completed=true")
    [ordered]@{
        Pid = $ownedPid; StartedAtUtc = $startedAt.ToString('O'); EndedAtUtc = [DateTime]::UtcNow.ToString('O')
        ExitCode = $code; TimedOut = $timedOut; ResourcesUnchanged = $unchanged; ResourceIdentityMatched = $identityMatched; EngineError = $engineError
        FixtureCompleted = ($null -ne $fixture -and $fixture.completed -eq $true); Passed = $passed
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'process-result.json') -Encoding utf8
    Write-Output "GPU_EVIDENCE=$evidence"
    Write-Output "GPU_RESULT:language=$Language exit=$code timeout=$timedOut resourcesUnchanged=$unchanged passed=$passed"
    if (-not $passed) { exit 1 }
} finally { $process.Dispose() }
