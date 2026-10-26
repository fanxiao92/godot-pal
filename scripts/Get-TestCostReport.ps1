# Read-only existing-evidence CLI. Never runs tests, resolves HEAD or enumerates processes.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputPath,
    [string]$BaselinePath
)
$ErrorActionPreference = 'Stop'
# Set the console encoding before either the JSON or failure-diagnostic path can write.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    Import-Module (Join-Path $PSScriptRoot 'TestCostReport.psm1') -Force
    $report = Get-TestCostReport -InputPath $InputPath -BaselinePath $BaselinePath
    [Console]::WriteLine((ConvertTo-Json -InputObject $report -Depth 60))
    exit 0
} catch {
    [Console]::Error.WriteLine("Cost report failed (tool exit 2; original task exit/logs unchanged): $($_.Exception.Message)")
    exit 2
}
