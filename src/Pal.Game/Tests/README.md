# Static exploration / Main integration tests

For new or migrated cases, follow [Game test layers and SceneRunner](../../../docs/testing/game-test-layers.md): Core for deterministic rules/time, SceneRunner by default for real scene behavior, and minimal real processes for argv/diagnostic/exit/READY/isolation contracts. This is a responsibility split, not a blanket migration of every process test.

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
# Source requirement: 245 discovered = 242 positive cases + 3 deliberate failure controls.
# For complete regression use Test-M0 below: each of three rounds is serial 66/79/97.
# A direct unbatched Game/solution run is not the shared gate and may hit the 120s session limit.

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

The adapter generates ignored `src/Pal.Game/gdunit4_testadapter_v5/` itself; it must not be committed. The solution includes Prepare tests and orders them before Game through a solution-only dependency: Prepare runs Game builds/imports and must finish before the Game adapter runs. Do not launch these projects concurrently against the same Godot project from separate shells. The shared gate retains solution restore/build but runs Core/CLI/Prepare explicitly in sequence before its first batched Game round; it never invokes an unbatched solution test.

Shared local/Windows CI acceptance (PowerShell 7):

```powershell
./scripts/Test-M0.ps1 -GodotPath 'C:/path/to/godot-mono.console.exe' `
  -EvidenceDirectory 'C:/external/new-empty-evidence-directory'
```

The entry first checks source partitions with the offline metadata regression and runs the eight real-process .NET boundary prechecks, then uses empty NuGet package/HTTP caches to restore/build the solution. Current source requirements are Core328, CLI330 + 2 named platform skips, Prepare52 and Game242; discovery245 includes three controls. The P2 follow-up adds eight strict-UTF8 CLI cases and two exception-path key-release cases; these source counts are not whole-suite pass results. It runs Core/CLI/Prepare serially, then executes all242 positives as66/79/97 in each of the solution/standalone/repeat rounds (nine serial Game sessions), verifies each control with numeric exit1 and exactly one designated failure, and runs import/Main startup. See the [fixed batch and identity rules](../../../docs/testing/game-test-layers.md#完整门的固定串行批次):15 complete TRX, independent source FQN sets, exact discovery/round coverage, result-definition-entry links, no partial-result stitching and unchanged120s session budget. Batching resets cross-batch hosts; this is not single-session full-suite or a demonstrated speed improvement. The unified M3 candidate preserves the unique main normal-deadline Fact and both profile-cleanup and NPC/display-failure contracts: five Main contracts, not four. Static counts are not pass results. After the initial88.664s entry failed at an incorrect generic xUnit display-name uniqueness check, the user authorized continued repair through M3 delivery. The corrected helper keeps case-ID links strict, permits legitimate coincident xUnit displays and still enforces unique source-derived Game FQNs. A new complete entry on0ee8204 plus the harness patch passed in819.764s: Core328, CLI330+2 original skips, Prepare52, all nine Game batches (three complete242-case rounds), discovery245, three designated controls and all six startup stages. All15 TRX/1441 execution IDs were independently audited; no old partial results were stitched in. The old failure remains failed. See the [current evidence checkpoint](../../../docs/milestones/m3/acceptance.md#2026-10-23-固定串行拆批修复后完整入口通过); CI/publication are separate evidence. The original full gate actually passed on integration `dab79ad85a4a352826a31eb0569a58e1f17eca88` (tree `b4fb69689d6820727b31ce6909117b1904a5b540`): Core327P, CLI322P+2 exact historical skips, Prepare52P, Game239P/Completed in solution/standalone/repeat, discovery242 unique matching an independently source-derived239 FQN set each round, three exact controls and six startup stages complete. Independent evidence review supports that automation result. **The user confirmed the Windows manual checklist (both languages, physical input, Alt-Tab release/refocus, integer scaling and normal close).** This is user feedback, not agent-independent measurement or new GPU evidence. Historical CI39884910037 stays cancelled; candidate CI39988957557 and diagnostic CI39990595247 failed in the synthetic boundary fixture before Core/Game. The diagnostic run exposed a750ms observer-ACK dependency. A no-ACK supervisor first reproduced the old root registration failure, then all eight cases passed after removing the child's unnecessary ACK wait. Retained duplicated handles and atomic publication still establish ownership even if the source exits before observation; all budgets, production code and exit/stream assertions remain unchanged, and temporary probes are removed. A fresh complete local entry on7dab88c plus this fixture fix passed in569.930s: all15 TRX/1441 execution IDs, three242-case Game rounds, discovery245, designated controls and six startup stages were audited. No partial runs were stitched together. Exact remote CI, main publication and Issue closure receipts are recorded separately in[#26](https://github.com/fanxiao92/godot-pal/issues/26)/[#20](https://github.com/fanxiao92/godot-pal/issues/20); local PASS does not sign those layers. The agent-added ancillary process Observer remains discontinued, not repaired; its PID reuse/handle race, missing command6 identity, unknown internal/build-server exits and verifier criterion-1=`not-satisfied` stay historical. Restricted empty snapshots only cover their observation times, not all descendants or historical privacy compliance. Private originals stay local without unknown-command-line inspection, upload, commit, alteration or deletion; no Observer rerun. See the [current M3 checkpoint](../../../docs/milestones/m3/acceptance.md#当前统一收尾候选) for manual-feedback provenance, private-evidence limits and the separately authorized delivery receipts. Post-run documentation changes are not new CPU/GPU runs. Prepare52 retains the M2 static preparation chain; it does not cover the new dialogue importer (its separate existing preparation evidence is listed in the [M3 evidence checkpoint](../../../docs/milestones/m3/acceptance.md)). The two existing Windows CLI case-ambiguity skips are explicitly identified; Game/Prepare/Core may not skip or execute zero tests. Raw logs, numeric results, discovery and process snapshots are retained. CI uploads only those evidence files, not package caches or any Assets.

`Test-GameStartup.ps1 -GodotPath <exe>` remains independently callable. It uses the sole `M1Bundle.Write` fixture in an external temporary input, explicitly prepares a unique Assets subroot and a matching typed artificial native profile (raw WORD0, start320,320, South0/layer0/phase0), then checks the actual Main's selected map URI plus Core ready and exit0. It only removes its own recorded, unchanged synthetic trees. Optional `-ResourceRoot res://Assets/<prepared-root>` uses caller-owned native resources without writing/deleting them. Add `-ProfileUri res://<selected-profile>.tres` to select a caller-owned native profile explicitly. With caller resources and no ProfileUri, Main retains its independent default `res://Assets/Profiles/Exploration.tres`; a changed resource root does not relocate it. ProfileUri alone is rejected before synthetic preparation. Synthetic output cleanup is snapshotted only after profile authoring. Neither script populates the default real Assets root; ordinary game startup with missing resources still fails. Timeout or error is a failure, not a fallback. Headless automation is not GPU pixel or human desktop acceptance.

The preparation-only `tests/Fixtures/VerifyNativeScene.gd` checks native PackedScene data and actual decoded PNG textures without entering the scene tree; all 48 literal map-array values remain independent fixture expectations. Runtime presentation instead loads a checked profile and calls `InitializeScene` before AddChild. The preparation process regression rejects engine ERROR/SCRIPT ERROR/Unhandled diagnostics even when the child exits0 and prints its marker.

Artificial GPU literal pixels and static-frame checks are opt-in through `PAL_STATIC_GPU_EVIDENCE` in the existing observer; they are not part of headless CI. Real scene2/profile and non-headless GPU evidence are local, ignored and never uploaded. Neither is physical OS presentation, operator interaction or remote CI acceptance. These fixtures are exploration test entries, not original spawn/save/story data; no movement/input/scripts are authorized by static rendering.


## M3 scene 4 static acceptance mode

The normal M2 route remains the default (`res://Assets/Scenes/Scene.tscn` and the default exploration profile). A separately prepared scene 4 package can be selected explicitly, without startup extraction or fallback:

```powershell
& $env:GODOT_BIN --path src/Pal.Game -- --m3-static-resources res://Assets/StaticScene4 `
  --m3-static-profile res://Assets/StaticScene4/Profiles/Scene4Static.tres
```

Both `--m3-static-resources` and `--m3-static-profile` are mandatory, must be supplied together, and cannot be mixed with M2 flags. Main validates scene 4/map 10, all 32 source objects, owner 60 at (1216,1344) with sprite 207/state 2/vanish 0, and the independent profile start (1232,1336), South0/layer0/phase0/role0. Profile loading retains the same source identity, texture, collision, neighbor and role0 walk-frame checks as M2. Startup emits `PAL_M3_STATIC_ACCEPTANCE`; it schedules no scene event. The selected origin is a test start, not the original story spawn. Owner 57 has a hotspot covering this location in ordinary scene behavior, so this mode is not scene 4 free exploration; it does not enable trigger search or dialogue. Camera placement continues to be derived by the existing scene plan from its (160,112) reference anchor.

Use `extract-scene --scene 4 --palette <explicit id> --lighting <day|night>` into a new, unique M1 output directory outside every repository, then `Prepare-GameScene.ps1` into the new ignored `src/Pal.Game/Assets/StaticScene4` directory. Do not reuse an existing output. For example, substitute a freshly generated GUID path for `<unique-temp>` and choose the palette explicitly (it is never inferred from scene ID):

```powershell
$godot = 'C:/Users/xiaofan/scoop/apps/godot-mono/4.7.2/godot-mono.console.exe'
$bundle = Join-Path ([IO.Path]::GetTempPath()) ('pal-scene4-' + [guid]::NewGuid().ToString('N'))
$paletteId = 0 # Explicit local selection; choose deliberately, never infer from scene ID.
dotnet run --project tools/Pal.Extract --no-build -- extract-scene `
  --input C:/Users/xiaofan/Repos/godot-pal/data/raw/sdlpal --output $bundle `
  --scene 4 --palette $paletteId --lighting day
./scripts/Prepare-GameScene.ps1 -InputPath $bundle `
  -OutputPath src/Pal.Game/Assets/StaticScene4 -GodotPath $godot
./scripts/Write-StaticScene4Profile.ps1 `
  -DataMkf C:/Users/xiaofan/Repos/godot-pal/data/raw/sdlpal/DATA.MKF -GodotPath $godot
```

The profile author step is the supported local authoring procedure. It reads role0 `rgwWalkFrames` as the raw WORD at byte offset 768 of DATA.MKF chunk 3 (the existing M1 source layout requires the 900-byte PLAYERROLES record), computes DATA.MKF's full SHA-256, and refuses to proceed unless it matches the prepared `data.mkf` source identity. It verifies prepared scene/map IDs, 32 objects, owner60 identity/state, writes only the documented profile URI without overwrite, then reloads the saved profile and checks bundle/source provenance and the independent start. The raw archive is opened read-only; do not manually substitute a walk-frame value. Both generated package and profile remain local/ignored. Review the extraction manifest, generated profile metadata, and their identities before launching the M3 command above. CI uses artificial resources and does not depend on this local data.

## Desktop focus and exploration behavior

SDLPal's application-background events set `g_bRenderPaused`, which gates video presentation; its
ordinary game loop and keyboard polling continue through `PAL_ProcessEvent` / `PAL_UpdateKeyboardState`.
The inspected SDLPal source has no desktop window-focus business pause. The Godot Main therefore has no
focus signal subscriptions, and `Pal.Core.ExplorationInput` has no focus state or reset path. Main keeps
its ordinary input, public Core update, and 100ms schedule; the change does not synthesize or suppress key
release events.

`WindowFocusSignalsDoNotFreezeExplorationOrClearHeldInput` injects public Window focus signals around
normal logical key input to the shipped Main. Each injection temporarily subscribes a public observation
callback, requires `Error.Ok` and exactly one callback, and unsubscribes in `finally`; Godot 4.7.2 can return
`Unavailable` for a valid signal with no subscribers, so an unchecked emit does not prove delivery.
The two key aliases must reach literal walking states at actual Main updates both after `FocusExited`
and after `FocusEntered`, then stand after explicit key release. This checks that focus signals neither
freeze updates nor clear logical held input; input remains governed by ordinary delivery. It does **not**
simulate desktop focus loss, establish OS/Godot key-release behavior, or
prove what keys are delivered on return. No Windows/other-platform claim or manual Alt-Tab acceptance
follows from this headless signal injection. Core timing boundaries remain covered through explicit
`ExplorationSession.Update(ticks)` tests; Godot observer waits are bounded liveness guards, not timing
measurements.


## M3 modern trigger/display fixture (#24)

`TriggerDialogueFixtureTests` uses the existing display scene, actual modern JSON files and native
manual resources in `Tests/Fixtures/TriggerDialogue`. It drives `PalTriggerDialogue.Initialize/Start`,
not a hand-authored UI command sequence; `request.Display` is the actual rendered/updated Core session.
The current suite has24 cases, including four later reference input/timing regressions. Its original20 cases cover: two language/repeat loops, font preflight, leading CLEAR with real background
and palette consumers, a two-request input/context boundary, ten resource/entry negatives, two completed
restore cases (CLEAR/name), synchronous multiple-restore draining before body text, and two initialization
diagnostic cases. The latter require exact resource-path-prefixed messages for an invalid Prompt-01 plane
and language format version, with the original InvalidDataException and its reason retained as the inner exception. The current
ten `DialogueDisplayFixtureTests` include the two later exception-path key-release cases; the original three preceded the reference regressions.

Run with the existing exact engine/packages/runsettings, serially after the usual PID/Name preflight:

```powershell
dotnet build src/Pal.Game --no-restore
& $env:GODOT_BIN --headless --path src/Pal.Game --editor --import
dotnet test src/Pal.Game --no-build --no-restore `
  --filter 'FullyQualifiedName~TriggerDialogueFixtureTests|FullyQualifiedName~DialogueDisplayFixtureTests' `
  --logger 'trx;LogFileName=trigger-dialogue.trx' --results-directory $logs
# Current source slice: 34 cases; validate actual exit, Summary/RunInfo and exact results.
# The original #24 validation had23 completed passes on its historical source.
```

Snapshots used for runtime assertions are saved from `PalDialogueDisplay.UpdateCompleted`, emitted at
the end of the actual display `_Process` after palette/background restoration and before return.
The coordinator subscribes first and handles receipts before the fixture saves its state. This avoids
assuming a `SceneTree.ProcessFrame` coroutine resumed after the display callback, or measuring a
scheduler delay as a business deadline. The new request is observed at that exact completion boundary:
its initial typing snapshot must be empty even when two advance keys were delivered in the same batch.
A controlled erroneous second `_Process(0)` after receipt is caught as WaitingForAdvance instead of
Typing. Core retains deterministic millisecond cutoff coverage; fixture frame bounds are liveness
protection only. Logical focus signals are delivered with an explicit subscriber and must not stop
normal dialogue updates; this is not an OS focus/physical-keyboard test.

The restoration fixture binds `BackgroundRestoreRequested` to restore an independently loaded native
background into the actual indexed surface and `ActivePaletteChanged` to its texture palette. It checks
real target pixels, not just ClearDisplay flags. These are headless resource/state/texture checks, not
GPU screen pixels. Main/search/map10 are connected in the explicit #25 mode documented below; physical keyboard and
shared-gate acceptance remain separate #26 responsibilities. The original display fixture is not map10 acceptance. See [the runtime contract](../../../docs/design/trigger-execution.md).

## M3 independent NPC interaction host (#25)

Normal Main explicitly accepts `--m3-dialogue-profile res://.../Profile.tres`, optionally
`--m3-dialogue-language en` (default `zh-Hans`). The composite native profile references the existing
static profile as the sole origin/walk-frame source, the complete modern events, and seven native
dialogue resources with file identity locks. No automatic preparation, M2/static replacement, or
ordinary scene4 story scheduling is performed. Full authoring/startup and lifecycle details:
[Godot host contract](../../../docs/design/npc-interaction-host.md).

`NpcInteractionHostTests` has32 cases at the approved native resource/normal Main process seams:
explicit arguments, exact native search metadata, indexed tile/object/leader textures, resource/owner/
identity/unsupported-entry rejection, two-language search-to-return/repeat and synchronous no-wait
return. Its child sessions reuse `RealDialogueFixture`, load the shipped Main, deliver real logical
input, and observe actual Main/Display completion boundaries. The adapter receipts first, Main must
already be Returning in the same display completion callback, with caller60/ReturnEntry0188. A
controlled delayed-Main-receipt counterexample is caught there. Core deterministic49/50ms tests
remain authoritative for exact deadlines; engine scheduling/observer durations are not that proof.
Short artificial English can legally finish on the first update; it does not prove visible typing or
acceleration. Batches finish only the current native body part without consuming a later wait.

Real map GPU uses the same `Test-RealDialogueGpu.ps1` with optional `-InteractionProfileUri` and a new
external evidence directory. Each language completes two full actual-search sessions and ten64000-pixel
checks at paired completed-update/FramePostDraw boundaries. It independently composes portrait/name/
body/shadows/prompt from native inputs and literal layout. Map prestate is only a restoration oracle,
not an independent whole-map renderer. The real scene's273 indexed frames have no opaqueF9–FE pixels;
they correctly stay unchanged while full scene palettes are still delivered. Artificial indexed
texture checks cover actual F9/FA changes and alpha separately. Neither GPU nor headless is physical
keyboard, OS Alt-Tab, human desktop/close feedback, remote CI or final shared-gate acceptance.

## #26 reference display timing/input regression checkpoint

Typing consumes fresh non-echo Search/Menu presses, never the held key that initiated interaction. The short press latches until the current glyph post-delay completes; skip then remains active over body lines until a wait resets it. Waiting accepts every mapped reference logical key (including arrows/keypad, PageUp/PageDown/Home/End and r/a/d/e/w/q/f/s); right Ctrl, unmapped keys and echo do not advance. Non-confirm direction presses remain visible to Main for held movement after return50ms, with release processed normally.

The first glyph is visible on Update0, followed by its delay; the last glyph also owns its post-draw delay. Waiting latches a short press but checks it only on the accumulated100ms boundary, after palette rotation. Deterministic Core tests cover exact deadlines and late updates; engine tests capture UpdateCompleted snapshots and do not replace those deadlines with wall-clock observations. One controlled consume-all-mapped-input mutant is rejected by the actual Main held-direction/return/release regression. Existing immediate-completion fixtures now wait for the real guarded completion callback, without enlarging timeouts or accepting arbitrary phases.

Current unified-source requirements are Core328 (the M3 327 plus main's normal-deadline Fact), CLI330+2 existing skips, Game242 positives and discovery245 including the three unchanged controls. Five shared-initialization contracts preserve both branches' unique failure/cleanup coverage. #30 replaces the eight `ExplorationProcessTests.LogicalBindingsMoveHoldReleaseAndRestartActualMain` cases with the same method in `ExplorationSceneTests`: four directions × Arrow/WASD, still eight cases, not eight additional cases. Native fixtures, real logical key input and 20000ms case timeouts remain; precise timing assertions belong to the existing explicit-time Core tests, while scene state is captured at actual Main.UpdateCompleted boundaries (details below). The observer's `arrow-*` and `key-*` routes remain compatible for explicitly retained uses; other process tests are unchanged. The new complete batched entry passed three242-case rounds after correcting the CLI metadata false positive; that local result does not establish a CI fix, single-session full-suite performance or historical root cause. The integration gate above remains historical evidence for its original 239/242 matrix, not the revised source. Historical matched GPU verification on wt26 exact `5bf481f`/the same source tree completed zh-Hans and en map10/profile runs: each10 checks of64000 pixels with0 differences, two complete Main calls and consumed-resource identity matched; the separate legacy no-profile zh-Hans run has six0-difference checks, not Main/profile coverage. Original1.1/255 per-channel tolerance is unchanged, not byte/float identity. GPU fresh pre/post tracked and DLL/PDB manifests were constant; the gate used a different worktree/DLL hash and has only pre-build/post-gate manifests, no fresh post-build/pre-test attestation. Old67f results retain their historical identity. Those automated results do not sign physical keys/OS release/desktop/close/current CI or repair the historical Observer P1. The separate user manual confirmation and retained reproduction checklist are in the [M3 checkpoint](../../../docs/milestones/m3/acceptance.md#windows-人工清单用户反馈已完成); current CI and publication authority remain pending, and the discontinued specification-extra Observer is not an M3 functional acceptance condition.


## Shared Main initialization (#29)

`Main.Configure(GameSceneConfiguration.FromArguments(...))` must be called exactly once
before the node enters the tree. `Main._Ready` owns the single resource-loading and
assembly path for normal exploration, static acceptance and independent NPC dialogue.
`GameStartup` (the script on shipped `Main.tscn`, still a `Main`) selects process argv
in its tree-out constructor; only that outer policy sets the window minimum and
turns the shared failure notification into the original diagnostic and exit1.
An embedded host uses `new Main()`, configures it, then
`ISceneRunner.Load(main, autoFree: true)`; it must inspect `Failure`/subscribe `Failed`
and fail the test on unexpected errors, not just wait for READY.

Missing configuration and initialization/runtime errors set the first `Failure` and
notify `Failed` once. The entire scene subtree stops processing/input; renderer and
Main completion subscriptions disconnect. A failure does not free the node or quit
an embedded host. The host owns all instantiated nodes, including partial startup;
free the whole scene on success or failure. A removed/failed scene is not reusable.
Repeated or late Configure calls throw instead of reassembling.

`MainSceneTests` has five contracts: configuration/lifecycle, resource failure,
walking-update failure followed by fresh/cached-resource scenes, profile-validation
failure freeing the already-loaded map, and NPC/display failure followed by an
independently initialized interactive scene. At the #29 handoff, these were not
the #30 eight-route migration: all eight original process cases and Observer paths
remained unchanged. #30 now replaces only those eight automatic process entries;
the Observer and every other process test remain. The baseline is one no-build eight-case run; the single
selected compatibility group includes the original four new contracts. These are historical integration results, not
results for the current five-contract union or its242/245 source inventory.

`DialogueDisplayFixtureTests` keeps each owned Space/Escape press, its real-update wait and
checks inside a `try/finally` release scope. Two controlled check exceptions verify actual
`Input.IsKeyPressed` state after cleanup, with a separate outer cleanup protecting the
negative-control run itself. Its ten cases passed once in the P2 follow-up; this is not
a full Game gate. Held-writer prechecks also require a cancellation request, completed
cleanup, the retained stream's `Canceled` status and the other stream's `RanToCompletion`.

Embedded tests explicitly release their own parsed key events, unsubscribe handlers,
use autoFree and verify node invalidation, restore window/engine/focus state and
delete only GUID-owned artificial fixtures. They never clear ResourceLoader caches
or global InputMap. Actual state samples come from Main.UpdateCompleted, not from
assuming a runner frame-wait return means Main ran. No virtual clock/TimeFactor or
manual production callback invocation is used.

## Eight exploration SceneRunner routes (#30)

`ExplorationSceneTests.LogicalBindingsMoveHoldReleaseAndRestartActualMain` keeps the
method filter stable while moving its class identity. Each GUID-owned native fixture
configures a new `Main` before `ISceneRunner.Load(main, autoFree: true)`. The eight
logical cases no longer launch business child Godot processes; the fixed adapter
still launches its own editor and GdUnit runtime host. Run serially, using the same
fixed toolchain, existing prepared package/import caches and PID/Name preflight:

```powershell
dotnet build src/Pal.Game --no-restore
dotnet test src/Pal.Game --no-build --no-restore `
  --settings tests/Pal.Game.Tests/game.runsettings `
  --filter 'FullyQualifiedName~LogicalBindingsMoveHoldReleaseAndRestartActualMain' `
  --results-directory $logs --logger 'trx;LogFileName=exploration-scene.trx'
# Expected: exactly eight completed passes, no skip/RunInfo and exit0.
```

The first actual Main completion after input is checked unconditionally, not selected
by waiting for walking. A narrow synchronous completion observer checks the literal
position/direction/phase/frame, first camera and fixed (160,112) anchor, holds through
second/third steps, releases at the third completion, checks stop/idle/raw WORD0,
then restarts at the idle completion and releases at the restart completion. Three
real InputEventKey echoes and both exact logical InputMap aliases remain covered.
Input control occurs at that boundary so late test-coroutine resumption cannot skip
a state or cause extra walking. Assertion exceptions are recorded inside the observer
and rethrown by the test, not allowed to become Main's business-failure channel.
A frame wait only yields; it is not evidence that Main ran. The inherited 650ms
per-boundary guard is liveness protection, not a timing assertion.

The old <70ms/short-frame observations map to unchanged Core tests:
`ExplorationInputTests.Input_uses_the_normal_100ms_schedule_across_wrap_and_late_updates`,
`Echoes_and_duplicate_presses_do_not_reorder_add_held_aliases_or_accelerate`, and
`Initially_idle_and_all_released_keep_the_sessions_normal_stop_phase`, together with
`ExplorationSessionTests.Update_is_due_at_equality_wraps_uint_ticks_and_reschedules_from_now_without_catchup`,
`Every_idle_step_toggles_stop_phase_without_moving_or_animating_standing`, and
`Restart_foot_depends_on_how_many_idle_steps_elapsed`. These independently cover
99/100ms, wrap, late updates without catch-up, echo, stop and restart phase;
SceneRunner does not supply a synthetic business clock or prove exact deadlines.

The helper always releases its own key, unsubscribes, disposes its runner, asserts
old Main invalidation and explicitly frees any surviving owned Main in finally,
including on assertion failure. Window size/minimum, valid focus owner and engine
time/physics settings are restored. A subsequent new Main loads the same URI and
checks the initial position and first actual input/update; resource file hashes and
authoring raw WORD/frame count remain unchanged. No global InputMap/cache clearing.

Responsibilities remain distinct: `ExplorationProcessTests` retains collision,
priority/aliases/focus/static/dynamic/runtime-exit routes; `MainProcessTests` retains
argv, READY, resource/assembly and exit contracts; `NpcInteractionHostTests` retains
its existing parser/resource and real search/display/process matrix. Only these
eight exploration cases moved. The unified candidate adds main's unique profile
cleanup contract; the P2 follow-up additionally adds two failure-path key-release cases,
so current static totals are242 positives /245 including controls;
this is not evidence that the revised full gate or failed CI passed.
The #29 baseline and #30 final comparison each ran once with the same no-build filter,
fixed toolchain and disclosed reused-cache conditions: command wall 17.9371793s to
11.6042306s, summed case durations 9.570s to 5.209s, extra business Godot starts 8 to 0;
the fixed editor and runtime host still start separately. Selected compatibility completed
12 passes. The time and disposal mutants failed on their designated assertions; the original
Press-disconnection run was contaminated by a residual time-mutant Core DLL and is not
clean isolation evidence. A separately user-authorized, one-run clean South Arrow supplement
returned normal exit1/Failed1 with the exact first-actual-update assertion, using the correct
Core DLL. Independent Standards/Spec follow-up reviews accepted this evidence gap as closed;
the historical execution deviation and originals are retained, not reclassified as valid.
All mutations are restored; source and Game/Core DLL/PDB bytes match the prior final run,
with normally refreshed mtimes. No second final measurement or revised full gate was run.
These timings belong to the original uncommitted M3 integration patch. A separate
M2-only port was reviewed and delivered on local main at ab1d64c/52afa6c, with
#29/#30/#28 closed; it was not pushed and does not sign this unified M3 candidate.
Current M3 delivery/Issue status must still be checked separately. A single pair is not
a performance distribution, GPU/desktop validation, a failed-CI repair or authorization to
migrate another matrix.
