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
    $source = Join-Path $repository 'scripts/Test-M0.ps1'
    $ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw 'Production script parse error.' }
    $functions = @($ast.FindAll({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-DotnetChecked'
    }, $true))
    if ($functions.Count -ne 1) { throw 'Expected the single actual production boundary.' }
    $functions[0].Extent.Text | Set-Content (Join-Path $evidence 'function-source.ps1')
    Invoke-Expression $functions[0].Extent.Text
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
       sourceSha256 = (Get-FileHash $source -Algorithm SHA256).Hash; actualFunction = $true } |
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
