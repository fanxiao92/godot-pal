Set-StrictMode -Version Latest

function Invoke-CapabilityLedgerCheck {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [switch]$Update,
        [string]$LedgerPath = ''
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $extractProj = Join-Path $root 'tools/Pal.Extract/Pal.Extract.csproj'
    if (-not (Test-Path -LiteralPath $extractProj -PathType Leaf)) {
        throw "Missing Pal.Extract project: $extractProj"
    }

    $ledgerRel = 'docs/generated/capabilities-ledger.md'
    $targetLedgerPath = if ([string]::IsNullOrWhiteSpace($LedgerPath)) {
        Join-Path $root $ledgerRel
    } else {
        [IO.Path]::GetFullPath($LedgerPath)
    }

    $tempDir = Join-Path ([IO.Path]::GetTempPath()) ("pal-ledger-" + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($tempDir)
    $tempOut = Join-Path $tempDir 'capabilities-ledger.md'

    try {
        Write-Host "==> Generating capability ledger from code truth (Pal.Extract)"
        & dotnet run --project $extractProj --configuration Debug -- export-capability-ledger --output $tempOut --repo-root $root
        if ($LASTEXITCODE -ne 0) {
            throw "Pal.Extract export-capability-ledger failed (exit $LASTEXITCODE)."
        }
        if (-not (Test-Path -LiteralPath $tempOut -PathType Leaf)) {
            throw "Capability ledger output was not created: $tempOut"
        }

        $generatedBytes = [IO.File]::ReadAllBytes($tempOut)
        $generatedText = [Text.Encoding]::UTF8.GetString($generatedBytes)

        if ($Update) {
            $parent = Split-Path $targetLedgerPath -Parent
            if ($parent) { [void][IO.Directory]::CreateDirectory($parent) }
            [IO.File]::WriteAllBytes($targetLedgerPath, $generatedBytes)
            Write-Host "UPDATED capability ledger: $ledgerRel ($($generatedText.Split("`n").Count) lines)"
            return
        }

        if (-not (Test-Path -LiteralPath $targetLedgerPath -PathType Leaf)) {
            throw "Capability ledger missing: $ledgerRel. Regenerate with: pwsh -NoProfile -NonInteractive -File ./pal.ps1 test check-ledger --update"
        }

        $actualBytes = [IO.File]::ReadAllBytes($targetLedgerPath)
        $actualText = [Text.Encoding]::UTF8.GetString($actualBytes)

        # Normalize CRLF only for diagnostic diff reporting; require exact byte/LF parity.
        $same = ($actualBytes.Length -eq $generatedBytes.Length)
        if ($same) {
            for ($i = 0; $i -lt $actualBytes.Length; $i++) {
                if ($actualBytes[$i] -ne $generatedBytes[$i]) { $same = $false; break }
            }
        }

        if (-not $same) {
            $genLines = $generatedText.Split("`n")
            $actLines = $actualText.Replace("`r`n", "`n").Split("`n")
            $firstDiff = -1
            $maxLines = [Math]::Max($genLines.Length, $actLines.Length)
            for ($idx = 0; $idx -lt $maxLines; $idx++) {
                $a = if ($idx -lt $actLines.Length) { $actLines[$idx] } else { '<EOF>' }
                $g = if ($idx -lt $genLines.Length) { $genLines[$idx] } else { '<EOF>' }
                if ($a -cne $g) { $firstDiff = $idx + 1; break }
            }
            $crlfNote = if ($actualText.Contains("`r`n")) { ' (file contains CRLF; ledger requires LF)' } else { '' }
            throw "Capability ledger drift detected in $ledgerRel at line $firstDiff$crlfNote. Regenerate with: pwsh -NoProfile -NonInteractive -File ./pal.ps1 test check-ledger --update"
        }

        Write-Host "PASS capability-ledger: $ledgerRel is up-to-date ($($generatedText.Split("`n").Count) lines, LF)."
    } finally {
        if (Test-Path -LiteralPath $tempDir) {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Export-ModuleMember -Function Invoke-CapabilityLedgerCheck
