# 决策记录与待定问题

## 已拍板的决策

| # | 决策 | 说明 |
|---|---|---|
| D1 | 目标版本 = **1998 柔情版** | 非 1995 DOS 原版。数据格式按 Win95/98 系;有语音文件、AVI 过场。 |
| D2 | **原生 Godot .NET + C# 桌面版重写**,不编译 sdlpal 的 C 代码 | 为了可读、可扩展。 |
| D3 | **忠实移植优先** | 第一目标是完整忠实跑原版;扩展能力是第二阶段。 |
| D4 | 不做通用游戏引擎 | 就针对仙剑这一个游戏;"可扩展"指这份代码以后好改。 |
| D5 | sdlpal 作**参考规格**,不 fork | C 源码当规格说明书,用 C# 重写。 |
| D6 | **两阶段架构**:离线 `pal-extract` + 运行时游戏 | 运行时完全脱离 MKF。 |
| D7 | 资源转现代格式;原版事件转**忠实低层 JSON 命令** | 格式以 [事件契约](events-schema.md) 为准,结构化子集留给手写新内容。 |
| D8 | 移植方式 = **方案 B(分层重构移植)** | 忠实还原行为,但用模块化 C# 重新分层。 |
| D9 | 法律不在考虑范围 | 个人自用,不发布。 |
| D10 | 原版不恢复结构化控制流 | 取舍与被替代方案见 [ADR-0001](../adr/0001-faithful-event-transcription.md)。 |
| D11 | 公共事件与语言包分离，默认简体中文 | 使用当前包的 `m_chs.slf` / `m_eng.slf`，按参考保留消息映射及显示语义；切换语言配置不改剧情逻辑，不另加对话中途热切换。M3 本地候选已实现该切片，不阻塞 M1/M2；见 [事件契约](events-schema.md#语言包与-slf-来源)。 |
| D12 | 场景构建与事件里程碑分开 | M1 提取静态场景资源，M2 构建 Godot 探索场景，M3 接入最小真实事件与对话；后续战斗和全量转换顺延，见 [总体路线](development-plan.md)。 |
| D13 | M2 采用单领队静态探索切片 | 直接进入场景 2，以独立测试配置补足起点和经离线核对的角色行走参数；显式准备 M1 包，不改原包、不运行脚本或对象计时。M2 当时验收的桌面行为包括失焦暂停清键；#21 已在当前代码中按 SDLPal 参考取消该策略，M2 历史验收记录不变。详见 [M2 计划](../milestones/m2/plan.md)。 |
| D14 | M2 先验证 Godot 场景测试框架 | #12 有界隔离关卡已通过并接入，不降 Godot/.NET 基线、不迁移 Core xUnit；#18 后续另获批准交付特定固定源码 API/adapter 包、完整门禁与同字节发布，见[包来源与 bootstrap](../testing/gdunit-source-packages.md)。进一步升级或回切官方包仍需另行决定。 |
| D15 | M2 原生资源与独立 typed profile | #13 后续批准以 M1 PNG/JSON 为显式离线输入，运行时加载 Godot Resource/PackedScene/Texture2D；#14 独立 `.tres` profile 提供经核对的起点/行走参数，Prepare 不 author profile，见 [M2 计划](../milestones/m2/plan.md#3-资源准备与测试配置)。不取消未来事件 JSON。 |
| D16 | M2 离散调度与桌面扩展（历史决策） | 首步立即、后续 100ms、晚到不补走，不加插值；当时批准的失焦冻结/清键与回焦 fresh-press 策略是明确扩展，不宣称 SDLPal 原版行为。#21 已取消当前探索链路中的该扩展；默认窗口 1280×800（4×）、逻辑视野 320×200 保留。 |

## 待定问题

- **Q1 其余表现层 / 外壳层能力**：M2 的逻辑步进、键盘输入、缩放与原生资源接入已实现并通过其历史 Windows 分层验收；M2 失焦暂停在 #21 中按参考撤销。历史验收与当前行为区分见 [M2 计划](../milestones/m2/plan.md)、[M2 验收记录](../milestones/m2/acceptance.md)及[Game 测试说明](../../src/Pal.Game/Tests/README.md#desktop-focus-and-exploration-behavior)。#11/#17/#19 已关闭；转场、音视频及其他后续能力仍按对应阶段设计。
- **Q2 events.json 契约与实现进度**:忠实转写策略、v1 封套、引用和基础命令已定义,见 [事件契约](events-schema.md)。完整 faithful 导出/重建及有限 trigger 执行已本地实现；具名映射与 raw 保存不等于全量执行。当前契约见 [trigger 执行](trigger-execution.md)，手写新内容的结构化 profile 及完整指令集仍不在当前切片。
- **Q3 数据来源与其余资源仍待验证**:当前输入在 `data/raw/sdlpal/`,外层归档与对象表符合 Windows 数据分支,但含 SDLPal 翻译内容,未证明与未修改的 98 柔情版完全一致。Windows 静态场景切片的 YJ2/图像/原始记录提取已本地验证，完整包见 [M1 计划](../milestones/m1/plan.md)；M3 已有目标文字/字形的分层本地证据，但不证明未修改发行盘一致性，见 [M3 检查点](../milestones/m3/acceptance.md)。
- **Q4 符号表方案已确定,待实现**:数字 ID 为真值,游戏可核对名称与可选 `symbols.json` 生成 `_` 注释字段。具体规则见 [事件契约](events-schema.md#7-可读名字),不另建一套剧情标志来替代对象状态和可变入口。

## 设计讨论进度

brainstorming 已完成:可行性、目标、版本、移植方式、两阶段架构、资源 / 事件模型、核心层划分。

**已统一的技术方向**:跨平台桌面版、Godot .NET + C#;不以浏览器为运行环境。存档使用 `user://` 本地文件,渲染与资源加载使用 Godot API。

**M0 已确认**:Godot/.NET 基线、C#/.NET 独立 CLI、xUnit 与进程级 smoke test。当前版本及命令见 [根目录 README](../../README.md#技术栈与开发环境)。

**事件设计已确认**:忠实低层转写、具名命令与 `raw`、完整来源信息、可变入口、trigger/auto 区分及字节往返与行为测试分离。本文为决策索引,格式细节以事件契约为准。

**M2 历史设计及后续修正**：单领队探索、静态对象、独立测试配置与显式资源准备、320×200 整数缩放、键盘和参考步进见 [M2 计划](../milestones/m2/plan.md)。其失焦暂停曾作为 M2 扩展交付；#21 当前将该扩展从共享探索链路移除，不改写 M2 的历史验收记录。功能、Windows 自动/人工验收及文档独立双轴终审均已接受；各切片和整体验收最终提交/收尾记录见 [#17](https://github.com/fanxiao92/godot-pal/issues/17) / [父规格 #11](https://github.com/fanxiao92/godot-pal/issues/11)。#11/#17 及后续跨盘测试缺陷 [#19](https://github.com/fanxiao92/godot-pal/issues/19) 已关闭，成果已合入并推送 main，远端完整 Windows headless CI 已通过；这不扩大音频、导出包和其他平台的验收范围。实际证据与当前任务状态以验收记录和 GitHub 为准。

**当前 M3**：已批准的独立 scene4/map10/owner60 对话切片已在本地候选实现，包含现代资源、完整事件转写/重建与有限执行、搜索/等待/返回及再次交互；默认简中、显式英文。组合原生 profile 与失败路径见 [宿主契约](npc-interaction-host.md)。这是既有批准的落实，不是新架构决定；owner57 等行为不调度、原对象不修改，不宣称正常场景自由探索。本地已集成 `dab79ad8`；双轴复核关闭原六项source finding，`5bf481f`同tree匹配双语GPU各10零差/2calls，精确 `dab79ad8` 原完整自动门PASS获独立证据复核支持。本对话用户已确认 Windows 人工清单通过（含两语言/物理输入/Alt-Tab/缩放关闭），非agent独立实测；当前integration人工资源Windows CI待验证，#20整体收尾及发布回填仍待结果/权限。规格外 Observer 为agent追加工具，已停用；历史缺陷及 verifier not-satisfied 保留但不作为 M3 功能门，不宣称工具修复或隐私豁免，不新增架构决定，见 [M3 检查点](../milestones/m3/acceptance.md)。其余表现层/外壳、全量 opcode/auto 及其他游戏系统仍未完成。

## 曾经考虑过、已否决的方向

- **方案 A(直译式移植)**:把 sdlpal 的 C 模块 1:1 翻成 C#。否决:会继承 C 的全局状态和大 switch,以后扩展痛苦。
- **方案 C(第一天就做现代脚本 API)**:否决:偏离"忠实移植优先",属第二阶段。
- **运行时直接解析 MKF**:否决:改为离线 `pal-extract` 一次性转换,运行时脱离 MKF。
- **运行时直接执行原始字节码**:否决:事件以现代 JSON 命令表达,由事件系统执行。离线转写保留来源字段与未知 opcode 的 `raw` 用于无损重建,不授权运行时解释这些字节码。
- **原版优先恢复结构化控制流,特殊情况才用 goto**:已被 [ADR-0001](../adr/0001-faithful-event-transcription.md) 替代;原版统一忠实低层转写,结构化子集留给手写新内容。

