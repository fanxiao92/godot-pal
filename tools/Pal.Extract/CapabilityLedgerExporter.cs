using System.Text;
using Pal.Core.Events;

namespace Pal.Extract;

/// <summary>Code-derived SDLPal alignment and opcode capability ledger generator (Scheme A, #275).
/// Reads OpcodeRegistry.ByOpcode + TriggerScriptSession.SupportedOpcodes + DynamicEventSession.SupportedOpcodes
/// directly at compile time, paired with a strongly-typed Pal.Game host manifest verified against repo paths.</summary>
public static class CapabilityLedgerExporter
{
    public sealed record ModuleAlignmentRow(
        string SdlPalModule,
        string Responsibility,
        string ExtractTypes,
        string CoreTypes,
        string GameTypes,
        string StageStatus,
        string[] RepoPathsToVerify);

    public sealed record GameHostOverride(
        int Opcode,
        string StatusText,
        string[] RepoPathsToVerify);

    public static IReadOnlyList<GameHostOverride> GameHostOverrides { get; } =
    [
        new(0x0005, "✅ Main.Dynamic/Main (PendingTransition)", ["src/Pal.Game/Scripts/Main.Dynamic.cs", "src/Pal.Game/Scripts/Main.cs"]),
        new(0x0007, "✅ Main.Story + PalStoryBattleView", ["src/Pal.Game/Scripts/Main.Story.cs", "src/Pal.Game/Scripts/Battle/PalStoryBattleView.cs"]),
        new(0x0009, "✅ Main.Dynamic/Main (AdvanceTriggerTick)", ["src/Pal.Game/Scripts/Main.Dynamic.cs", "src/Pal.Game/Scripts/Main.cs"]),
        new(0x0010, "✅ Main.Dynamic/Main (AdvanceTriggerTick)", ["src/Pal.Game/Scripts/Main.Dynamic.cs", "src/Pal.Game/Scripts/Main.cs"]),
        new(0x0026, "✅ Main.Story + PalShopView", ["src/Pal.Game/Scripts/Main.Story.cs", "src/Pal.Game/Scripts/UI/PalShopView.cs"]),
        new(0x0027, "✅ Main.Story + PalShopView", ["src/Pal.Game/Scripts/Main.Story.cs", "src/Pal.Game/Scripts/UI/PalShopView.cs"]),
        new(0x003B, "✅ PalTriggerDialogue + PalDialogueDisplay", ["src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs", "src/Pal.Game/Scripts/Dialogue/PalDialogueDisplay.cs"]),
        new(0x003C, "✅ PalTriggerDialogue + PalDialogueDisplay", ["src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs", "src/Pal.Game/Scripts/Dialogue/PalDialogueDisplay.cs"]),
        new(0x003D, "✅ PalTriggerDialogue + PalDialogueDisplay", ["src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs", "src/Pal.Game/Scripts/Dialogue/PalDialogueDisplay.cs"]),
        new(0x003E, "✅ PalTriggerDialogue + PalDialogueDisplay", ["src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs", "src/Pal.Game/Scripts/Dialogue/PalDialogueDisplay.cs"]),
        new(0x0043, "⚠️ 仅状态/存档,无音频播放", ["src/Pal.Game/Scripts/Main.Story.cs"]),
        new(0x004E, "✅ Main.Story (PendingLostReload)", ["src/Pal.Game/Scripts/Main.Story.cs"]),
        new(0x004F, "✅ Main.Story (StoryRedPalette)", ["src/Pal.Game/Scripts/Main.Story.cs"]),
        new(0x0050, "✅ Main.Dynamic/Main (PendingTransition)", ["src/Pal.Game/Scripts/Main.Dynamic.cs", "src/Pal.Game/Scripts/Main.cs"]),
        new(0x0073, "✅ Main.Dynamic/Main (PendingTransition)", ["src/Pal.Game/Scripts/Main.Dynamic.cs", "src/Pal.Game/Scripts/Main.cs"]),
        new(0xFFFF, "✅ PalTriggerDialogue + PalDialogueDisplay", ["src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs", "src/Pal.Game/Scripts/Dialogue/PalDialogueDisplay.cs"])
    ];

    public static IReadOnlyList<ModuleAlignmentRow> ModuleAlignments { get; } =
    [
        new(
            "`script.c`",
            "字节码解释器 (trigger/auto)",
            "`OpcodeRegistry`, `EventArchive`",
            "`Events/TriggerScriptSession`, `Events/DynamicEventSession`, `Events/TriggerEventCollection`, `Events/TriggerNamedContentProfile`",
            "`Dialogue/PalTriggerDialogue`, `Main.Story`, `Main.Dynamic`",
            "{SCRIPT_COVERAGE}",
            [
                "tools/Pal.Extract/OpcodeRegistry.cs",
                "tools/Pal.Extract/EventArchive.cs",
                "src/Pal.Core/Events/TriggerScriptSession.cs",
                "src/Pal.Core/Events/DynamicEventSession.cs",
                "src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs",
                "src/Pal.Game/Scripts/Main.Story.cs",
                "src/Pal.Game/Scripts/Main.Dynamic.cs"
            ]),
        new(
            "`battle.c` / `fight.c`",
            "回合战斗、公式、AI、结算",
            "`BattleDataProjector`, `BattleContextAuthor`, `BattleImageExporter`",
            "`Battle/ClassicBattleSession`, `Battle/ClassicBattleLifecycleSession`, `Battle/ClassicBattleModels`, `Battle/BattleEquipmentState`, `State/PartyPoisonState`",
            "`Battle/PalStoryBattleView`, `Battle/StoryBattleNumbers`, `Battle/StoryBattlePoseClock`, `Battle/Story*AttackPresentation`",
            "✅ M4/M6 经典战斗闭环（局部扩展中）",
            [
                "tools/Pal.Extract/BattleDataProjector.cs",
                "src/Pal.Core/Battle/ClassicBattleSession.cs",
                "src/Pal.Core/Battle/ClassicBattleLifecycleSession.cs",
                "src/Pal.Core/State/PartyPoisonState.cs",
                "src/Pal.Game/Scripts/Battle/PalStoryBattleView.cs",
                "src/Pal.Game/Scripts/Battle/StoryBattleNumbers.cs"
            ]),
        new(
            "`scene.c` / `map.c`",
            "瓦片地图、精灵绘制、碰撞",
            "`MapExporter`, `SceneExporter`, `SceneBundleExporter`, `SpriteDecoder`",
            "`Exploration/StaticSceneRules`, `Exploration/SceneMetadata`, `Events/DynamicEventSession`",
            "`Resources/PalMapScene`, `Map/NativeSceneLoader`, `Map/NativeMapValidation`",
            "⚠️ 单场景渲染+碰撞（待全场景切换）",
            [
                "tools/Pal.Extract/MapExporter.cs",
                "src/Pal.Core/Exploration/StaticSceneRules.cs",
                "src/Pal.Core/Exploration/SceneMetadata.cs",
                "src/Pal.Game/Scripts/Resources/PalMapScene.cs",
                "src/Pal.Game/Scripts/Map/NativeSceneLoader.cs"
            ]),
        new(
            "`play.c`",
            "探索主循环、行走、NPC 交互",
            "—",
            "`Exploration/ExplorationSession`, `Exploration/ExplorationInput`, `Events/NpcInteractionSession`, `Events/DynamicEventSession`",
            "`Main`, `Main.Dynamic`, `Main.Story`",
            "⚠️ 探索+搜索触发（待全场景 touch/auto）",
            [
                "src/Pal.Core/Exploration/ExplorationSession.cs",
                "src/Pal.Core/Exploration/ExplorationInput.cs",
                "src/Pal.Core/Events/NpcInteractionSession.cs",
                "src/Pal.Game/Scripts/Main.cs"
            ]),
        new(
            "`game.c` / `global.c`",
            "全局状态、数据表、存档",
            "`DataGameData`, `DataTables`, `ObjectUnionTable`",
            "`State/RoleLiveState`, `State/InventoryLiveState`, `State/CashLiveState`, `Save/PlayerSaveContract`, `Save/PlayerSaveJson`, `Save/PlayerSaveRestorePlan`, `Save/PlayerSaveWorldRestorePlan`, `Save/ExplorationSaveGate`",
            "`PlayerSaveStorage`, `Main.Story`",
            "⚠️ 核心域+5槽存档（待正常开局/全场景）",
            [
                "tools/Pal.Extract/DataGameData.cs",
                "src/Pal.Core/State/RoleLiveState.cs",
                "src/Pal.Core/Save/PlayerSaveContract.cs",
                "src/Pal.Game/Scripts/PlayerSaveStorage.cs"
            ]),
        new(
            "`ui.c` / `uigame.c` / `itemmenu.c` / `magicmenu.c`",
            "探索菜单、状态、装备、物品、法术、商店",
            "`BallImageDecoder`, `DataTables`",
            "`Menu/ExplorationMenuSession`, `Menu/ExplorationShopSession`",
            "`UI/PalMenuView`, `UI/PalShopView`",
            "✅ 探索菜单/装备/物品/法术/商店/系统",
            [
                "src/Pal.Core/Menu/ExplorationMenuSession.cs",
                "src/Pal.Core/Menu/ExplorationShopSession.cs",
                "src/Pal.Game/Scripts/UI/PalMenuView.cs",
                "src/Pal.Game/Scripts/UI/PalShopView.cs"
            ]),
        new(
            "`text.c` / `font.c`",
            "对话框、控制符、点阵字库",
            "`DialogueLanguageExporter`, `DialogueFontExporter`, `DialogueImageExporter`",
            "`Dialogue/DialogueDisplaySession`, `Dialogue/DialogueTextTokenizer`, `Dialogue/DialogueLanguagePackage`",
            "`Dialogue/PalDialogueDisplay`, `Dialogue/PalDialoguePaletteSurface`",
            "✅ 中/英双语对话与点阵渲染",
            [
                "tools/Pal.Extract/DialogueLanguageExporter.cs",
                "src/Pal.Core/Dialogue/DialogueDisplaySession.cs",
                "src/Pal.Game/Scripts/Dialogue/PalDialogueDisplay.cs",
                "src/Pal.Game/Scripts/Dialogue/PalDialoguePaletteSurface.cs"
            ]),
        new(
            "`palette.c` / `video.c`",
            "调色板、淡入淡出、视口",
            "`PaletteDecoder`, `FbpImageDecoder`",
            "`Events/TriggerScriptSession` (`fadeToRed`/`fadeOut`/`fadeScene`)",
            "`Main.Story` (`StoryRedPalette`), `Dialogue/PalDialoguePaletteSurface`",
            "⚠️ 静态调色板+红淡/过场（待水火循环/震屏）",
            [
                "tools/Pal.Extract/PaletteDecoder.cs",
                "src/Pal.Game/Scripts/Main.Story.cs",
                "src/Pal.Game/Scripts/Dialogue/PalDialoguePaletteSurface.cs"
            ]),
        new(
            "`res.c` / `yj1.c`",
            "MKF 归档、YJ1/YJ2 解压",
            "`MkfArchive`, `Yj2Decoder`, `ArchiveInspector`",
            "—（墙后零 MKF）",
            "—（墙后零 MKF）",
            "✅ 离线提取全覆盖",
            [
                "tools/Pal.Extract/MkfArchive.cs",
                "tools/Pal.Extract/Yj2Decoder.cs",
                "tools/Pal.Extract/ArchiveInspector.cs"
            ]),
        new(
            "`input.c`",
            "键盘输入映射与防重入",
            "—",
            "`Exploration/ExplorationInput`",
            "`Main`",
            "✅ 方向/确认/取消/战斗快捷键",
            [
                "src/Pal.Core/Exploration/ExplorationInput.cs",
                "src/Pal.Game/Scripts/Main.cs"
            ]),
        new(
            "`audio.c` / `sound.c` / `rixplay.cpp`",
            "RIX 音乐、音效",
            "❌ 未实现",
            "`Events/TriggerScriptSession` (`TriggerMusicState` 仅记录字)",
            "❌ 无音频播放",
            "❌ 仅状态透传与存档",
            []),
        new(
            "`ending.c` / `aviplay.c` / `rngplay.c`",
            "RNG 过场动画、AVI、结局",
            "❌ 未实现",
            "❌ 未实现",
            "❌ 未实现",
            "❌ 未开工",
            [])
    ];

    public static string RenderMarkdown(string? repoRoot = null)
    {
        var descriptors = OpcodeRegistry.ByOpcode.Values
            .OrderBy(d => d.Opcode)
            .ToArray();
        var triggerSupport = TriggerScriptSession.SupportedOpcodes;
        var autoSupport = DynamicEventSession.SupportedOpcodes;
        var hostOverrides = GameHostOverrides.ToDictionary(o => o.Opcode);

        ValidateConsistency(descriptors, triggerSupport, autoSupport, hostOverrides, repoRoot);

        int total = descriptors.Length;
        int trigApplicable = descriptors.Count(d => d.Executor != OpcodeExecutor.AutoSwitchOnly);
        int autoApplicable = descriptors.Count(d => d.Executor != OpcodeExecutor.TriggerSwitchOnly);
        int trigFull = triggerSupport.Values.Count(c => c.Level == OpcodeSupportLevel.Full);
        int trigPartial = triggerSupport.Values.Count(c => c.Level == OpcodeSupportLevel.Partial);
        int autoFull = autoSupport.Values.Count(c => c.Level == OpcodeSupportLevel.Full);
        int autoPartial = autoSupport.Values.Count(c => c.Level == OpcodeSupportLevel.Partial);

        int trigMissing = trigApplicable - trigFull - trigPartial;
        double trigPct = Math.Round((trigFull + trigPartial) * 100.0 / trigApplicable, 1, MidpointRounding.AwayFromZero);
        int autoMissing = autoApplicable - autoFull - autoPartial;
        double autoPct = Math.Round((autoFull + autoPartial) * 100.0 / autoApplicable, 1, MidpointRounding.AwayFromZero);

        var activeDescriptors = descriptors
            .Where(d => triggerSupport.ContainsKey(d.Opcode) || autoSupport.ContainsKey(d.Opcode))
            .ToArray();
        int inactiveCount = total - activeDescriptors.Length;

        string scriptCoverageText = $"⚠️ 切片覆盖 ({trigFull + trigPartial}/{trigApplicable} trigger, {autoFull + autoPartial}/{autoApplicable} auto)";

        var sb = new StringBuilder(32 * 1024);
        void Line(string text = "")
        {
            sb.Append(text);
            sb.Append('\n');
        }

        Line("<!-- AUTO-GENERATED BY `pal-extract export-capability-ledger` — DO NOT EDIT BY HAND -->");
        Line("<!-- Regenerate: pwsh -NoProfile -NonInteractive -File ./pal.ps1 test check-ledger --update -->");
        Line();
        Line("# `sdlpal` 对位与能力账本");
        Line();
        Line("> **状态**：本文件由代码真源（`OpcodeRegistry.ByOpcode`、`TriggerScriptSession.SupportedOpcodes`、`DynamicEventSession.SupportedOpcodes` 与受验宿主清单）自动生成。由 `./pal.ps1 test check-ledger` 在 T1 CI 执行零漂移门禁校验，严禁手改。");
        Line();
        Line("## 1. 能力覆盖总览");
        Line();
        Line("| 维度 | 已完整支持 (`✅`) | 受限/部分 (`⚠️`) | 未实现 (`❌`) | 适用总数 | 覆盖率 |");
        Line("|---|---:|---:|---:|---:|---:|");
        Line($"| 离线提取转写 (`Pal.Extract`) | {total} | 0 | 0 | {total} | 100.0% |");
        Line(FormattableString.Invariant($"| `Pal.Core` `trigger` 执行器 | {trigFull} | {trigPartial} | {trigMissing} | {trigApplicable} | {trigPct:0.0}% |"));
        Line(FormattableString.Invariant($"| `Pal.Core` `auto` 执行器 | {autoFull} | {autoPartial} | {autoMissing} | {autoApplicable} | {autoPct:0.0}% |"));
        Line();
        Line("图例：`✅` 已实现 · `⚠️` 条件受限/部分实现（括号注明边界） · `❌` 未实现 · `—` 原版该执行器不适用（`TriggerSwitchOnly`/`AutoSwitchOnly`）。");
        Line();
        Line("## 2. `reference/sdlpal` 核心模块对位索引");
        Line();
        Line("| `sdlpal` 模块 | 职责 | `Pal.Extract` (离线) | `Pal.Core` (纯逻辑) | `Pal.Game` (宿主/表现) | 阶段状态 |");
        Line("|---|---|---|---|---|---|");
        foreach (var row in ModuleAlignments)
        {
            string status = row.StageStatus == "{SCRIPT_COVERAGE}" ? scriptCoverageText : row.StageStatus;
            Line($"| {row.SdlPalModule} | {row.Responsibility} | {row.ExtractTypes} | {row.CoreTypes} | {row.GameTypes} | {status} |");
        }
        Line();
        Line($"## 3. 已接入运行时 Opcode 速览（{activeDescriptors.Length} 条）");
        Line();
        Line($"> Agent 规划运行时切片时先扫本节；未列出的 {inactiveCount} 条具名指令均处于「离线已转写、运行时未实现（`❌`）」，明细见第 4 节。");
        Line();
        Line("| Opcode | Op | Executor | Successor | `Pal.Extract` | `Core:trigger` | `Core:auto` | `Pal.Game` 宿主 |");
        Line("|---|---|---|---|---|---|---|---|");
        foreach (var d in activeDescriptors)
        {
            FormatRow(sb, d, triggerSupport, autoSupport, hostOverrides);
        }
        Line();
        Line($"## 4. 全量 Opcode 明细（{total} 条，按地址升序）");
        Line();
        Line("<details>");
        Line($"<summary>展开查看全部 {total} 条具名指令（含 {inactiveCount} 条尚未接入运行时的指令）</summary>");
        Line();
        Line("| Opcode | Op | Executor | Successor | `Extract` | `Core:trigger` | `Core:auto` | `Pal.Game` |");
        Line("|---|---|---|---|---|---|---|---|");
        foreach (var d in descriptors)
        {
            FormatRow(sb, d, triggerSupport, autoSupport, hostOverrides);
        }
        Line();
        Line("</details>");

        return sb.ToString();
    }

    public static void Export(string outputPath, string? repoRoot = null)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));
        string markdown = RenderMarkdown(repoRoot);
        string fullOut = Path.GetFullPath(outputPath);
        string? dir = Path.GetDirectoryName(fullOut);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(fullOut, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void FormatRow(
        StringBuilder sb,
        OpcodeDescriptor d,
        IReadOnlyDictionary<int, OpcodeCapability> triggerSupport,
        IReadOnlyDictionary<int, OpcodeCapability> autoSupport,
        IReadOnlyDictionary<int, GameHostOverride> hostOverrides)
    {
        bool hasTrig = triggerSupport.TryGetValue(d.Opcode, out var trigCap);
        bool hasAuto = autoSupport.TryGetValue(d.Opcode, out var autoCap);
        string trigCol = d.Executor == OpcodeExecutor.AutoSwitchOnly ? "—"
            : hasTrig ? trigCap!.FormatCell() : "❌";
        string autoCol = d.Executor == OpcodeExecutor.TriggerSwitchOnly ? "—"
            : hasAuto ? autoCap!.FormatCell() : "❌";
        string gameCol = hostOverrides.TryGetValue(d.Opcode, out var host) ? host.StatusText
            : (hasTrig || hasAuto) ? "✅ Core状态直通" : "❌";
        sb.Append($"| `0x{d.Opcode:X4}` | `{d.Op}` | `{d.Executor}` | `{d.Successor}` | ✅ | {trigCol} | {autoCol} | {gameCol} |\n");
    }

    private static void ValidateConsistency(
        OpcodeDescriptor[] descriptors,
        IReadOnlyDictionary<int, OpcodeCapability> triggerSupport,
        IReadOnlyDictionary<int, OpcodeCapability> autoSupport,
        IReadOnlyDictionary<int, GameHostOverride> hostOverrides,
        string? repoRoot)
    {
        var byCode = descriptors.ToDictionary(d => d.Opcode);

        foreach (var (code, cap) in triggerSupport)
        {
            if (!byCode.TryGetValue(code, out var desc))
                throw new InvalidDataException($"TriggerScriptSession.SupportedOpcodes contains unknown opcode 0x{code:X4}.");
            if (!string.Equals(desc.Op, cap.Verb, StringComparison.Ordinal))
                throw new InvalidDataException($"TriggerScriptSession.SupportedOpcodes 0x{code:X4} verb '{cap.Verb}' differs from OpcodeRegistry '{desc.Op}'.");
            if (desc.Executor == OpcodeExecutor.AutoSwitchOnly)
                throw new InvalidDataException($"TriggerScriptSession.SupportedOpcodes 0x{code:X4} is marked AutoSwitchOnly in OpcodeRegistry.");
        }

        foreach (var (code, cap) in autoSupport)
        {
            if (!byCode.TryGetValue(code, out var desc))
                throw new InvalidDataException($"DynamicEventSession.SupportedOpcodes contains unknown opcode 0x{code:X4}.");
            if (!string.Equals(desc.Op, cap.Verb, StringComparison.Ordinal))
                throw new InvalidDataException($"DynamicEventSession.SupportedOpcodes 0x{code:X4} verb '{cap.Verb}' differs from OpcodeRegistry '{desc.Op}'.");
            if (desc.Executor == OpcodeExecutor.TriggerSwitchOnly)
                throw new InvalidDataException($"DynamicEventSession.SupportedOpcodes 0x{code:X4} is marked TriggerSwitchOnly in OpcodeRegistry.");
        }

        foreach (var (code, host) in hostOverrides)
        {
            if (!byCode.ContainsKey(code))
                throw new InvalidDataException($"GameHostOverrides contains unknown opcode 0x{code:X4}.");
            if (!triggerSupport.ContainsKey(code) && !autoSupport.ContainsKey(code))
                throw new InvalidDataException($"GameHostOverrides 0x{code:X4} is not supported by either trigger or auto executor.");
        }

        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            string fullRoot = Path.GetFullPath(repoRoot);
            foreach (var host in GameHostOverrides)
            {
                foreach (var relPath in host.RepoPathsToVerify)
                {
                    string candidate = Path.Combine(fullRoot, relPath);
                    if (!File.Exists(candidate))
                        throw new InvalidDataException($"Capability ledger host override 0x{host.Opcode:X4} references missing repository path: {relPath}");
                }
            }
            foreach (var row in ModuleAlignments)
            {
                foreach (var relPath in row.RepoPathsToVerify)
                {
                    string candidate = Path.Combine(fullRoot, relPath);
                    if (!File.Exists(candidate))
                        throw new InvalidDataException($"Capability ledger alignment row '{row.SdlPalModule}' references missing repository path: {relPath}");
                }
            }
        }
    }
}
