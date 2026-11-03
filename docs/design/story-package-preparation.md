# Story package 离线准备（#46 checkpoint A）

这是正常 Editor/PowerShell 的**生产准备路径**，不是运行时准备或测试业务装配。它组合完整现代 faithful 事件与**已显式准备**的原生静态、数值、双语/字形/画像和 battle visual leaves。输入**非自包含**；不会复制、自动提取、导入或同步这些 native dependencies。完整可提交、可见的自有人工作者内容包已由 [checkpoint B](../../samples/authored-battle/README.md) 提供；它与测试的小图/空字形数据区分，尚不包含正常 Main 接线或桌面验收。

## 公共入口与输出

```csharp
(string StoryUri, string VisualUri) PrepareStoryPackage.Prepare(
    string inputDirectory, string outputDirectory);
```

类型在 `Pal.Game.Editor`。返回正常 `NativeStoryProfile.Load(StoryUri, "zh-Hans" | "en")` 和 `NativeBattleVisualProfile.Load(VisualUri, checkedStory)` 可消费的两个 URI。不是新 Core factory，不调用 Start、创建 trigger/battle/RNG 或模拟 Won；不改 Core/VM、冻结真实 `first-mandatory-battle-pre-trigger-v1`、owner28/@09C3 gate。

```powershell
# 在仓库根目录；依赖必须事先正式准备/author 并核对。
./scripts/Prepare-StoryPackage.ps1 `
  -InputPath C:/outside-project/story-input `
  -OutputPath src/Pal.Game/Assets/MyStory `
  -GodotPath C:/path/to/godot-mono.console.exe
```

wrapper 必需且仅一次 `-InputPath/-OutputPath`，可选 `-GodotPath/-TimeoutSeconds`（沿既有 wrapper 60秒默认、1..600显式范围）；参数错误 exit2，准备失败 exit1，成功 exit0。它调用 shipped `res://Editor/PrepareStoryPackage.tscn`；真实 `_Ready` **精确四个、按顺序** userargs：`--story-input <physical-directory> --story-output <physical-directory>`。直接 API、Editor、wrapper 走同一转换函数。wrapper 核工具版本、build 与 marker；API 自己实现安全/转换/严格回读，不依赖 PS 才安全。

```
外部 input/                       新/空 Assets output/
  story-package.json               Events/index.json
  events/index.json                Events/<所有index列出fileId>.json
  events/<所有fileId>.json           Visual.tres
                                  Story.tres
```

输出事件按输入原始 bytes 完整复制，不裁剪未选文件、raw、零地址或 Won/Lost/flee 分支。事件格式沿现有 `TriggerEventCollection.Parse`；输出再由正常 Story loader/TriggerEventLoader 严格读全套。不是提取器、事件语义重编码器或另一版 JSON→Resource→JSON pipeline。

## 新 metadata v1 schema

根必须**恰好**以下字段；所有对象未知/缺失/重复字段均拒绝，JSON 不接受注释/尾逗号。字符串非空，整数必须 int32；位置是长度2的 SHORT 整数数组。下例 URI 是布局示意，须替换成实际正式 prepared/native 作者资源，不能从清洁检出直接运行：

```json
{
  "formatVersion": 1,
  "sourceIdentity": "author declaration, not authentication",
  "staticResources": "res://Assets/MyStatic",
  "staticProfile": "res://Assets/MyStatic/Profiles/Exploration.tres",
  "bundleSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "entryFileId": "story",
  "entryId": "npc",
  "ownerId": 1,
  "entryAddress": 1,
  "battleContentUri": "res://Assets/MyNumbers/BattleContent.tres",
  "dialogueDirectory": "res://Assets/MyDialogue",
  "portraits": [{ "id": 7, "uri": "res://Assets/MyDialogue/Portrait-7.tres" }],
  "visual": {
    "roleId": 0,
    "playerSpriteUri": "res://Assets/MyVisual/Player.tres",
    "playerPosition": [240, 170],
    "paletteUri": "res://Assets/MyStatic/Palettes/Palette.tres",
    "teams": [{
      "teamId": 18, "targetId": 7,
      "backgroundUri": "res://Assets/MyVisual/Background.tres",
      "enemySpriteUri": "res://Assets/MyVisual/Enemy.tres",
      "enemyPosition": [100, 120],
      "idleFrames": 1, "magicFrames": 1, "attackFrames": 1, "actWaitFrames": 0
    }]
  }
}
```

`portraits` 可为空；非零 WORD id 唯一，实际精确集合由 Story 所选闭包严格验证。`visual.teams` 精确匹配 requirements，team/target/role、WORD metadata、完整 nominal frames 与 aliases、palette 一致性由既有 Visual loader 校验，不再复制 validator。writer 显式写 typed roots 的 `FormatVersion=1`。`EventsIndex` 与两个 `ResourceSha256` 不由 metadata 作者填写：生产函数确定输出路径，并通过共享 `NativeContentDependencies` 计算各自**精确** data graph 和每条文件 SHA-256。Story 的 hash 另包含完整现代事件集合。

静态场景实际 owner 的 `wTriggerScript` 必须指向匹配的具名 entry；profile/bundle/实际 scene 身份必须一致，初始 role0 限制不变。两语内容、画像与真实 font/prompts 均走正常 Story 检查。仅格式合法的未选 numeric 内容不因此获可执行能力授权。

## 前置资产的明确来源

- 静态：已有已核对 native 场景优先复用；需要首次准备时，正常 `Prepare-GameScene.ps1 -InputPath <显式M1离线包> -OutputPath <Assets静态根> -GodotPath <engine>` 产出 `Scenes/Scene.tscn` 与 native map/sprites/palette/textures。它**不创建**探索 profile，`PalExplorationProfile` 必须另行显式 author，遵循既有 DATA3@768/role0 和实际 bundle 契约。
- 数值：正常 `Prepare-BattleData.ps1 -InputPath <仅battle-data.json外部目录> -OutputPath <Assets数值根> -GodotPath <engine>` 产出 `BattleContent.tres`；不重写现代数值转换器。
- 语言：可复用正常 `Prepare-DialoguePackage.ps1 -InputPath <现有现代对话包> -OutputPath <Assets语言根> -GodotPath <engine>` 的 `Language.zh-Hans/en.tres`、`Font.tres`、三个 prompts 和画像。该既有 schema 有固定原档来源约束，**不修改它冒充通用人工转换器**。现代作者也可直接提供这些正式 native Resource 类型，完整布局/消息/可见字形必须满足 Story 契约；不使用系统字体 fallback。
- 视觉：显式 author 正式 `PalSprite`/`PalFrame`、`PalPalette`、`PalDialogueIndexedImage` native leaves。player/enemy indices+alpha 可直接消费，不必另造图像 DTO；RGB palette 必须与静态当前 palette 一致。外部 texture source 必须已有正式 `.import`，本准备函数不触发 runtime/import 自动准备。

所有这些 native 依赖仍留在原位置。规范 `res://Tests/...` 等项目 data 路径也允许（不误套 #45 仅 Assets 的 helper）；只有输出必须位于 `res://Assets`。B 已提供可提交、可复现、可见人工资产与实际准备步骤；这里不把测试构造器当作完整作者包。

## 安全、发布与所有权

- 外部物理 input，Assets 新/空 output；平台正确路径比较、双向不重叠、完整祖先/输入树/输出树无 reparse。native 根/所有 seeds 与实际 graph 文件也核规范项目 URI、物理项目内边界、存在及祖先无 reparse；不把整个路径 lowercase，不依赖 Windows 上大小写敏感的 LocalizePath。
- 本新包格式每输入文件 **1..16MiB**、总 **64MiB**、最多 **1024 event files**（另 metadata/index 共2）；只允许一个 `events` 子目录。严格 UTF-8（不接受 UTF-16/非法 bytes；JSON 前不加 BOM）。这是新包大小约束，未改变旧 #45 的空输入子目录或任何既有工具运行预算。
- 所有实际消费输入与 native data graph（含 texture source 和 `.import` sidecar）持稳定 read lease/hash，写前、严格加载后、发布前后复核 bytes/完整事件文件集和 dependency graph。hash 不认证 scripts、engine、`.godot` import output、cache一致性、版权、作者真实性或 GPU。
- output 内唯一 owned staging；完整 Events 子目录 no-overwrite move 到最终位置，typed temporary profiles 引用最终 Events，正常 Save→严格 Load；Visual 无覆盖发布在前，Story 作为约定完成入口最后发布，final strict Load 和 source hash 复核全部成功才返回 URI pair/打印 `PAL_PREPARE_STORY_PACKAGE:<StoryUri>;<VisualUri>`。
- **不是多文件事务或整个目录原子发布**。并发观察者必须等准备成功再消费；失败/晚期 I/O 不用成功 marker掩盖。caller 已有空 output 保留。失败先释放 leases，只删除本轮记录且当前 hash 相符的 owned 文件及新建空目录；不递归删除 output/staging，不删第三方新增或内容变化文件。没有全局 filesystem lock/sandbox、文件身份认证或“覆盖全部late-race cleanup”保证。

## 本轮证据与剩余边界

`PrepareStoryPackageTests` direct31 覆盖完整 typed准备/严格读取、完整事件/hash、双语 glyph/owner/visual拒绝后清理、UTF8/schema/大小/文件数、caller目录/无覆盖、规范大小写/缺祖先、input/events/output/native junction拒绝。Prepare process10 覆盖正常 wrapper+真实 Editor发布、argv exit/marker和严格 metadata诊断；它们是离线契约，不是 Main 交互/GPU验收。

本 checkpoint A 不接 Main/input/HUD、不推进动作/动画、不绑定 attack effect；后续B交付可见人工数据但尚不签桌面验收、真实Main战斗往返或 #47+。#46 和 M4 仍未完成；冻结真实内容由 #53 完整接入，不能用人工资产替代。


## #46 checkpoint B: original authored sample

完整可提交人工作者 native 静态/双语非空bitmap字形/索引视觉资源及 Win/Loss 现代输入已提供，见 [正常准备命令、来源、搜索和数值设计](../../samples/authored-battle/README.md)。正常 numeric/story wrappers 分别准备两种场景；两个精确 Game 数据用例核双语 strict Load、真实搜索/可见平面和公开 Core Won/Lost 续接。本文上文 checkpoint A 的历史范围不改写；本B仍不接 Main/HUD/input/动作/effect，不签 SceneRunner/GPU/人工/CI、#46或M4完成。


## D2-A optional visual author fields

Within the same formatVersion1 metadata, `visual.attackEffectSpriteUri` and `visual.attackEffectIndex` may be supplied **together** (nonempty native PalSprite URI and WORD group, including0). Each team may supply `idleAnimSpeed` (WORD including0). Omission preserves old C's absent sentinels; explicit −1 in author JSON is not a default and is rejected. Duplicate/unknown fields remain errors. Native writer stays1/root default−1; no separate converter or Resource→JSON round-trip exists.

The normal public Prepare includes effect seeds in the existing `HashSet<string>` native dependency closure and `Dictionary<string,string>` SHA-256 manifest, with the same stable-read lease/path/no-overwrite/owned cleanup checks. Strict native loading validates any supplied data even without selecting the reference-presentation capability; consumers select that capability with `checkedVisual.RequireReferencePresentation()` without reloading resources. See [the full native contract](native-battle-visuals.md#46-d2-a-optional-reference-presentation-data-not-playback). This checkpoint prepares/binds data, not animation playback. Changed source bytes require **fresh** Story outputs; old outputs are not repaired or declared hash-consistent.
