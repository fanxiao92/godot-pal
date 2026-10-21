# Pure PowerShell regression at the public collector seam; never queries real processes.
$ErrorActionPreference = 'Stop'
$collector = Join-Path $PSScriptRoot '../../scripts/Get-TestProcesses.ps1'
$originalCim = Get-Command Get-CimInstance
try {
    & {
        $godotQuery = "SELECT ProcessId, Name FROM Win32_Process WHERE Name LIKE 'godot%.exe'"
        $repoQuery = "SELECT ProcessId, ParentProcessId, Name, CommandLine, CreationDate FROM Win32_Process WHERE (Name = 'dotnet.exe' OR Name = 'testhost.exe' OR Name LIKE 'vstest%.exe') AND (CommandLine LIKE '%godot-pal%' OR CommandLine LIKE '%Pal.Game%' OR CommandLine LIKE '%Pal.Prepare%')"
        $queries = [Collections.Generic.List[string]]::new()
        $rejected = [Collections.Generic.List[string]]::new()
        function Get-CimInstance {
            [CmdletBinding()]
            param([string]$ClassName, [string]$Query)
            $queries.Add($Query)
            if (-not $Query) {
                $rejected.Add('unfiltered')
                throw 'FAKE CIM: refused unfiltered Win32_Process enumeration.'
            }
            if ($Query -eq $godotQuery) {
                # Deliberately over-return sensitive fields: collector must discard them too.
                [pscustomobject]@{ ProcessId = 101; Name = 'godot-fixture.exe'; CommandLine = 'SENSITIVE_FIXTURE'; ParentProcessId = 999; CreationDate = 'SENSITIVE_FIXTURE'; ExecutablePath = 'SENSITIVE_FIXTURE' }
            } elseif ($Query -eq $repoQuery) {
                # Server permits only these repository executors, including relative arguments.
                [pscustomobject]@{ ProcessId = 201; ParentProcessId = 200; Name = 'dotnet.exe'; CommandLine = 'dotnet test C:/fixtures/godot-pal/GodotPal.sln'; CreationDate = 'fixture-created' }
                [pscustomobject]@{ ProcessId = 202; ParentProcessId = 201; Name = 'testhost.exe'; CommandLine = 'testhost Pal.Game.dll'; CreationDate = 'fixture-created' }
                [pscustomobject]@{ ProcessId = 203; ParentProcessId = 201; Name = 'vstest.console.exe'; CommandLine = 'vstest Pal.Prepare.Tests.dll'; CreationDate = 'fixture-created' }
            } else {
                # Foreign .NET details are unavailable, even if the caller intends to filter later.
                $rejected.Add('foreign-or-wide')
                throw 'FAKE CIM: refused query exposing foreign process details.'
            }
        }
        foreach ($attempt in @('unfiltered', 'foreign-or-wide')) {
            $message = $null
            try {
                if ($attempt -eq 'unfiltered') { Get-CimInstance -ClassName Win32_Process }
                else { Get-CimInstance -Query "SELECT * FROM Win32_Process WHERE Name = 'dotnet.exe'" }
            } catch { $message = $_.Exception.Message }
            if ($message -notlike 'FAKE CIM: refused*') { throw "Fake boundary did not reject $attempt." }
        }
        Write-Host 'FAKE_ONLY: strict CIM stub rejects unfiltered/foreign queries; no real CIM invocation.'
        $processes = @(& $collector)
        if ($queries.Count -ne 4 -or $queries[2] -ne $godotQuery -or $queries[3] -ne $repoQuery -or $rejected.Count -ne 2) {
            throw 'Collector must use exactly the two server-filtered, limited-projection queries.'
        }
        if (($processes.ProcessId -join ',') -ne '101,201,202,203') { throw 'Godot or explicit repository .NET residual detection was lost.' }
        $godot = $processes[0]
        if (($godot.PSObject.Properties.Name -join ',') -ne 'ProcessId,Name' -or $godot.Name -ne 'godot-fixture.exe') {
            throw 'Global Godot snapshot exposed more than PID and Name.'
        }
        foreach ($process in $processes[1..3]) {
            if (($process.PSObject.Properties.Name -join ',') -ne 'ProcessId,ParentProcessId,Name,CommandLine,CreationDate' -or
                $process.ParentProcessId -le 0 -or -not $process.CommandLine -or $process.CreationDate -ne 'fixture-created') {
                throw 'Repository executor residual identity was lost or widened.'
            }
        }
        Write-Host 'PASS: both server filters, Godot PID/Name projection, foreign .NET refusal and repository .NET residual identities.'
    }
} finally {
    $restoredCim = Get-Command Get-CimInstance
    if ($restoredCim.CommandType -ne $originalCim.CommandType -or $restoredCim.Source -ne $originalCim.Source -or
        $restoredCim.Definition -ne $originalCim.Definition) { throw 'Fake CIM escaped its independent child scope.' }
    Write-Host 'PASS: CIM mock did not leak into the caller/shared gate.'
}
Write-Host 'PASS: ProcessSnapshot fake regression; real CIM invocations = 0.'
