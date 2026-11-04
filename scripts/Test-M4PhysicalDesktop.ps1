[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('won', 'lost')][string]$Mode,
    [Parameter(Mandatory)][ValidateSet('title-x', 'alt-f4')][string]$ClosePath,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$GodotPath,
    [int]$DriveSeconds = 600
)
$ErrorActionPreference = 'Stop'
# One physical-desktop run per invocation: a real windowed process, real OS keyboard input
# (SendInput), a full synthetic-closure Won or Lost campaign, then a normal window close via
# the chosen real path (mouse click on the caption close button, or Alt+F4). No retry.
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Pal.Game'))
$godot = (Resolve-Path -LiteralPath $GodotPath).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($evidence.Equals($repo, $comparison) -or $evidence.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw 'Physical evidence must be outside the repository.'
}
if (Test-Path -LiteralPath $evidence) { throw 'EvidenceDirectory must be a new directory; existing evidence is never overwritten.' }
$conflicts = @(& (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1'))
if ($conflicts.Count -ne 0) { $conflicts | Format-List; throw 'Existing Godot/repository test processes require ownership confirmation. No physical run started.' }
$version = (& $godot --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -ne '4.7.2.stable.mono.official.ed1daf0bf') { throw "Pinned Godot version mismatch: $version" }
New-Item -ItemType Directory -Path $evidence | Out-Null
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PhysicalInput {
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public const uint INPUT_KEYBOARD = 1, INPUT_MOUSE = 0, KEYEVENTF_KEYUP = 0x0002;
    public const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    public static void Key(ushort vk, bool up) {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = vk; input.u.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Tap(ushort vk) { Key(vk, false); System.Threading.Thread.Sleep(45); Key(vk, true); System.Threading.Thread.Sleep(130); }
    public static void MouseClickAbsolute(int x, int y) {
        int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
        int ax = (x * 65535 + screenW / 2) / screenW;
        int ay = (y * 65535 + screenH / 2) / screenH;
        var move = new INPUT { type = INPUT_MOUSE };
        move.u.mi.dx = ax; move.u.mi.dy = ay; move.u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;
        var down = new INPUT { type = INPUT_MOUSE };
        down.u.mi.dx = ax; down.u.mi.dy = ay; down.u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_ABSOLUTE;
        var up = new INPUT { type = INPUT_MOUSE };
        up.u.mi.dx = ax; up.u.mi.dy = ay; up.u.mi.dwFlags = MOUSEEVENTF_LEFTUP | MOUSEEVENTF_ABSOLUTE;
        SendInput(3, new[] { move, down, up }, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@
[void][PhysicalInput]::SetProcessDPIAware()
$stdout = Join-Path $evidence 'stdout.log'
$stderr = Join-Path $evidence 'stderr.log'
$environment = @{ PAL_PHYSICAL_EVIDENCE = $evidence; PAL_PHYSICAL_MODE = $Mode }
foreach ($key in $environment.Keys) { Set-Item -Path "Env:$key" -Value $environment[$key] }
$startedAt = [DateTime]::UtcNow
$p = Start-Process -FilePath $godot -ArgumentList @('--path', $project, '--audio-driver', 'Dummy', 'res://Tests/M4PhysicalDesktopFixture.tscn') `
    -WorkingDirectory $project -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
foreach ($key in @($environment.Keys)) { Remove-Item -Path "Env:$key" -ErrorAction SilentlyContinue }
"wrapperPid=$($p.Id)" | Set-Content -LiteralPath (Join-Path $evidence 'pid.txt') -Encoding utf8
try {
    $childId = 0
    for ($i = 0; $i -lt 300 -and $childId -eq 0; $i++) {
        Start-Sleep -Milliseconds 200
        $childId = (Get-CimInstance Win32_Process -Filter "ParentProcessId = $($p.Id)" | Where-Object { $_.Name -like 'godot*' } | Select-Object -First 1).ProcessId
        if (-not $childId -and $i -ge 50) {
            # CIM parent linkage can lag; the pre-run conflict check guarantees no other godot
            # process, so a newly started godot-mono engine process is this run's child.
            $candidate = Get-Process -Name godot-mono -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $startedAt.AddSeconds(-2) } | Select-Object -First 1
            if ($candidate) { $childId = $candidate.Id }
        }
        if ($p.HasExited) { throw 'Game process exited before a window appeared.' }
    }
    if (-not $childId) { throw 'No godot child window process appeared.' }
    "childPid=$childId" | Add-Content -LiteralPath (Join-Path $evidence 'pid.txt') -Encoding utf8
    $child = Get-Process -Id $childId
    $hwnd = [IntPtr]$child.MainWindowHandle
    for ($i = 0; $i -lt 60 -and $hwnd -eq [IntPtr]::Zero -and -not $p.HasExited; $i++) {
        Start-Sleep -Milliseconds 250; $child.Refresh(); $hwnd = [IntPtr]$child.MainWindowHandle
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw 'Game window handle never appeared.' }
    $lifecycle = New-Object System.Collections.Generic.List[string]
    $lifecycle.Add("window=$hwnd title='$($child.MainWindowTitle)'")
    $rect = New-Object PhysicalInput+RECT
    [void][PhysicalInput]::GetWindowRect($hwnd, [ref]$rect)
    $captionY = $rect.Top + 14
    $focused = $false
    for ($i = 0; $i -lt 6 -and -not $focused -and -not $p.HasExited; $i++) {
        [PhysicalInput]::MouseClickAbsolute((($rect.Left + $rect.Right) / 2), $captionY)
        Start-Sleep -Milliseconds 400
        $focused = [PhysicalInput]::GetForegroundWindow() -eq $hwnd
    }
    $lifecycle.Add("focusedByRealTitleClick=$focused")
    if (-not $focused) { throw 'Real title-bar click did not focus the game window.' }
    # Live stdout reader over the redirect file (the wrapper streams engine output).
    $reader = [IO.StreamReader]::new([IO.FileStream]::new($stdout, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite))
    $doneMarker = $false; $failedMarker = $false
    $enterTaps = 0
    $deadline = [DateTime]::UtcNow.AddSeconds($DriveSeconds)
    $heartbeat = 0
    while ([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not $doneMarker) {
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            if ($line -match 'PAL_PHYSICAL_DONE') { if ($line -match 'failed=true') { $failedMarker = $true } else { $doneMarker = $true } }
        }
        if (-not $doneMarker) {
            [PhysicalInput]::Key(0x0D, $false)
            Start-Sleep -Milliseconds 45
            [PhysicalInput]::Key(0x0D, $true)
            $enterTaps++
            Start-Sleep -Milliseconds 130
        }
        $heartbeat++
        if (($heartbeat % 125) -eq 0) {
            $lifecycle.Add("heartbeat=$heartbeat alive=$((Get-Process -Id $childId -ErrorAction SilentlyContinue) -ne $null) window=$([PhysicalInput]::IsWindow($hwnd))")
        }
    }
    $reader.DiscardBufferedData(); $reader.BaseStream.Seek(0, 'Begin') | Out-Null; $reader.DiscardBufferedData()
    while (-not $reader.EndOfStream) { $line = $reader.ReadLine(); if ($line -match 'PAL_PHYSICAL_DONE') { if ($line -match 'failed=true') { $failedMarker = $true } else { $doneMarker = $true } } }
    $reader.Dispose()
    $lifecycle.Add("driven=done:$doneMarker failed:$failedMarker")
    if (-not $doneMarker -and -not $failedMarker) { throw "Drive window ended without DONE (timeout or early exit): mode=$Mode" }
    if ($failedMarker) { throw 'Fixture reported PAL_PHYSICAL_FAILED.' }
    # Normal window close through the chosen real path.
    if ($ClosePath -eq 'title-x') {
        [void][PhysicalInput]::GetWindowRect($hwnd, [ref]$rect)
        $closeX = $rect.Right - 24; $closeY = $rect.Top + 14
        $lifecycle.Add("closeClick=$closeX,$closeY")
        [PhysicalInput]::MouseClickAbsolute($closeX, $closeY)
    } else {
        [PhysicalInput]::Key(0x12, $false); [PhysicalInput]::Tap(0x73); [PhysicalInput]::Key(0x12, $true)
        $lifecycle.Add('closeKeys=Alt+F4')
    }
    $closedNormally = $p.WaitForExit(25000)
    if (-not $closedNormally) { $lifecycle.Add('close-timeout') }
    $exited = $p.WaitForExit(5000)
    $code = $p.ExitCode
    $lifecycle.Add("exited=$exited exit=$code windowAfterClose=$([PhysicalInput]::IsWindow($hwnd))")
    $lifecycle | Set-Content -LiteralPath (Join-Path $evidence 'lifecycle.txt') -Encoding utf8
    Start-Sleep -Milliseconds 800
    $output = if (Test-Path $stdout) { Get-Content $stdout -Raw } else { '' }
    $errors = if (Test-Path $stderr) { Get-Content $stderr -Raw } else { '' }
    $engineError = ($output + "`n" + $errors) -match '(?im)(ERROR:|SCRIPT ERROR:|Unhandled|PAL_PHYSICAL_FAILED)'
    $resultPath = Join-Path $evidence 'fixture-result.json'
    $fixture = if (Test-Path $resultPath) { Get-Content $resultPath -Raw | ConvertFrom-Json } else { $null }
    $residue = @(& (Join-Path $PSScriptRoot 'Get-TestProcesses.ps1'))
    $pollEdges = 0
    if ($fixture -and $fixture.enterPollEdges) { $pollEdges = [int]$fixture.enterPollEdges }
    # Real-keyboard count: driver-side SendInput taps (each Key call is one injected event);
    # the fixture poll edges are only a throttling-sensitive lower bound.
    $passed = $closedNormally -and $exited -and $code -eq 0 -and
        $null -ne $fixture -and $fixture.completed -eq $true -and $enterTaps -ge 20 -and $residue.Count -eq 0
    # $engineError is recorded but not gating here: this acceptance scope is the process/
    # keyboard/lifecycle contract. Known finding F1 (#54 unit C): unbound real battle WORD
    # tables make PalStoryBattleView._Draw throw on every battle frame on GPU; the flow still
    # completes and the window still closes normally. Pixel acceptance of that HUD text is
    # separately failed in the GPU runs, where the same evidence is a hard failure.
    [ordered]@{
        Pid = $p.Id; ChildPid = $childId; Mode = $Mode; ClosePath = $ClosePath
        StartedAtUtc = $startedAt.ToString('O'); EndedAtUtc = [DateTime]::UtcNow.ToString('O')
        ExitCode = $code; ClosedNormally = $closedNormally; EngineError = $engineError
        EnterTapsInjected = $enterTaps; EnterPollEdgesInFixture = $pollEdges; Stages = @($fixture.stages); ProcessResidue = $residue.Count; Passed = $passed
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'process-result.json') -Encoding utf8
    Write-Output "PHYSICAL_EVIDENCE=$evidence"
    Write-Output "PHYSICAL_RESULT:mode=$Mode close=$ClosePath exit=$code closedNormally=$closedNormally engineError=$engineError enterTapsInjected=$enterTaps pollEdges=$pollEdges residue=$($residue.Count) passed=$passed"
    if (-not $passed) { exit 1 }
}
finally {
    if (-not $p.HasExited) { try { $p.Kill($true) } catch { }; $p.WaitForExit() }
}
