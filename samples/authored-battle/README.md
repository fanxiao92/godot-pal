# Original authored battle package (#46 checkpoints B/C)

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

Load with `NativeStoryProfile.Load(storyUri, "zh-Hans" | "en")`, then `NativeBattleVisualProfile.Load(visualUri, checkedStory)`. Both languages are checked even when selecting one. Normal Main now accepts these explicit inputs (checkpoint C):

```powershell
& $Godot --path src/Pal.Game -- --story-profile res://Assets/AuthoredBattle/Win/Story/Story.tres --battle-visual-profile res://Assets/AuthoredBattle/Win/Story/Visual.tres --story-language zh-Hans --battle-seed 1
```

Use `Loss` for the other numeric scenario and `en` for English. Seed is optional; normal instances randomize once, and a supplied seed is not a forced outcome or SDL replay guarantee. At the starting position press Enter to search owner1, finish the pre-battle dialogue, press Enter for Attack then separately for Target, and continue selecting only when the attack menu returns. After actual KO, finish the result dialogue and return to exploration. No scene-enter jump, teleport or direct-battle shortcut is supplied. See [the C contract](../../docs/design/main-story-roundtrip.md).

**Existing B outputs:** Words/font now have new bytes, so old Story manifests fail exact hash validation; they are not unchanged bundles. Preserve them. With the existing static/numeric dependencies, copy `samples/authored-battle/{Win,Loss}/Story` to external owned input directories and run the same normal Story wrapper into **fresh** outputs such as `Assets/AuthoredBattleC/{Win,Loss}/Story`. No metadata relocation is needed when reusing the old static/numeric URI references. If those dependencies move too, relocate the external metadata explicitly. The fresh Story/Visual pair becomes the two launch URIs; never overwrite old outputs or let runtime silently repair hashes.

## Ownership, visible data and search

The 14 committed native text files contain original literal indexed pixel art, embedded `Image`/`ImageTexture` world textures and bitmap lettering. They total approximately 1.12 MB; eight external JSON inputs total approximately 20 KB. There are no image binaries, LFS objects, texture imports or private cache references. `.tres`/`.tscn` use normal text storage. Embedded texture data is present in the same native graph, not a missing/empty Texture2D. No system-font fallback or external copyrighted artwork was used.

- Scene1/map1 has 16,384 explicit raw/decoded cells. Tile0 is a 32×16 green/turquoise diamond; cell0 is explicitly blocked, the arena around the starting point is walkable. Static sprites are cyan leader41 and yellow NPC42, 12 nominal 16×24 frames each. `WalkFramesWord=3` supports all four directions. Battle player0..9 are retained, not confused with role0.
- Leader role0 starts `(320,320)`, South0/layer0/phase0. Owner1 stands `(304,328)`, state2, triggerMode1, `wTriggerScript=1`. Normal `StaticSceneRules.FindSearchTarget` hits **sample1** (direction0 offset −16,+8); no movement/teleport is needed. The starting map position is not blocked and has legal neighbors. This is neither M3 owner60/@0188 nor frozen owner28/@09C3.
- Enemy sprite97 has three 24×24 red frames, partitioned idle1/magic1/attack1, wait0. Role0/team18/target7 are separate identities. Bottom centers are player `(240,170)`, enemy `(100,120)`; no unsigned-coordinate trick.
- The 320×200 indexed battle background has blue sky, checkerboard green floor and a gold ring. All indexed planes have explicit alpha; world frames additionally have valid RGBA Texture2D data. A shared 768-byte palette gives cyan `(64,224,240)`, gold `(248,208,64)`, red `(232,72,80)`, and warm white default text at **0x4F** `(255,248,224)` against dark blue `(8,16,32)`. No all-black palette or empty geometry.
- Formal languages carry `战/胜/败` and `FIGHT/WIN/LOSE`. The original dialogue glyphs plus C UI letters, digits, punctuation and Chinese labels are original hand-plotted, nonempty 8×15/16×16 bitmaps. Optional typed WORDs 60000..60004 provide Attack/Target/Miss/Critical/HP in both languages; these modern demonstration IDs/labels are not retail translations or SDL IDs. Three 8×8 gold triangle prompts have nonempty alpha. Portrait0 intentionally has no portrait; the exact nonzero portrait map is empty.

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

A's direct31/completePrepare89 and unchanged Core780/numeric43/frozen33/Story58/Visual49 are reused by their original identities, not claimed as rerun. That B evidence did **not** validate Main/input/HUD or SceneRunner. C adds separately identified normal Main/SceneRunner and process evidence, but still does **not** validate full animation/effects, GPU pixels, human desktop use, CI, #47+, or completion of #46/M4. Native collections are read-only projections but Resource instances remain mutable; consumers must honor prepared-data ownership. Dependency hashes do not authenticate code, engine, `.godot` caches, GPU output, authorship or retail copyright.


## D2-A original effect data (not yet displayed by Main)

The author metadata now pairs `attackEffectSpriteUri` with group0 and gives the enemy `idleAnimSpeed=2`. `Visual/AttackEffect.tres` contains three independently hand-plotted 16×16 diagonal light-stroke frames0/1/2 with distinct indexed planes (gold5, warm-white79, gold5), explicit transparent background and opaque strokes. This is an original resolved sprite41/column1→group0 mapping, **not** a retail table or effect-art extraction. Group is distinct from effect pack SpriteId0, role0, team18, target7 and enemy sprite97.

With existing Static/Numbers, use the unchanged normal Story wrapper on these current input directories and **fresh** output paths such as `src/Pal.Game/Assets/AuthoredBattleD2A/{Win,Loss}/Story`. Never overwrite old B/C/Story directories: their historical hashes are not updated. `NativeBattleVisualProfile.Load` retains the checked effect and original palette/index/alpha; `RequireReferencePresentation()` verifies completeness without reloading. The ordinary B data tests independently check literal frame IDs/pixels and both languages through the same public producer. Main still displays C/D1 only; D2-B must connect these resources, idle counters and full feedback. Preparing this package is not proof that effects render, nor GPU/desktop/CI/fullgate or #46/M4 completion.


## D2-C-A original UI digits (resource-only)

Both current Story metadata inputs add optional `numberSpriteUri` pointing to committed `Visual/Numbers.tres`. It uses existing PalSprite/PalFrame and the unchanged active palette: Yellow frames19..28/index5, Blue29..38/index1, Cyan56..65/index4. Original5×7 strokes have an offset warm-white79 shadow in6×8 planes for visibility, transparent background and nominal frame0 plus legal indexed aliases. This is original digit art, not retail or dialogue glyph conversion. Six pixels is the reference layout advance, not a general size limit.

With existing Static/Numbers dependencies, run the same normal Story wrapper above with current `samples/authored-battle/{Win,Loss}/Story` and **new** outputs, e.g. `Assets/AuthoredBattleD2CA-20261103/{Win,Loss}/Story`. Never overwrite old B/C/D2A outputs or repair their manifests. Strict loading exposes retained native digit frames and `RequireNumberPresentation()`; Main does not yet select or display them. D2-C-B must implement floating digits/feedback lifetime. Headless resource preparation is not GPU/manual/CI/fullgate or #46/M4 completion.


## #47 party authored packages (slice A)

`PartyWin` and `PartyLoss` are original two-player authored inputs (no retail data) reusing the committed Win/Loss static, dialogue and event sources with one numeric scenario each: two roles (dex100, hp100/atk40/def20 and hp95/atk35/def18; Loss both hp20/atk1/def20) versus team18 with two enemy slots sharing enemy id 7, hp43, resistance1 (Win slots atk0/def0/dex0; Loss atk100/def999/dex500). The story metadata declares the optional strict `visual.teams[].enemies` slots (targetId 7 at (100,120) and (128,104)) and `playerPosition` (200,176), the reference `g_rgPlayerPos[1][0]` two-player head origin; two players share the single authored player sprite by authored design. Reproduce through the same normal wrappers or `AuthoredStoryFixture` into fresh GUID-owned outputs; never overwrite old AuthoredBattle* outputs. These inputs cover the resource/profile chain and public Prepare readback only; Main party presentation and SceneRunner roundtrips are #47 slice B.

## #49 Midbattle authored package (dying / friend-death role hooks)

`Midbattle` is an original two-player input reusing the committed static, dialogue and visual sources with the #48 five-object dual-attack closure on role1. Numeric shape: role0 hp150/atk40/def999/dex100 (enemy strikes deal 1 there), role1 hp100/base atk35/def18/dex100 with effective 46/52/119, versus two id7 slots (hp160, attack82 => strength 118, dex500/501) so one enemy strike crosses role1's dying threshold mid-queue and a later strike kills it with the responsive coverer role0 beside it. `players` declare `dyingScript`@10 and `friendDeathScript`@36 into the event stream.

The event file transcribes the **retail role-hook script structure** (read-only reference Sss.mkf object37 `@A96E-A987` dying: three random bands 75/66/50 over four dialogue blocks; object36 `@A9B5-A9D1` friend-death: same bands, each block ending in per-WORD `changeHp`/`changeMp`/`boostStat` fields 17/18/20/21) with **original authored text, portraits and glyphs** — no retail bytes. The original 65-instruction author stream serializes to 520 bytes, sha256 `903bac85c6a7fbd2b51595479a62994a3d1f064b51f263ff686033bebda95556`. Shared dialogue additions: messages 4..22 and WORD 60005 (`提升`/`BOOST`) in both languages (consecutive merged displays share one message range), 38 new hand-plotted glyphs plus a space glyph, and three original 24x24 indexed portraits (2/5/15) exactly covering the scripts' nonzero portrait IDs.

Prepare with the normal wrappers into fresh outputs (e.g. `Assets/AuthoredBattleMidbattle/...`); never overwrite old outputs. Honest boundary: the native `PalBattlePlayer` declares no base magicPower/flee panels, so friend-death boost registers 18/21 apply as zero through this authored chain (Core unit 1 covers nonzero bases directly); the visible boost panel therefore shows the attack/dexterity-scaled registers (2/10/90 bands).
