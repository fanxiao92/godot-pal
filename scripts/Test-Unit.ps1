# Explicit Core + CLI unit feedback. Not the solution/full acceptance gate.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [AllowEmptyCollection()][AllowEmptyString()][string[]]$Project = @('core', 'cli-unit'),
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$utf8 = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'GameTestBatches.ps1') # Definitions only; never Get-GameBatchPlan.
$paths = @{ core = 'tests/Pal.Core.Tests/Pal.Core.Tests.csproj'; 'cli-unit' = 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj' }
$journal = [Collections.Generic.List[object]]::new()
$projects = [Collections.Generic.List[object]]::new()
$exitCode = 1
$ownsOutput = $false
$report = [ordered]@{ schemaVersion = 1; runId = [guid]::NewGuid().ToString(); status = 'incomplete'; repositoryRoot = $RepositoryRoot; selection = $Project; startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null; elapsedMs = $null; sdk = $null; sourceIdentity = @(); projects = @(); commands = @(); failure = $null; cache = 'Ordinary current-user NuGet cache and existing build outputs; not empty-cache evidence. No engine/resources/fixed-package inputs supplied by this command.' }
$report.environment = @{ os = [Environment]::OSVersion.VersionString; runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription; architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString(); powershell = $PSVersionTable.PSVersion.ToString() }
$report.head = $null
$report.toolIdentity = @(foreach ($file in @($PSCommandPath, (Join-Path $PSScriptRoot 'GameTestBatches.ps1'), (Join-Path $PSScriptRoot 'Test-UnitBoundary.ps1'))) { @{ path = $file; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } })
$whole = [Diagnostics.Stopwatch]::StartNew()
function Fail([string]$Code, [string]$Message) { throw "[unit:$Code] $Message" }
function Save-Report {
    $report.commands = $journal.ToArray(); $report.projects = $projects.ToArray()
    [IO.File]::WriteAllText((Join-Path $out 'unit-run.json'), ($report | ConvertTo-Json -Depth 30), $utf8)
}
function Invoke-UnitCommand([string]$Name, [string]$File, [string[]]$Arguments) {
    $record = [ordered]@{ name = $Name; executable = $File; arguments = $Arguments; cwd = $root; startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null; elapsedMs = $null; exitCode = $null; exitKnown = $false; stdoutComplete = $false; stderrComplete = $false; status = 'incomplete'; stdout = "$Name.stdout.log"; stderr = "$Name.stderr.log" }
    $journal.Add($record); Save-Report
    $psi = [Diagnostics.ProcessStartInfo]::new($File)
    $psi.WorkingDirectory = $root; $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $utf8; $psi.StandardErrorEncoding = $utf8
    $psi.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'
    $psi.Environment['DOTNET_NOLOGO'] = '1'
    foreach ($argument in $Arguments) { $psi.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $psi
    $stdout = [IO.File]::Create((Join-Path $out $record.stdout))
    $stderr = [IO.File]::Create((Join-Path $out $record.stderr))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $started = $false
    try {
        $started = $process.Start()
        if (-not $started) { Fail 'start' "Cannot start $Name" }
        $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
        # Same 600s work budget for root exit and both EOFs; no engine/process observer.
        $all = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($process.WaitForExitAsync(), $copyOut, $copyErr))
        if (-not $all.Wait(600000)) { Fail 'incomplete-command' "$Name did not confirm exit and both EOFs within 600000ms; stop, retain evidence" }
        $record.exitCode = $process.ExitCode; $record.exitKnown = $true
        $record.stdoutComplete = $copyOut.IsCompletedSuccessfully; $record.stderrComplete = $copyErr.IsCompletedSuccessfully
        $record.status = 'completed'
    } finally {
        if ($started -and $process.HasExited) {
            $record.exitCode = $process.ExitCode; $record.exitKnown = $true
            if ($record.exitCode -ne 0) { $script:exitCode = $record.exitCode }
        }
        if ($started) { $record.stdoutComplete = $copyOut.IsCompletedSuccessfully; $record.stderrComplete = $copyErr.IsCompletedSuccessfully }
        if ($started -and -not $process.HasExited) {
            # Only this command's owned process tree; never query/kill unrelated processes.
            try { $process.Kill($true); $record.cleanupComplete = $process.WaitForExit(5000) } catch { $record.cleanupError = $_.Exception.Message }
        }
        $watch.Stop(); $record.elapsedMs = $watch.Elapsed.TotalMilliseconds; $record.finishedUtc = [DateTime]::UtcNow.ToString('o')
        $stdout.Dispose(); $stderr.Dispose(); $process.Dispose(); Save-Report
    }
    $text = [IO.File]::ReadAllText((Join-Path $out $record.stdout), $utf8) + "`n" + [IO.File]::ReadAllText((Join-Path $out $record.stderr), $utf8)
    if ($record.exitCode -ne 0) {
        [Console]::Error.WriteLine($text.TrimEnd())
        $script:exitCode = $record.exitCode
        if ($text -match '(?i)Aborting test run|Test Run Aborted|test run timeout|test host process crashed|Unhandled exception|WindowsStdOutHook') { Fail 'abnormal-session' "$Name aborted/crashed; stop and retain original exit/logs in $out" }
        Fail 'command-exit' "$Name exited $($record.exitCode); original logs: $out/$($record.stdout), $out/$($record.stderr)"
    }
    if ($text -match '(?im)^\s*(ERROR:|SCRIPT ERROR:|Unhandled exception|.*\berror (MSB|NU|CS)\d+)|Aborting test run|Test Run Aborted|test run timeout|test host process crashed|WindowsStdOutHook') { Fail 'logged-error' "$Name emitted failure diagnostics; see original logs in $out" }
    return $text.Trim()
}
function Read-UnitDiscovery([string]$Name, [string]$Assembly) {
    $diag = Join-Path $out "$Name.discovery.diag.log"
    if (-not [IO.File]::Exists($diag)) { Fail 'discovery' "Missing discovery diagnostics: $diag" }
    $cases = [Collections.Generic.List[object]]::new()
    $completed = $null
    $warnings = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadLines($diag, $utf8)) {
        if ($line -notmatch 'TestRequestSender.OnDiscoveryMessageReceived: Received message: (.*)$') { continue }
        $message = $Matches[1] | ConvertFrom-Json
        if ($message.Version -ne 7) { Fail 'discovery' 'Unsupported VSTest diagnostic protocol (expected Version 7)' }
        switch -CaseSensitive ($message.MessageType) {
            'TestDiscovery.TestFound' {
                if ($null -ne $completed) { Fail 'discovery' 'Cases arrived after discovery completion' }
                foreach ($case in $message.Payload) { $cases.Add($case) }
            }
            'TestDiscovery.Completed' {
                if ($null -ne $completed -or $message.Payload.IsAborted -ne $false) { Fail 'discovery' 'Duplicate or aborted discovery completion' }
                $completed = $message.Payload
                foreach ($case in $completed.LastDiscoveredTests) { $cases.Add($case) }
            }
            'TestSession.Message' { if ($message.Payload.MessageLevel -ne 0) { $warnings.Add($message.Payload.Message) } }
            default { Fail 'discovery' "Unrecognized discovery message: $($message.MessageType)" }
        }
    }
    if ($null -eq $completed -or $cases.Count -ne $completed.TotalTests) { Fail 'discovery' 'Missing/incomplete discovery completion or count mismatch' }
    if ($cases.Count -eq 0) { Fail 'zero-discovery' "$Name discovered zero tests; no execution can count as success" }
    if ($warnings.Count) { Fail 'discovery' ("Discovery warning/error: " + ($warnings -join '; ')) }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $cases) {
        $id = [guid]::Empty
        $serialization = @($case.Properties | Where-Object { $_.Key.Id -ceq 'XunitTestCaseSerialization' })
        $unique = @($case.Properties | Where-Object { $_.Key.Id -ceq 'XunitTestCaseUniqueID' })
        if (-not [guid]::TryParse($case.Id, [ref]$id) -or $id -eq [guid]::Empty -or -not $ids.Add($case.Id) -or
            -not $case.FullyQualifiedName -or -not $case.DisplayName -or [IO.Path]::GetFullPath($case.Source) -cne $Assembly -or
            $case.ExecutorUri -cne 'executor://xunit/VsTestRunner3/netcore/' -or $serialization.Count -ne 1 -or -not $serialization[0].Value -or $unique.Count -ne 1 -or -not $unique[0].Value) { Fail 'discovery' 'Invalid/incomplete case identity, duplicate ID or unexpected assembly/adapter' }
    }
    $frozen = [ordered]@{ assembly = $Assembly; sha256 = (Get-FileHash -LiteralPath $Assembly -Algorithm SHA256).Hash; total = $cases.Count; completed = $completed; cases = $cases.ToArray() }
    [IO.File]::WriteAllText((Join-Path $out "$Name.discovery.json"), ($frozen | ConvertTo-Json -Depth 30), $utf8)
    return $frozen
}
function Assert-UnitRun([string]$Name, $Discovery) {
    $path = Join-Path $out "$Name/results.trx"
    if (-not [IO.File]::Exists($path)) { Fail 'results-missing' "$Name did not produce the current run's TRX file: $path" }
    try { $identity = Read-TestRunIdentity $path } catch { Fail 'results' "$Name invalid TRX/identity graph: $($_.Exception.Message)" }
    $doc = $identity.document; $ns = $identity.ns
    foreach ($element in @('Times','Results','TestDefinitions','TestEntries','ResultSummary')) {
        if ($doc.SelectNodes("/t:TestRun/t:$element", $ns).Count -ne 1) { Fail 'results' "$Name missing/duplicate TRX element: $element" }
    }
    $guid = [guid]::Empty
    foreach ($id in @($identity.runId) + @($identity.executionIds)) {
        if (-not [guid]::TryParse($id, [ref]$guid) -or $guid -eq [guid]::Empty) { Fail 'identity' "$Name invalid run/execution GUID" }
    }
    $times = $doc.SelectSingleNode('/t:TestRun/t:Times', $ns)
    foreach ($attribute in @('creation','queuing','start','finish')) {
        $stamp = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse($times.GetAttribute($attribute), [ref]$stamp)) { Fail 'results' "$Name missing/invalid session timestamp: $attribute" }
    }
    if ([DateTimeOffset]::Parse($times.GetAttribute('finish')) -lt [DateTimeOffset]::Parse($times.GetAttribute('start'))) { Fail 'results' "$Name session finishes before start" }
    $summary = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
    $counters = $summary.SelectSingleNode('t:Counters', $ns)
    if ($summary.GetAttribute('outcome') -cne 'Completed' -or -not $counters -or $summary.SelectNodes('t:RunInfos/t:RunInfo', $ns).Count) { Fail 'results' "$Name incomplete summary or unexpected RunInfo" }
    foreach ($required in @('total','executed','passed','failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending')) {
        $expected = if ($required -in @('total','executed','passed')) { $Discovery.total } else { 0 }
        if (-not $counters.HasAttribute($required) -or $counters.GetAttribute($required) -cnotmatch '^\d+$' -or [long]$counters.GetAttribute($required) -ne $expected) { Fail 'results' "$Name invalid counter: $required" }
    }
    foreach ($attribute in $counters.Attributes) { if ($attribute.Name -notin @('total','executed','passed') -and $attribute.Value -cne '0') { Fail 'results' "$Name unexpected nonzero counter: $($attribute.Name)" } }
    $rows = @($doc.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    if ($rows.Count -ne $Discovery.total) { Fail 'identity' "$Name executed/discovered case sets differ" }
    foreach ($case in $Discovery.cases) {
        $row = @($rows | Where-Object { $_.GetAttribute('testId') -ceq $case.Id })
        $definition = @($doc.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns) | Where-Object { $_.GetAttribute('id') -ceq $case.Id })
        if ($row.Count -ne 1 -or $definition.Count -ne 1 -or $row[0].GetAttribute('testName') -cne $case.DisplayName -or $row[0].GetAttribute('outcome') -cne 'Passed') { Fail 'identity' "$Name missing/changed/nonpassing case: $($case.Id)" }
        $method = $definition[0].SelectSingleNode('t:TestMethod', $ns)
        if (($method.GetAttribute('className') + '.' + $method.GetAttribute('name')) -cne $case.FullyQualifiedName -or [IO.Path]::GetFullPath($method.GetAttribute('codeBase')) -cne $Discovery.assembly) { Fail 'identity' "$Name definition method/assembly mismatch" }
    }
    return $identity
}
try {
    $seen = @{}
    if (-not $Project.Count) { Fail 'selection' 'Select a nonempty subset of core, cli-unit' }
    foreach ($name in $Project) {
        if (-not $name -or -not $paths.ContainsKey($name) -or $seen.ContainsKey($name)) { Fail 'selection' "Unknown, empty or duplicate project: '$name'; allowed: core, cli-unit" }
        $seen[$name] = $true
    }
    if (-not $RepositoryRoot -or -not [IO.Directory]::Exists($RepositoryRoot)) { Fail 'project-missing' "Repository root not found: $RepositoryRoot" }
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('TestResults/unit-' + $report.runId) }
    $out = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $out) { Fail 'output-exists' "Output directory must not exist: $out" }
    $null = New-Item -ItemType Directory -Path $out
    $ownsOutput = $true
    Save-Report
    $selection = @($Project | ForEach-Object { [pscustomobject]@{ name = $_; path = Join-Path $root $paths[$_]; discovery = $null; assembly = $null; artifacts = @() } })
    # The boundary public command contains exit. Isolate it and transport a real array, not CSV.
    $quote = { param($s) "'" + $s.Replace("'", "''") + "'" }
    $array = (@($Project | ForEach-Object { & $quote $_ }) -join ',')
    $boundaryCode = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(`$false); & $(& $quote (Join-Path $PSScriptRoot 'Test-UnitBoundary.ps1')) -RepositoryRoot $(& $quote $root) -Project @($array)"
    $tokens = $null; $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($boundaryCode, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { Fail 'boundary-invocation' ($parseErrors | Out-String) }
    $null = Invoke-UnitCommand 'boundary' ([Environment]::ProcessPath) @('-NoProfile', '-EncodedCommand', [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($boundaryCode)))
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $report.sdk = Invoke-UnitCommand 'sdk' $dotnet @('--version')
    if (Test-Path -LiteralPath (Join-Path $root '.git')) {
        $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($git) { $report.head = Invoke-UnitCommand 'git-head' $git.Source @('rev-parse', 'HEAD') }
    }
    # Record exact applicable source/project bytes, not a claim that HEAD describes a dirty tree.
    $sourceFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $selection) {
        $production = if ($item.name -eq 'core') { 'src/Pal.Core' } else { 'tools/Pal.Extract' }
        foreach ($dir in @((Split-Path $item.path -Parent), (Join-Path $root $production))) {
            foreach ($file in Get-ChildItem -LiteralPath $dir -Recurse -File | Where-Object { $_.Extension -in @('.cs','.csproj') -and [IO.Path]::GetRelativePath($dir, $_.FullName) -notmatch '(^|[\\/])(bin|obj)([\\/]|$)' }) { $null = $sourceFiles.Add($file.FullName) }
        }
    }
    $linked = Join-Path $root 'tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs'
    if ('cli-unit' -in $Project -and [IO.File]::Exists($linked)) { $null = $sourceFiles.Add($linked) }
    foreach ($file in @('global.json','NuGet.Config')) {
        $path = Join-Path $root $file
        if ([IO.File]::Exists($path)) { $null = $sourceFiles.Add($path) }
    }
    $report.sourceIdentity = @(foreach ($file in ($sourceFiles | Sort-Object)) { @{ path = [IO.Path]::GetRelativePath($root, $file); sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } })
    foreach ($item in $selection) {
        $null = Invoke-UnitCommand "$($item.name).build" $dotnet @('build', $item.path, '--configuration', 'Debug', '--nologo')
        $item.assembly = Join-Path (Split-Path $item.path -Parent) ('bin/Debug/net10.0/' + [IO.Path]::GetFileNameWithoutExtension($item.path) + '.dll')
        $productionAssembly = if ($item.name -eq 'core') { 'Pal.Core' } else { 'Pal.Extract' }
        $artifactPaths = @($item.assembly, [IO.Path]::ChangeExtension($item.assembly, '.pdb'), [IO.Path]::ChangeExtension($item.assembly, '.deps.json'), (Join-Path (Split-Path $item.assembly -Parent) "$productionAssembly.dll"), (Join-Path (Split-Path $item.assembly -Parent) "$productionAssembly.pdb"))
        $item.artifacts = @(foreach ($path in $artifactPaths) { @{ path = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } })
        $null = Invoke-UnitCommand "$($item.name).discover" $dotnet @('test', $item.path, '--configuration', 'Debug', '--no-build', '--no-restore', '--list-tests', '--diag', (Join-Path $out "$($item.name).discovery.diag.log"))
        $item.discovery = Read-UnitDiscovery $item.name $item.assembly
    }
    # Both complete discovery snapshots are durable before any selected test execution.
    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $selection) {
        $null = Invoke-UnitCommand "$($item.name).test" $dotnet @('test', $item.path, '--configuration', 'Debug', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=results.trx', '--results-directory', (Join-Path $out $item.name))
        $identity = Assert-UnitRun $item.name $item.discovery
        if (-not $runIds.Add($identity.runId)) { Fail 'identity' 'Reused TRX run ID' }
        foreach ($id in $identity.executionIds) { if (-not $executionIds.Add($id)) { Fail 'identity' 'Reused execution ID' } }
        foreach ($artifact in $item.artifacts) {
            if ((Get-FileHash -LiteralPath $artifact.path -Algorithm SHA256).Hash -cne $artifact.sha256) { Fail 'identity' "Compiled artifact changed after discovery: $($artifact.path)" }
        }
        $projects.Add(@{ artifacts = $item.artifacts; name = $item.name; project = $paths[$item.name]; assembly = $item.assembly; assemblySha256 = $item.discovery.sha256; discovered = $item.discovery.total; executed = $identity.names.Count; skipped = 0; runId = $identity.runId; caseIds = @($item.discovery.cases.Id); executionIds = $identity.executionIds; trx = "$($item.name)/results.trx" })
        Save-Report
    }
    foreach ($source in $report.sourceIdentity) {
        if ((Get-FileHash -LiteralPath (Join-Path $root $source.path) -Algorithm SHA256).Hash -cne $source.sha256) { Fail 'source-changed' "Source/config changed during run: $($source.path)" }
    }
    foreach ($tool in $report.toolIdentity) {
        if ((Get-FileHash -LiteralPath $tool.path -Algorithm SHA256).Hash -cne $tool.sha256) { Fail 'source-changed' "Entry/helper source changed during run: $($tool.path)" }
    }
    $report.status = 'completed'; $exitCode = 0
    Write-Output "PASS unit: $($selection.Count) projects; complete discovery/execution identities; evidence $out"
} catch {
    $report.failure = $_.Exception.Message
    $report.status = if (@($journal | Where-Object status -CNE 'completed').Count -or $report.failure -match '\[unit:abnormal-session\]') { 'incomplete' } else { 'failed' }
    [Console]::Error.WriteLine($_.Exception.Message)
} finally {
    $whole.Stop(); $report.finishedUtc = [DateTime]::UtcNow.ToString('o'); $report.elapsedMs = $whole.Elapsed.TotalMilliseconds
    if ($ownsOutput -and [IO.Directory]::Exists($out)) {
        try { Save-Report } catch { [Console]::Error.WriteLine("[unit:report] $($_.Exception.Message)"); if ($exitCode -eq 0) { $exitCode = 1 } }
    }
}
exit $exitCode
