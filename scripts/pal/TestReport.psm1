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
    foreach ($s in $Summaries) {
        $st = if ($s.status -eq 'passed') { 'PASS' } else { [string]$s.status.ToUpperInvariant() }
        $passed = if ($s.counts) { [int]$s.counts.passed } else { 0 }
        $failed = if ($s.counts) { [int]$s.counts.failed } else { 0 }
        $skipped = if ($s.counts) { [int]$s.counts.skipped } else { 0 }
        $dur = "$([Math]::Round([double]$s.durationSec, 1))s"
        $art = if ($s.artifactsDir) { "``$($s.artifactsDir)``" } else { '-' }
        $lines.Add("| ``$($s.command)`` | $st | $passed | $failed | $skipped | ``$dur`` | $art |")
        if ($s.errorMsg) { $errors.Add("- ``$($s.command)``: $($s.errorMsg)") }
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

    $e2eBlocks = [Collections.Generic.List[object]]::new()
    foreach ($s in $Summaries) {
        if ($s.PSObject.Properties['e2e'] -and $null -ne $s.e2e) {
            $e2eBlocks.Add(@{ Summary = $s; E2E = $s.e2e })
        }
    }

    if ($e2eBlocks.Count -gt 0) {
        $lines.Add('')
        $lines.Add('#### T3 端到端回归（本地 GPU + 金图比对）')
        foreach ($blk in $e2eBlocks) {
            $e2e = $blk.E2E
            $sum = $blk.Summary
            if ($e2e.PSObject.Properties['cacheKey'] -and $e2e.cacheKey) {
                $lines.Add("- **真实资源缓存键**：``$($e2e.cacheKey)``（不提交、不上传真实资产与版权截图）")
            }
            if ($e2e.PSObject.Properties['updateGolden'] -and $e2e.updateGolden) {
                $lines.Add('- **模式**：显式金图基线刷新（`--update-golden`）')
            }
            if ($sum.artifactsDir) {
                $cs = if ($e2e.PSObject.Properties['contactSheet'] -and $e2e.contactSheet) { "，拼图 ``$($e2e.contactSheet)``" } else { '' }
                $lines.Add("- **本地产物目录**：``$($sum.artifactsDir)``$cs")
            }
            $lines.Add('')
            $lines.Add('| 场景 | 探针断言 | 金图比对 | 耗时 | 关键帧数 |')
            $lines.Add('| :--- | :---: | :---: | ---: | ---: |')
            foreach ($sc in @($e2e.scenarios)) {
                $probeMark = if ($sc.probePassed) { 'PASS' } else { 'FAIL' }
                $frames = @($sc.frames)
                $maxDiff = 0.0
                $tol = 1.0
                $anyFrameFail = $false
                foreach ($fr in $frames) {
                    if ([double]$fr.diffPercent -gt $maxDiff) { $maxDiff = [double]$fr.diffPercent }
                    if ($fr.PSObject.Properties['tolerancePercent']) { $tol = [double]$fr.tolerancePercent }
                    if ($fr.status -eq 'failed') { $anyFrameFail = $true }
                }
                $goldMark = if ($anyFrameFail) { "FAIL (max ``$maxDiff%`` / ``≤ $tol%``)" } else { "PASS (max ``$maxDiff%`` / ``≤ $tol%``)" }
                $lines.Add("| ``$($sc.scenario)`` | $probeMark | $goldMark | ``$($sc.durationSec)s`` | $($frames.Count) |")
            }

            $lines.Add('')
            $lines.Add('| 场景 / 帧 | 状态 | Diff % / 容差 | 实际图 SHA-256 | 金图 SHA-256 | 差异图 |')
            $lines.Add('| :--- | :---: | :---: | :--- | :--- | :--- |')
            foreach ($sc in @($e2e.scenarios)) {
                foreach ($fr in @($sc.frames)) {
                    $aSha = if ($fr.actualSha256) { "``$(([string]$fr.actualSha256).Substring(0, [Math]::Min(16, ([string]$fr.actualSha256).Length)))``" } else { '-' }
                    $gSha = if ($fr.goldenSha256) { "``$(([string]$fr.goldenSha256).Substring(0, [Math]::Min(16, ([string]$fr.goldenSha256).Length)))``" } else { '<missing>' }
                    $diffCol = if ($fr.diffPath) { "``$($fr.diffPath)``" } else { '-' }
                    $lines.Add("| ``$($sc.scenario)/$($fr.frameName)`` | $($fr.status.ToUpperInvariant()) | ``$($fr.diffPercent)% / ≤ $($fr.tolerancePercent)%`` | $aSha | $gSha | $diffCol |")
                }
            }
        }
        if ($Format -eq 'pr-comment') {
            $lines.Add('')
            $lines.Add('- **人工 5 分钟审阅清单**：[ ] 打开本地 `contact-sheet.png` 核对画面无花屏/错位/字体缺字；[ ] 抽查变更相关场景原图与探针 JSON。')
        }
    } elseif ($Format -eq 'pr-comment') {
        $lines.Add('')
        $lines.Add('#### T3 端到端 / 截图对比')
        $lines.Add('- 本次报告未包含 T3 GPU 端到端产物（如需 T3 证据，请运行 `./pal.ps1 test e2e --gpu`）。')
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
