<!-- AUTO-GENERATED PROTOTYPE (Ticket #267) — DO NOT EDIT BY HAND -->
<!-- Source: OpcodeRegistry.cs + TriggerScriptSession + DynamicEventSession + Pal.Game host registry -->

# `sdlpal` 对位与能力账本（原型样本）

> **状态**：本文件由代码真源自动生成（当前为 `#267` 原型，基线 `57434ac`）。正式落地后由 `./pal.ps1 test check-ledger` 在 T1 CI 执行零漂移门禁校验，严禁手改。

## 1. 能力覆盖总览

| 维度 | 已完整支持 (`✅`) | 受限/部分 (`⚠️`) | 未实现 (`❌`) | 适用总数 | 覆盖率 |
|---|---:|---:|---:|---:|---:|
| 离线提取转写 (`Pal.Extract`) | 165 | 0 | 0 | 165 | 100.0% |
| `Pal.Core` `trigger` 执行器 | 45 | 9 | 110 | 164 | 32.9% |
| `Pal.Core` `auto` 执行器 | 13 | 0 | 143 | 156 | 8.3% |

图例：`✅` 已实现 · `⚠️` 条件受限/部分实现（括号注明边界） · `❌` 未实现 · `—` 原版该执行器不适用（`TriggerSwitchOnly`/`AutoSwitchOnly`）。

## 2. `reference/sdlpal` 核心模块对位索引

| `sdlpal` 模块 | 职责 | `Pal.Extract` (离线) | `Pal.Core` (纯逻辑) | `Pal.Game` (宿主/表现) | 阶段状态 |
|---|---|---|---|---|---|
| `script.c` | 字节码解释器 (trigger/auto) | `OpcodeRegistry`, `EventArchive` | `TriggerScriptSession`, `DynamicEventSession`, `TriggerEventCollection`, `TriggerNamedContentProfile` | `PalTriggerDialogue`, `Main.Story`, `Main.Dynamic` | ⚠️ 切片覆盖 (54/164 trigger, 13/156 auto) |
| `battle.c` / `fight.c` | 回合战斗、公式、AI、结算 | `BattleDataProjector`, `BattleContextAuthor`, `BattleImageExporter` | `ClassicBattleSession`, `ClassicBattleLifecycleSession`, `ClassicBattleModels`, `BattleEquipmentState`, `PartyPoisonState` | `PalStoryBattleView`, `StoryBattleNumbers`, `StoryBattlePoseClock`, `Story*AttackPresentation` | ✅ M4/M6 经典战斗闭环（局部扩展中） |
| `scene.c` / `map.c` | 瓦片地图、精灵绘制、碰撞 | `MapExporter`, `SceneExporter`, `SceneBundleExporter`, `SpriteDecoder` | `StaticSceneRules`, `SceneMetadata`, `DynamicEventWorld` | `PalMapScene`, `NativeSceneLoader`, `NativeMapValidation` | ⚠️ 单场景渲染+碰撞（待全场景切换） |
| `play.c` | 探索主循环、行走、NPC 交互 | — | `ExplorationSession`, `ExplorationInput`, `NpcInteractionSession`, `DynamicEventSession` | `Main`, `Main.Dynamic`, `Main.Story` | ⚠️ 探索+搜索触发（待全场景 touch/auto） |
| `game.c` / `global.c` | 全局状态、数据表、存档 | `DataGameData`, `DataTables`, `ObjectUnionTable` | `RoleLiveState`, `InventoryLiveState`, `CashLiveState`, `PlayerSaveContract`, `PlayerSaveJson`, `PlayerSaveRestorePlan`, `PlayerSaveWorldRestorePlan`, `ExplorationSaveGate` | `PlayerSaveStorage`, `Main.Story` | ⚠️ 核心域+5槽存档（待正常开局/全场景） |
| `ui.c` / `uigame.c` / `itemmenu.c` / `magicmenu.c` | 探索菜单、状态、装备、物品、法术、商店 | `BallImageDecoder`, `DataTables` | `ExplorationMenuSession`, `ExplorationShopSession` | `PalMenuView`, `PalShopView` | ✅ 探索菜单/装备/物品/法术/商店/系统 |
| `text.c` / `font.c` | 对话框、控制符、点阵字库 | `DialogueLanguageExporter`, `DialogueFontExporter`, `DialogueImageExporter` | `DialogueDisplaySession`, `DialogueTextTokenizer`, `DialogueLanguagePackage` | `PalDialogueDisplay`, `PalDialoguePaletteSurface` | ✅ 中/英双语对话与点阵渲染 |
| `palette.c` / `video.c` | 调色板、淡入淡出、视口 | `PaletteDecoder`, `FbpImageDecoder` | `TriggerScriptSession` (`fadeToRed`/`fadeOut`/`fadeScene`) | `Main.Story` (`StoryRedPalette`), `PalDialoguePaletteSurface` | ⚠️ 静态调色板+红淡/过场（待水火循环/震屏） |
| `res.c` / `yj1.c` | MKF 归档、YJ1/YJ2 解压 | `MkfArchive`, `Yj2Decoder`, `ArchiveInspector` | —（墙后零 MKF） | —（墙后零 MKF） | ✅ 离线提取全覆盖 |
| `input.c` | 键盘输入映射与防重入 | — | `ExplorationInput` | `Main` | ✅ 方向/确认/取消/战斗快捷键 |
| `audio.c` / `sound.c` / `rixplay.cpp` | RIX 音乐、音效 | ❌ 未实现 | `TriggerMusicState`（仅记录字） | ❌ 无音频播放 | ❌ 仅状态透传与存档 |
| `ending.c` / `aviplay.c` / `rngplay.c` | RNG 过场动画、AVI、结局 | ❌ 未实现 | ❌ 未实现 | ❌ 未实现 | ❌ 未开工 |

## 3. 已接入运行时 Opcode 速览（54 条）

> Agent 规划运行时切片时先扫本节；未列出的 111 条具名指令均处于「离线已转写、运行时未实现（`❌`）」，明细见第 4 节。

| Opcode | Op | Executor | Successor | `Pal.Extract` | `Core:trigger` | `Core:auto` | `Pal.Game` 宿主 |
|---|---|---|---|---|---|---|---|
| `0x0000` | `end` | `BothSwitches` | `Stop` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0001` | `end` | `BothSwitches` | `Stop` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0002` | `end` | `BothSwitches` | `ConditionalJump` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0003` | `goto` | `BothSwitches` | `ConditionalJump` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0004` | `call` | `BothSwitches` | `Sequential` | ✅ | ✅ | ❌ | ✅ Core状态直通 |
| `0x0005` | `redraw` | `TriggerSwitchOnly` | `Sequential` | ✅ | ✅ (World) | — | ✅ Main.Dynamic/Main (PendingTransition) |
| `0x0006` | `randomJump` | `BothSwitches` | `ConditionalJump` | ✅ | ✅ (BattleRng) | ❌ | ✅ Core状态直通 |
| `0x0007` | `startBattle` | `TriggerSwitchOnly` | `BattleResult` | ✅ | ✅ (Battle) | — | ✅ Main.Story + PalStoryBattleView |
| `0x0008` | `advanceEntry` | `TriggerSwitchOnly` | `Sequential` | ✅ | ✅ | — | ✅ Core状态直通 |
| `0x0009` | `waitUpdate` | `BothSwitchesDiffer` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Main.Dynamic/Main (AdvanceTriggerTick) |
| `0x000B` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000C` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000D` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000E` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000F` | `face` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x0010` | `walkTo` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Main.Dynamic/Main (AdvanceTriggerTick) |
| `0x0013` | `setObjectPosition` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0014` | `setObjectGesture` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0015` | `setPartyFacing` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x001B` | `changeHp` | `Shared` | `Sequential` | ✅ | ⚠️ 仅RoleHook单体 | ❌ | ✅ Core状态直通 |
| `0x001C` | `changeMp` | `Shared` | `Sequential` | ✅ | ⚠️ 仅RoleHook单体 | ❌ | ✅ Core状态直通 |
| `0x001D` | `changeHpMp` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x001E` | `changeCash` | `Shared` | `ConditionalJump` | ✅ | ✅ (Cash) | ❌ | ✅ Core状态直通 |
| `0x001F` | `addItem` | `Shared` | `Sequential` | ✅ | ✅ (Inv) | ❌ | ✅ Core状态直通 |
| `0x0020` | `removeItem` | `Shared` | `ConditionalJump` | ✅ | ⚠️ 需完整Owner,非战中 | ❌ | ✅ Core状态直通 |
| `0x0022` | `revivePlayer` | `Shared` | `Sequential` | ✅ | ⚠️ 仅HP,无解毒 | ❌ | ✅ Core状态直通 |
| `0x0024` | `setAutoEntry` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0025` | `setTriggerEntry` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0026` | `showBuyMenu` | `Shared` | `Sequential` | ✅ | ✅ (Shop) | ❌ | ✅ Main.Story + PalShopView |
| `0x0027` | `showSellMenu` | `Shared` | `Sequential` | ✅ | ✅ (Shop) | ❌ | ✅ Main.Story + PalShopView |
| `0x0030` | `boostStat` | `Shared` | `Sequential` | ✅ | ⚠️ 仅RoleHook单体 | ❌ | ✅ Core状态直通 |
| `0x003B` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x003C` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x003D` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x003E` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x0040` | `setTriggerMode` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0041` | `markFailed` | `Shared` | `Sequential` | ✅ | ✅ | ❌ | ✅ Core状态直通 |
| `0x0043` | `setMusic` | `Shared` | `Sequential` | ✅ | ✅ (State) | ❌ | ⚠️ 仅状态/存档,无音频播放 |
| `0x0046` | `setPartyPos` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0049` | `setState` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x004A` | `setBattlefield` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x004E` | `requestReload` | `Shared` | `Stop` | ✅ | ✅ | ❌ | ✅ Main.Story (PendingLostReload) |
| `0x004F` | `fadeToRed` | `Shared` | `Sequential` | ✅ | ✅ | ❌ | ✅ Main.Story (StoryRedPalette) |
| `0x0050` | `fadeOut` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Main.Dynamic/Main (PendingTransition) |
| `0x0052` | `hide` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x0058` | `itemShortageJump` | `Shared` | `ConditionalJump` | ✅ | ✅ (Inv) | ❌ | ✅ Core状态直通 |
| `0x006D` | `setSceneScripts` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0073` | `fadeScene` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Main.Dynamic/Main (PendingTransition) |
| `0x0075` | `setParty` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0078` | `noop` | `Shared` | `Sequential` | ✅ | ✅ | ❌ | ✅ Core状态直通 |
| `0x008F` | `halveCash` | `Shared` | `Sequential` | ✅ | ✅ (Cash) | ❌ | ✅ Core状态直通 |
| `0x0090` | `setObjectScript` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x009A` | `setStateRange` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0xFFFF` | `showDialog` | `BothSwitchesDiffer` | `MessageSpan` | ✅ | ✅ | ❌ | ✅ PalTriggerDialogue + PalDialogueDisplay |

## 4. 全量 Opcode 明细（165 条，按地址升序）

<details>
<summary>展开查看全部 165 条具名指令（含 111 条尚未接入运行时的指令）</summary>

| Opcode | Op | Executor | Successor | `Extract` | `Core:trigger` | `Core:auto` | `Pal.Game` |
|---|---|---|---|---|---|---|---|
| `0x0000` | `end` | `BothSwitches` | `Stop` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0001` | `end` | `BothSwitches` | `Stop` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0002` | `end` | `BothSwitches` | `ConditionalJump` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0003` | `goto` | `BothSwitches` | `ConditionalJump` | ✅ | ✅ | ✅ | ✅ Core状态直通 |
| `0x0004` | `call` | `BothSwitches` | `Sequential` | ✅ | ✅ | ❌ | ✅ Core状态直通 |
| `0x0005` | `redraw` | `TriggerSwitchOnly` | `Sequential` | ✅ | ✅ (World) | — | ✅ Main.Dynamic/Main (PendingTransition) |
| `0x0006` | `randomJump` | `BothSwitches` | `ConditionalJump` | ✅ | ✅ (BattleRng) | ❌ | ✅ Core状态直通 |
| `0x0007` | `startBattle` | `TriggerSwitchOnly` | `BattleResult` | ✅ | ✅ (Battle) | — | ✅ Main.Story + PalStoryBattleView |
| `0x0008` | `advanceEntry` | `TriggerSwitchOnly` | `Sequential` | ✅ | ✅ | — | ✅ Core状态直通 |
| `0x0009` | `waitUpdate` | `BothSwitchesDiffer` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Main.Dynamic/Main (AdvanceTriggerTick) |
| `0x000A` | `confirmNoJump` | `TriggerSwitchOnly` | `ConditionalJump` | ✅ | ❌ | — | ❌ |
| `0x000B` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000C` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000D` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000E` | `walkStep` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x000F` | `face` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x0010` | `walkTo` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Main.Dynamic/Main (AdvanceTriggerTick) |
| `0x0011` | `walkToSlow` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0012` | `setObjectPositionRelativeToParty` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0013` | `setObjectPosition` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0014` | `setObjectGesture` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0015` | `setPartyFacing` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0016` | `setObjectFacing` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0017` | `setExtraAttr` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0018` | `equipItem` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0019` | `incPlayerAttr` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x001A` | `setPlayerStat` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x001B` | `changeHp` | `Shared` | `Sequential` | ✅ | ⚠️ 仅RoleHook单体 | ❌ | ✅ Core状态直通 |
| `0x001C` | `changeMp` | `Shared` | `Sequential` | ✅ | ⚠️ 仅RoleHook单体 | ❌ | ✅ Core状态直通 |
| `0x001D` | `changeHpMp` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x001E` | `changeCash` | `Shared` | `ConditionalJump` | ✅ | ✅ (Cash) | ❌ | ✅ Core状态直通 |
| `0x001F` | `addItem` | `Shared` | `Sequential` | ✅ | ✅ (Inv) | ❌ | ✅ Core状态直通 |
| `0x0020` | `removeItem` | `Shared` | `ConditionalJump` | ✅ | ⚠️ 需完整Owner,非战中 | ❌ | ✅ Core状态直通 |
| `0x0021` | `inflictDamageToEnemy` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0022` | `revivePlayer` | `Shared` | `Sequential` | ✅ | ⚠️ 仅HP,无解毒 | ❌ | ✅ Core状态直通 |
| `0x0023` | `removeEquipment` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0024` | `setAutoEntry` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0025` | `setTriggerEntry` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0026` | `showBuyMenu` | `Shared` | `Sequential` | ✅ | ✅ (Shop) | ❌ | ✅ Main.Story + PalShopView |
| `0x0027` | `showSellMenu` | `Shared` | `Sequential` | ✅ | ✅ (Shop) | ❌ | ✅ Main.Story + PalShopView |
| `0x0028` | `applyPoisonToEnemy` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0029` | `applyPoisonToPlayer` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x002A` | `curePoisonByObjectIdForEnemy` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x002B` | `curePoisonByObjectIdForPlayer` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x002C` | `curePoisonsByLevel` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x002D` | `setStatusPlayer` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x002E` | `setStatusEnemy` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x002F` | `clearPlayerStatus` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0030` | `boostStat` | `Shared` | `Sequential` | ✅ | ⚠️ 仅RoleHook单体 | ❌ | ✅ Core状态直通 |
| `0x0031` | `changeBattleSprite` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0033` | `collectEnemyForItems` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0034` | `transformCollectedEnemies` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0035` | `shakeScreen` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0036` | `setPlayingRng` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0037` | `playRng` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0038` | `teleportParty` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0039` | `drainHpFromEnemy` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x003A` | `playerFlee` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x003B` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x003C` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x003D` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x003E` | `setDialog` | `TriggerSwitchOnly` | `Sequential` | ✅ | ⚠️ RngWord=0 | — | ✅ PalTriggerDialogue + PalDialogueDisplay |
| `0x003F` | `rideEventObjectSlow` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0040` | `setTriggerMode` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0041` | `markFailed` | `Shared` | `Sequential` | ✅ | ✅ | ❌ | ✅ Core状态直通 |
| `0x0042` | `simulateMagic` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0043` | `setMusic` | `Shared` | `Sequential` | ✅ | ✅ (State) | ❌ | ⚠️ 仅状态/存档,无音频播放 |
| `0x0044` | `rideEventObject` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0045` | `setBattleMusic` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0046` | `setPartyPos` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0047` | `playSound` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0049` | `setState` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x004A` | `setBattlefield` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x004B` | `nullifyObject` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x004C` | `chasePlayer` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x004D` | `waitForAnyKey` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x004E` | `requestReload` | `Shared` | `Stop` | ✅ | ✅ | ❌ | ✅ Main.Story (PendingLostReload) |
| `0x004F` | `fadeToRed` | `Shared` | `Sequential` | ✅ | ✅ | ❌ | ✅ Main.Story (StoryRedPalette) |
| `0x0050` | `fadeOut` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Main.Dynamic/Main (PendingTransition) |
| `0x0051` | `fadeIn` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0052` | `hide` | `Shared` | `Sequential` | ✅ | ✅ (World) | ✅ | ✅ Core状态直通 |
| `0x0053` | `useDayPalette` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0054` | `useNightPalette` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0055` | `addMagic` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0056` | `removeMagic` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0057` | `setMagicBaseDamageByMp` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0058` | `itemShortageJump` | `Shared` | `ConditionalJump` | ✅ | ✅ (Inv) | ❌ | ✅ Core状态直通 |
| `0x0059` | `changeScene` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x005A` | `halveHp` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x005B` | `halveEnemyHp` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x005C` | `setBattleHidingTime` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x005D` | `playerLacksPoisonJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x005E` | `enemyLacksPoisonJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x005F` | `killPlayer` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0060` | `killEnemy` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0061` | `notPoisonedJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0062` | `pauseEnemyChasing` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0063` | `speedUpEnemyChasing` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0064` | `enemyHpAboveJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0065` | `setPlayerSprite` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0066` | `throwWeapon` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0067` | `enemyUseMagic` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0068` | `enemyTurnJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0069` | `enemyEscape` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x006A` | `stealFromEnemy` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x006B` | `blowAwayEnemies` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x006C` | `walkNpcStep` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x006D` | `setSceneScripts` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x006E` | `moveParty` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x006F` | `syncState` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0070` | `partyWalkToSlow` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0071` | `waveScreen` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0073` | `fadeScene` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Main.Dynamic/Main (PendingTransition) |
| `0x0074` | `partyHpNotFullJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0075` | `setParty` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0076` | `showFbp` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0077` | `stopMusic` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0078` | `noop` | `Shared` | `Sequential` | ✅ | ✅ | ❌ | ✅ Core状态直通 |
| `0x0079` | `roleInPartyJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x007A` | `partyWalkTo` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x007B` | `partyWalkToFast` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x007C` | `walkToFast` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x007D` | `moveObject` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x007E` | `setObjectLayer` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x007F` | `moveViewport` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0080` | `toggleDayNightPalette` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0081` | `notFacingObjectJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0082` | `walkToFastest` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0083` | `objectOutOfZoneJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0084` | `placeUsedItem` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0085` | `delay` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0086` | `itemNotEquippedJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0087` | `animateObject` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0088` | `setMagicBaseDamageByCash` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0089` | `setBattleResult` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x008A` | `enableAutoBattle` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x008B` | `setPalette` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x008C` | `colorFade` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x008D` | `increaseLevel` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x008E` | `restoreScreen` | `TriggerSwitchOnly` | `Sequential` | ✅ | ❌ | — | ❌ |
| `0x008F` | `halveCash` | `Shared` | `Sequential` | ✅ | ✅ (Cash) | ❌ | ✅ Core状态直通 |
| `0x0090` | `setObjectScript` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x0091` | `notFirstOfKindJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0092` | `showMagicCastAnimation` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0093` | `sceneFade` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0094` | `stateMatchJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0095` | `sceneMatchJump` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x0096` | `showEndingAnimation` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0097` | `rideEventObjectFast` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0098` | `setFollower` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x0099` | `changeSceneMap` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x009A` | `setStateRange` | `Shared` | `Sequential` | ✅ | ✅ (World) | ❌ | ✅ Core状态直通 |
| `0x009B` | `fadeToScene` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x009C` | `enemyDivide` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x009E` | `enemySummon` | `Shared` | `ConditionalJump` | ✅ | ❌ | ❌ | ❌ |
| `0x009F` | `enemyTransform` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x00A0` | `quitGame` | `Shared` | `Stop` | ✅ | ❌ | ❌ | ❌ |
| `0x00A1` | `gatherParty` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x00A2` | `randomBranch` | `Shared` | `RandomBranch` | ✅ | ❌ | ❌ | ❌ |
| `0x00A3` | `playCdMusic` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x00A4` | `scrollFbp` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x00A5` | `showFbpWithEffectSprite` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x00A6` | `backupScreen` | `Shared` | `Sequential` | ✅ | ❌ | ❌ | ❌ |
| `0x00A7` | `advanceEntry` | `AutoSwitchOnly` | `Sequential` | ✅ | — | ❌ | ❌ |
| `0xFFFF` | `showDialog` | `BothSwitchesDiffer` | `MessageSpan` | ✅ | ✅ | ❌ | ✅ PalTriggerDialogue + PalDialogueDisplay |

</details>
