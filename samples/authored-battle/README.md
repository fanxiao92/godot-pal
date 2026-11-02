# Original authored battle package (#46 checkpoint B)

This is **original artificial content**, not PAL retail data, extracted art, a saved game, the real first mandatory battle, or certification of an archive. The formal native resources in `src/Pal.Game/Samples/AuthoredBattle` are the shipped author data, not test factories. There is no runtime generation step. These resources and this directory must remain available together: the input packages are deliberately **not self-contained**.

## Clean checkout preparation

From the repository root, use the approved .NET SDK, fixed GdUnit packages (bootstrap per the repository instructions), and Godot **4.7.2 .NET**. No download, original archives, private Assets, RuntimeTests, system fonts, or machine-specific content is needed. All Godot/Prepare/Game operations are globally serial; stop if an unknown Godot process is present.

The static loader requires an `Assets` root. Explicitly copy the committed native static data once, then use the **normal production wrappers**, with their unchanged budgets. The copy refuses an existing destination (including an empty one) and never overwrites or nests `Static/Static`. Numeric and Story preparation also refuse nonempty outputs. To reproduce again choose a fresh checkout or deliberately manage your own old outputs; do not silently erase them.

```powershell
$ErrorActionPreference = 'Stop'
$Godot = 'C:/Users/xiaofan/scoop/apps/godot-mono/4.7.2/godot-mono.console.exe'
$existing = @(Get-Process -Name 'godot*' -ErrorAction SilentlyContinue | Select-Object Id,ProcessName)
if ($existing.Count) { $existing | Format-Table; throw 'Confirm existing Godot ownership before preparing.' }
$static = 'src/Pal.Game/Assets/AuthoredBattle/Static'
if (Test-Path -LiteralPath $static) { throw 'Static destination already exists; nothing was copied.' }
New-Item -ItemType Directory -Path $static | Out-Null
Copy-Item -Path 'src/Pal.Game/Samples/AuthoredBattle/Static/*' -Destination $static -Recurse
foreach ($scenario in @('Win','Loss')) {
    & ./scripts/Prepare-BattleData.ps1 `
        -InputPath "samples/authored-battle/$scenario/Numbers" `
        -OutputPath "src/Pal.Game/Assets/AuthoredBattle/$scenario/Numbers" -GodotPath $Godot
    if ($LASTEXITCODE -ne 0) { throw "Numeric preparation failed: $LASTEXITCODE; stop and preserve evidence." }
    & ./scripts/Prepare-StoryPackage.ps1 `
        -InputPath "samples/authored-battle/$scenario/Story" `
        -OutputPath "src/Pal.Game/Assets/AuthoredBattle/$scenario/Story" -GodotPath $Godot
    if ($LASTEXITCODE -ne 0) { throw "Story preparation failed: $LASTEXITCODE; stop and preserve evidence." }
}
```

Each scenario produces `Numbers/BattleContent.tres` and `Story/{Story.tres,Visual.tres,Events/index.json,Events/arena.json}`. The returned pairs are:

- `res://Assets/AuthoredBattle/Win/Story/{Story,Visual}.tres`
- `res://Assets/AuthoredBattle/Loss/Story/{Story,Visual}.tres`

Load with `NativeStoryProfile.Load(storyUri, "zh-Hans" | "en")`, then `NativeBattleVisualProfile.Load(visualUri, checkedStory)`. Both languages are checked even when selecting one. **Main has no Story/Visual mode wired yet**; these are future explicit Main inputs, not current launch arguments. No scene-enter jump, teleport or direct-battle shortcut is supplied.

## Ownership, visible data and search

The 13 committed native text files contain original literal indexed pixel art, embedded `Image`/`ImageTexture` world textures and bitmap lettering. They total approximately 1.12 MB; eight external JSON inputs total approximately 20 KB. There are no image binaries, LFS objects, texture imports or private cache references. `.tres`/`.tscn` use normal text storage. Embedded texture data is present in the same native graph, not a missing/empty Texture2D. No system-font fallback or external copyrighted artwork was used.

- Scene1/map1 has 16,384 explicit raw/decoded cells. Tile0 is a 32×16 green/turquoise diamond; cell0 is explicitly blocked, the arena around the starting point is walkable. Static sprites are cyan leader41 and yellow NPC42, 12 nominal 16×24 frames each. `WalkFramesWord=3` supports all four directions. Battle player0..9 are retained, not confused with role0.
- Leader role0 starts `(320,320)`, South0/layer0/phase0. Owner1 stands `(304,328)`, state2, triggerMode1, `wTriggerScript=1`. Normal `StaticSceneRules.FindSearchTarget` hits **sample1** (direction0 offset −16,+8); no movement/teleport is needed. The starting map position is not blocked and has legal neighbors. This is neither M3 owner60/@0188 nor frozen owner28/@09C3.
- Enemy sprite97 has three 24×24 red frames, partitioned idle1/magic1/attack1, wait0. Role0/team18/target7 are separate identities. Bottom centers are player `(240,170)`, enemy `(100,120)`; no unsigned-coordinate trick.
- The 320×200 indexed battle background has blue sky, checkerboard green floor and a gold ring. All indexed planes have explicit alpha; world frames additionally have valid RGBA Texture2D data. A shared 768-byte palette gives cyan `(64,224,240)`, gold `(248,208,64)`, red `(232,72,80)`, and warm white default text at **0x4F** `(255,248,224)` against dark blue `(8,16,32)`. No all-black palette or empty geometry.
- Formal languages carry `战/胜/败` and `FIGHT/WIN/LOSE`. Eleven Latin 8×15 glyphs and three Chinese 16×16 glyphs are original hand-plotted, nonempty bitmaps. Three 8×8 gold triangle prompts have nonempty alpha. Portrait0 intentionally has no portrait; the exact nonzero portrait map is empty.

The required logical `data.mkf`/chunk3/role0/sprite offset24/walkFrames offset768 fields are **author-declared compatibility metadata only**. Their SHA is the UTF-8 SHA256 of this declaration, not a fabricated extraction receipt:

`Original pixel art and bitmap lettering authored for godot-pal #46; no retail assets or extraction evidence.`

The bundle SHA is SHA256 of UTF-8 `godot-pal authored arena v1`; it is an author identity, not a retail bundle hash. This is distinct from #45 real provenance. The `.tscn` copied into Assets retains absolute references to the **committed Samples native leaves**. The two metadata inputs reference their explicitly prepared numeric outputs. Nothing resolves through ignored private data or test factories.

## Complete original instruction stream

The faithful schema's `SSS.MKF/chunk4` names are logical schema metadata for this **original author stream**, not a claim that retail SSS was consumed. Serialize the following four-WORD rows as little-endian unsigned 16-bit integers (80 bytes):

| address | opcode | operand0 | operand1 | operand2 | meaning |
|---|---:|---:|---:|---:|---|
|0|134|0|65535|8|retained unselected raw row|
|1|60|0|0|0|upper dialogue, no portrait, inherited color|
|2|65535|1|0|0|pre-battle message1|
|3|7|18|7|0|team18, Lost→7, boss=true/no flee target|
|4|60|0|0|0|Won upper dialogue|
|5|65535|2|0|0|Won message2|
|6|0|0|0|0|explicit keep-entry stop|
|7|60|0|0|0|Lost upper dialogue|
|8|65535|3|0|0|Lost message3|
|9|0|0|0|0|explicit keep-entry stop|

The genuine author-stream SHA256 is `0ad42c6e02646a69b76934eb478f4a917ac11e81436f61c35cd91f65e61f7ca8`. Both packages preserve **all ten rows**, raw0, source operands and sequential `next` (including stop6→7). Named semantic values re-encode to these same source operands; the zero third battle operand means **boss=true**, not false. Entry `arena#challenge` is owner1 trigger→1. Won follows next4; Lost follows its own operand7; both display and stop. No reload, unsupported side effects, fake completion or forced-result field exists.

## Numeric scenarios and evidence boundary

Both have role0 level2, maxHP150, MP20/50, seven explicit hidden XP rows and primary XP value0/level1/count0, six zero equipment slots, 32 zero magics, nine zero statuses, zero cover storage (actual player cover=null), poison[0], zero hooks, no attackAll, all 100 growth thresholds100 and no learnable magic rows. Enemy target7 level0 has resistance1, zero statuses/poison/hooks/magic/dualMove/items/rewards/cash.

| input | player HP / Attack / Defense / Dex | enemy HP / attack offset / defense offset / dex offset |
|---|---|---|
|Win|100 / 40 / 20 / 100|43 / 0 / 0 / 0|
|Loss|20 / 1 / 20 / 100|100 / 100 / 999 / 500|

With legal verification draws (float1, integer lower bound except 0..5→1), Win acts first and defeats the enemy in one action (role0's normal extra-damage draw applies); victory half recovery yields **HP125/MP35**. Loss enemy acts first, normal attack depletes playerHP20, yielding **HP0/MP20**, no victory recovery. These are the same engine and lifecycle, not artificial-mode rules. No finite dataset guarantees loss under an infinite RNG sequence of evades; validation does not claim that. Selected-entry preflight checks the full Won/Lost closure without RNG; only the behavioral test draws randomness.

`AuthoredBattlePackageTests` has two exact native-map cases: normal public numeric/story Prepare from these committed sources, strict Load in both languages, complete stream hash, literal visible pixels/Texture2D/glyphs/palette/search and real public trigger/lifecycle outcomes, owned receipt, appropriate result message and stop. It uses GUID-owned external metadata copies to relocate only output references, never constructs a substitute business fixture. Real normal PowerShell/Editor wrappers were separately run for both scenarios. Data-only work has no fabricated compile-RED phase.

A's direct31/completePrepare89 and unchanged Core780/numeric43/frozen33/Story58/Visual49 are reused by their original identities, not claimed as rerun. This unit does **not** validate Main, input/HUD, animation/effect, SceneRunner round-trip, GPU pixels, human desktop use, CI, #47+, or completion of #46/M4. Native collections are read-only projections but Resource instances remain mutable; consumers must honor prepared-data ownership. Dependency hashes do not authenticate code, engine, `.godot` caches, GPU output, authorship or retail copyright.
