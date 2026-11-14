$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
# Privacy boundary: enumerate names/PIDs only; never read command lines or parent chains.
$rows = @(Get-Process -Name '*godot*' -ErrorAction SilentlyContinue | Select-Object Id, ProcessName)
ConvertTo-Json -InputObject $rows -Compress
