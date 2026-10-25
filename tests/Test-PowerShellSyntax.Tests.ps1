# Public CLI regression; intentionally invalid scripts are UTF-8 fixture data, never project source.
[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$previousEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$utf8 = [Text.UTF8Encoding]::new($false)
$entry = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../scripts/Test-PowerShellSyntax.ps1'))
$hostPath = (Get-Process -Id $PID).Path
$evidence = if ($EvidenceDirectory) { [IO.Path]::GetFullPath($EvidenceDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('syntax-evidence-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $evidence) { throw 'Use a new syntax regression evidence directory.' }
$null = [IO.Directory]::CreateDirectory($evidence)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('syntax 中文 空格 ' + [guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixture)
$records = [Collections.Generic.List[object]]::new()
$completed = $false
function Put([string]$Name, [string]$Text) {
    $file = Join-Path $fixture $Name
    $null = [IO.Directory]::CreateDirectory((Split-Path $file -Parent))
    [IO.File]::WriteAllText($file, $Text, $utf8)
    return $file
}
function Quote([string]$Text) { "'" + $Text.Replace("'", "''") + "'" }
function Check([string]$Name, [string[]]$Paths, [int]$Expected, [string]$Diagnostic, [switch]$Default) {
    $number = $records.Count + 1
    $code = "& $(Quote $entry) -RepositoryRoot $(Quote $fixture)"
    if (-not $Default) { $code += ' -Path @(' + ((@($Paths | ForEach-Object { Quote $_ })) -join ',') + ')' }
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw 'Regression invocation must parse before execution.' }
    $out = Join-Path $evidence "$number.stdout.log"; $err = Join-Path $evidence "$number.stderr.log"
    Push-Location $foreign
    try {
        & $hostPath -NoProfile -EncodedCommand ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))) 1> $out 2> $err
        $actual = $LASTEXITCODE
    } finally { Pop-Location }
    [IO.File]::WriteAllText((Join-Path $evidence "$number.exit"), [string]$actual, $utf8)
    $text = [IO.File]::ReadAllText($out, $utf8) + "`n" + [IO.File]::ReadAllText($err, $utf8)
    $records.Add(@{ name = $Name; exitCode = $actual; command = $code; expectedExit = $Expected })
    [IO.File]::WriteAllText((Join-Path $evidence 'commands.json'), (ConvertTo-Json -InputObject $records.ToArray() -Depth 8), $utf8)
    if ($actual -ne $Expected -or $text -notmatch $Diagnostic) { throw "${Name}: expected $Expected / $Diagnostic; got $actual / $text" }
    Write-Host "PASS $Name"
}
try {
    $foreign = Join-Path $fixture 'foreign cwd'; $null = [IO.Directory]::CreateDirectory($foreign)
    $null = [IO.Directory]::CreateDirectory((Join-Path $fixture 'tests'))
    $marker = Join-Path $fixture 'must not exist.txt'
    $good = Put 'scripts/中文 ok.ps1' ("[IO.File]::WriteAllText($(Quote $marker),'executed'); throw 'must never execute'")
    $module = Put 'scripts/valid.psm1' 'function Get-Example { "中文" }'
    $data = Put 'tests/valid.psd1' '@{ Name = "中文" }'
    Check 'valid side-effect script is parsed only' @($good) 0 'PASS syntax: 1 file'
    if (Test-Path -LiteralPath $marker) { throw 'Checker executed its target.' }
    Check 'Chinese module and data files' @($module, $data) 0 'PASS syntax: 2 file'
    Check 'explicit empty selection must not become default' @() 1 '\[syntax:empty\]'
    $empty = Join-Path $fixture 'empty'; $null = [IO.Directory]::CreateDirectory($empty)
    Check 'empty directory' @($empty) 1 '\[syntax:empty\]'
    Check 'missing file' @('missing.ps1') 1 '\[syntax:missing\]'
    $other = Put 'not-source.txt' 'hello'
    Check 'unsupported explicit extension' @($other) 1 '\[syntax:range\]'
    $bracket = Put 'bad/bracket.ps1' 'if ('
    $quote = Put 'bad/quote.ps1' 'Write-Output "'
    Check 'bracket error has file line column and diagnostic' @($bracket) 1 'bracket\.ps1:1:[0-9]+: .+'
    Check 'string error' @($quote) 1 'quote\.ps1:1:[0-9]+: .+'
    Check 'mixed error is not hidden by later valid file' @($bracket, $good) 1 '\[syntax:error\]'
    Check 'relative explicit source path ignores foreign cwd' @('scripts/中文 ok.ps1') 0 'PASS syntax: 1 file'
    $null = Put 'scripts/bin/generated.ps1' 'if ('
    $null = Put 'tests/obj/generated.ps1' 'Write-Output "'
    Check 'default source excludes generated files' @() 0 'PASS syntax: 3 file' -Default
    Check 'explicit generated subtree rejected' @('scripts/bin') 1 '\[syntax:range\]'
    $external = Join-Path $evidence 'generated temporary.ps1'
    [IO.File]::WriteAllText($external, 'Write-Output "只解析"', $utf8)
    Check 'one explicit generated external file can be prechecked' @($external) 0 'PASS syntax: 1 file'
    Check 'external directory is not recursively scanned' @($evidence) 1 '\[syntax:range\]'
    $completed = $true
    Write-Host "PASS syntax regression: $($records.Count) public invocations; no target execution."
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
    [Console]::OutputEncoding = $previousEncoding
    if ($completed -and -not $EvidenceDirectory) { Remove-Item -LiteralPath $evidence -Recurse -Force }
    elseif (-not $completed) { Write-Host "Syntax regression failed; raw evidence retained: $evidence" }
}
