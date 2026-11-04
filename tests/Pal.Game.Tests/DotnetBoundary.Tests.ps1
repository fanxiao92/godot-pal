# Real production-function regression; synthetic own processes only, no Godot or CIM queries.
[CmdletBinding()]
param(
    [string]$EvidenceDirectory,
    [string]$DriverDll,
    [switch]$OnlyInheritedWriter,
    [string]$CaseDirectory,
    [string]$Mode,
    [int]$SupervisorPid
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($CaseDirectory) {
    # Invoked in an independent pwsh process by the owned-handle supervisor.
    $evidence = $CaseDirectory
    $commands = [Collections.Generic.List[object]]::new()
    $script:sequence = 0
    function Save-ProcessSnapshot([string]$Name) {
        # Collector behavior is independently tested by ProcessSnapshot.Tests.ps1.
        # This regression exercises real Process/streams/waits, not process enumeration.
        $Name | Add-Content (Join-Path $evidence 'snapshot-noop.log')
    }
    $tokens = $null; $parseErrors = $null
    $source = Join-Path $repository 'scripts/DotnetCheckedDefinition.ps1'
    $productionSource = Join-Path $repository 'scripts/Test-M0.ps1'
    $ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw 'Production script parse error.' }
    $functions = @($ast.FindAll({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-DotnetChecked'
    }, $true))
    if ($functions.Count -ne 1) { throw 'Expected the single actual production boundary helper.' }
    $functions[0].Extent.Text | Set-Content (Join-Path $evidence 'function-source.ps1')
    $m0Tokens = $null; $m0Errors = $null
    $m0Ast = [Management.Automation.Language.Parser]::ParseFile($productionSource, [ref]$m0Tokens, [ref]$m0Errors)
    $m0Functions = @($m0Ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-DotnetChecked' }, $true))
    if ($m0Errors.Count -or $m0Functions.Count -ne 0) { throw 'M0 must only dot-source the unique boundary helper.' }
    $helperHash = (Get-FileHash $source -Algorithm SHA256).Hash
    $productionHash = (Get-FileHash $productionSource -Algorithm SHA256).Hash
    . $source
    $expected = if ($Mode -eq 'expected') { 1 } else { 0 }
    $parameters = @{ Arguments = @('exec', $DriverDll, 'root', $evidence, $Mode, "$SupervisorPid"); ExpectedExit = $expected }
    # The old function is run unmodified for RED: its root exits promptly, EOF is what blocks.
    if ((Get-Command Invoke-DotnetChecked).Parameters.ContainsKey('TimeoutMilliseconds')) {
        $parameters.TimeoutMilliseconds = 1500
        $parameters.CleanupTimeoutMilliseconds = 1000
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $threw = $false; $text = ''
    try { $text = Invoke-DotnetChecked @parameters }
    catch { $threw = $true; $text = $_.Exception.Message }
    @{ threw = $threw; text = [string]$text; elapsedMs = $clock.Elapsed.TotalMilliseconds;
       sourceSha256 = $productionHash; helperSha256 = $helperHash; actualFunction = $true } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'invocation.json')
    exit 0
}

$root = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else {
    Join-Path ([IO.Path]::GetTempPath()) ('pal-dotnet-boundary-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path $root) { throw 'Use a new boundary evidence directory.' }
$null = New-Item -ItemType Directory $root
if (-not $DriverDll) {
    $project = Join-Path $PSScriptRoot 'Fixtures/DotnetBoundary/DotnetBoundary.csproj'
    & dotnet build $project --configfile (Join-Path (Split-Path $project) 'NuGet.Config') `
        --disable-build-servers -p:UseSharedCompilation=false -nodeReuse:false *> (Join-Path $root 'fixture-build.log')
    $buildExit = $LASTEXITCODE
    if ($EvidenceDirectory) { Copy-Item (Join-Path $root 'fixture-build.log') (Join-Path (Split-Path $root) 'boundary-fixture-build.log') }
    if ($buildExit -ne 0) { throw "Boundary fixture build failed: $root/fixture-build.log" }
    $DriverDll = Join-Path (Split-Path $project) 'bin/Debug/net10.0/DotnetBoundary.dll'
}
$DriverDll = (Resolve-Path $DriverDll).Path
# Deterministic locked publication regression at the actual owned-handle receiver.
& dotnet exec $DriverDll handle-publication-check (Join-Path $root 'handle-publication-check') *> (Join-Path $root 'handle-publication-check.log')
$handlePublicationExit = $LASTEXITCODE
if ($EvidenceDirectory) {
    Copy-Item (Join-Path $root 'handle-publication-check.log') (Join-Path (Split-Path $root) 'boundary-handle-publication-check.log')
    $handlePublicationResult = Join-Path $root 'handle-publication-check/result.json'
    if (Test-Path $handlePublicationResult) { Copy-Item $handlePublicationResult (Join-Path (Split-Path $root) 'boundary-handle-publication-check.json') }
}
if ($handlePublicationExit -ne 0) { throw "Boundary handle publication regression failed (exit=$handlePublicationExit); inspect $root/handle-publication-check.log" }
# Deterministic file-sharing/partial-write regression at the actual journal reader.
& dotnet exec $DriverDll journal-check (Join-Path $root 'journal-check') *> (Join-Path $root 'journal-check.log')
$journalExit = $LASTEXITCODE
if ($EvidenceDirectory) {
    Copy-Item (Join-Path $root 'journal-check.log') (Join-Path (Split-Path $root) 'boundary-journal-check.log')
    $journalResult = Join-Path $root 'journal-check/result.json'
    if (Test-Path $journalResult) { Copy-Item $journalResult (Join-Path (Split-Path $root) 'boundary-journal-check.json') }
}
if ($journalExit -ne 0) { throw "Boundary journal regression failed (exit=$journalExit); inspect $root/journal-check.log" }
$pwsh = (Get-Process -Id $PID).Path # This process's own executable, not a global query.
$modes = if ($OnlyInheritedWriter) { @('stdout-held') } else {
    @('normal', 'expected', 'unexpected', 'error', 'timeout', 'stdout-held', 'stderr-held', 'isolated')
}
$results = [Collections.Generic.List[object]]::new()
foreach ($name in $modes) {
    $casePath = Join-Path $root $name
    & dotnet exec $DriverDll case $casePath $name $pwsh $PSCommandPath *> (Join-Path $root "$name-supervisor.log")
    $exit = $LASTEXITCODE
    $resultPath = Join-Path $casePath 'result.json'
    $result = if (Test-Path $resultPath) { Get-Content $resultPath -Raw | ConvertFrom-Json } else { $null }
    $results.Add(@{ name = $name; exitCode = $exit; result = $result })
    $results | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $root 'results.json')
    # Flatten key evidence for the existing shared gate's top-level *.log/*.json artifact glob.
    if ($EvidenceDirectory) {
        $parent = Split-Path $root
        Copy-Item (Join-Path $root 'results.json') (Join-Path $parent 'boundary-results.json')
        foreach ($file in @(Get-ChildItem $casePath -File -ErrorAction SilentlyContinue | Where-Object Extension -in @('.log', '.json'))) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $parent "boundary-$name-$($file.Name)")
        }
    }
    if ($exit -ne 0) { throw "Boundary regression $name failed (exit=$exit); inspect $resultPath" }
}
Write-Host "PASS: $($results.Count) real dotnet-boundary cases; no Godot/CIM/foreign process operations. Evidence: $root"
