# Static exploration / Main integration tests

For new or migrated cases, follow [Game test layers and SceneRunner](../../../docs/testing/game-test-layers.md): Core for deterministic rules/time, real scenes for input/update wiring, and minimal real processes for argv/diagnostic/exit/READY/isolation. This is not a blanket migration of every process test.

Approved seam: real native `PackedScene` / `Resource` / `Texture2D`, real scene-tree entry and frames, checked Core draw plans rendered by Godot, viewport transforms, and child-process exit/diagnostics from the shipped Main. No mocks or internal-only test interfaces. This does not replace graphical/desktop pixel acceptance.

Tests live in the existing Godot assembly, matching the #12 proven arrangement; no second Godot project or Core test migration. Fixed packages: gdUnit4.api 5.1.0-rc6.pal.a5f20fb8.1, gdUnit4.test.adapter 3.2.0-pal.a5f20fb8.1, Microsoft.NET.Test.Sdk 18.0.1. See [pinned source-package bootstrap and provenance](../../../docs/testing/gdunit-source-packages.md); direct dotnet commands require bootstrap first. Godot.NET.Sdk 4.7.2 / net10.0 and repository global.json remain authoritative.

Run from repository root in PowerShell 7, with the installed Godot 4.7.2 mono console executable selected explicitly. The suite runs whether or not a default `Assets/Scenes/Scene.tscn` or `Assets/Profiles/Exploration.tres` exists: the missing-resource case selects its own absent GUID root through the real Main arguments, without touching default assets. Test resources are independent native literals, saved under ignored `Assets/RuntimeTests/<guid>` and removed after each test. They are not a duplicate M1 JSON fixture.

```powershell
$env:GODOT_BIN = 'C:/path/to/godot-mono.console.exe'
$logs = 'C:/external/test-results'
./scripts/Get-GdUnitPackages.ps1
dotnet build src/Pal.Game
& $env:GODOT_BIN --headless --path src/Pal.Game --editor --import
$env:PAL_TEST_GODOT = $env:GODOT_BIN
dotnet test src/Pal.Game --no-build --list-tests
# Expected: 173 discovered = 170 positive cases + 3 deliberate failure controls.
dotnet test src/Pal.Game --no-build --results-directory $logs `
  --logger 'trx;LogFileName=game.trx'
# Expected: 170 passed, 0 failed/skipped; exit0. Repeat should have identical counts.

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

This starts with empty NuGet package/HTTP caches, restores/builds the solution, checks Core 130, CLI 300 + 2 named platform skips, Prepare 52 and Game 170; lists 173 unique Godot cases and repeats the 170 positive cases, verifies each of the three controls with numeric exit1 and exactly one failed test, and runs the existing import/Main startup regression. These are current source requirements: main170 minus four duplicate Arrow entries plus four M2 initialization contracts remains170/173; the eight moved cases are replacements, not additions. Core130 adds one explicit normal release/restart deadline case to the original129. Historical M2 full-gate results retain their original129/170/173 identity; no revised full gate or discovery is implied. The two existing Windows CLI case-ambiguity skips are explicitly identified; Game/Prepare/Core may not skip or execute zero tests. Raw logs, numeric results, discovery and process snapshots are retained. CI uploads only those evidence files, not package caches or any Assets.

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


## Shared Main initialization

`new Main()` → `Configure(GameSceneConfiguration.FromArguments(...))` →
`ISceneRunner.Load(main, autoFree: true)` is the embedded public seam. Configuration
is immutable and accepted exactly once before tree entry. Real `_Ready` owns the
same M2 native loading, profile validation, exploration and renderer assembly as
normal startup. Shipped `Main.tscn` uses `GameStartup : Main`: its tree-out constructor
selects actual argv, and its outer policy alone sets window minimum and turns shared
failure into the original diagnostic/exit1. Selected scene/profile, MAP/READY and
normal resource defaults are unchanged.

`Failure`/`Failed` retain/report the first initialization or update failure once;
the scene disables subtree/input/process and disconnects its Window focus signals,
without quitting an embedded host. Host owns all nodes on success or failure and
must free them. Removed/failed nodes cannot be reconfigured or reused. Unexpected
scene failure must fail the test rather than be mistaken for successful READY.

`MainSceneTests` has four M2 contracts: missing/repeated/late configuration; selected
resource failure; walking-frame failure followed by healthy/new and cached-resource
scenes; and invalid profile after native map loading followed by healthy interaction.
No NPC, event or dialogue runtime is added. `ExplorationProcessTests` retains focus,
priority, aliases, repeat, collision, dynamic renderer and runtime-exit duties;
`MainProcessTests` retains argv/resource/normal-startup exit and diagnostic matrices.
Only the eight logical-binding cases move to the scene layer, and the four entirely
duplicate Arrow-only entries are removed. Still-used Observer/manual/GPU modes stay.

## Eight exploration SceneRunner routes

`ExplorationSceneTests.LogicalBindingsMoveHoldReleaseAndRestartActualMain` replaces
four directions × Arrow/WASD process cases, with no extra business Godot per case.
The fixed adapter still starts editor/runtime hosts. Run serially with both engine
environment variables, bootstrap/build/import as required, and a PID/Name-only
Godot preflight (pause on existing processes):

```powershell
dotnet test src/Pal.Game --no-build --no-restore `
  --settings tests/Pal.Game.Tests/game.runsettings `
  --filter 'FullyQualifiedName~ExplorationSceneTests.LogicalBindingsMoveHoldReleaseAndRestartActualMain' `
  --results-directory $logs --logger 'trx;LogFileName=exploration-scene.trx'
# Exactly eight completed passes, exit0, no skip or abnormal RunInfo.
```

Each unique native fixture configures an independent Main before Load. M2 focus
freeze/clear/fresh-press remains production behavior: healthy embedded hosts emit
public Window FocusExited then FocusEntered and check Error.Ok before input. This
establishes a fresh-press gate independently of headless OS focus. It does not claim
physical window focus or desktop input acceptance.

InputEventKey reaches real `_Input`/InputMap. A synchronous `Main.UpdateCompleted`
observer checks the FIRST actual update unconditionally, then literal held second
and third steps, releases at third, checks stop/idle/raw WORD0, restarts at idle,
and releases at restart. Position, direction, phase, frame, first camera, (160,112)
anchor, logical InputMap aliases and three echo events remain checked. Samples and
control occur in that completion, not when an awaiting coroutine happens to resume.
Assertion exceptions stay in the test channel, not Main's failure channel. The650ms
per-boundary guard is liveness protection, never a business deadline assertion.

Old70ms/short-frame wall-clock predicates map to deterministic Core tests:
`Each_key_moves_one_literal_world_step_only_on_update`,
`Echoes_and_duplicate_presses_do_not_reorder_add_held_aliases_or_accelerate`,
`Fresh_press_after_focus_resets_old_deadline_then_uses_100ms_wrap_and_no_catchup`,
`Press_release_idle_and_restart_respect_exact_normal_deadlines`,
`Update_is_due_at_equality_wraps_uint_ticks_and_reschedules_from_now_without_catchup`,
`Every_idle_step_toggles_stop_phase_without_moving_or_animating_standing`, and
`Restart_foot_depends_on_how_many_idle_steps_elapsed`. Explicit public time covers
99/100ms, wrap, late no-catchup, release, restart and focus reset; scene integration
cannot replace that deterministic proof with external scheduling observations.

Finally, tests explicitly release their own input and subscriptions, Dispose runner
with autoFree true, assert old Main invalid, fallback-free owned nodes even on an
assertion, restore window/GUI focus and Engine settings, and delete only their GUID
fixtures. Same-URI/new-node interaction plus file hashes check shared-resource
isolation; no global ResourceLoader/InputMap clearing. Fixed runner defaults to
NOT freeing nodes and changes TimeScale/physics settings; this is why explicit
ownership and restoration matter. No TimeFactor/virtual delta/manual `_Process`
is used. Negative controls must fail for their specified functional assertion,
not compilation/resource absence/crash/timeout. Verify actual runtime-directory
Core DLL identity after each rebuild/restore, not only source bytes, and never
rewind source mtime. A local port is not a performance comparison: no new main
baseline or performance benefit, whole-suite/CI/GPU/manual acceptance is claimed.
