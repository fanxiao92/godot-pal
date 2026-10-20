# Native map / Main integration tests

Approved seam: real native `PackedScene` / `Resource` / `Texture2D`, real scene-tree entry and frames, native `Sprite2D` draw nodes, viewport transforms, and child-process exit/diagnostics from the shipped Main. No mocks or internal-only test interfaces. This does not replace graphical/desktop pixel acceptance.

Tests live in the existing Godot assembly, matching the #12 proven arrangement; no second Godot project or Core test migration. Fixed packages: gdUnit4.api 5.1.0-rc5, gdUnit4.test.adapter 3.1.1, Microsoft.NET.Test.Sdk 18.0.1. Godot.NET.Sdk 4.7.2 / net10.0 and repository global.json remain authoritative.

Run from repository root in PowerShell 7, with the installed Godot 4.7.2 mono console executable selected explicitly. The suite runs whether or not a default `Assets/Scenes/Scene.tscn` exists: the missing-resource case selects its own absent GUID root through the real Main arguments, without touching default assets. Test resources are independent native literals, saved under ignored `Assets/RuntimeTests/<guid>` and removed after each test. They are not a duplicate M1 JSON fixture.

```powershell
$env:GODOT_BIN = 'C:/path/to/godot-mono.console.exe'
$logs = 'C:/external/test-results'
dotnet build src/Pal.Game
& $env:GODOT_BIN --headless --path src/Pal.Game --editor --import
dotnet test src/Pal.Game --no-build --settings tests/Pal.Game.Tests/game.runsettings --list-tests
# Expected: 29 discovered = 26 positive cases + 3 deliberate failure controls.
dotnet test src/Pal.Game --no-build --settings tests/Pal.Game.Tests/game.runsettings `
  --filter 'FullyQualifiedName!~FailureControlTests' --results-directory $logs `
  --logger 'trx;LogFileName=game.trx'
# Expected: 26 passed, 0 failed/skipped; exit0. Repeat should have identical counts.

# Each command below must fail: 1 executed, 1 failed, 0 skipped; exit1.
foreach ($control in @('IntentionalAssertionMismatch', 'IntentionalTimeout', 'NativeReadyException')) {
  dotnet test src/Pal.Game --no-build --settings tests/Pal.Game.Tests/game.runsettings `
    --filter $control --results-directory $logs --logger "trx;LogFileName=$control.trx" `
    *> (Join-Path $logs "$control.log")
  $exitCode = $LASTEXITCODE
  "EXIT_CODE=$exitCode" | Tee-Object -FilePath (Join-Path $logs "$control.log") -Append
  if ($exitCode -ne 1) { throw "$control expected exit1, got $exitCode; inspect its log and TRX counts" }
}
```

Never treat unfiltered `dotnet test src/Pal.Game` as the positive suite: controls intentionally fail. Scene tests use `GodotExceptionMonitor`; the process tests explicitly assert child exit codes, absence of ready on rejection, and diagnostic content. Child processes have a 15-second watchdog with process-tree kill and asynchronously drained stdout/stderr. Session timeout is 120 seconds; individual scene/process tests are bounded. Check TRX counts and diagnostics, not only the CLI exit or ready marker.

`ObserveMain.gd` independently loads the **shipped** Main scene, enters the tree, waits three frames and observes actual public Godot draw nodes and display transforms. It is a test observer, not a runtime fallback/alternate scene. The normal Main process is separately exercised without the observer. Headless viewport-transform checks prove the 320x200 logical field and integer display rectangle, not rendered pixel colors or a desktop manual pass.

The adapter generates `src/Pal.Game/gdunit4_testadapter_v5/` itself. It is not source and must not be committed. This seam deliberately leaves the shared .gitignore, solution, CI and startup script integration to the serial integration owner. Existing startup smoke without explicit resources now fails by design; it must select an explicitly prepared artificial native root, not regain a text fallback.
