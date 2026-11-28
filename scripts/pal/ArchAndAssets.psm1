Set-StrictMode -Version Latest

function Invoke-ArchCheck {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot
    )
    $root = [IO.Path]::GetFullPath($RepositoryRoot)

    Write-Host '==> [check-arch 1/6] PowerShell syntax'
    & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'scripts/Test-PowerShellSyntax.ps1') -RepositoryRoot $root
    if ($LASTEXITCODE -ne 0) { throw "check-arch failed at Test-PowerShellSyntax.ps1 (exit $LASTEXITCODE)." }

    Write-Host '==> [check-arch 2/6] Unit boundary'
    & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'scripts/Test-UnitBoundary.ps1') -RepositoryRoot $root
    if ($LASTEXITCODE -ne 0) { throw "check-arch failed at Test-UnitBoundary.ps1 (exit $LASTEXITCODE)." }

    Write-Host '==> [check-arch 3/6] Repo hygiene (-AllDocs)'
    & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'scripts/Test-RepoHygiene.ps1') -RepositoryRoot $root -AllDocs
    if ($LASTEXITCODE -ne 0) { throw "check-arch failed at Test-RepoHygiene.ps1 (exit $LASTEXITCODE)." }

    Write-Host '==> [check-arch 4/6] Affected map self-check'
    $mapRes = Test-AffectedMapIntegrity -RepositoryRoot $root
    if (-not $mapRes.Passed) {
        throw "check-arch failed at affected-map self-check: $($mapRes.Errors -join '; ')"
    }
    Write-Host "PASS map self-check: $($mapRes.CheckedProjects) solution projects verified."

    Write-Host '==> [check-arch 5/6] Capability ledger zero-drift'
    Invoke-CapabilityLedgerCheck -RepositoryRoot $root

    Write-Host '==> [check-arch 6/6] Compile-time guardrails build (RS0030 + PAL0001~PAL0003)'
    $sln = Join-Path $root 'GodotPal.sln'
    & dotnet build $sln --configuration Debug --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "check-arch failed at dotnet build GodotPal.sln (exit $LASTEXITCODE)." }

    Write-Host 'PASS check-arch: syntax, boundary, repo-hygiene (-AllDocs), map, capability-ledger, and compile-time guardrails all passed.'
}

function Invoke-AssetsInspect {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [switch]$AsJson
    )
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $rawDir = [string]$env:PAL_RAW_DATA_DIR
    if ([string]::IsNullOrWhiteSpace($rawDir)) {
        throw 'PAL_RAW_DATA_DIR is not configured. Set $env:PAL_RAW_DATA_DIR to your local raw data directory before running assets inspect.'
    }
    $fullRawDir = [IO.Path]::GetFullPath($rawDir)
    if (-not [IO.Directory]::Exists($fullRawDir)) {
        throw 'Configured PAL_RAW_DATA_DIR does not exist on disk (private path omitted).'
    }

    $extractProj = Join-Path $root 'tools/Pal.Extract/Pal.Extract.csproj'
    $stdoutLines = & dotnet run --project $extractProj --configuration Debug -- inspect --input $fullRawDir
    if ($LASTEXITCODE -ne 0) {
        throw "Pal.Extract inspect failed (exit $LASTEXITCODE)."
    }
    $rawJson = ($stdoutLines -join "`n").Trim()
    $parsed = $rawJson | ConvertFrom-Json

    # Strictly redact all local/absolute paths and never emit raw text or binary content.
    # Schema matches tools/Pal.Extract/ArchiveInspector.cs (InspectionReport / ArchiveReport).
    $sanitizedArchives = [Collections.Generic.List[object]]::new()
    foreach ($a in @($parsed.archives)) {
        $chunkCount = [int]$a.chunkCount
        $emptyCount = [int]$a.emptyChunkCount
        $sanitizedArchives.Add([ordered]@{
            fileName       = [IO.Path]::GetFileName([string]$a.name)
            sizeBytes      = [long]$a.byteLength
            chunkCount     = $chunkCount
            nonEmptyChunks = ($chunkCount - $emptyCount)
        })
    }

    $t3Cache = [ordered]@{
        configured = $false
        cacheKey   = $null
        assetsHit  = $false
        goldensHit = $false
    }
    try {
        $cacheInfo = Get-T3CacheInfo -RepositoryRoot $root
        $t3Cache.configured = [bool]$cacheInfo.Configured
        $t3Cache.cacheKey   = if ($cacheInfo.Configured) { [string]$cacheInfo.CacheKey } else { $null }
        $t3Cache.assetsHit  = if ($cacheInfo.Configured) { [bool]$cacheInfo.AssetsHit } else { $false }
        $t3Cache.goldensHit = if ($cacheInfo.Configured) { [bool]$cacheInfo.GoldensHit } else { $false }
    } catch {
        # Directory has the 6 base MKFs for Pal.Extract inspect but is missing optional T3-only archives.
        $t3Cache.configured = $false
    }

    $result = [ordered]@{
        source          = '<redacted:PAL_RAW_DATA_DIR>'
        versionHint     = if ($parsed.PSObject.Properties['versionHint']) { [string]$parsed.versionHint } else { 'unknown' }
        archiveCount    = $sanitizedArchives.Count
        archives        = @($sanitizedArchives)
        t3Cache         = $t3Cache
    }

    if ($AsJson) {
        Write-Output (ConvertTo-Json -InputObject $result -Depth 6)
    } else {
        Write-Output "Assets inspect (source=<redacted:PAL_RAW_DATA_DIR>, archives=$($result.archiveCount)):"
        foreach ($item in $result.archives) {
            $neText = if ($null -ne $item.nonEmptyChunks) { ", nonEmpty=$($item.nonEmptyChunks)" } else { '' }
            Write-Output "  - $($item.fileName): $($item.sizeBytes) bytes, chunks=$($item.chunkCount)$neText"
        }
        $assetState = if ($result.t3Cache.assetsHit) { 'HIT' } else { 'MISS' }
        $goldenState = if ($result.t3Cache.goldensHit) { 'HIT' } else { 'MISS' }
        Write-Output "T3 cache: key=$($result.t3Cache.cacheKey) assets=$assetState goldens=$goldenState (private paths omitted)"
    }
}

Export-ModuleMember -Function Invoke-ArchCheck, Invoke-AssetsInspect
