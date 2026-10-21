# Static exploration / Main integration tests

Approved seam: real native `PackedScene` / `Resource` / `Texture2D`, real scene-tree entry and frames, checked Core draw plans rendered by Godot, viewport transforms, and child-process exit/diagnostics from the shipped Main. No mocks or internal-only test interfaces. This does not replace graphical/desktop pixel acceptance.

Tests live in the existing Godot assembly, matching the #12 proven arrangement; no second Godot project or Core test migration. Fixed packages: gdUnit4.api 5.1.0-rc5, gdUnit4.test.adapter 3.1.1, Microsoft.NET.Test.Sdk 18.0.1. Godot.NET.Sdk 4.7.2 / net10.0 and repository global.json remain authoritative.

Run from repository root in PowerShell 7, with the installed Godot 4.7.2 mono console executable selected explicitly. The suite runs whether or not a default `Assets/Scenes/Scene.tscn` or `Assets/Profiles/Exploration.tres` exists: the missing-resource case selects its own absent GUID root through the real Main arguments, without touching default assets. Test resources are independent native literals, saved under ignored `Assets/RuntimeTests/<guid>` and removed after each test. They are not a duplicate M1 JSON fixture.

```powershell
$env:GODOT_BIN = 'C:/path/to/godot-mono.console.exe'
$logs = 'C:/external/test-results'
dotnet build src/Pal.Game
& $env:GODOT_BIN --headless --path src/Pal.Game --editor --import
$env:PAL_TEST_GODOT = $env:GODOT_BIN
dotnet test src/Pal.Game --no-build --list-tests
# Expected: 141 discovered = 138 positive cases + 3 deliberate failure controls.
dotnet test src/Pal.Game --no-build --results-directory $logs `
  --logger 'trx;LogFileName=game.trx'
# Expected: 138 passed, 0 failed/skipped; exit0. Repeat should have identical counts.

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

Ordinary `dotnet test src/Pal.Game` and `dotnet test GodotPal.sln` exclude the three controls by the Game project's conditional `VSTestTestCaseFilter`. An explicit CLI `--filter` overrides that default, including for each control above. `RunSettingsFilePath` selects the checked-in headless settings unless `--settings` overrides it. No machine-specific Godot executable is checked in: set both environment variables as above or use the shared script's explicit `-GodotPath`. Scene tests use `GodotExceptionMonitor`; the process tests explicitly assert child exit codes, absence of ready on rejection, and diagnostic content. Child processes have a 15-second watchdog with process-tree kill and asynchronously drained stdout/stderr. Session timeout is 120 seconds; individual scene/process tests are bounded. Check TRX counts, completed session outcome and diagnostics, not only the CLI exit or ready marker. Session abort/error/timeout fails even if partial counters show passed tests.

`ObserveMain.gd` independently loads the **shipped** Main scene, enters the tree, waits three frames and observes native scene state and display transforms. It is a test observer, not a runtime fallback/alternate scene. The normal Main process is separately exercised without the observer. Headless viewport-transform checks prove the 320x200 logical field and integer display rectangle, not rendered pixel colors or a desktop manual pass.

The adapter generates ignored `src/Pal.Game/gdunit4_testadapter_v5/` itself; it must not be committed. The solution includes Prepare tests and orders them before Game through a solution-only dependency: Prepare runs Game builds/imports and must finish before the Game adapter runs. Do not launch these projects concurrently against the same Godot project from separate shells.

Shared local/Windows CI acceptance (PowerShell 7):

```powershell
./scripts/Test-M0.ps1 -GodotPath 'C:/path/to/godot-mono.console.exe' `
  -EvidenceDirectory 'C:/external/new-empty-evidence-directory'
```

This starts with empty NuGet package/HTTP caches, restores/builds the solution, checks Core 54, CLI 300 + 2 named platform skips, Prepare 52 and Game 138; lists 141 unique Godot cases and repeats the 138 positive cases, verifies each of the three controls with numeric exit1 and exactly one failed test, and runs the existing import/Main startup regression. The two existing Windows CLI case-ambiguity skips are explicitly identified; Game/Prepare/Core may not skip or execute zero tests. Raw logs, numeric results, discovery and process snapshots are retained. CI uploads only those evidence files, not package caches or any Assets.

`Test-GameStartup.ps1 -GodotPath <exe>` remains independently callable. It uses the sole `M1Bundle.Write` fixture in an external temporary input, explicitly prepares a unique Assets subroot and a matching typed artificial native profile (raw WORD0, start320,320, South0/layer0/phase0), then checks the actual Main's selected map URI plus Core ready and exit0. It only removes its own recorded, unchanged synthetic trees. Optional `-ResourceRoot res://Assets/<prepared-root>` uses caller-owned native resources without writing/deleting them. Add `-ProfileUri res://<selected-profile>.tres` to select a caller-owned native profile explicitly. With caller resources and no ProfileUri, Main retains its independent default `res://Assets/Profiles/Exploration.tres`; a changed resource root does not relocate it. ProfileUri alone is rejected before synthetic preparation. Synthetic output cleanup is snapshotted only after profile authoring. Neither script populates the default real Assets root; ordinary game startup with missing resources still fails. Timeout or error is a failure, not a fallback. Headless automation is not GPU pixel or human desktop acceptance.

The preparation-only `tests/Fixtures/VerifyNativeScene.gd` checks native PackedScene data and actual decoded PNG textures without entering the scene tree; all 48 literal map-array values remain independent fixture expectations. Runtime presentation instead loads a checked profile and calls `InitializeScene` before AddChild. The preparation process regression rejects engine ERROR/SCRIPT ERROR/Unhandled diagnostics even when the child exits0 and prints its marker.

Artificial GPU literal pixels and static-frame checks are opt-in through `PAL_STATIC_GPU_EVIDENCE` in the existing observer; they are not part of headless CI. Real scene2/profile and non-headless GPU evidence are local, ignored and never uploaded. Neither is physical OS presentation, operator interaction or remote CI acceptance. These fixtures are exploration test entries, not original spawn/save/story data; no movement/input/scripts are authorized by static rendering.


## Focus recovery: processing boundary versus business time

`ExplorationObserver.FocusRecovery` uses its own test-only `_Process` callback in the same default
main-thread process group, with priority `Main.ProcessPriority + 1`. Godot 4.7.2's `Node.ProcessPriority`
and `_Process` documentation specify lower priorities run first; `SceneTree.ProcessFrame` is emitted
**before** node processing and is not evidence that Main has completed an update.

In the sampler's post-Main callback, `Input.ParseInputEvent` and `Input.FlushBufferedEvents` deliver the
fresh logical key through normal `_Input`/InputMap. The first following post-Main callback unconditionally
captures and checks that completed update: it must no longer be the frozen state and must match the
literal first position/direction/phase/frame/camera. Subsequent changed states are captured and checked
as the literal held step and released standing frame, with normal duplicate Window focus/echo events.
Snapshots are retained in the callback; a late awaiting coroutine never substitutes a later live state.
The local await-frame counter is not Main's process-frame identity. Buffered boundary diagnostics use
`Engine.GetProcessFrames`; timestamps are diagnostic, not performance or cooldown assertions.

The existing Core test `Fresh_press_after_focus_resets_old_deadline_then_uses_100ms_wrap_and_no_catchup`
uses explicit public `Update(ticks)` inputs for reset-before-old-deadline, 99/100ms, wrap and no-catchup.
These exact business-time boundaries cannot be inferred from integration observer latency: a delayed
first engine update can make both a correct implementation and a wrong cooldown appear to move.
The integration sampler instead verifies actual wiring and the first completed-update literal trace.
Its unchanged 650ms wait is a liveness guard: missing samples are incomplete observation, while a
captured wrong first update is a functional failure. No production clocks/state or callbacks are set
or invoked by the observer. This scoped sampler does not change the separate opt-in scene2/GPU modes
and is not OS keyboard/focus or graphical acceptance. No cases or gate counts are added.
