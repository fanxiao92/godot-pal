# Normal authored story roundtrip (#46 checkpoint C)

Checkpoint C adds an explicit **normal Main** mode, not a second fixture battle host. It remains an authored single-player/single-enemy, zero-growth supported slice. It does not open frozen owner28/@09C3, implement #47+, close #46, or complete M4/main delivery.

## Selection and prepared data

```
--story-profile <res:// URI> --battle-visual-profile <res:// URI>
[--story-language zh-Hans|en] [--battle-seed <unsigned decimal ulong>]
```

Both profiles are mandatory. This mode is exclusive with M2, M3 and frozen M4 selections. Duplicates, missing values, unknown flags, mixed modes, invalid URIs/languages and non-decimal/overflow seeds are rejected. `GameSceneConfiguration.FromArguments` is shared by shipped `GameStartup` and embedded Main. Old selections retain their existing parser and diagnostics.

Main strictly loads Story and Visual and validates UI capability before publishing scene children. `CheckedStoryProfile` retains the same `IgnoreDeep`-loaded PackedScene it validated; Main instantiates it without reloading its URI. Native Resources remain mutable isolated prepared instances, consumed read-only by convention, **not deep-immutable objects**. Hashes bind complete native data, including both language files and the font, not engine/code/cache or authorship.

Every Main owns one Godot `RandomNumberGenerator`: `Randomize()` by default, or one `Seed` assignment for any explicitly seeded story. Its `IBattleRandom` adapter uses inclusive `RandiRange`/`RandfRange`; `to <= from` returns `from` without consuming entropy (SDLPal `util.c`). This is not replay, forced outcome, a stable cross-engine sequence, or SDL same-seed equivalence.

## Search, ownership and return

`NpcInteractionSession.ForSelectedEntry(rules, leader, entry)` shares the original search, held-direction, 100ms late-no-catchup and 50ms return implementation. The old three-argument constructor remains owner60/@0188-only. This is **not a general event scheduler or return VM**: the currently supported closure only ends with `end keep`; other end modes fail the real VM's preflight.

A successful real `TriggerScriptSession.Start` publishes a new `CallId` and its actual `StartedEntry`; failed preflight leaves the old call identity intact. Main starts the actual typed story session, then binds that object and CallId once to the pending search Guid. A second bind cannot replace it. Completion additionally requires that same object/CallId, Guid, caller, Completed phase and original ReturnEntry. A reused object started again cannot claim the old search. Legacy unbound owner60 receipt semantics remain unchanged.

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

`StoryRoundtripSceneTests` drives real Main/SceneRunner keys for Win/Loss × both languages, separate menu confirmations, real callback observations, completed overlay restoration/owned result continuation, stop/exploration, same-URI independence, missing-resource no-partial failure, optional Words and degenerate RNG behavior. `StoryMainProcessTests` covers real argv/exit/READY; public Core tests cover actual-call binding and deterministic return boundaries. These headless checks are not GPU/manual/CI/full-gate acceptance. Existing producer A/numeric/frozen tests are not rerun merely for the checkpoint label.
