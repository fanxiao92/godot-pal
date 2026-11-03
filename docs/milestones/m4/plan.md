# M4：首个必经剧情战独立切片

## 权威范围与当前状态

规格以 [#44](https://github.com/fanxiao92/godot-pal/issues/44) 正文、评论和 #45–#54 的原生依赖为准；下表是已发布的十票顺序，不沿用父规格末尾较早的七项草案。本文是本次隔离实施的计划，不覆盖其他工作树中的用户文件，也不把规格标签当作实现证据。

### 当前本地验收

- #45 已验证并集成；#46 在 `4c4dad72c04cf82aa4307c0b1e54262f3c6e9054` 本地验收并集成；#47 双人双敌已在 `f3a1aaa`（集成合并 `dc6ddfde`）本地整票验收：同一引擎多人队列 Core（`02ede4b`）、多人 lifecycle/trigger/codec/native/公共 Prepare 内容链（`26943eb`，Core816）、EnemySlots 资源与选中闭包守卫（`8e430bc`）、真实 Main 双人双敌选令/表现/SceneRunner 往返（`f3a1aaa`，scene-dialogue 109/109）；收据 `47/ticket-acceptance.md`。下一票为 #48 装备属性/双击/掩护。
- #46 包含实际 Won/Lost 续接、输入/owned 回执隔离、玩家/敌方动作、命中 HP、原生数字与反馈生命周期；敌方数字在真实 prehit 入队、等待后的 impact 首绘，非伪造出生时间。
- 最后受影响 scene-dialogue 整批 107/107 通过；启动进程 10/10 保留其 `0a84315` 源码条件（之后只改敌方动作接线及回归），不重签为最终源码的新进程运行。Core786、原生/公共准备69/42/2、Prepare89沿未变层原证据复用；当前库存570，正向567，串行84/107/376。
- 这是逐票本地完成，不是 GitHub 关闭或 main 发布。真实 owner28/`@09C3` 仍待 #53 闭包；完整 M4 共享门、GPU、Windows 人工、远端 CI 与完整 fresh 双轴评审仍待 #54，音频与存档限制不变。

### 历史实施检查点

以下保留各阶段当时的边界说明；未实现/待后继等措辞不覆盖上面的当前状态。

#45 的战前上下文及必要补强已验证并集成到 M4 集成分支（`0672bdd35021af534092d08de223592d32b90e39`）；#46 已有首个 Core 单人单敌经典普攻 combat-only 检查点（见[公开 interface 与参考规则](../../design/classic-battle-session.md)），已追加同一 combat owner 的正式零成长生命周期检查点（开场复活/reset、真实动作counts、合法阈值/法术预检、Won半恢复/Lost不恢复、清理退出和独立完成receipt），以及 `startBattle` faithful 具名双向编解码与 Core 严格集合加载检查点（完整 source/next、结果引用和旧 raw7 包兼容，见[事件契约](../../design/events-schema.md)）。另已追加显式 typed content+RNG 的正式 Core trigger binding：准确 active lifecycle、等待阻塞、一次性真实完成回执、持久 settled role 承接及 Won/Lost 续接，next/loss/编码flee 的保守闭包在副作用前纯预检；原无配置入口仍拒绝开战，raw从不执行（见[正式接口及保守支持域](../../design/classic-battle-session.md#trigger-事件binding46-有界检查点)）。已追加纯 Core [现代战斗数值 JSON 严格读取](../../design/modern-battle-data.md) → 同一 typed content（含下一 native writer 可读取的不可变公开投影）；格式合法与选中入口能力闭包分离，公开字符串用例真实胜负续接。已追加同一纯 typed 格式验证与 Godot 原生数值 codec 检查点（复用 Role/XP，严格 Write/CreateCore/Load，真实保存/加载后 Won/Lost/receipt）；现已追加同一 public Editor Prepare / `_Ready` / 正常 `Prepare-BattleData.ps1` 的完整显式离线数值准备检查点：外部单文件、严格 UTF-8/16MiB、路径/lease/hash 复核、typed 保存回读和原子无覆盖发布；新 Game direct32例及完整 Prepare79例已实际通过。数值准备不构成完整人工 package、CLI 作者命令或 Main 资源链交付。已追加 native binding 的前置纯 Core `TriggerScriptSession.ValidateEntry`：以 InitialRole 复用 Start 同一完整闭包算法，无会话/战斗/RNG/副作用；既有 Start 仍用当前 settled role。其后已追加[完整 native story CONTENT binding](../../design/native-story-content.md)：显式typed PalStoryProfile / NativeStoryProfile.Load，复用同一Core闭包返回requirements，真实静态/数值/完整events/双语言字形画像及精确本地data依赖散列校验；返回已校验的隔离资源供正常Main后续消费。该内容绑定不是战斗视觉映射、正式package Prepare或Main接线。现已追加独立 [native battle visual binding](../../design/native-battle-visuals.md)：严格绑定所选Core role/team/target到原索引完整帧、背景、同一active palette及显式presentation metadata，复用native leaf与局部依赖校验；不推进动作/动画，不含attack effect/Main/HUD。已追加[正式 story package 离线准备](../../design/story-package-preparation.md) checkpoint A：public Editor Prepare/真实argv/正常PS走同一生产转换，完整现代事件、typed Story/Visual、精确native data graph依赖hash与严格回读；输入非自包含，优先复用已正式准备依赖。direct31与完整Prepare89（79+10）本轮通过；不是Main或完整可见人工包。人工作者静态/可见art/非空双语glyph/Win-Loss完整包 [checkpoint B](../../../samples/authored-battle/README.md) 已交付正常准备数据和两个精确native-map验证；已追加[normal Main人工往返 checkpoint C](../../design/main-story-roundtrip.md)：严格双profile正常argv、每实例Godot RNG、同真实trigger/lifecycle与owned receipt、搜索/完整前后对白/分离攻击及目标确认、逐动作原索引pose/真实HP伤害、地图恢复与50ms探索返回；UI使用正式双语Words及自有font增补，旧已准备hash包不覆盖。C后已追加D1敌方主动普攻表示：按参考magic/preMove/inclusive attack/zero-wait、受击或闪避、索引色移6、后退返回和hit时点揭示真实HP/结果；Core/RNG/原生schema及作者包不变。D1不是完整D：D2仍需玩家frame7/effects、敌idle速度/持久counter和剩余数字表现闭合；GPU/人工/CI未验收，#47–#54 尚未实施。**这不是 main 交付、GitHub 任务关闭或整个 M4 验收通过。** 后继按实际实现、匹配验证和集成结果释放；远端任务收尾随真实交付更新。

已交付的 M2/M3 能力不重做。当前默认 Unit 与 CI 继续遵循 [ADR-0003](../../adr/0003-native-unit-feedback.md)，不能用 required Unit 成功代签 M4 完整验收。

## 最终玩法与不扩张范围

显式启动 Windows 独立验收模式，正常 Main 加载来源明确的战前快照，经真实搜索从 scene2/map12/owner28 的完整 `@09C3` 进入；`@09FC` 组 role0/1、`@09FD` 选战场24、`@09FE` 按原操作数 `(0012,A073,0000)` 对敌队18的两个 enemy97。不得另选简单战斗、跳到开战地址、裁剪事件或伪造胜利回执。

经典玩法保留整轮选令、随机身法排序、普攻/暴击/适用额外伤害、装备属性与持续双击、目标变化、掩护、濒死与友亡全部随机分支、成长与结算，以及胜后 trigger/auto 的真实变化。奖励和有效面板由数据及正式规则计算，不硬编码调查合计。

Lost 进入原失败剧情至 `0x004E` 重载请求，输出一次可定位诊断并保留可关闭的终态和 TODO；不实现读档、假恢复、自动重试或返回探索。主动魔法/道具/合击、完整菜单、音频播放、存档系统、全量 VM、正常场景全部动态行为及其他平台不在本轮范围；这些限制不删除本战必需的法术进度、装备效果、角色钩子、红屏或局部画面恢复。

## 依赖与内部检查点

| 任务 | 交付 | 直接阻塞 |
|---|---|---|
| [#45](https://github.com/fanxiao92/godot-pal/issues/45) | 来源明确的战前上下文、严格 author/Prepare/native/Main、合法站位及副作用前拒绝 | 无 |
| [#46](https://github.com/fanxiao92/godot-pal/issues/46) | 人工单人单敌普攻事件，经正式资源与真实交互完成胜负往返 | #45 |
| [#47](https://github.com/fanxiao92/godot-pal/issues/47) | 双人双敌选令、目标变化与经典执行顺序 | #46 | 已本地整票验收 `f3a1aaa`；远端收尾随最终交付 |
| [#48](https://github.com/fanxiao92/godot-pal/issues/48) | 六角色装备初始化、有效属性、双击、掩护 | #47 | 已本地整票验收 `8c409fa`（unit1 59cab8f+unit2）；远端收尾随最终交付 |
| [#49](https://github.com/fanxiao92/godot-pal/issues/49) | 战中濒死/友亡剧情并返回战斗 | #48 | 已本地整票验收 `677f888`；远端收尾随最终交付 |
| [#49](https://github.com/fanxiao92/godot-pal/issues/49) | 濒死/友亡剧情全随机闭包及返回原战斗位置 | #48 |
| [#50](https://github.com/fanxiao92/godot-pal/issues/50) | 胜利成长、奖励、法术列表及清理/恢复顺序 | #48 | 已本地整票验收 `780a587`（批拆分至8批）；远端收尾随最终交付 |
| [#51](https://github.com/fanxiao92/godot-pal/issues/51) | boss 逃跑失败、Lost 分支及重载 TODO 终态 | #46 | 已本地整票验收 `0a27d20`；native-map 2既有失败转小修复；远端收尾随最终交付 |
| [#52](https://github.com/fanxiao92/godot-pal/issues/52) | 局部动态剧情、等待、入口回写和真实 auto 移动 | #45 |
| [#53](https://github.com/fanxiao92/godot-pal/issues/53) | 真实完整事件、战斗/图像/双语资源与全部已支持闭包组合 | #49、#50、#51、#52 |
| [#54](https://github.com/fanxiao92/godot-pal/issues/54) | 分层完整验收、独立复核和最终交付 | #53 |

优先串行实施 45→46→47→48→49→50→51→52→53→54；每票可分短上下文的内部实现检查点，不能把一个检查点、静态调查或发布动作当作整票完成。每个工作树只有一个源码写入者，集成分支由协调者维护；所有实际 Godot/GdUnit/Prepare/含引擎的 solution 执行全局串行。

## 复用与契约

- 术语见 [GLOSSARY](../../../GLOSSARY.md)，忠实转写遵循 [ADR-0001](../../adr/0001-faithful-event-transcription.md)，参考入口见 [reference/README](../../../reference/README.md)；SDLPal 只读，不编译运行。
- Core 保持纯 .NET，负责角色、装备、战斗、成长与局部剧情状态/调度；Godot 负责输入、节点、显示、现代资源和平台诊断；离线工具不成为运行依赖。
- 复用现有探索、完整 faithful 集合及可暂停 trigger、对话与 Main，不把另一 NPC 切片仅改 owner 常量冒充新剧情宿主，不预造全局状态库或总线。
- #45 的冻结真实 v1 及来源规则见 [战前上下文契约](../../design/first-story-battle-context.md)。真实 role1 不补满；六 role 装备与五可玩 role 升级法术表分开处理；独立 author policy 不冒称自然存档。
- #46 的单人单敌、零装备效果/钩子/奖励人工包必须是合法且明确标识的内容，使用同一正式战斗实现和 Main。先结合现有配置/事件契约确定最小人工内容封套，不放松真实 v1 身份与来源验证，也不通过测试专用业务装配或特判跳过规则。真实组合仍由 #53 接入。
- 所有具名事件命令保留完整 source/next/保留位并真实反向编码语义字段；运行时不解释 raw。预检覆盖实际可达分支、装备、角色和动态 auto，不修改生产状态或消耗生产随机数。
- 等待和战斗只有一个实际推进所有者。请求、真实完成状态及继续位置绑定，提前/重复/陈旧/跨会话回执无效；输入只由当前模式消费，不新增失焦业务冻结或改变既有离散行走/整数缩放。
- 场景/战斗数据及图像沿既有 native Resource、PackedScene、Texture2D 准备；公共事件读取完整现代 JSON，不在运行时读原档、SLF、M1 author 包或参考字库，不启动提取/同步/补配置。
- 复用可核身份的现存产物，新输出只写新建/空且不重叠的目录。真实原始/派生资源、图形证据和私密来源旁证不提交上传；远端仅用可提交人工夹具。
- #45 的 contract-only 依赖声明不是现代战斗/对话资源。#53 须替换全部声明、重绑 author/native envelope 并严格核完整闭包后才开放真实 `@09C3`，不能凭引用或散列把缺口标为已支持。

## 增量验证与最终验收

沿用 #44 已确认的三个 seam，不重复访谈、不增加平行测试框架：

1. **公开 Core 会话**：正常构造、输入、显式时间/随机源和公开状态，独立参考/手算核对随机范围与消费顺序、调度/伤害、装备/成长、事件 PC/入口及回执；不以被测实现生成预期。
2. **真实独立游戏会话**：人工现代资源经显式准备，由正常 Main/SceneRunner 接受真实输入，在生产更新完成处观察，覆盖胜负交接、失败传播、输入/订阅/节点清理和同 URI 隔离；不直调 UI 或注入战斗结果。
3. **CLI/Prepare/native 与必要进程**：文件、版本/引用、完整 faithful 往返、资源诊断、路径安全、argv/退出/READY 由所属层验证，不全部搬入 SceneRunner。

具体执行读[测试职责矩阵](../../testing/test-layers.md)、[Game 分层](../../testing/game-test-layers.md)、[时序规则](../../testing/time-boundaries.md)及[异常处理](../../agents/test-diagnostics.md)。普通 Unit 原生运行；按实际改动选受影响层，新增用例同步源码身份、参数行、严格计数和串行分区。未变源码/条件的成功检查不因阶段名重跑；独立范围成功不拼成完整门 PASS。

每票增量验证核心行为和关键边界，不把所有测试拖到 #54。最终固定候选仍必须完成：

- 完整共享门的发现、重复、三项指定失败控制、原生准备/导入/启动及 M2/M3 回归；退出、TRX 终态、完整性和指定诊断共同判定，不以 partial 或标记文件代签。
- 完整真实 chunk 本机逐字节往返、前置来源/合法站位，以及真实简中/英文战前—战斗—胜后和失败 TODO 各自证据。
- 真实战场/敌我/动作/双击、对白/字形/画像、UI/结算/恢复/红屏的独立 GPU 核对；Windows 物理键盘、Alt-Tab、缩放/letterbox、正常关闭和人工反馈另行留证，不将自动输入冒称人工操作。
- 删除双击、经验均分、错误清除装备状态、提前回执、漏半恢复、失败误走胜后等定向反例或受控变异；恢复后核实际产物。
- 人工资源 Windows CI、本地 headless、真实资源、GPU、人工分别绑定实际身份；不拿历史 M3 或当前 required Unit 通过签收 M4。
- 完整 M4 候选 fresh Standards/Spec 独立复核、必要修复和匹配验证；通过授权的普通 PR 流程合入 main，并核真实主干结果、同步任务与入口文档。

工具链与会话预算沿用批准基线，不升级或加时换绿。未知 Godot 仅查 PID/Name并暂停，不结束未知进程或采集无关信息。非预期测试失败、中止、崩溃、未知退出或缺报告保全后停止该执行，诊断和明确有界恢复分开记录；不自动重试、拼接 partial 或放宽断言。


#### #46 D2-B bounded runtime checkpoint

Normal Story now requires the optional-format reference-presentation capability before map/session/RNG creation and consumes retained checked FX for the full16tick player physical sequence, impact-only HP and per-battle persistent enemy idle frame/WORD counter. See [runtime details and exact caller/counter boundaries](../../design/main-story-roundtrip.md#d2-b-player-effects-persistent-enemy-idle-and-impact-hp). This supersedes the earlier runtime-D2 gaps in the historical status paragraph, not the unchanged native format. Floating-number layout/color and retail label lifecycle are explicit D2-C; fullD/#46/M4, GPU/manual/CI/fullgate remain open. No new native schema, producer, Core entropy, prepared-output overwrite, M3 or frozen owner28 gate changes.

#### #46 D2-C-B bounded runtime checkpoint

Normal Main consumes retained digit frames through the selected number capability, with battle-owned16slot floating feedback, exact top-left/right-aligned6px layout, unsigned wall-age expiry and player FX0 birth/FX1 eligibility versus enemy last-prehit birth/first-post-wait impact eligibility. Resolved SHORT/WORD damage preserves overkill rather than using clamped HP differences; modern Miss/Critical labels have explicit impact/expiry/action-reset lifetime. Matching scene-dialogue107 and StoryMainProcess10 ranges passed; inventory570, positives567, serial84/107/376. Core/native/producer/author/prepared resources remain unchanged. Details and remaining independent GPU/manual/fullgate/CI/whole-ticket acceptance boundaries are in the [runtime contract](../../design/main-story-roundtrip.md#d2-c-b-battle-owned-floating-numbers-and-modern-feedback).
