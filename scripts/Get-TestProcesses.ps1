# Global conflicts disclose only Godot PID/Name; detailed identities are repo executors only.
$ErrorActionPreference = 'Stop'
Get-CimInstance -Query "SELECT ProcessId, Name FROM Win32_Process WHERE Name LIKE 'godot%.exe'" |
    Select-Object ProcessId, Name
Get-CimInstance -Query "SELECT ProcessId, ParentProcessId, Name, CommandLine, CreationDate FROM Win32_Process WHERE (Name = 'dotnet.exe' OR Name = 'testhost.exe' OR Name LIKE 'vstest%.exe') AND (CommandLine LIKE '%godot-pal%' OR CommandLine LIKE '%Pal.Game%' OR CommandLine LIKE '%Pal.Prepare%')" |
    Select-Object ProcessId, ParentProcessId, Name, CommandLine, CreationDate
