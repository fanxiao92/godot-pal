Set-StrictMode -Version Latest

if (-not (Get-Command Read-SafeXmlDocument -ErrorAction SilentlyContinue)) {
    Import-Module (Join-Path $PSScriptRoot 'PalCommon.psm1') -Global
}

function Open-TrxDocument {
    param([Parameter(Mandatory)][string]$TrxPath)
    $xml = Read-SafeXmlDocument -Path $TrxPath
    $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    return @{ Xml = $xml; Ns = $ns }
}

function Test-TrxFailureOutcome {
    param([string]$Outcome)
    return ($Outcome -in @('Failed', 'Error', 'Timeout', 'Aborted'))
}

function Read-TrxSummary {
    param([Parameter(Mandatory)][string]$TrxPath)
    $trx = Open-TrxDocument -TrxPath $TrxPath
    $counters = $trx.Xml.SelectSingleNode('//t:ResultSummary/t:Counters', $trx.Ns)
    if (-not $counters) {
        return [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0; outcome = 'Unknown' }
    }
    $summary = $trx.Xml.SelectSingleNode('//t:ResultSummary', $trx.Ns)
    $total = [int]($counters.GetAttribute('total'))
    $passed = [int]($counters.GetAttribute('passed'))
    $failed = [int]($counters.GetAttribute('failed')) + [int]($counters.GetAttribute('error')) + [int]($counters.GetAttribute('timeout')) + [int]($counters.GetAttribute('aborted'))
    $executed = [int]($counters.GetAttribute('executed'))
    $skipped = [Math]::Max(0, $total - $executed)
    return [ordered]@{
        total   = $total
        passed  = $passed
        failed  = $failed
        skipped = $skipped
        outcome = [string]($summary.GetAttribute('outcome'))
    }
}

function Read-TrxFailures {
    param([Parameter(Mandatory)][string]$TrxPath)
    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) { return @() }
    $trx = Open-TrxDocument -TrxPath $TrxPath
    $failures = [Collections.Generic.List[object]]::new()
    foreach ($r in @($trx.Xml.SelectNodes('//t:Results/t:UnitTestResult', $trx.Ns))) {
        $outcome = [string]$r.GetAttribute('outcome')
        if (Test-TrxFailureOutcome -Outcome $outcome) {
            $msgNode = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $trx.Ns)
            $msg = if ($msgNode) { ($msgNode.InnerText -replace '[\r\n]+', ' ').Trim() } else { $outcome }
            $failures.Add([ordered]@{
                testName = [string]$r.GetAttribute('testName')
                outcome  = $outcome
                message  = $msg
            })
        }
    }
    return @($failures)
}

function Convert-TrxToJunit {
    param(
        [Parameter(Mandatory)][string]$TrxPath,
        [Parameter(Mandatory)][string]$JunitPath
    )
    $trx = Open-TrxDocument -TrxPath $TrxPath
    $results = @($trx.Xml.SelectNodes('//t:Results/t:UnitTestResult', $trx.Ns))

    $doc = [xml]::new()
    $decl = $doc.CreateXmlDeclaration('1.0', 'utf-8', $null)
    [void]$doc.AppendChild($decl)
    $suites = $doc.CreateElement('testsuites')
    $suite = $doc.CreateElement('testsuite')
    $suite.SetAttribute('name', [IO.Path]::GetFileNameWithoutExtension($TrxPath))

    $total = 0; $failures = 0; $skipped = 0; $totalSec = 0.0
    foreach ($r in $results) {
        $total++
        $tc = $doc.CreateElement('testcase')
        $testName = [string]$r.GetAttribute('testName')
        $tc.SetAttribute('name', $testName)
        $tc.SetAttribute('classname', [IO.Path]::GetFileNameWithoutExtension($TrxPath))
        $durStr = [string]$r.GetAttribute('duration')
        $sec = 0.0
        [TimeSpan]$ts = [TimeSpan]::Zero
        if ($durStr -and [TimeSpan]::TryParse($durStr, [ref]$ts)) {
            $sec = $ts.TotalSeconds
            $totalSec += $sec
        }
        $tc.SetAttribute('time', ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.###}', $sec)))
        $outcome = [string]$r.GetAttribute('outcome')
        if (Test-TrxFailureOutcome -Outcome $outcome) {
            $failures++
            $failEl = $doc.CreateElement('failure')
            $msgNode = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $trx.Ns)
            $stackNode = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $trx.Ns)
            $failEl.SetAttribute('message', $(if ($msgNode) { $msgNode.InnerText } else { $outcome }))
            if ($stackNode) { $failEl.InnerText = $stackNode.InnerText }
            [void]$tc.AppendChild($failEl)
        } elseif ($outcome -in @('NotExecuted', 'Inconclusive')) {
            $skipped++
            [void]$tc.AppendChild($doc.CreateElement('skipped'))
        }
        [void]$suite.AppendChild($tc)
    }

    $suite.SetAttribute('tests', [string]$total)
    $suite.SetAttribute('failures', [string]$failures)
    $suite.SetAttribute('skipped', [string]$skipped)
    $suite.SetAttribute('time', ([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.###}', $totalSec)))
    [void]$suites.AppendChild($suite)
    [void]$doc.AppendChild($suites)

    $parent = Split-Path $JunitPath -Parent
    if ($parent) { [void][IO.Directory]::CreateDirectory($parent) }
    $utf8 = [Text.UTF8Encoding]::new($false)
    $sw = [IO.StreamWriter]::new($JunitPath, $false, $utf8)
    try { $doc.Save($sw) } finally { $sw.Dispose() }
}

function Read-CoberturaCoverage {
    param([Parameter(Mandatory)][string]$CoveragePath)
    if (-not (Test-Path -LiteralPath $CoveragePath -PathType Leaf)) { return @() }
    $xml = Read-SafeXmlDocument -Path $CoveragePath
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($pkg in @($xml.SelectNodes('//packages/package'))) {
        $name = [string]$pkg.GetAttribute('name')
        $lineRate = 0.0; $branchRate = 0.0
        [void][double]::TryParse([string]$pkg.GetAttribute('line-rate'), [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$lineRate)
        [void][double]::TryParse([string]$pkg.GetAttribute('branch-rate'), [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$branchRate)
        $entries.Add([ordered]@{
            package    = $name
            lineRate   = [Math]::Round($lineRate * 100.0, 1)
            branchRate = [Math]::Round($branchRate * 100.0, 1)
        })
    }
    return @($entries)
}

function Write-RunSummaryJson {
    param(
        [Parameter(Mandatory)][string]$ArtifactsDir,
        [Parameter(Mandatory)][hashtable]$Summary
    )
    [void][IO.Directory]::CreateDirectory($ArtifactsDir)
    $summaryPath = Join-Path $ArtifactsDir 'run-summary.json'
    $json = $Summary | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($summaryPath, $json + "`n", [Text.UTF8Encoding]::new($false))
    return $summaryPath
}

function Format-MarkdownReport {
    param(
        [Parameter(Mandatory)][object[]]$Summaries,
        [string]$CommitSha = '',
        [string]$Format = 'pr-comment',
        [AllowEmptyCollection()][object[]]$TrxFailures = @()
    )
    $lines = [Collections.Generic.List[string]]::new()
    $allPassed = $true
    $totalDuration = 0.0
    foreach ($s in $Summaries) {
        if ($s.status -ne 'passed') { $allPassed = $false }
        if ($s.durationSec) { $totalDuration += [double]$s.durationSec }
    }
    $badge = if ($allPassed) { 'PASS' } else { 'FAIL' }
    $header = if ($Format -eq 'summary') { '### 验证摘要（pal.ps1）' } else { '### 验证证据（pal.ps1 report）' }
    $lines.Add($header)
    $lines.Add('')
    if ($CommitSha) { $lines.Add("- **提交**：``$CommitSha``") }
    $lines.Add("- **结论**：$badge（总耗时 ``$([Math]::Round($totalDuration, 1))s``）")
    $lines.Add('')
    $lines.Add('| 命令 | 状态 | 通过 | 失败 | 跳过 | 耗时 | 产物目录 |')
    $lines.Add('| :--- | :---: | ---: | ---: | ---: | ---: | :--- |')

    $coverageMap = [ordered]@{}
    $errors = [Collections.Generic.List[string]]::new()
    $stepRows = [Collections.Generic.List[object]]::new()
    foreach ($s in $Summaries) {
        $st = if ($s.status -eq 'passed') { 'PASS' } else { [string]$s.status.ToUpperInvariant() }
        $passed = if ($s.counts) { [int]$s.counts.passed } else { 0 }
        $failed = if ($s.counts) { [int]$s.counts.failed } else { 0 }
        $skipped = if ($s.counts) { [int]$s.counts.skipped } else { 0 }
        $dur = "$([Math]::Round([double]$s.durationSec, 1))s"
        $art = if ($s.artifactsDir) { "``$($s.artifactsDir)``" } else { '-' }
        $lines.Add("| ``$($s.command)`` | $st | $passed | $failed | $skipped | ``$dur`` | $art |")
        if ($s.errorMsg) { $errors.Add("- ``$($s.command)``: $($s.errorMsg)") }
        if ($s.PSObject.Properties['steps'] -and $s.steps) {
            foreach ($step in @($s.steps)) { $stepRows.Add($step) }
        }
        if ($s.PSObject.Properties['coverage'] -and $s.coverage) {
            foreach ($c in @($s.coverage)) {
                $pkg = [string]$c.package
                if ($pkg -in @('Pal.Core', 'Pal.Extract')) {
                    if (-not $coverageMap.Contains($pkg) -or ([double]$c.lineRate -gt [double]$coverageMap[$pkg].lineRate)) {
                        $coverageMap[$pkg] = $c
                    }
                }
            }
        }
    }

    if ($stepRows.Count -gt 0) {
        $lines.Add('')
        $lines.Add('#### 分步明细')
        $lines.Add('| 步骤 | 通过 | 失败 | 跳过 | 耗时 |')
        $lines.Add('| :--- | ---: | ---: | ---: | ---: |')
        foreach ($stRow in $stepRows) {
            $sDur = "$([Math]::Round([double]$stRow.durationSec, 1))s"
            $lines.Add("| ``$($stRow.name)`` | $([int]$stRow.passed) | $([int]$stRow.failed) | $([int]$stRow.skipped) | ``$sDur`` |")
        }
    }

    if ($coverageMap.Count -gt 0) {
        $parts = [Collections.Generic.List[string]]::new()
        foreach ($k in $coverageMap.Keys) {
            $c = $coverageMap[$k]
            $parts.Add("``$k`` 行覆盖 ``$($c.lineRate)%`` / 分支 ``$($c.branchRate)%``")
        }
        $lines.Add('')
        $lines.Add("- **覆盖率（仅展示，无门槛）**：$($parts -join '；')")
    }

    if ($errors.Count -gt 0) {
        $lines.Add('')
        $lines.Add('#### 失败诊断')
        foreach ($e in $errors) { $lines.Add($e) }
    }

    if ($TrxFailures -and $TrxFailures.Count -gt 0) {
        $lines.Add('')
        $lines.Add('#### 失败用例（来自 TRX）')
        foreach ($f in $TrxFailures) {
            $lines.Add("- ``$($f.testName)`` ($($f.outcome)): $($f.message)")
        }
    }

    if ($Format -eq 'pr-comment') {
        $lines.Add('')
        $lines.Add('#### T3 端到端 / 截图对比（预留）')
        $lines.Add('- 本次报告未包含 T3 GPU 截图产物（由 M7-5 补齐）。')
    }

    return ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function @(
    'Read-TrxSummary',
    'Read-TrxFailures',
    'Convert-TrxToJunit',
    'Read-CoberturaCoverage',
    'Write-RunSummaryJson',
    'Format-MarkdownReport'
)
