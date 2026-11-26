Set-StrictMode -Version Latest

$script:PinnedGodotVersion = '4.7.2.stable.mono.official.ed1daf0bf'

function Read-SafeXmlDocument {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "XML file not found: $Path"
    }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $xml = [xml]::new()
        $xml.Load($reader)
        return $xml
    } finally {
        $reader.Dispose()
    }
}

function Resolve-GodotBinary {
    param([string]$ExplicitPath)
    $candidate = $ExplicitPath
    if (-not $candidate) { $candidate = $env:GODOT_BIN }
    if (-not $candidate) { $candidate = $env:PAL_TEST_GODOT }
    if ($candidate) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [IO.Path]::GetFullPath($candidate)
        }
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
        throw "Configured Godot executable not found: $candidate"
    }
    foreach ($name in @('godot-mono.console.exe', 'godot-mono', 'godot')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    throw 'Godot executable not found. Pass --godot-path/-GodotPath, set GODOT_BIN, or run `./pal.ps1 godot install`.'
}

function Assert-NoRunningGodotProcess {
    # Cross-platform process check: strictly PID and ProcessName only.
    $procs = @(Get-Process -Name 'godot*' -ErrorAction SilentlyContinue | Select-Object Id, ProcessName)
    if ($procs.Count -gt 0) {
        $list = ($procs | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ', '
        throw "Existing Godot process detected ($list); stop and confirm ownership before starting Godot."
    }
}

function Assert-PinnedGodotVersion {
    param([Parameter(Mandatory)][string]$GodotExe)
    $ver = (& $GodotExe --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $ver -ne $script:PinnedGodotVersion) {
        throw "Godot version mismatch: expected '$($script:PinnedGodotVersion)', got '$ver' (exit $LASTEXITCODE)."
    }
    return $ver
}

function New-RunSummaryObject {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$Status,
        [AllowNull()][string]$ErrorMsg = $null,
        [double]$DurationSec = 0.0,
        [int]$Total = 0,
        [int]$Passed = 0,
        [int]$Failed = 0,
        [int]$Skipped = 0,
        [AllowEmptyCollection()][string[]]$TrxFiles = @(),
        [AllowEmptyCollection()][string[]]$CoverageFiles = @(),
        [AllowEmptyCollection()][object[]]$Coverage = @(),
        [Parameter(Mandatory)][string]$ArtifactsDir
    )
    return [ordered]@{
        command       = $Command
        status        = $Status
        errorMsg      = $ErrorMsg
        durationSec   = [Math]::Round($DurationSec, 2)
        counts        = [ordered]@{
            total   = $Total
            passed  = $Passed
            failed  = $Failed
            skipped = $Skipped
        }
        trxFiles      = @($TrxFiles)
        coverageFiles = @($CoverageFiles)
        coverage      = @($Coverage)
        artifactsDir  = $ArtifactsDir
    }
}

Export-ModuleMember -Function @(
    'Read-SafeXmlDocument',
    'Resolve-GodotBinary',
    'Assert-NoRunningGodotProcess',
    'Assert-PinnedGodotVersion',
    'New-RunSummaryObject'
)
