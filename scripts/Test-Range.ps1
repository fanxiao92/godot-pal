# Explicit range regression, not the M0 acceptance scheduler.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [AllowEmptyCollection()][AllowEmptyString()][string[]]$Scope = @(),
    [AllowEmptyCollection()][AllowEmptyString()][string[]]$GameBatch = @(),
    [AllowEmptyString()][string]$Filter,
    [string]$OutputDirectory,
    [string]$GodotPath,
    [string]$FixedPackageDirectory
)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
. (Join-Path $PSScriptRoot 'GameTestBatches.ps1')
. (Join-Path $PSScriptRoot 'TestRunDefinitions.ps1')
. (Join-Path $PSScriptRoot 'DotnetCheckedDefinition.ps1')
$paths = [ordered]@{ 'cli-integration' = 'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj'; prepare = 'tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj'; game = 'src/Pal.Game/Pal.Game.csproj' }
$commands = [Collections.Generic.List[object]]::new()
$script:sequence = 0
$exitCode = 1; $ownsOutput = $false; $out = $null; $recordingError = $null
$report = [ordered]@{ schemaVersion = 1; kind = 'range-regression'; runId = [guid]::NewGuid().ToString(); status = 'incomplete'; repositoryRoot = $RepositoryRoot; scope = @($Scope); gameBatch = @($GameBatch); filter = $Filter; startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null; elapsedMs = $null; head = $null; sdk = $null; toolIdentity = @(); sourceIdentity = @(); generatedSourceIdentity = @(); preparation = $null; projects = @(); commands = @(); failure = $null; cache = 'Ordinary current-user packages/existing build outputs; explicit verified bundle for Prepare/Game; no private process observer or full-gate certificate.' }
$clock = [Diagnostics.Stopwatch]::StartNew()
$previous = @{}
foreach ($key in @('GODOT_BIN', 'PAL_TEST_GODOT', 'DOTNET_CLI_UI_LANGUAGE', 'DOTNET_NOLOGO')) { $previous[$key] = [Environment]::GetEnvironmentVariable($key) }
function Fail([string]$Code, [string]$Message) { throw "[range:$Code] $Message" }
function Save-Report {
    if ($ownsOutput) {
        $report.commands = $commands.ToArray()
        try { [IO.File]::WriteAllText((Join-Path $out 'range-run.json'), ($report | ConvertTo-Json -Depth 40), $utf8) }
        catch { $script:recordingError = $_.Exception.Message; [Console]::Error.WriteLine("[range:evidence] $script:recordingError") }
    }
}
# The shared executor calls this hook. Range records owned PID/exit/streams but
# deliberately does not invoke M0's repository/global process snapshot collector.
function Save-ProcessSnapshot([string]$Name) { }
function Invoke-RangeCommand([string]$Name, [string[]]$Arguments) {
    if ($recordingError) { Fail 'evidence' 'Cannot record a new command.' }
    $repository = $root; $evidence = $out
    $before = $commands.Count; $failure = $null; $text = $null
    try { $text = Invoke-DotnetChecked -Arguments $Arguments } catch { $failure = $_ }
    if ($commands.Count -ne $before + 1) { Fail 'incomplete-command' "$Name did not produce its own command record." }
    $record = $commands[$before]; $record.name = $Name; $record.executable = $dotnet; $record.cwd = $root
    # Save the observed nonzero exit before any report/decoder can fail.
    if ($record.exitKnown -and $record.exitCode -ne 0) { $script:exitCode = $record.exitCode }
    Save-Report
    if (-not $record.exitKnown -or -not $record.rootExited -or -not $record.outputComplete -or -not $record.cleanupComplete -or $record.failureKind -in @('ProcessTimeout','OutputDrainTimeout','CaptureFailure','CleanupFailure')) {
        $script:exitCode = 2
        Fail 'abnormal-session' "$Name lacks successful root/EOF/cleanup completion; failureKind=$($record.failureKind). Stop; retain $out."
    }
    if ($record.exitCode -ne 0) {
        [Console]::Error.WriteLine($text)
        Fail 'command-exit' "$Name exited $($record.exitCode); raw streams retained in $out."
    }
    if ($failure) { Fail 'command-diagnostic' "$Name failed: $($failure.Exception.Message)" }
    if ($recordingError) { Fail 'evidence' 'Command journal could not be written; stop.' }
    try {
        $stdout = [IO.File]::ReadAllText((Join-Path $out $record.stdout), $strictUtf8)
        $stderr = [IO.File]::ReadAllText((Join-Path $out $record.stderr), $strictUtf8)
    } catch { $script:exitCode = 2; Fail 'encoding' "$Name streams are not complete UTF-8." }
    if (($stdout + "`n" + $stderr) -match '(?i)Aborting test run|Test Run Aborted|test run timeout|test host process crashed|WindowsStdOutHook') { $script:exitCode = 2; Fail 'abnormal-session' "$Name reports an aborted/crashed session." }
    return $stdout
}
function Hash-File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Read-GdUnitRunnerTemplate([string]$ApiAssembly) {
    # Read the pinned API's embedded source as data; do not call its internal
    # installer, prewarm tests or change SDK configuration. The adapter itself
    # installs this exact renamed template on its first runtime execution.
    $api = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($ApiAssembly))
    $stream = $api.GetManifestResourceStream('GdUnit4.src.core.runners.GdUnit4TestRunnerSceneTemplate.cs')
    if (-not $stream) { Fail 'identity' 'Pinned API runner resource is missing.' }
    $reader = [IO.StreamReader]::new($stream, $strictUtf8, $true)
    try { return $reader.ReadToEnd().Replace('GdUnit4TestRunnerSceneTemplate', 'GdUnit4TestRunnerScene', [StringComparison]::Ordinal) }
    finally { $reader.Dispose() }
}
function Match-Method([string]$Name) {
    if (-not $Filter) { return $true }
    if ($Filter.StartsWith('FullyQualifiedName=', [StringComparison]::Ordinal)) { return $Name -ceq $filterValue }
    return $Name.StartsWith($filterValue, [StringComparison]::Ordinal)
}
function Snapshot-Sources {
    $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $directories = @('src/Pal.Core')
    if ('cli-integration' -in $Scope) { $directories += @('tests/Pal.Extract.IntegrationTests', 'tools/Pal.Extract', 'tests/Fixtures') }
    if ('prepare' -in $Scope) { $directories += @('tests/Pal.Prepare.Tests', 'tests/Fixtures', 'tools/Pal.Extract', 'src/Pal.Game') }
    if ('game' -in $Scope) { $directories += 'src/Pal.Game' }
    foreach ($relative in $directories) {
        $dir = Join-Path $root $relative
        if (-not [IO.Directory]::Exists($dir)) { continue }
        $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($dir)
        while ($pending.Count) {
            foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
                if ($item.PSIsContainer) { if ($item.Name -notin @('bin','obj','.godot','.packages','TestResults','.git')) { $pending.Push($item.FullName) } }
                elseif ($item.Extension -in @('.cs','.csproj','.gd','.tscn','.tres','.godot')) { $null = $files.Add($item.FullName) }
            }
        }
    }
    foreach ($name in @('global.json', 'NuGet.Config', 'tests/Pal.Extract.Tests/Migration/baseline.json', 'tests/Pal.Game.Tests/game.runsettings')) { $file = Join-Path $root $name; if ([IO.File]::Exists($file)) { $null = $files.Add($file) } }
    return @(foreach ($file in ($files | Sort-Object)) { @{ path = [IO.Path]::GetRelativePath($root, $file); sha256 = (Hash-File $file) } })
}
function Assert-GameSession($Identity, $Command) {
    $doc = $Identity.document; $ns = $Identity.ns
    foreach ($id in @($Identity.runId) + @($Identity.executionIds)) { $g = [guid]::Empty; if (-not [guid]::TryParse($id, [ref]$g) -or $g -eq [guid]::Empty) { Fail 'identity' 'Invalid Game run/execution GUID.' } }
    $times = $doc.SelectSingleNode('/t:TestRun/t:Times', $ns)
    if (-not $times) { Fail 'results' 'Game TRX has no session times.' }
    foreach ($key in @('creation','queuing','start','finish')) { $stamp = [DateTimeOffset]::MinValue; if (-not [DateTimeOffset]::TryParse($times.GetAttribute($key), [ref]$stamp)) { Fail 'results' "Invalid Game time $key." } }
    if ([DateTimeOffset]::Parse($times.finish) -lt [DateTimeOffset]::Parse($times.start)) { Fail 'results' 'Game finish precedes start.' }
    foreach ($c in $doc.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $ns).Attributes) { if ($c.Value -notmatch '^\d+$') { Fail 'results' 'Invalid Game counter type.' } }
    if ($doc.SelectNodes('/t:TestRun/t:ResultSummary/t:RunInfos/t:RunInfo', $ns).Count) { Fail 'results' 'Unexpected Game RunInfo.' }
}
try {
    # Selection errors precede all SDK, bundle or engine prerequisites.
    if (-not $Scope.Count) { Fail 'selection' 'Scope must be a nonempty subset of cli-integration, prepare, game.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($s in $Scope) { if ($s -cnotin @($paths.Keys) -or -not $seen.Add($s)) { Fail 'selection' "Unknown/empty/duplicate scope '$s'." } }
    $Scope = @($paths.Keys | Where-Object { $seen.Contains($_) }); $report.scope = $Scope
    if ($PSBoundParameters.ContainsKey('Filter') -and [string]::IsNullOrWhiteSpace($Filter)) { Fail 'filter-syntax' 'Explicit Filter must not be empty.' }
    if ($Filter -and $Filter -cnotmatch '^FullyQualifiedName(?:=|~)[^|&()!<>=\r\n]+$') { Fail 'filter-syntax' 'Use FullyQualifiedName=<method> or FullyQualifiedName~<class-prefix> only.' }
    if ($Filter -and ($Scope.Count -ne 1 -or $Scope[0] -ceq 'prepare')) { Fail 'filter-scope' 'Filter requires one CLI integration or Game scope; Prepare does not accept filters.' }
    $filterValue = if ($Filter) { $Filter.Substring($Filter.IndexOfAny([char[]]'=~') + 1) } else { '' }
    $batches = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($b in $GameBatch) { if ($b -cnotin @('process','scene-dialogue','native-map') -or -not $batches.Add($b)) { Fail 'selection' "Unknown/empty/duplicate GameBatch '$b'." } }
    if (('game' -cin $Scope) -ne ($GameBatch.Count -gt 0)) { Fail 'batch-scope' 'Game requires explicit positive GameBatch selection; other scopes do not accept batches.' }
    if (-not $RepositoryRoot -or -not [IO.Directory]::Exists($RepositoryRoot)) { Fail 'project-missing' 'RepositoryRoot must exist.' }
    $root = [IO.Path]::GetFullPath($RepositoryRoot); $report.repositoryRoot = $root
    foreach ($s in $Scope) { if (-not [IO.File]::Exists((Join-Path $root $paths[$s]))) { Fail 'project-missing' "Missing selected project $($paths[$s])." } }
    $engineScope = 'prepare' -cin $Scope -or 'game' -cin $Scope
    if ($engineScope -and (-not $GodotPath -or -not [IO.File]::Exists($GodotPath))) { Fail 'prerequisite' 'Prepare/Game require explicit existing GodotPath.' }
    if ($engineScope -and (-not $FixedPackageDirectory -or -not [IO.Directory]::Exists($FixedPackageDirectory))) { Fail 'prerequisite' 'Prepare/Game require explicit existing verified FixedPackageDirectory.' }
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('TestResults/range-' + $report.runId) }
    $out = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $out) { Fail 'output-exists' "Use a new output directory: $out" }
    $null = [IO.Directory]::CreateDirectory($out); $ownsOutput = $true; Save-Report
    if ($engineScope) {
        try {
            $conflicts = @(if ($IsWindows) {
                Get-CimInstance -Query "SELECT ProcessId, Name FROM Win32_Process WHERE Name LIKE 'godot%.exe'" | Select-Object ProcessId, Name
            } else {
                Get-Process -Name '*godot*' -ErrorAction Stop | Select-Object @{Name='ProcessId';Expression={$_.Id}}, Name
            })
        } catch {
            $report.godotPreflight = @{ status = 'unknown'; checkedUtc = [DateTime]::UtcNow.ToString('o'); processes = @() }
            Save-Report; Fail 'process-preflight' 'Global Godot PID/Name inventory could not be verified; no work started.'
        }
        $report.godotPreflight = @{ status = 'completed'; checkedUtc = [DateTime]::UtcNow.ToString('o'); processes = @($conflicts) }
        Save-Report
        if ($recordingError) { Fail 'evidence' 'Cannot record process prerequisites.' }
        if ($conflicts.Count) { Fail 'process-conflict' 'An existing Godot PID/Name is recorded; stop and confirm ownership. No process was terminated.' }
    }
    $env:DOTNET_CLI_UI_LANGUAGE = 'en-US'; $env:DOTNET_NOLOGO = '1'
    if ($engineScope) {
        $env:GODOT_BIN = [IO.Path]::GetFullPath($GodotPath); $env:PAL_TEST_GODOT = $env:GODOT_BIN
        # Public bootstrap verifies explicit input even if its destination is cached.
        # Explicit InputDirectory means no implicit network download is possible.
        $bootstrap = Join-Path $root 'scripts/Get-GdUnitPackages.ps1'
        if (-not [IO.File]::Exists($bootstrap)) { Fail 'prerequisite' 'Selected repository has no package bootstrap.' }
        $tokens = $null; $errors = $null; $null = [Management.Automation.Language.Parser]::ParseFile($bootstrap, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { Fail 'preparation' 'Package bootstrap syntax failed.' }
        $lock = Get-Content -LiteralPath (Join-Path $root 'scripts/gdunit-packages.lock.json') -Raw -Encoding utf8 | ConvertFrom-Json
        $bundleInputs = @(foreach ($asset in $lock.assets) {
            $file = Get-Item -LiteralPath (Join-Path $FixedPackageDirectory $asset.file)
            @{ path = $file.FullName; length = $file.Length; mtimeTicks = $file.LastWriteTimeUtc.Ticks; sha256 = (Hash-File $file.FullName) }
        })
        $bundleLog = Join-Path $out 'bundle-verification.log'
        $verified = @(& $bootstrap -InputDirectory ([IO.Path]::GetFullPath($FixedPackageDirectory)) 6> $bundleLog)
        $report.preparation = @{ input = [IO.Path]::GetFullPath($FixedPackageDirectory); destination = $verified[-1]; scriptSha256 = (Hash-File $bootstrap); log = 'bundle-verification.log'; status = 'completed'; godotPath = $env:GODOT_BIN; inputs = $bundleInputs }
        Save-Report
    }
    $report.toolIdentity = @(foreach ($name in @('Test-Range.ps1','TestRunDefinitions.ps1','DotnetCheckedDefinition.ps1','GameTestBatches.ps1','Get-GdUnitPackages.ps1','gdunit-packages.lock.json')) { $file = Join-Path $PSScriptRoot $name; @{ path = $file; sha256 = (Hash-File $file) } })
    $report.sourceIdentity = @(Snapshot-Sources)
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $report.sdk = (Invoke-RangeCommand 'sdk' @('--version')).Trim()
    if (Test-Path -LiteralPath (Join-Path $root '.git')) {
        $git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $head = & $git -C $root rev-parse HEAD 2> (Join-Path $out 'git-head.stderr.log'); $gitExit = $LASTEXITCODE
        if ($gitExit -ne 0) { Fail 'identity' 'Cannot read actual repository HEAD.' }
        $report.head = ([string]$head).Trim()
    }
    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    # Entire selected Prepare scope completes before Game build/discovery/execution.
    foreach ($s in $Scope) {
        $project = Join-Path $root $paths[$s]
        $build = Invoke-RangeCommand "$s.build" @('build', $project, '--configuration', 'Debug', '--nologo')
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)
        $matches = [regex]::Matches($build, ('(?m)^\s*' + [regex]::Escape($assemblyName) + ' -> (.+\.dll)\s*\r?$'))
        if ($matches.Count -ne 1) { Fail 'artifact' "Build did not identify exactly one $assemblyName assembly." }
        $assembly = [IO.Path]::GetFullPath($matches[0].Groups[1].Value.Trim())
        if (-not [IO.File]::Exists($assembly)) { Fail 'artifact' "Built assembly missing: $assembly" }
        $artifacts = @(foreach ($f in Get-ChildItem -LiteralPath (Split-Path $assembly -Parent) -File | Where-Object { $_.Extension -in @('.dll','.pdb') -or $_.Name -match '\.(deps|runtimeconfig)\.json$' }) { @{ path = $f.FullName; sha256 = (Hash-File $f.FullName) } })
        $diag = Join-Path $out "$s.discovery.diag.log"
        $discoveryArgs = @('test', $project, '--configuration', 'Debug', '--no-build', '--no-restore', '--list-tests')
        if ($s -ceq 'game') { $discoveryArgs += @('--settings', (Join-Path $root 'tests/Pal.Game.Tests/game.runsettings')) }
        else { $discoveryArgs += @('--diag', $diag) }
        $listed = Invoke-RangeCommand "$s.discover" $discoveryArgs
        if ($s -ceq 'game') {
            $plan = Get-GameBatchPlan $root
            $null = Assert-GameDiscovery $listed $plan
            $selectedBatches = @($plan.batches | Where-Object { $batches.Contains($_.name) })
            $expected = @($selectedBatches.expected)
            $selectedInventory = @($plan.inventory | Where-Object { $_.name -cin $expected })
            $selectedInventory = @($selectedInventory | Where-Object {
                $method = $_.name.Substring(('Pal.Game.Tests.' + $_.class + '.').Length).Split('.')[0]
                Match-Method ('Pal.Game.Tests.' + $_.class + '.' + $method)
            })
            $expected = @($selectedInventory.name)
            if (-not $expected.Count) { Fail 'filter-empty' 'No tests match within the selected positive Game batches.' }
            $batchFilter = (@($selectedBatches.filter) -join '|')
            $actualFilter = $batchFilter
            if ($Filter) {
                $terms = @(foreach ($case in $selectedInventory) { $prefix = 'Pal.Game.Tests.' + $case.class + '.'; $method = $case.name.Substring($prefix.Length).Split('.')[0]; if ($case.name -ceq ($prefix + $method)) { 'FullyQualifiedName=' + $case.name } else { 'FullyQualifiedName~' + $prefix + $method + '.' } })
                $actualFilter = '(' + $batchFilter + ')&(' + (($terms | Sort-Object -Unique) -join '|') + ')'
            }
            $snapshot = @{ all = $plan.inventory; selected = $selectedInventory; actualFilter = $actualFilter }
        } else {
            $discovery = Read-Version7Discovery -Name $s -DiagnosticPath $diag -Assembly $assembly
            if ($s -ceq 'prepare' -and $discovery.total -ne 54) { Fail 'discovery' "Prepare must discover exactly the 54 approved cases, got $($discovery.total)." }
            $selected = @($discovery.cases | Where-Object { Match-Method $_.FullyQualifiedName })
            if (-not $selected.Count) { Fail 'filter-empty' 'No independent discovered cases match the selected filter.' }
            $expected = @($selected.Id)
            $actualFilter = if ($Filter) { (@($selected.FullyQualifiedName | Sort-Object -Unique | ForEach-Object { 'FullyQualifiedName=' + $_ }) -join '|') } else { $null }
            $snapshot = @{ all = $discovery; selected = $selected; actualFilter = $actualFilter }
        }
        [IO.File]::WriteAllText((Join-Path $out "$s.discovery.json"), ($snapshot | ConvertTo-Json -Depth 40), $utf8)
        $args = @('test', $project, '--configuration', 'Debug', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=results.trx', '--results-directory', (Join-Path $out $s))
        if ($s -ceq 'game') { $args += @('--settings', (Join-Path $root 'tests/Pal.Game.Tests/game.runsettings')) }
        if ($actualFilter) { $args += @('--filter', $actualFilter) }
        $runnerAdded = $false
        if ($s -ceq 'game') {
            $runner = Join-Path $root 'src/Pal.Game/gdunit4_testadapter_v5/GdUnit4TestRunnerScene.cs'
            $hadRunner = [IO.File]::Exists($runner)
            $apiAssembly = Join-Path (Split-Path $assembly -Parent) 'GdUnit4Api.dll'
            $runnerTemplate = Read-GdUnitRunnerTemplate $apiAssembly
            if ($hadRunner -and [IO.File]::ReadAllText($runner, $strictUtf8) -cne $runnerTemplate) { Fail 'identity' 'Existing GdUnit runner differs from the pinned API template.' }
        }
        $null = Invoke-RangeCommand "$s.test" $args
        $testCommand = $commands[$commands.Count - 1]; $trx = Join-Path $out "$s/results.trx"
        if (-not [IO.File]::Exists($trx)) { Fail 'results-missing' "Current command did not produce its TRX file: $trx" }
        if ($s -ceq 'game') { $identity = Assert-GameBatch $trx $expected; Assert-GameSession $identity $testCommand }
        else {
            $allowed = if ($s -ceq 'cli-integration') { @('Pal.Extract.Tests.ExportMapCliTests.Export_map_rejects_case_ambiguous_archives','Pal.Extract.Tests.InspectCliTests.Inspect_rejects_ambiguous_case_insensitive_names') } else { @() }
            $validation = Assert-Version7TrxRun -Path $trx -Discovery ([pscustomobject]@{ total = $selected.Count; cases = $selected; assembly = $assembly }) -AllowedSkippedNames $allowed
            $identity = $validation.identity
        }
        $times = $identity.document.SelectSingleNode('/t:TestRun/t:Times', $identity.ns)
        if ([DateTimeOffset]::Parse($times.start) -lt [DateTimeOffset]::Parse($testCommand.startedUtc) -or [DateTimeOffset]::Parse($times.finish) -gt [DateTimeOffset]::Parse($testCommand.finishedUtc)) { Fail 'identity' 'TRX session does not belong to the current command interval.' }
        if (-not $runIds.Add($identity.runId)) { Fail 'identity' 'TRX run ID reused across selected scopes.' }
        foreach ($id in $identity.executionIds) { if (-not $executionIds.Add($id)) { Fail 'identity' 'Execution ID reused across selected scopes.' } }
        if ($s -ceq 'game') {
            if (-not [IO.File]::Exists($runner) -or [IO.File]::ReadAllText($runner, $strictUtf8) -cne $runnerTemplate) { Fail 'identity' 'Runtime runner does not match the pinned API template.' }
            $runnerAdded = -not $hadRunner
            if ($runnerAdded) { $report.generatedSourceIdentity += @{ path = [IO.Path]::GetRelativePath($root, $runner); sha256 = (Hash-File $runner); apiPath = $apiAssembly; apiSha256 = (Hash-File $apiAssembly); reason = 'Pinned GdUnit first-runtime runner installation; adapter recompiles the Game assembly.' } }
        }
        foreach ($artifact in $artifacts) {
            $artifact.afterSha256 = Hash-File $artifact.path
            if ($artifact.afterSha256 -cne $artifact.sha256 -and -not ($s -ceq 'game' -and $runnerAdded -and $artifact.path -cin @($assembly, [IO.Path]::ChangeExtension($assembly, '.pdb')))) { Fail 'identity' "Unexpected built artifact change after discovery: $($artifact.path)" }
        }
        $report.projects += @{ scope = $s; project = $paths[$s]; assembly = $assembly; artifacts = $artifacts; discovery = "$s.discovery.json"; expected = $expected; executed = $identity.names; runId = $identity.runId; executionIds = $identity.executionIds; filter = $actualFilter; trx = "$s/results.trx" }
        Save-Report
    }
    if ($report.preparation) {
        foreach ($input in $report.preparation.inputs) {
            $file = Get-Item -LiteralPath $input.path
            if ($file.Length -ne $input.length -or $file.LastWriteTimeUtc.Ticks -ne $input.mtimeTicks -or (Hash-File $input.path) -cne $input.sha256) { Fail 'input-changed' 'Explicit fixed-package input changed during the run.' }
        }
    }
    foreach ($source in $report.sourceIdentity) { if ((Hash-File (Join-Path $root $source.path)) -cne $source.sha256) { Fail 'source-changed' "Source changed during the run: $($source.path)" } }
    $initialPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $report.sourceIdentity) { $null = $initialPaths.Add($source.path) }
    foreach ($source in (Snapshot-Sources)) {
        if (-not $initialPaths.Contains($source.path)) {
            $known = @($report.generatedSourceIdentity | Where-Object { $_.path -ceq $source.path -and $_.sha256 -ceq $source.sha256 })
            if ($known.Count -ne 1) { Fail 'source-changed' "Unexpected new source during the run: $($source.path)" }
        }
    }
    foreach ($tool in $report.toolIdentity) { if ((Hash-File $tool.path) -cne $tool.sha256) { Fail 'source-changed' "Entry/helper changed during the run: $($tool.path)" } }
    if ($recordingError) { Fail 'evidence' 'Cannot finalize an incomplete journal.' }
    $report.status = 'completed'; $exitCode = 0
} catch {
    $report.failure = $_.Exception.Message
    $report.status = if ($exitCode -eq 2) { 'incomplete' } else { 'failed' }
    [Console]::Error.WriteLine($report.failure)
} finally {
    $report.finishedUtc = [DateTime]::UtcNow.ToString('o'); $report.elapsedMs = $clock.Elapsed.TotalMilliseconds
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
    Save-Report
    if ($recordingError -and $exitCode -eq 0) { $exitCode = 1; $report.status = 'failed'; $report.failure = '[range:evidence] Final report could not be written.' }
}
if ($exitCode -eq 0) { Write-Output "PASS range-regression: scope=$($Scope -join ',') evidence=$out" }
exit $exitCode
