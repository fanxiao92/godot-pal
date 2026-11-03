# Normal authored story roundtrip (#46 checkpoint C)

Checkpoint C adds an explicit **normal Main** mode, not a second fixture battle host. It remains an authored single-player/single-enemy, zero-growth supported slice. It does not open frozen owner28/@09C3, implement #47+, close #46, or complete M4/main delivery.

## Selection and prepared data

```
--story-profile <res:// URI> --battle-visual-profile <res:// URI>
[--story-language zh-Hans|en] [--battle-seed <unsigned decimal ulong>]
```

Both profiles are mandatory. This mode is exclusive with M2, M3 and frozen M4 selections. Duplicates, missing values, unknown flags, mixed modes, invalid URIs/languages and non-decimal/overflow seeds are rejected. `GameSceneConfiguration.FromArguments` is shared by shipped `GameStartup` and embedded Main. Old selections retain their existing parser and diagnostics.

Main strictly loads Story and Visual and requires reference-presentation and UI capabilities before instantiating the map, creating sessions/RNG or publishing scene children. Base-format-valid old visuals without effect/speed now fail normal Story startup explicitly; format loading remains compatible. `CheckedStoryProfile` retains the same `IgnoreDeep`-loaded PackedScene it validated; Main instantiates it without reloading its URI. Native Resources remain mutable isolated prepared instances, consumed read-only by convention, **not deep-immutable objects**. Hashes bind complete native data, including both language files and the font, not engine/code/cache or authorship.

Every Main owns one Godot `RandomNumberGenerator`: `Randomize()` by default, or one `Seed` assignment for any explicitly seeded story. Its `IBattleRandom` adapter uses inclusive `RandiRange`/`RandfRange`; `to <= from` returns `from` without consuming entropy (SDLPal `util.c`). This is not replay, forced outcome, a stable cross-engine sequence, or SDL same-seed equivalence.

## Search, ownership and return

`NpcInteractionSession.ForSelectedEntry(rules, leader, entry)` shares the original search, held-direction, 100ms late-no-catchup and 50ms return implementation. The old three-argument constructor remains owner60/@0188-only. This is **not a general event scheduler or return VM**: the currently supported closure only ends with `end keep`; other end modes fail the real VM's preflight.

A successful real `TriggerScriptSession.Start(file, entry, caller, sourceRequestId)` publishes a new `CallId`, its actual `StartedEntry` and read-only `SourceRequestId`; an empty origin or failed preflight leaves all old call facts intact. Legacy three-argument Start publishes a null origin. Main passes the actual pending search Guid into typed story Start, then binds that same origin, object and CallId once to the pending search. Calls originating from older searches (even previously completed objects) or other interactions cannot bind; no history registry or wall-clock fence is used. Typed story Start without an origin is rejected. A second bind cannot replace it. Completion additionally requires that same object/CallId, Guid, caller, Completed phase and original ReturnEntry. A reused object started again cannot claim the old search. Legacy unbound owner60 receipt semantics remain unchanged.

`PalTriggerDialogue.Initialize(display, checkedStory, random)` directly consumes checked events, language, font, portraits, prompts, palette and battle content. No URI reload, battle-crossing dialogue preview, substitute RNG or simulated win is used. The display alone advances dialogue; the adapter only binds and receipts completed displays/battles.

Main alone owns battle input, lifecycle advancement and feedback:

1. Actual exploration search → complete pre-battle dialogue → real pending battle.
2. Enter/Space/Ctrl confirms AttackMenu → TargetMenu. A separate press selects the actual target; that callback does not also resolve an action.
3. Each Resolving callback calls `ResolveNextAction` exactly once. No fixed turn order or automatic attack policy is added. Feedback reads the real `LastAction` and combat HP, never recomputes damage/critical/evade.
4. Feedback poses finish before another action or menu. On KO, Main removes/frees its overlay, restores the map and acknowledges the **same Guid and lifecycle-owned Completion**. The result display is resumed only on the next real Main callback, so the battle callback's elapsed time cannot enter it.
5. Result dialogue → actual keep stop → shared 50ms return → independent exploration. Direction presses/releases still reach the retained exploration input while blocked; search is not forwarded through battle/dialogue transitions.

Exit disconnects host subscriptions, releases the overlay/textures and RNG, drops retained session/resource references and leaves no shared battle state. Initialization failure frees owned unpublished nodes; no fallback mode or partial story is exposed.

## UI data and bounded presentation

`PalDialogueLanguage.Words` is an optional typed `Dictionary<int,string>`, default empty for old resources. Format validation accepts the full WORD domain **0..65535**, including unused blank strings, but rejects invalid IDs/null text. `CheckedStoryProfile.SelectedWords` is a copied read-only selected-language projection. Main separately requires nonblank UI words and every visible glyph, plus digits and `:/`; there is no system-font fallback.

The modern demonstration UI explicitly assigns 60000 Attack, 60001 Target, 60002 Miss, 60003 Critical, 60004 HP. These are **this authored extension's conventions**, not retail IDs or globally reserved game IDs. SDLPal attack selection uses an icon (`uibattle.c`), not these WORDs. The committed language files contain original Chinese/English labels and their formal font contains original hand-plotted glyphs; arbitrary dialogue message IDs are not used as WORDs. HUD `HP:player/enemy` reports both current combat HP values (not lifecycle post-win recovery).

Drawing uses original frame indices/alpha with the checked active palette, 320×200 background and explicit SHORT bottom-center positions. The minimal player approach/strike uses frames8/9 (`fight.c2079–2120`); enemy poses use its declared idle/magic/attack segments and zero-attack fallback (`fight.c4987–5045`). Presentation has a 40ms minimum pose cadence (`battle.h`, 25FPS) and does not skip poses on late callbacks. This is **minimal visible motion/feedback**, not complete original animation timing, attack effects, audio or GPU fidelity. Those remain checkpoint D and independent desktop/GPU checks.

## Running and evidence boundary

Prepare from [the original authored package](../../samples/authored-battle/README.md). Changed Words/font bytes invalidate old prepared Story manifests. Never overwrite accepted old outputs: use fresh Story output directories or external metadata copies that explicitly relocate references. Numeric/static dependencies may be reused unchanged. Runtime never prepares/synchronizes resources.

`StoryRoundtripSceneTests` drives real Main/SceneRunner keys for Win/Loss × both languages, separate menu confirmations, real callback observations, completed overlay restoration/owned result continuation, stop/exploration, same-URI independence, missing-resource no-partial failure, optional Words and degenerate RNG behavior. `StoryMainProcessTests` covers real argv/exit/READY; public Core tests cover actual-call binding and deterministic return boundaries. These headless checks are not GPU/manual/CI/full-gate acceptance. The Game-layer `StoryBattlePoseClock` is the sole production pose cadence: explicit uint checks cover 39/40ms, late single advance, wrap and restart; Main update observers verify actual pose changes and final feedback release on the same sampled production clock. An active-feedback removal case checks overlay release and cleared Main/adapter bindings before detached nodes are finally freed, then exercises a fresh host on the same URI. Existing producer A/numeric/frozen tests are not rerun merely for the checkpoint label.

## Reference presentation checkpoint D1: enemy active physical action

D1 replaces C's fixed enemy pose approximation with `StoryEnemyAttackPresentation`, a Game-only read-only timeline consumed by the actual battle view. Main still resolves exactly one real lifecycle action, captures pre-resolution HP, and advances the existing 40ms pose clock. The renderer consumes the same snapshot's frame, bottom-center position, indexed color shift, displayed player HP and result visibility; it does not leak the resolved HP or Miss label before impact. Core combat/RNG, selected native resources, language conventions and owned acknowledgment are unchanged.

The independent source is `fight.c4987–5135`: magic poses each last two ticks; `max(0,3-MagicFrames)` preparation moves each subtract `(2,1)`, followed by one tick; attack moves to player+`(-44,-16)`. Zero attack uses idle−1 for two ticks. Nonzero attack uses the **inclusive** loop `i=0..AttackFrames` and frame `IdleFrames+MagicFrames+i−1`, with exactly `ActWaitFrames` ticks each. A zero wait performs assignments without inventing a timed pose. Compact duration segments avoid allocating one object per wait tick.

Impact reveals the actual HP/result, with player frame4 and color shift6 for a hit, or frame3 for evade. Shift is the reference low-nibble saturated addition preserving high nibble and alpha (`palcommon.c386–412`), not RGB tint. One tick later shift clears and player recoils `(8,4)`, then another `(2,1)` for three ticks. Enemy returns to its original position/frame0 for one tick, player restores dead2/dying1/healthy0 for the five-tick tail, then its original position. The dying predicate is exactly HP < min(100,maxHP/5), not a percentage approximation. No next action/menu or owned completion is released until all timed poses finish. Late callbacks still advance only one 40ms tick, including across uint wrap.

**Bounded, not full D or #46 completion:** native enemy idle speed and persistent idle counter are not yet bound. The final tail retains frame0 rather than claiming SDLPal's idle-gesture updates. D2 must bind that metadata/counter through formal preparation, add player frame7 windup/full three-frame attack effects and recoil/return, and close remaining reference presentation (including floating-number layout rather than claiming the authored HUD is the retail UI). Audio remains out of scope. No schema, author data or prepared outputs change in D1: existing typed complete sprites/palette are reused without URI reload. Frozen owner28/@09C3 and old M3 boundaries remain closed/unchanged.

`StoryRoundtripSceneTests` adds a real Main Loss first-feedback regression (displayed HP20 before impact, actual nonzero enemy damage), an independent literal timing table for magic0/1/3 × attack0/2 × wait0/2 × hit/evade, exact restored/dying frames and indexed-shift endpoints. Real UpdateCompleted observers check impact tick5, actual HP/result facts, frame4/3, final tick15 and unchanged action identity for the authored zero-wait enemy. Existing 39/40/late/wrap/restart and active-exit cleanup checks remain. These headless facts are not GPU pixels, physical keyboard, CI or full-gate evidence.


## D2-A resource checkpoint (not runtime presentation)

Formal optional attack-effect and enemy idle-speed data now follow the same author metadata → public Story Prepare → strict native Visual → retained checked objects path. Old C inputs remain base-format valid and Main still runs C/D1 without selecting `RequireReferencePresentation()`. No effect is displayed by this checkpoint, no idle counter is yet advanced, and player/reference floating feedback remains D2-B. See [the schema/capability contract](native-battle-visuals.md#46-d2-a-optional-reference-presentation-data-not-playback). Core, Main, clock, RNG and owned acknowledgment code are unchanged.


## D2-B: player effects, persistent enemy idle and impact HP

Normal Story now selects `RequireReferencePresentation()` during preflight. It consumes the **retained checked** three effect frames and palette/index/alpha planes; runtime does not reload their URI, prepare resources, create another combat session or consume presentation RNG. Native Resources are mutable prepared instances used read-only, not deep-immutable. Existing C/D1 descriptions above record those checkpoints, not a legacy fallback mode. Fresh `AuthoredBattleD2A/{Win,Loss}/Story/{Story,Visual}.tres` inputs have the selected capability; old C inputs remain format-loadable but cannot run this presentation.

`StoryPlayerAttackPresentation` follows the supported single-target/no-dual-attack caller (`fight.c3590–3758`) and animation (`2008–2292`). At 40ms ticks0–3 the player is frame7 at its original position;4–5 are frame8 at enemy+(64,20);6 is frame8 at +(54,18). Ticks7–9 use frame9 and the three checked effect frames. The local x−=16/y−=4 before FX does not assign player.pos: player stays +(54,18) for FX0/1 and becomes +(56,19) only for FX2. Target height is captured **after the four windup gesture updates**, before approach, from the actual current enemy frame. Effect bottom-centers are enemy+(0,−height/3+10), then (−16,+16) each; each effect uses its own dimensions when drawing.

FX0's scene is drawn before reference shift6 is assigned, so only FX1/2 draw the enemy with saturated indexed shift6. Actual before-enemy HP/result stays hidden until FX0; enemy attacks likewise retain before-player HP until their existing impact. Main reads the one real LastAction and before/after combat views without deriving damage or RNG. The single draw snapshot used by `_Draw` carries frames, positions, both displayed HP, indexed shifts, FX and idle counter, and is sampled at `UpdateCompleted` by integration tests.

Ticks10–12 clear shift and recoil the enemy relative to origin by (−8,−4), (−4,−2), (−6,−3). Tick13 restores fighters and performs one explicit UpdateFighters gesture update **plus** the first TRUE tail update;13–15 are three rest ticks. Completion is16, not an extra invented rest frame. No next Resolve or owned acknowledgment occurs before feedback ends, even when a real KO receipt already exists.

Each battle owns one persistent enemy frame and WORD counter, initially copied from the selected metadata. Startup UpdateFighters (`battle.c1782`) and the immediately due first outer StartFrame (`battle.c763–792`, `fight.c1102–1104`) decrement twice. TRUE delays and each manual FX decrement once before their draw; FALSE enemy action segments preserve the counter while assigning their active frame. The five TRUE enemy tail ticks advance the same frame/counter, followed by the final explicit UpdateFighters decrement without adding a timed pose (`fight.c5130–5137`). Speed0 decrements to65535, never coerces to1. Neither frame nor counter is reconstructed per action.

Main samples one `now` and owns both scheduling cursors, reusing `StoryBattlePoseClock`'s one-step/40ms/no-catchup/wrap algorithm. Outside feedback, completed outer ticks advance idle once. Returning from a blocking action leaves an overdue outer update, consumed **once on the next real callback**, not delayed a fresh40ms and not consumed again on the finishing callback. Existing menu/target/Resolving input remains per-callback and mode-isolated; this bounded presentation adapter does **not** claim retail40ms menu input gating. Authored idleFrames1 legitimately stays frame0 while its live WORD counter changes; literal multi-frame tests independently prove cycling and zero wrap.

**Still D2-C, not full D/#46/M4 completion:** floating-number positions/colors, critical/evade retail-label placement and full reference start/end lifecycle remain deferred. The current authored HUD/WORD labels use the existing selected formal font/Words, not system-font fallback or an English bitmap table. Audio, GPU pixels, physical keyboard/desktop, CI and full shared gate remain separate follow-up evidence. Core/RNG, native schema/producer, prepared outputs, M3 and frozen owner28/@09C3 are unchanged.
