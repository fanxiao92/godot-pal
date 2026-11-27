param(
    [string]$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')),
    [string]$OutPath = (Join-Path $PSScriptRoot 'capabilities-ledger-prototype.md')
)

$ErrorActionPreference = 'Stop'

# 1. Read real OpcodeRegistry.cs (165 descriptors)
$regText = [IO.File]::ReadAllText((Join-Path $RepoRoot 'tools/Pal.Extract/OpcodeRegistry.cs'), [Text.Encoding]::UTF8)
$matches = [regex]::Matches($regText, 'new\((0x[0-9A-Fa-f]{4}),\s*"([^"]+)",.*?OpcodeExecutor\.(\w+),\s*OpcodeSuccessor\.(\w+)', [Text.RegularExpressions.RegexOptions]::Singleline)

$descriptors = @()
foreach ($m in $matches) {
    $hex = $m.Groups[1].Value.ToUpperInvariant().Replace('0X', '0x')
    $descriptors += [pscustomobject]@{
        Hex       = $hex
        Code      = [Convert]::ToInt32($hex, 16)
        Op        = $m.Groups[2].Value
        Executor  = $m.Groups[3].Value
        Successor = $m.Groups[4].Value
    }
}
$descriptors = $descriptors | Sort-Object Code

# 2. Prototype representation of TriggerScriptSession.SupportedOpcodes (derived from real Continue() in TriggerScriptSession.cs)
$triggerSupport = @{
    0x0000 = '✅'
    0x0001 = '✅'
    0x0002 = '✅'
    0x0003 = '✅'
    0x0004 = '✅'
    0x0005 = '✅ (World)'
    0x0006 = '✅ (BattleRng)'
    0x0007 = '✅ (Battle)'
    0x0008 = '✅'
    0x0009 = '✅ (World)'
    0x000B = '✅ (World)'
    0x000C = '✅ (World)'
    0x000D = '✅ (World)'
    0x000E = '✅ (World)'
    0x000F = '✅ (World)'
    0x0010 = '✅ (World)'
    0x0013 = '✅ (World)'
    0x0014 = '✅ (World)'
    0x0015 = '✅ (World)'
    0x001B = '⚠️ 仅RoleHook单体'
    0x001C = '⚠️ 仅RoleHook单体'
    0x001D = '✅ (World)'
    0x001E = '✅ (Cash)'
    0x001F = '✅ (Inv)'
    0x0020 = '⚠️ 需完整Owner,非战中'
    0x0022 = '⚠️ 仅HP,无解毒'
    0x0024 = '✅ (World)'
    0x0025 = '✅ (World)'
    0x0026 = '✅ (Shop)'
    0x0027 = '✅ (Shop)'
    0x0030 = '⚠️ 仅RoleHook单体'
    0x003B = '⚠️ RngWord=0'
    0x003C = '⚠️ RngWord=0'
    0x003D = '⚠️ RngWord=0'
    0x003E = '⚠️ RngWord=0'
    0x0040 = '✅ (World)'
    0x0041 = '✅'
    0x0043 = '✅ (State)'
    0x0046 = '✅ (World)'
    0x0049 = '✅ (World)'
    0x004A = '✅ (World)'
    0x004E = '✅'
    0x004F = '✅'
    0x0050 = '✅ (World)'
    0x0052 = '✅ (World)'
    0x0058 = '✅ (Inv)'
    0x006D = '✅ (World)'
    0x0073 = '✅ (World)'
    0x0075 = '✅ (World)'
    0x0078 = '✅'
    0x008F = '✅ (Cash)'
    0x0090 = '✅ (World)'
    0x009A = '✅ (World)'
    0xFFFF = '✅'
}

# 3. Prototype representation of DynamicEventSession.SupportedOpcodes (derived from real RunAutoOneTick() in DynamicEventSession.cs)
$autoSupport = @{
    0x0000 = '✅'
    0x0001 = '✅'
    0x0002 = '✅'
    0x0003 = '✅'
    0x0009 = '✅'
    0x000B = '✅'
    0x000C = '✅'
    0x000D = '✅'
    0x000E = '✅'
    0x000F = '✅'
    0x0010 = '✅'
    0x0049 = '✅'
    0x0052 = '✅'
}

# 4. Prototype representation of Pal.Game host integration
$gameHostOverride = @{
    0x0005 = '✅ Main.Dynamic/Main (PendingTransition)'
    0x0007 = '✅ Main.Story + PalStoryBattleView'
    0x0009 = '✅ Main.Dynamic/Main (AdvanceTriggerTick)'
    0x0010 = '✅ Main.Dynamic/Main (AdvanceTriggerTick)'
    0x0026 = '✅ Main.Story + PalShopView'
    0x0027 = '✅ Main.Story + PalShopView'
    0x003B = '✅ PalTriggerDialogue + PalDialogueDisplay'
    0x003C = '✅ PalTriggerDialogue + PalDialogueDisplay'
    0x003D = '✅ PalTriggerDialogue + PalDialogueDisplay'
    0x003E = '✅ PalTriggerDialogue + PalDialogueDisplay'
    0x0043 = '⚠️ 仅状态/存档,无音频播放'
    0x004E = '✅ Main.Story (PendingLostReload)'
    0x004F = '✅ Main.Story (StoryRedPalette)'
    0x0050 = '✅ Main.Dynamic/Main (PendingTransition)'
    0x0073 = '✅ Main.Dynamic/Main (PendingTransition)'
    0xFFFF = '✅ PalTriggerDialogue + PalDialogueDisplay'
}

# Counts
$total = $descriptors.Count
$trigApplicable = ($descriptors | Where-Object { $_.Executor -ne 'AutoSwitchOnly' }).Count
$autoApplicable = ($descriptors | Where-Object { $_.Executor -ne 'TriggerSwitchOnly' }).Count
$trigFull = ($triggerSupport.Values | Where-Object { $_.StartsWith('✅') }).Count
$trigPartial = ($triggerSupport.Values | Where-Object { $_.StartsWith('⚠️') }).Count
$autoFull = ($autoSupport.Values | Where-Object { $_.StartsWith('✅') }).Count

$sb = [Text.StringBuilder]::new()
[void]$sb.AppendLine('<!-- AUTO-GENERATED PROTOTYPE (Ticket #267) — DO NOT EDIT BY HAND -->')
[void]$sb.AppendLine('<!-- Source: OpcodeRegistry.cs + TriggerScriptSession + DynamicEventSession + Pal.Game host registry -->')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('# `sdlpal` 对位与能力账本（原型样本）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> **状态**：本文件由代码真源自动生成（当前为 `#267` 原型，基线 `57434ac`）。正式落地后由 `./pal.ps1 test check-ledger` 在 T1 CI 执行零漂移门禁校验，严禁手改。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 1. 能力覆盖总览')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 维度 | 已完整支持 (`✅`) | 受限/部分 (`⚠️`) | 未实现 (`❌`) | 适用总数 | 覆盖率 |')
[void]$sb.AppendLine('|---|---:|---:|---:|---:|---:|')
[void]$sb.AppendLine('| 离线提取转写 (`Pal.Extract`) | ' + "$total | 0 | 0 | $total | 100.0% |")
$trigMissing = $trigApplicable - $trigFull - $trigPartial
$trigPct = [Math]::Round((($trigFull + $trigPartial) / $trigApplicable) * 100, 1)
[void]$sb.AppendLine('| `Pal.Core` `trigger` 执行器 | ' + "$trigFull | $trigPartial | $trigMissing | $trigApplicable | $trigPct% |")
$autoMissing = $autoApplicable - $autoFull
$autoPct = [Math]::Round(($autoFull / $autoApplicable) * 100, 1)
[void]$sb.AppendLine('| `Pal.Core` `auto` 执行器 | ' + "$autoFull | 0 | $autoMissing | $autoApplicable | $autoPct% |")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('图例：`✅` 已实现 · `⚠️` 条件受限/部分实现（括号注明边界） · `❌` 未实现 · `—` 原版该执行器不适用（`TriggerSwitchOnly`/`AutoSwitchOnly`）。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 2. `reference/sdlpal` 核心模块对位索引')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| `sdlpal` 模块 | 职责 | `Pal.Extract` (离线) | `Pal.Core` (纯逻辑) | `Pal.Game` (宿主/表现) | 阶段状态 |')
[void]$sb.AppendLine('|---|---|---|---|---|---|')
[void]$sb.AppendLine('| `script.c` | 字节码解释器 (trigger/auto) | `OpcodeRegistry`, `EventArchive` | `TriggerScriptSession`, `DynamicEventSession`, `TriggerEventCollection`, `TriggerNamedContentProfile` | `PalTriggerDialogue`, `Main.Story`, `Main.Dynamic` | ' + "⚠️ 切片覆盖 ($($trigFull + $trigPartial)/$trigApplicable trigger, $autoFull/$autoApplicable auto) |")
[void]$sb.AppendLine('| `battle.c` / `fight.c` | 回合战斗、公式、AI、结算 | `BattleDataProjector`, `BattleContextAuthor`, `BattleImageExporter` | `ClassicBattleSession`, `ClassicBattleLifecycleSession`, `ClassicBattleModels`, `BattleEquipmentState`, `PartyPoisonState` | `PalStoryBattleView`, `StoryBattleNumbers`, `StoryBattlePoseClock`, `Story*AttackPresentation` | ✅ M4/M6 经典战斗闭环（局部扩展中） |')
[void]$sb.AppendLine('| `scene.c` / `map.c` | 瓦片地图、精灵绘制、碰撞 | `MapExporter`, `SceneExporter`, `SceneBundleExporter`, `SpriteDecoder` | `StaticSceneRules`, `SceneMetadata`, `DynamicEventWorld` | `PalMapScene`, `NativeSceneLoader`, `NativeMapValidation` | ⚠️ 单场景渲染+碰撞（待全场景切换） |')
[void]$sb.AppendLine('| `play.c` | 探索主循环、行走、NPC 交互 | — | `ExplorationSession`, `ExplorationInput`, `NpcInteractionSession`, `DynamicEventSession` | `Main`, `Main.Dynamic`, `Main.Story` | ⚠️ 探索+搜索触发（待全场景 touch/auto） |')
[void]$sb.AppendLine('| `game.c` / `global.c` | 全局状态、数据表、存档 | `DataGameData`, `DataTables`, `ObjectUnionTable` | `RoleLiveState`, `InventoryLiveState`, `CashLiveState`, `PlayerSaveContract`, `PlayerSaveJson`, `PlayerSaveRestorePlan`, `PlayerSaveWorldRestorePlan`, `ExplorationSaveGate` | `PlayerSaveStorage`, `Main.Story` | ⚠️ 核心域+5槽存档（待正常开局/全场景） |')
[void]$sb.AppendLine('| `ui.c` / `uigame.c` / `itemmenu.c` / `magicmenu.c` | 探索菜单、状态、装备、物品、法术、商店 | `BallImageDecoder`, `DataTables` | `ExplorationMenuSession`, `ExplorationShopSession` | `PalMenuView`, `PalShopView` | ✅ 探索菜单/装备/物品/法术/商店/系统 |')
[void]$sb.AppendLine('| `text.c` / `font.c` | 对话框、控制符、点阵字库 | `DialogueLanguageExporter`, `DialogueFontExporter`, `DialogueImageExporter` | `DialogueDisplaySession`, `DialogueTextTokenizer`, `DialogueLanguagePackage` | `PalDialogueDisplay`, `PalDialoguePaletteSurface` | ✅ 中/英双语对话与点阵渲染 |')
[void]$sb.AppendLine('| `palette.c` / `video.c` | 调色板、淡入淡出、视口 | `PaletteDecoder`, `FbpImageDecoder` | `TriggerScriptSession` (`fadeToRed`/`fadeOut`/`fadeScene`) | `Main.Story` (`StoryRedPalette`), `PalDialoguePaletteSurface` | ⚠️ 静态调色板+红淡/过场（待水火循环/震屏） |')
[void]$sb.AppendLine('| `res.c` / `yj1.c` | MKF 归档、YJ1/YJ2 解压 | `MkfArchive`, `Yj2Decoder`, `ArchiveInspector` | —（墙后零 MKF） | —（墙后零 MKF） | ✅ 离线提取全覆盖 |')
[void]$sb.AppendLine('| `input.c` | 键盘输入映射与防重入 | — | `ExplorationInput` | `Main` | ✅ 方向/确认/取消/战斗快捷键 |')
[void]$sb.AppendLine('| `audio.c` / `sound.c` / `rixplay.cpp` | RIX 音乐、音效 | ❌ 未实现 | `TriggerMusicState`（仅记录字） | ❌ 无音频播放 | ❌ 仅状态透传与存档 |')
[void]$sb.AppendLine('| `ending.c` / `aviplay.c` / `rngplay.c` | RNG 过场动画、AVI、结局 | ❌ 未实现 | ❌ 未实现 | ❌ 未实现 | ❌ 未开工 |')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 3. 已接入运行时 Opcode 速览（54 条）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> Agent 规划运行时切片时先扫本节；未列出的 111 条具名指令均处于「离线已转写、运行时未实现（`❌`）」，明细见第 4 节。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| Opcode | Op | Executor | Successor | `Pal.Extract` | `Core:trigger` | `Core:auto` | `Pal.Game` 宿主 |')
[void]$sb.AppendLine('|---|---|---|---|---|---|---|---|')

foreach ($d in $descriptors) {
    $hasTrig = $triggerSupport.ContainsKey($d.Code)
    $hasAuto = $autoSupport.ContainsKey($d.Code)
    if (-not ($hasTrig -or $hasAuto)) { continue }
    $trigCol = if ($d.Executor -eq 'AutoSwitchOnly') { '—' } elseif ($hasTrig) { $triggerSupport[$d.Code] } else { '❌' }
    $autoCol = if ($d.Executor -eq 'TriggerSwitchOnly') { '—' } elseif ($hasAuto) { $autoSupport[$d.Code] } else { '❌' }
    $gameCol = if ($gameHostOverride.ContainsKey($d.Code)) { $gameHostOverride[$d.Code] } else { '✅ Core状态直通' }
    [void]$sb.AppendLine("| ``$($d.Hex)`` | ``$($d.Op)`` | ``$($d.Executor)`` | ``$($d.Successor)`` | ✅ | $trigCol | $autoCol | $gameCol |")
}

[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 4. 全量 Opcode 明细（165 条，按地址升序）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('<details>')
[void]$sb.AppendLine('<summary>展开查看全部 165 条具名指令（含 111 条尚未接入运行时的指令）</summary>')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| Opcode | Op | Executor | Successor | `Extract` | `Core:trigger` | `Core:auto` | `Pal.Game` |')
[void]$sb.AppendLine('|---|---|---|---|---|---|---|---|')

foreach ($d in $descriptors) {
    $hasTrig = $triggerSupport.ContainsKey($d.Code)
    $hasAuto = $autoSupport.ContainsKey($d.Code)
    $trigCol = if ($d.Executor -eq 'AutoSwitchOnly') { '—' } elseif ($hasTrig) { $triggerSupport[$d.Code] } else { '❌' }
    $autoCol = if ($d.Executor -eq 'TriggerSwitchOnly') { '—' } elseif ($hasAuto) { $autoSupport[$d.Code] } else { '❌' }
    $gameCol = if ($gameHostOverride.ContainsKey($d.Code)) {
        $gameHostOverride[$d.Code]
    } elseif ($hasTrig -or $hasAuto) {
        '✅ Core状态直通'
    } else {
        '❌'
    }
    [void]$sb.AppendLine("| ``$($d.Hex)`` | ``$($d.Op)`` | ``$($d.Executor)`` | ``$($d.Successor)`` | ✅ | $trigCol | $autoCol | $gameCol |")
}

[void]$sb.AppendLine('')
[void]$sb.AppendLine('</details>')

[IO.File]::WriteAllText($OutPath, $sb.ToString(), [Text.UTF8Encoding]::new($false))
Write-Output "Generated prototype ledger: $OutPath (lines: $($sb.ToString().Split("`n").Count), opcodes: $total)"
