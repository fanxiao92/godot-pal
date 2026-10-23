# Static exploration / Main integration tests

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
# Expected: 242 discovered = 239 positive cases + 3 deliberate failure controls.
dotnet test src/Pal.Game --no-build --results-directory $logs `
  --logger 'trx;LogFileName=game.trx'
# Expected: 239 passed, 0 failed/skipped; exit0. Repeat should have identical counts.

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

This starts with empty NuGet package/HTTP caches, restores/builds the solution, checks Core 327, CLI 322 + 2 named platform skips, Prepare 52 and Game 239; lists 242 unique Godot cases and repeats the 239 positive cases, verifies each of the three controls with numeric exit1 and exactly one failed test, and runs the existing import/Main startup regression. These remain the current requirements. The original full gate actually passed on integration `dab79ad85a4a352826a31eb0569a58e1f17eca88` (tree `b4fb69689d6820727b31ce6909117b1904a5b540`): Core327P, CLI322P+2 exact historical skips, Prepare52P, Game239P/Completed in solution/standalone/repeat, discovery242 unique matching an independently source-derived239 FQN set each round, three exact controls and six startup stages complete. Independent evidence review supports that automation result. **The user has now confirmed the Windows manual checklist in this conversation (both languages, physical input, Alt-Tab release/refocus, integer scaling and normal close); current integration artificial-resource Windows CI remains pending.** This is user feedback, not agent-independent measurement or new GPU evidence. The agent-added ancillary process Observer is outside the approved M3 functional gate and has been discontinued, not repaired: PID reuse/handle race, missing primary command6 PID/UTC and unknown internal/build-server exits remain historical limitations; the verifier's criterion-1 stays `not-satisfied`. Restricted process snapshots were empty at their observation times; they do not prove all descendants' ownership/exit or observer privacy compliance. Private originals remain local with no unknown command-line inspection, upload, commit, alteration or deletion; no automatic rerun. This scope correction does not waive privacy requirements. See the M3 checkpoint for the manual-feedback source, private-evidence restrictions, pending CI and publication authority. This documentation commit was not itself tested. Prepare52 retains the M2 static preparation chain; it does not cover the new dialogue importer (its separate existing preparation evidence is listed in the [M3 evidence checkpoint](../../../docs/milestones/m3/acceptance.md)). The two existing Windows CLI case-ambiguity skips are explicitly identified; Game/Prepare/Core may not skip or execute zero tests. Raw logs, numeric results, discovery and process snapshots are retained. CI uploads only those evidence files, not package caches or any Assets.

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
The suite has 20 cases: two language/repeat loops, font preflight, leading CLEAR with real background
and palette consumers, a two-request input/context boundary, ten resource/entry negatives, two completed
restore cases (CLEAR/name), synchronous multiple-restore draining before body text, and two initialization
diagnostic cases. The latter require exact resource-path-prefixed messages for an invalid Prompt-01 plane
and language format version, with the original InvalidDataException and its reason retained as the inner exception. The existing
three `DialogueDisplayFixtureTests` remain unchanged and run in the same targeted validation.

Run with the existing exact engine/packages/runsettings, serially after the usual PID/Name preflight:

```powershell
dotnet build src/Pal.Game --no-restore
& $env:GODOT_BIN --headless --path src/Pal.Game --editor --import
dotnet test src/Pal.Game --no-build --no-restore `
  --filter 'FullyQualifiedName~TriggerDialogueFixtureTests|FullyQualifiedName~DialogueDisplayFixtureTests' `
  --logger 'trx;LogFileName=trigger-dialogue.trx' --results-directory $logs
# This slice: 23 completed passes; validate actual exit, Summary/RunInfo and all result counts.
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

`NpcInteractionHostTests` has 31 cases at the approved native resource/normal Main process seams:
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

Current source requirements are Core327 (=316+11 timing cases), CLI322+2 existing skips (=317+5 boundary cases), Game239 positives (=229+4 stride+6 input/host cases), discovery242 including the three unchanged controls. The exact integration gate above now supplies the complete rounds; component evidence is not substituted for them. Newly matched GPU verification on wt26 exact `5bf481f`/the same source tree completed zh-Hans and en map10/profile runs: each10 checks of64000 pixels with0 differences, two complete Main calls and consumed-resource identity matched; the separate legacy no-profile zh-Hans run has six0-difference checks, not Main/profile coverage. Original1.1/255 per-channel tolerance is unchanged, not byte/float identity. GPU fresh pre/post tracked and DLL/PDB manifests were constant; the gate used a different worktree/DLL hash and has only pre-build/post-gate manifests, no fresh post-build/pre-test attestation. Old67f results retain their historical identity. Those automated results do not sign physical keys/OS release/desktop/close/current CI or repair the historical Observer P1. The separate user manual confirmation and retained reproduction checklist are in the [M3 checkpoint](../../../docs/milestones/m3/acceptance.md#windows-人工清单用户反馈已完成); current CI and publication authority remain pending, and the discontinued specification-extra Observer is not an M3 functional acceptance condition.
