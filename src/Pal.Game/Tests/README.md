# Game 测试当前入口

Game 是[默认 Unit 反馈](../../../docs/testing/getting-started.md)之后的按需路径，不是第一阶段自动 CI 或测试合并要求。本页只维护当前 Game 测试入口、覆盖口径与失败判定；Configure→立即入树→输入→生产更新完成→清理的示例只在[Game 专题](../../../docs/testing/game-test-layers.md#新人最小模型仅按需进入-godot)维护，不复制第二套规则或命令。职责选择见[测试职责矩阵](../../../docs/testing/test-layers.md)，初始化、SceneRunner 生命周期、时序边界和完整门规则见[Game 测试分层](../../../docs/testing/game-test-layers.md)。旧命令、变异、性能对照、M2/M3 交付年表与历史失败见[历史运行记录](../../../docs/archive/m3/game-test-run-history.md)。

## 当前入口与覆盖

Game 测试继续使用现有 Godot 程序集、固定 GdUnit 包和 `tests/Pal.Game.Tests/game.runsettings`。真实版本、bootstrap 和包来源以[当前包消费说明](../../../docs/testing/gdunit-source-packages.md)及仓库配置为准。测试资源是本例自有、忽略的 native fixture；真实资源和私密证据不进入提交或上传。

共享完整门仍唯一使用 `scripts/Test-M0.ps1`，依赖 Core→CLI→Prepare→Game 的串行顺序和当前脚本生成的批次/身份。当前库存和批次从 `scripts/GameTestBatches.ps1` 与测试定义核对，不在本页复制案例计数；已完成运行见历史索引并保持原身份。保留完整门实现不证明当前 M4 候选的完整门运行兼容性已验证。

## 选择运行范围

- 新增/迁移场景行为：默认选 SceneRunner，遵循真实 Main 初始化、`UpdateCompleted` 完成边界、输入释放、finally 清理和资源隔离。
- argv、READY、诊断、退出码或必要进程隔离：只选对应最小真实进程契约。
- 精确业务时间：运行 Core 显式时间测试；Game 等待仅是活性保护。
- 完整候选：只调用共享 `scripts/Test-M0.ps1`，不将局部 TRX、静态 discovery 或历史结果拼成全门。

定点运行使用[显式范围入口](../../../docs/testing/range-regression.md)，而非从历史命令墙取命令。下例只核对当前参数定义，未因文档维护实际执行，不是运行证明，也不授予运行授权；两个变量须预先指向批准的 Godot console 和已验证的四文件 bundle，证据目录须不存在：

```powershell
& ./scripts/Test-Range.ps1 -Scope game -GameBatch process `
  -GodotPath $ApprovedGodotConsole -FixedPackageDirectory $VerifiedFourFileBundle `
  -OutputDirectory $NewEvidenceDirectory
```

game-only 在冷工作树上也须先显式完成现有 CI-style Godot editor import（`--headless --path <src/Pal.Game> --editor --import`）；范围入口不会代做该前提。#155 的原冷入口失败与显式 import 后恢复完整433成功各保留原收据，恢复不改写为首次成功。

该入口设置并恢复 `GODOT_BIN` / `PAL_TEST_GODOT`，不隐式运行未选的 Prepare；需要 Prepare 时显式加入 Scope，按固定顺序先于 Game。绕过入口直接 `dotnet` 时，必须先 bootstrap、设置这两个批准引擎路径变量并完成当前所需构建/import。实际 SDK/Godot/GdUnit/Prepare/solution 运行遵守根 AGENTS 的全局串行规则，完整参数以脚本/config 为准。

## 失败与证据

检查实际数字退出码、TRX Summary/RunInfo、Results/Definitions/Entries、发现身份和完整输出；部分 Passed、`aborted=0`、READY 或一条 PASS 日志都不表示完成。中止、崩溃、未知退出、输出不完整、缺结果、非预期 skip 或指定失败原因不符时，保存原件并停止，不自动重试、续跑、提高时限或放宽断言。按[当前异常处理规则](../../../docs/agents/test-diagnostics.md)分类；历史症状只在其明确指针触发时读取。

headless/SceneRunner、GPU、物理键盘/桌面人工和远端 CI 是互补证据，不能相互代替。M3 当前交付状态和历史限制见[M3 验收检查点](../../../docs/milestones/m3/acceptance.md#当前状态)；这不是新的运行结论。

## #127 M6-A T01：已声明面板的原生往返

`PalBattlePlayer` 的默认零 `MagicPower` / `FleeRate` 与既有 Core WORD 域对称映射，格式仍为 1。`NativeBattleContentTests` 增 9 例：现代输入经原生保存/重载后保留互异非零及 0/65535，旧资源缺属性仍为零，并经公开 trigger/lifecycle 的 boss 逃跑观察基值；非法 WORD 与重复角色明确拒绝。既有全字段隔离用例也加入非零面板。`PrepareBattleDataTests` 增 12 例，覆盖正式 Prepare 的同类正例、非法 WORD、重复/缺失角色，核对不改源、不发布坏内容。现有 M4 生命周期和装备法力规则用例保留。

`ModernBattleContentTests` 再增 8 例公开 typed 内容验证回归：互异非零、缺省与 WORD 边界经公开 boss 逃跑消费，两个面板的负值/越界拒绝并定位到字段。原生旧资源夹具只移除 `PalBattlePlayer` 子资源中的两项导出，不改隐藏经验引用；typed 校验与 JSON 输入一致要求 0..65535。

源码登记为 Game 647 = 644 positives + 3 controls，native-map 433（原 412 + 21）；Prepare 进程库存仍为 89，Core 为 936（原 928 + 8）。这是测试身份/计数登记，不是完整门、正式 Main 输入、GPU、人工桌面、真实资源或 CI 验收声明；本票执行证据由对应候选收据记录，恢复后的成功不覆盖先前失败原件。

## #149 M6-A T04a：Core 共享角色活状态职责

宿主从完整 `BattleRoleSnapshot` 集合一次 seed `RoleLiveState`，按 RoleId 0..5 持有深冻结的活记录，并以 `DynamicEventWorld.ForHost(rules, leader, sceneId, onEnter, onTeleport, roleLiveState, initialPartyRoleIds)` 绑定 world。新增七参 overload 的 `initialPartyRoleIds` 是显式有序 `IReadOnlyList<int>`：宿主应传 `content.Players.Select(p => p.RoleId).ToArray()`，不从 owner 字典枚举推导队伍。factory 只在初始化时快照只读复制，拒绝 null、空、重复、域外或不在 owner 中的 RoleId，以及超过三个 world party 槽位的输入；不重 seed、触发事件或提供运行中 rebind。原五参及六参（仅 owner）签名和默认 `[0]` 队伍保留；world 三槽并不扩大现有 battle 1–2 人能力。trigger 使用 `TriggerScriptSession.ForSharedRoles(events, language, battleContent, battleRandom, owner, world = null, inheritedColor = DialogueTextTokenizer.DefaultColor)`；旧 battle + world 构造若收到已 shared 的 world，也会自动绑定同一 owner。`PartyVitals` 读取该 owner 的 HP/MP，事件写入直接更新 owner；`BattleRoles` 仅为 `content.Players` 的 RoleId 与顺序所选的 live 只读投影，持有的投影也读取最新记录，不在每次入战重新 seed 初始模板。owner 可含六角色，不表示六人参战，也不扩大现有参战容量。

combat 中间态及战后脚本等待期间不提前发布最终角色记录。仅当 `AcknowledgeBattle(requestId, receipt)` 的 phase、requestId 与 lifecycle `OwnsCompletion` 全部通过，才 merge 此次参战者的完整结算记录（含既有成长、装备、法术等字段），保留旁观记录，然后继续外层脚本后缀；拒绝的旧、重复或 foreign 回执不提交。legacy dialogue-only、world-only + `InitializePartyVitals`、battle-only 路径保留；shared world 拒绝重复 `InitializePartyVitals` seed，显式 shared trigger 拒绝未绑定同一 owner 的 world。

未来外层 content loader 负责从已验证内容/恢复记录重新构造 owner，再将 world/trigger 重新绑定；本票不设计存档 schema。#148 的 D3/D4 已批准，其正式存读档规格不由这里重开或实现。#149 交付时 Main、preview、SceneRunner 尚未接入此 shared 路径；后续 #150 的正式宿主接线仍未整票验收或集成，#149 的公开 Core 能力及兼容回归不能替代 #130 的正式 Main 接线与验收，也不代表完整 revive 毒/状态、#139 或整个 M6 已交付。该 #149 交付时的历史计数登记为 Core 1195、CLI Unit 465（不是当前计数）；完整 Unit 证据复用 #149 U5 新鲜 Core 1195/1195、CLI Unit 465/465 收据：实际运行源码为 `feeea21523b75c0b43bc28ef03a971da6e6d7664 + dirty.patch`，其 patch 与 `b7c07e43efcb696af8808125f5feab0a8c3d725e` 提交差异逐字节相同，并非在后续 metadata checkpoint 重跑。Game 库存与批次未因本票改变。metadata 登记及 bounded shared-impact 不是完整门、GPU、人工或 CI 验收。

## #151 M6-A T04c：Core 首战所属调用与返回边界

`FirstMandatoryBattleSession.PendingRequestId` 是每次真实搜索的独立非空 origin，不参与原两字段 `FirstMandatoryBattleTriggerRequest` record 的构造、deconstruct 或值相等。worldful 宿主须在任何 facade `Update` / trigger `Start` 前，一次性 `InitializeTrigger` 关联实际生产 trigger；Core 通过 trigger 的 internal readonly `BoundWorld` 读取实际 world，不接受另传 world 或运行中 rebind。真实调用顺序为搜索 → `Start` 传 origin → `BindTrigger` 固定实际 session + CallId → 外层 `Completed` / endkeep → `CompleteTrigger`；未完成、旧请求、旧 call、foreign session 和重复完成不能释放。battle 最终 ACK 与外层脚本完成是两层不同的所有权，不以其中之一冒充另一层。

合法完成保存本次实际返回入口，再进入 50ms 返回保护；保留正常 100ms 探索节拍、uint wrap、晚到不补步，保护到期清瞬时 search 而保留 held 方向及优先顺序。legacy worldless 路径继续兼容。投影仅更新原当前场景对象集合及 legacy 已声明记录，保留 map、sprites/frames 身份，不把实际 world 的额外 global 上下文加入碰撞/搜索/渲染。首次 Start 前及返回后的正常探索都同步同一个 world 的 leader；不重 seed、重发奖励或重 merge #149 的角色结算。legacy `AutoIdleFrameCount` 保留原始 opaque WORD：此路径既不 materialize 也不执行 auto，不能用 world 默认零覆盖；不新增完整 auto、VM 或 save schema。

#151 交付时的历史计数登记为 Core 1212、CLI Unit 465（不是当前计数）；复用 #151 U3 实际 focused 66/66、Core 1212/1212、CLI Unit 465/465 的完整 Unit 原件。其真实运行源码为 `883dc4b3515508bd016492059cb6a5325b3b911d + dirty.patch`，patch 与 `c1e7b0ed31ce93a604eeb230e8d683135b2ce06f` 提交差异逐字节相同，不是在后续 metadata HEAD 重跑。U3 非零 counter 17/65535 → 0 的指定 RED 与修复后原件各自保留。Game 库存及批次未变；最终 metadata 候选的新 source-bound headless shared range 以对应候选收据为准，不预告通过，也不拼成完整门、GPU、人工桌面、私有真实资源或 CI 验收。

本 Core 交付不修复或接入正式 Main：#150 仍须实际消费初始化 / Start origin / Bind / Complete，证明 post-Won 后新的 committed 探索更新、第二次真实搜索入战，以及 shared / preview / RNG / Story 的剩余接线与活性。#151 已通过父控独立验收、实际快进集成至本地 `integration/m6-126-148@8a2318ea470ef7b0239809927e4df790b92490c3` 并 CLOSED。#150 的 U2 仅为持有的 bounded checkpoint `b846259594addecad4f4e92c2f8048aba0caecfe`，不是整票验收或集成；不能据此关闭 #130、#126 或 #148。

## #152 M6-A T04d：当前动态世界的独立复制

`DynamicEventWorld.CreateIsolatedCopy()` 同步复制每次调用当时的完整当前 world：全部对象的当前词（位置、方向、帧、状态、消失时间、触发模式/入口、auto 入口/idle 计数）、SceneId、leader、有序 party、battlefield 及全部 scene/object script 条目。对象的只读静态 `Initial` 关联保留；实现保留该身份的静态代码核对与公开移动 tick 的初始帧周期行为回归是两种证据，不把 public 行为测试说成直接读取 private 关联。

shared world 的 copy 拥有独立 `RoleLiveState`，包含当前完整角色记录、深嵌套值及未参战旁观角色；copy 的 vitals 仍投影自己的 owner，不新增第二 HP/MP 银行。legacy owner=null 路径分别保留已初始化 vitals 与未初始化状态，不伪造完整角色。可变对象、词表、party 和角色存储与 source/其他 copies 互不污染，后续再次调用取得最新值而非缓存；静态冻结身份按既有设计共享。复制不执行脚本、奖励或 RNG，也不复制 `WorldChanged` delegates、外部执行器、trigger request/CallId/phase 或调用栈，不是生产 restore、持久 GameState、存档 schema 或新增 VM。

当前 metadata 登记 Core 1218、CLI Unit 465。完整 Unit 复用 #152 U2 实际 focused 6/6、RetailVerbs 37/37、Core 1218/1218、CLI Unit 465/465 原件：真实运行源码为 `dc031ac + dirty.patch`，四次运行的完整 patch SHA256 均为 `5f0b369eec50f4a9c85852012f1642fbf82390128b714f56b952adad7f7b7d00`，patch 与 `3eaf4e359d4de377a4a9bc53a839e3fdafe6d8ba` 的对应提交差异逐字节相同，两份实际 raw source 亦相同。最终 metadata/docs 候选在全部相关编译源码/配置不变的证明下复用这些 Unit，不声称在后续 clean HEAD 重跑。新增六例均为公开 Core Facts，不是 Game cases；Game 647 discovery、full 644 / committable 643 positives 及批次不变。最终冻结候选的新鲜 bounded shared-impact 结果只以该候选实际收据为准，不预告通过或拼成完整门。

#152 仍待父控最终 Standards/Spec 及实际本地集成；之后 #150 才能合入并消费当前 copy，继续验证真实 Main/preview/RNG/lifetime/Story 与原票其余验收。复制 Core 能力不代表 Main 已全部接线，不自动关闭 #152 或 #130/#126/#148，也不是 full gate、GPU、人工桌面、私有真实资源或 CI/M6 交付。

## #150 U10/U11 有界候选：公开 Story party 精确分区

唯一既有方法 `Pal.Game.Tests.StoryRoundtripSceneTests.PartyTwoPlayersPickTargetsByCyclingExecuteQueueAndWin` 从 `scene-battle-heavy-d` 迁入既有公开 `scene-realfull`，不是新增 batch、扩大 `Test-Range` allowlist 或选择整个 Story 类。以下仅为父控审查后的计划命令，本次 metadata 维护未执行 SDK / Game，也不授予运行权限；变量仍须指向批准的引擎、已验证 bundle 与全新输出目录：

```powershell
& ./scripts/Test-Range.ps1 -Scope game -GameBatch scene-realfull `
  -Filter FullyQualifiedName=Pal.Game.Tests.StoryRoundtripSceneTests.PartyTwoPlayersPickTargetsByCyclingExecuteQueueAndWin `
  -GodotPath $ApprovedGodotConsole -FixedPackageDirectory $VerifiedFourFileBundle `
  -OutputDirectory $NewEvidenceDirectory
```

当前分区权威仍为 `scripts/GameTestBatches.ps1` 与离线 `GameBatches.Tests.ps1`：heavy-d 5→4；realfull full 3→4 / committable 2→3。full 保留原私有 `RealFirstBattleFlowTests` 正例，committable 只排除该原正例，保留两例 Synthetic 与上述完整 Story 方法匹配；公开精确 filter 仅选该一个 Story 方法，不消费私有真实资产。U10 时库存为 647 = full 644 positives + 3 controls，committable 643 positives + 1 private exclusion + 3 controls；仅迁移执行归属，完整门执行集合/总数、10 batches、三轮/37 TRX 与 realfull 300000ms 预算均不变。native-map 433 及 process / scene-dialogue / native-map 合计 592 不变。这些是源码/离线 metadata 登记，不是本轮 runtime discovery 或 full gate 通过。

U9 扩展原方法的 shared owner/world/trigger、两人顺序、结算连续与同 URI 新 Main 初值观察。U11 已通过正常公开入口验证：临时反转初始 party 顺序时，在真实 Main 完成回调中发生指定断言失败；精确恢复后，同一方法在一次新运行中通过（647 discovery，单例 Completed/Passed，14 次 warning，原预算不变）。父控独立复核通过；这是有界观察器与分区候选验收，不是整票验收，也不能把初始 HP/MP 当作脚本前缀效果。真实 Story 全队 `0x001D` HP/MP 前缀及当前值消费、其余整票兼容/回执/资源 hash/lifetime 和新 source-bound shared-impact 证据仍欠。#150 尚未整票验收或集成，既有有界历史证据不重写；headless 合成证据也不替代私有真实资产、GPU、人工桌面或 CI/M6 交付。

## #155 PartyWin 全队 HP/MP 前缀准备

`AuthoredStoryFixture` 末尾增加可选 `partyHpMpPrefixAmount`，默认 null 保持原 events/index 字节和旧五位置参数；显式值仅支持 PartyWin 的 SHORT（含0），在自有输入 entry1 插入命名 `changeHpMp`/0x001D、scope1，而非修改初始数值。正常-3用例经原 Prepare、双语 typed Story/Visual strict Load 核对完整11行/88字节、独立源流 SHA 与输出 manifest；准备后的 HP100/95、MP20 保持初值。这是准备能力，不证明 Main 执行前缀、全队效果或当前值入战。

本票累计新增三个无参 case：正常-3、SHORT边缘（-32768/0/32767）和准确参数拒绝；显式0保留11条命令，65533不当作-3接受。原默认PartyWin/PartyLoss继续核原字节/初值；既有用例覆盖PartyWin旧合法numeric/presentation选项与第六位置prefix0组合，另以Win旧五位置调用核enemyHp、以PartyEquip核去dual装备，保持原选项的scenario域，不扩大fixture支持。当前登记650（647 positives +3 controls）、native-map436，committable646；这些是实际源码库存，不预填最终完整批通过数。正常-3指定RED/GREEN和SHORT边缘精确GREEN只读复用已验证的同内容完整433 marker，不签发或刷新新增case完整批证明；首次无Filter完整native-map因测试新增的非法PartyWin/重复enemy7 numeric组合失败，原件和R1 marker保留；父控批准按各旧选项的合法scenario修正测试后，fresh无Filter完整native-map实际Completed、436/436 Passed、RunInfo0、四条命令exit0且输出/清理完整，包含默认兼容增强；仅该完整成功通过Range正常更新marker。实测为be432ddd + dirty候选，不称后续clean checkpoint重跑；运行后仅README补本结果。该bounded准备影响仍待父控独立整票审核/实际集成，不等于 fullgate/GPU/人工/CI 或 #150/Main 交付。

#155 随后已由父控独立整票验收并实际本地集成至 `67be98f9de1178239e2d1def41cb9f60f6886e45`（Issue155已关闭，非Main交付）。#150 普通合并该已验收前置及其154/156祖先，保留自身U10精确路由；合并后实际源码库存650、full647/committable646，分区85/74/2/5/6/3/4/28/436/4（committable最后3），process/scene-dialogue/native-map合计595。这是合并后的metadata，不冒称在#150重跑了#155完整436或shared/full gate；#150真实前缀消费和其余验收仍待完成。

## #150 U13 Story 前缀消费者（仅源码候选，未运行 SDK）

原 `PartyTwoPlayersPickTargetsByCyclingExecuteQueueAndWin` 现在显式消费 `new AuthoredStoryFixture("PartyWin", partyHpMpPrefixAmount: -3)`；仍由原 Prepare、native profile 和 Main 真实 search 装配，不修改启动 HP100/95、MP20 或 prepared 文件。入树前已安装的 `StoryHost` 观察器在本次 search 交付且新 `Session.CallId` 出现后的首个 `Main.UpdateCompleted` 内无条件取样，记录前 CallId/request/phase 与新身份；不等 HP/phase 成功形状才采样，也不把尚未 Start 的排队回调当作前缀效果。原5s driver等待同时要求观察器已采样，避免FramePostDraw先建立display、但新CallId尚未经过Main完成回调时提前检查；不增加超时或等待正确数值。预期全队 HP97/92、MP17；实际 battle 入场先核当前 HP 与完整角色初值，再核 pending/lifecycle 引用，避免 current-value 数字负控被引用检查抢先截获。

owned 最终 ACK 发布后的首个适用 Main 完成回调按独立完整分支表核 HP：evade→123/121；命中 role0，damage20/21→113/121、22/23→112/121；命中 role1，damage21/22→123/110、23/24→123/109。两人 MP 均33，XP/cash 奖励0、level2、maxHP150/maxMP50不变；分支来自真实动作，数字不是由 owner/lifecycle 的实测 HP 反算。保留完整结算记录对接、同 owner/world/live view、party[0,1]、原真实动作/队列/目标/显示/后缀/探索返回及同 URI 新 Main 未 search 前原初值隔离检查。

本单元只冻结两路径 source patch、独立数值及实际行级 startup-value 消费负控方案；没有 SDK/build/discovery/Prepare/Godot/test/变异运行，不宣称新增断言已编译或通过，不复跑 U11 顺序 RED。原无参 FQN、RequireGodotRuntime、90000ms 方法、5s 观测和公开 scene-realfull 精确路由/300000ms 会话预算、650库存及所有分区不变。后续须父控实读 patch/数值/负控并另给原生预算；#150 其余验收及最终新 source-bound 影响证据仍欠，#155 准备436不是本候选 Main 证据。

## #150 U14 Story 当前值数字反例与恢复验证

U13 更正候选现已通过有界真实验证：临时仅在 Main 的 Story 入战消费点改为 startup lifecycle，prefix/owner/world/party 保持，公开 `scene-realfull` 精确 PartyTwo 单例产生指定 HP 数字 RED；精确恢复完整生产源码后，另一次 fresh 单例实际 `Completed / Passed1 / RunInfo0`，两轮 discovery650、所选/实际精确1，四命令均完整退出/双 EOF/清理（RED 0/0/0/1、GREEN 全0）。未重跑 U11 party-order RED，未修改 timeout/guard/allowlist。父控独立复核每轮221项源码、6项工具身份及原TRX/精确集合/实际DLL-PDB归档与恢复构建绑定；实测是 `4c11731 + dirty`、patch SHA `569d080ac5568ab69a3fae15d19493d761d16947c9f209a10a5f120b011f3f68`，不是后续 clean checkpoint 重跑。实测后仅本 README 补结果。

原 GREEN case stdout 记录 prefix HP97/92、MP17/17，以及动作34/44/敌对role1伤24/36、combatHP97/68；独立分支表的战后预期 HP123/109、MP33、完整 owned 提交与同URI新Main原初值，由冻结源码的无条件断言及完整方法通过作证，不称原日志独立输出了最终ACK/MP数值。RED原生栈定位 ObserveSharedOwner → StoryHost.Updated → Main._Process，未显示Wait/协程帧，不臆断adapter原因或动态证明该转抛。原GREEN证据helper错从dotnet成功摘要找prefix导致失败并停止；父控批准一次新SDK-free reader从原TRX唯一case stdout读取，原helper/STOP/日志保留，不重跑native。

父控收据见 `C:/evidence/m6/execution/tickets/150/coordinator-u14-verification.json`，原件在 `U14-story-current-value-red-green/`。这是独立复核后的当前值消费者小步，不是 #150 整票验收或集成；错/旧receipt、Enter echo/replay、完整资源hash、异常lifetime、legacy兼容及最终新源shared影响仍欠。#130/#126/#148/M6、GPU/人工/private-real/远端CI边界不变。

## 维护边界

保持 `Main.tscn` 的真实入口和现有测试装配，不新增第二 Godot 项目、替代业务实现或内部测试 seam。变更时先冻结独立预期与完整身份，再同步脚本/config/本页；需要时序变异、输出捕获或完整门诊断，进入 Game 分层与当前异常规则，不在本页重写历史证据。

### #46 Story package prepare bounded checks

`PrepareStoryPackageTests` 是 native-map 中的 direct31：真实 public Prepare → typed Save → Story/Visual strict Load、完整events/hash、UTF8/schema/limits/路径/无覆盖/owned cleanup。`StoryPackagePrepareProcessTests` 增10例真实 Editor/wrapper 契约；Prepare严格总数现89，Game metadata为505（502 positives，串行74/85/343）。这些数字是库存，不表示本轮跑了全部Game；本轮只新类31和完整Prepare89。前置依赖、schema与正常命令见[生产准备文档](../../../docs/design/story-package-preparation.md)。无Main/input/动作/effect/GPU/人工/CI或完整可见人工作者包交付声明。

### #46 authored package B

`AuthoredBattlePackageTests` adds two native-map cases (Win/Loss): committed original author data → normal public numeric/story Prepare → both languages strict Story/Visual Load → literal search, indexed colors/Texture2D, nonempty glyphs, complete source hash and real public Core outcome/owned receipt/branch stop. Inventory now 507 (504 positives; serial 74/85/345). Normal wrappers also prepare these exact inputs; see [sample instructions](../../../../samples/authored-battle/README.md). This is data/preparation evidence, not Main, SceneRunner, GPU, manual or CI acceptance. Earlier A direct31/Prepare89 evidence is reused, not rerun.

### #46 normal Main roundtrip C

`StoryRoundtripSceneTests` adds nine scene-dialogue cases, including Win/Loss × both languages through real Main search/keys, separate Attack/Target confirmations, actual action/display completion observations, owned result receipt, complete stop/return, same-URI isolation and cleanup. `StoryMainProcessTests` adds seven real argv/preload/READY/exit representatives. Core adds two actual-call binding tests (final inventory782). Game inventory is now523 =520 positives +3 controls, serial81/94/345. `GameBatches.Tests.ps1` remains16 offline metadata groups, not runtime evidence. See [C contract](../../../docs/design/main-story-roundtrip.md) and [fresh preparation/launch](../../../../samples/authored-battle/README.md). B Words/font changed; old prepared hashes are intentionally not rewritten. New owned outputs are required. This is not full Game/solution/fullgate, D effects/animation fidelity, GPU/manual/CI or #46/M4 completion.

#### C independent-review repair

Four additional Core regressions (inventory786) cover completed-call replay, older/foreign/pre-search calls and failed Start identity preservation. Typed story Start now requires the actual pending interaction origin. Two additional scene-dialogue cases cover deterministic Game-layer40ms pose boundaries and active-feedback Main removal/owned overlay cleanup; actual roundtrip observers verify each pose transition and second-search Start origin. Inventory525 =522 positives +3 controls, serial81/96/345. These counts describe registration, not a full-gate run.

### #46 enemy reference presentation D1

Two new Story scene-dialogue cases cover delayed displayed enemy-impact HP/result and independent physical-action tables (magic0/1/3, attack0/2, wait0/2, hit/evade, inclusive indices, recoil/return, dying threshold and indexed shift). Existing real Main observers additionally check production impact/release ticks and cleanup. Inventory527 =524 positives +3 controls, serial81/98/345; metadata remains16 groups. No Core/native schema/Prepare/author inputs changed. D1 does not close player effects, enemy idle-speed/counter binding, floating-number fidelity, GPU/manual/CI/full gate or #46/M4; see the [bounded contract](../../../docs/design/main-story-roundtrip.md#reference-presentation-checkpoint-d1-enemy-active-physical-action).


### #46 D2-A resource binding

Fourteen NativeVisual cases and seven public Story Prepare cases add optional effect/speed format-vs-capability, no-wrap int triples, zero idle speed, exact effect hash closure, retained checked planes, strict author schema and old-input compatibility. The two authored B cases additionally require the capability and literal original effect pixels. Inventory548 =545 positives+3 controls; serial81/98/366, metadata16 groups. NativeVisual63 and direct Story Prepare38 are the matching ranges; complete Prepare remains89. These are resource/preparation checks, not Main playback, an idle counter, floating feedback, GPU/manual/CI/fullgate or #46/M4 completion. Core/Main/clock are unchanged.


### #46 D2-B runtime presentation

StoryRoundtrip18 and StoryMainProcess9 cover the real Main player windup/three checked FX/impact-only enemy HP, persistent enemy gesture WORD counters and optional-format-vs-required-startup capability. The same `_Draw` snapshot is observed on actual `UpdateCompleted`; literal tables cover player positions/shift first-frame ordering/recoil/return, multi-idle cycling, zero WORD wrap and incoming enemy frame preservation. Existing40ms/late/wrap/restart, enemy impacts, owned Win/Loss × both languages, same-URI independence and active removal remain. Matching old Main11 and display10 are separate ranges, not full Game evidence. Inventory555=552 positives+3 controls, serial83/103/366; metadata16. No Prepare/native contract or authored source bytes change; D2-A evidence remains reusable. Floating numbers and retail label lifecycle are explicitly D2-C; GPU/manual/CI/full gate and #46/M4 completion are not claimed.

#### D2-B cross-action supplement

One Chinese `NonterminalActionsKeepIdleCounterAndConsumePendingOuterOnNextMainCallback` case uses an owned Win numeric author copy, changing only team18/enemy7 HP to1000 before the same public numeric/story Prepare. Source identity records the artificial override; selected Classic current HP remains1..32767. Independent SDLPal damage bounds (player<=297 per single hit; enemy<=23) guarantee three real Player/Enemy/Player actions with seed1 unchanged. Actual Main.UpdateCompleted observations assert two nonterminal feedback-end/next-callback pairs, literal speed2 counter persistence (configured idleFrames1 means frame0), no extra outer consumption on feedback end, exactly one overdue outer on the adjacent callback, live call/request ownership and real active-exit cleanup. Actual callback gaps are logged, not constrained to OS<40ms; deterministic39/40/late/wrap tests remain complementary. Inventory556=553 positives+3 controls, serial83/104/366; metadata16. This supplement is not a rerun of prior Story18/process9/Main11/display10 ranges, fullgate, GPU/manual/CI or parent acceptance.


### #46 D2-C-A digit resource vertical

NativeVisual69 (+6) covers the declared-number exact-closure RED/green, absence format-vs-capability, literal three-color groups, nominal1/legalaliases, preserved alpha, read-only lists/held native references and reload isolation, plus missing digits in all three groups. Public Story Prepare42 (+4) covers actual number publication/hash readback and blank/missing/duplicate author fields. Existing authored B2 now checks the original indexed digits through normal numeric/story Prepare in both languages. Whole Prepare89 remains the producer-process responsibility. Current Game inventory566=563 positives+3 controls, serial83/104/376; offline metadata remains16 groups. Main/clock/Core are unchanged and their old ranges are not relabeled/rerun for this resource checkpoint. C-B lifecycle, GPU/manual/CI/fullgate and #46/M4 completion remain pending.

### #46 D2-C-B runtime floating feedback

Normal Story now requires the optional numbers capability before publishing/instantiating runtime state. StoryRoundtrip adds literal uint39/40/439/440/wrap/late, overkill297-vs-HP43, zero/evade, first-empty16/full/reuse/clear and five-digit6px/color/layout checks. Actual Main.UpdateCompleted observers sample the same number/WORD snapshot `_Draw` uses, with player FX0 birth/FX1 first eligibility, enemy last-prehit birth/first-post-wait impact eligibility, HP1000/seed1 nonterminal three-action and existing owned-exit/return behavior. Story process adds absent-number no-READY startup. Current inventory570=567 positives+3controls, serial84/107/376, metadata16; matching ranges are complete scene-dialogue107 and StoryMainProcess10. No Core/native/Prepare/author/prepared changes; previous resource/preparation evidence is retained, not rerun. See [birth, wall-age and modern-label contract](../../../docs/design/main-story-roundtrip.md#d2-c-b-battle-owned-floating-numbers-and-modern-feedback). No fullgate/GPU/manual/CI or whole-ticket acceptance claim.


### #47 party-main slice A: resources and profile chain

`PalBattleVisualTeam` gains optional `EnemySlots[TargetId,EnemyPosition]`: one per story enemy slot in slot order; present means strict per-slot cross-check where duplicate enemy definition IDs stay separate slots, and a multi-enemy story team must not collapse into the legacy single pair. `NativeBattleVisualProfile` addresses `BattleContent.EnemyTeams` per slot, requires `PlayerPosition` to equal the reference `g_rgPlayerPos[party-1][0]` origin (battle.c:27-31; two players share the single authored player sprite as an authored limit), and the checked legacy `TargetId/EnemyPosition` projection throws for multi-slot teams instead of merging. `NativeStoryProfile` drops the blanket all-team single guard: capability is bounded by the actually selected closure (`TriggerScriptSession.ValidateEntry` validates each selected `startBattle` team; unselected multi-enemy teams remain data), and the party head `Players[0]` must match static leader role0. `PrepareStoryPackage` accepts optional strict `visual.teams[].enemies` (never mixed with the legacy pair, one or two slots). Committed `samples/authored-battle/{PartyWin,PartyLoss}` are original two-player authored inputs prepared through the unchanged public chain in fresh GUID-owned outputs; old AuthoredBattle* outputs stay untouched. Native-map +16: NativeVisual+8 (party slots, explicit single slot, legacy-pair/slot-count/slot-target/position/origin faults), Prepare+8 (authored party both languages, explicit single-slot publish, six strict metadata faults), Story party closure positive replaces the removed blanket multiplayer rejection case (count unchanged there). Counts now serial 84/107/395, inventory 589, metadata 16 groups. Resource/profile evidence only: Main party loop, PalStoryBattleView slot drawing and SceneRunner party roundtrips (re-inserted into `StoryRoundtripSceneTests.cs` by slice B), GPU/manual/CI/fullgate remain open.

### #47 party-main slice B: party battle runtime and SceneRunner roundtrips

`Main.Story` selects per member through the existing presentation modes: each alive member confirms attack (AttackMenu→Enter), then a target (TargetMenu→Enter); horizontal keys cycle the alive-slot highlight only inside the story battle (no exploration bleed), the reference has no previous-target memory, and the final member's target confirm builds the queue (Resolving). Resolving distinguishes queue advancement from action feedback by `LastAction` reference, so dead actors' passes never re-present the previous action; retarget follows the Core result. Player/enemy presentations are built per actor at the reference party origins (authored `PlayerPosition` column plus reference `g_rgPlayerPos[1][1]` 256,152) and per `EnemySlots` position; floating numbers are born at the struck actor's position on the same real prehit/impact callback boundaries. `PalStoryBattleView` draws every member and slot, per-actor HP panel, selector/target carets, while legacy scalar snapshot fields stay index-zero projections. `StoryRoundtripSceneTests` adds the two re-inserted party roundtrips (PartyWin: seed-1 two-round Won with member-1-first dice, one enemy answer, dead-slot pass and retarget bounds from the fight.c formula; PartyLoss: enemy-first rounds, member death pass, single living selector, Lost closure, post-result input isolation) plus same-URI fresh-Host reuse and leader-unchanged checks. Counts now serial 84/109/395, inventory 591, game positives 588. Scene-dialogue 109/109 complete this slice; process batch not rerun (its argv/READY/exit contracts deliver no input events and are unchanged); no native-map/Prepare sources touched. No GPU/manual desktop/CI/fullgate, no #47 whole-ticket or M4 completion claim.
