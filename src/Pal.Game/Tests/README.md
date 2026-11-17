# Game 测试当前入口

Game 是[默认 Unit 反馈](../../../docs/testing/getting-started.md)之后的按需路径，不是第一阶段自动 CI 或测试合并要求。本页只维护当前 Game 测试入口、覆盖口径与失败判定；Configure→立即入树→输入→生产更新完成→清理的示例只在[Game 专题](../../../docs/testing/game-test-layers.md#新人最小模型仅按需进入-godot)维护，不复制第二套规则或命令。职责选择见[测试职责矩阵](../../../docs/testing/test-layers.md)，初始化、SceneRunner 生命周期、时序边界和完整门规则见[Game 测试分层](../../../docs/testing/game-test-layers.md)。旧命令、变异、性能对照、M2/M3 交付年表与历史失败见[历史运行记录](../../../docs/archive/m3/game-test-run-history.md)。

## 当前入口与覆盖

Game 测试继续使用现有 Godot 程序集、固定 GdUnit 包和 `tests/Pal.Game.Tests/game.runsettings`。真实版本、bootstrap 和包来源以[当前包消费说明](../../../docs/testing/gdunit-source-packages.md)及仓库配置为准。测试资源是本例自有、忽略的 native fixture；真实资源和私密证据不进入提交或上传。

共享完整门仍唯一使用 `scripts/Test-M0.ps1`，依赖 Core→CLI→Prepare→Game 的串行顺序和当前脚本生成的批次/身份。当前库存和批次从 `scripts/GameTestBatches.ps1` 与测试定义核对，不在本页复制案例计数；已完成运行见历史索引并保持原身份。保留完整门实现不证明当前 M4 候选的完整门运行兼容性已验证。

## #161 first-battle 适配器用例的当前执行归属

`TriggerDialogueFixtureTests.First_battle_` 全族（含参数化实例）归入已有 `scene-realfull`，不再属于 `scene-dialogue`。其余 TriggerDialogue、NpcInteraction、DialogueDisplay 和 Exploration 场景仍在 `scene-dialogue`。两者按完整方法前缀互斥分区；没有新增批次或改变原会话预算、断言、捕获设置、控制测试与完整门总执行集合。full 保留既有私有真实正例，committable 仍只排除该私有正例，公开 first-battle 适配器用例不被排除。

定点 first-battle 用例须改用 `-GameBatch scene-realfull`；旧的 `-GameBatch scene-dialogue -Filter FullyQualifiedName~Pal.Game.Tests.TriggerDialogueFixtureTests.First_battle_` 不再是合法归属。需要运行整个 TriggerDialogue 类时，在 PowerShell 内传 `-GameBatch @('scene-dialogue', 'scene-realfull') -Filter FullyQualifiedName~Pal.Game.Tests.TriggerDialogueFixtureTests.`，由同一 Range 入口串行执行两个精确交集，而不是把整类重复运行两次。选择整个批次时省略 Filter。路径、资源包、全新输出目录和全局引擎串行要求仍按下方入口核对。

本票的二阶正控分别从真实敌方行动产生 Dying，再让 coverer role0 的真实 FriendDeath 消费前一个钩子通过0090写入的入口；经适配器、实际显示更新与输入完成 owned ACK。两个新增钩子正控只验证到仍在进行的战斗，不伪造 Completion 或外层返回；完整 Won/Lost 与外层回环仍由相应独立正控负责。初始态与完成 warmup 后的拒绝优先级使用真实搜索请求；display-busy 来自另一次公开显示 Start，不构造假请求/phase。

first-battle 的 `zeroDisplay` 仅为默认关闭的人工准备变体：保留 team18 双敌、原09FE操作数、A96E/A9B5声明及严格双语 Load，以独立字面命令移除显示。非零 portrait7 在合法静态分支声明并经正式 prepare 绑定，不修改只读 checked map。Load/Initialize 后才故意破坏本例字体、prompt 和可选 portrait planes；无 portrait 的正控还绑定255色 host palette。成功标准是忽略未使用资源、保留真实 pending battle 与缺键 seed；坏 portrait 必须在 Start 前拒绝且全状态不变。这不删掉严格战斗来伪装 world-bound 零显示，也不代表完整零售剧情/热重载保证。

此前完整 dialogue 会话的超时中止与输出排空失败仍是失败；拆批不将其部分结果转签成验收，也不以补跑缺失项拼接成功。新分区必须分别取得新鲜完整 TRX、精确身份集合、原生退出、双流完成及清理证据；离线分区检查不等于 runtime 或整票/M6 验收。

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

## 异种敌槽正式链

`StoryRoundtripSceneTests.PreparedHeterogeneousSlotsUseOwnActionsTargetsAndFreshMain` 属于
`scene-battle-light`。它从活跃 PartyWin author 的只读副本生成 test-owned 97/98 原生图像，
经正式 Prepare 后在正常 Main/SceneRunner 的 `UpdateCompleted` 内检查首呈现、
TargetIndex 的当前帧高度、ActorIndex 的独立物理帧/等待、重复 id7 的死槽隔离，
并以同 URI 的新 Main 重复首轮及校验输入/产物 hash。它不证明 GPU 像素、fade、可听声音、
私有原档来源或完整 #136；既有同种/单敌/失败用例仍保留。

`StoryRoundtripSceneTests.FailedStoryPreloadPublishesNoPartialScene` 保留缺文件预加载拒绝，
并以同一合法异种 fixture 验证真实部分纹理初始化：view 已接管槽0的六张纹理，接管槽1
首张纹理后由实例观察器精确抛错；在 `Main.Failed` 内记录全部原生实例已释放、离树 view
已 Free、owner/cache 集合为空、无半初始化战斗发布及原错误传播。finally 仅对失败断言后
仍存活的自有纹理救援 Dispose。随后相同 URI 的全新 Main 在首个战斗 `UpdateCompleted`
检查健康初态及资源/输入 hash；成功场景退出也核所有纹理实例释放。不清全局资源缓存。

纹理及逐槽 idle/显示状态属于每个 Main/view，不属于共享 native Resource。初始化成功后由
正常结束/`ExitTree` 释放；入树前初始化失败由 Main 显式释放 owner 的全部纹理并 Free，
不能依赖从未入树节点的 `ExitTree`。重复释放幂等。未来持久化只提交业务态，由新 Main
按显式 URI/业务态重建显示资源，不保存纹理/原生句柄；#148 的 D3/D4 与 v1 schema 不在此冻结。

## 本地组合登记（#200 + #203/#204/#136）

保留 #200 的27个 Game Prepare 与12个 Prepare 进程案例及 #201 严格批准计数入口；
叠加 #203 单玩家双敌、#136 异种 Main 与三个 native 案例后，native-map702、
scene-battle-light30、Game库存983、full980/committable979 positives，Prepare仍为101。
以上为独立批准增量的登记，不是运行通过；各历史章节计数仍只说明原候选。
范围入口保持 `Test-Range.ps1`，没有采用 #136 来源工作树中的 harness 裁撤或旧 loop。

## #200 显式投影 Prepare（路线 A 第三叶）

`PrepareBattleDataTests.ProjectionPrepare*` 新增27例：三参数公开入口、独立人工双源原字节/checked回读、BOM与空白、坏hash/长度/角色/覆盖、空值/缺失/目录/输入输出边界/reparse、两种写句柄冲突、释放lease、两参数不发现收据及无覆盖。两个固定人工JSON夹具保留[4,0]角色/[65535,0]队伍、14输入、10源槽（同对象97双实例）及503叶覆盖；预期SHA/字面槽映射不从生产parser/writer反推。`BattleDataPrepareProcessTests` 原18例保留，新增12例负责正常wrapper/Editor投影入口、必要错误argv/exit/无marker及路径预检/错误传播。

完整影响10路径（含两人工JSON、两生产入口、两测试、批次/metadata、两文档），无新Resource/UID或工具依赖。登记native-map699、库存978、full975/committable974 positives、Prepare101；两scope精确方法归属、原3控制/私有排除/会话预算/捕获不变。SOURCE metadata和登记不是runtime通过，实际RED/GREEN/数字退出以本轮冻结收据为准。未做晚期路径替换/磁盘/rename/cleanup故障注入，不补签Story/Main、正式CLI投影、私有/GPU/桌面/CI/完整门或#134；原165/168失败和#168装备WORD缺口保留。

## #199 离线 receipt 原字节核对（路线 A 第二叶）

新增95个 `NativeBattleContentTests.ReceiptParser*` 案例，经公开 `ProjectedBattleSource.Parse` 验证独立人工 numeric/receipt：角色[4,0]、队伍[65535,0]、同object97双实例、完整5槽与14输入；两类实际原字节SHA/长度（独立.NET cryptography预期）、BOM/空白/换行、等长改写拒绝、buffer与公开集合隔离。58种结构/声明/字段覆盖/选择/能力反例、9种非法整数拼写/类型各遍历多个整数section、21种raw UTF8/JSON/depth/递归重复键、surrogate属性名/文字/注释与两类叶路径碰撞反例，以及声明注释/非认证文字正例。显式空poisons数组必须有独立fieldSources条目，不能按空缺省忽略。

完整影响集7路径（新增离线静态类及其uid，测试/批次登记/两份文档），不改Core/工具/Prepare/Story/Main或运行时JSON。native-map登记672（577+95）、库存951、full948/committable947 positives，原3控制和私有排除/300000ms/捕获不变。metadata精确方法计数和两scope唯一归属；这些源码计数不是实测，实际运行/数字退出与指定RED原件由本轮冻结收据记录。不重跑旧165/168失败，不代签Prepare/Main/私有/GPU/桌面/CI/fullM6。

## #198 typed 投影来源（路线 A 首叶）

`NativeBattleContentTests` 新增98例：独立人工来源经 Write→ResourceSaver→LoadChecked 保留分离的receipt/numeric身份、全部14输入、有序[4,0]角色、[65535,0]队伍与全部源槽（含同对象双实例）；构造输入/native后变更、再次Load及公开集合写入均不能污染既有checked结果。另覆盖单role4/0、单敌、长度/SHA/WORD边界、普通旧无来源null、15种未选择非默认字段、76种选中结构/选择/host-free能力拒绝、旧Load/CreateCore不可绕过、writer拒绝错配。预期来自独立字面输入与断言，不从投影器或Write反推。

本叶完整7路径预算含测试/文档/登记，不新增Resource/UID/fixture资产或Core API。源码库存为856，native-map577（原479+98），full853/committable852 positives，原3控制和私有排除不变；离线metadata校验精确方法数量及两scope唯一native-map归属，不削弱guard或过滤完整native-map。本叶不执行receipt解析、Prepare投影接线、Story/Main来源闭环或私有/GPU/桌面/CI/完整门，具体执行/数字退出绑定本轮收据；源码计数不是实测。原165/168停止失败和#168装备WORD运输缺口仍保留。

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

## #150 U16 Enter echo 反例与正常双循环（有界实测）

父控随后审阅U15候选并补严当前world身份、live-view键集合及mode错误文本；实测身份为 `03334ae351a4e08d90eecec6088b7e26ecafb210 + dirty`、patch SHA `1b4198b6ca40141fa0b9075e85be65dc90b0542ad88f8c0e62bcf3dea11527a5`，不是以下U15源码阶段或U14运行的转签。只经原公开scene-realfull完整Synthetic Won FQN执行两次：Main.cs临时去掉confirm的Echo门，准确Failed1，指定 `Story rejected receipt or Enter echo must preserve presentation mode: expected AttackMenu, actual TargetMenu.`；完整Main bytes恢复后，唯一fresh GREEN **Completed/Passed1/RunInfo0**。两轮真实发现650、精确所选/实际1，4roots分别0/0/0/1与全0，完整双EOF/output/cleanup且无timeout/cancel；预算2/2，没有重跑或额外SDK探针。

RED原生TRX `86e8d680-aea4-4fb3-8ec0-c2cf36fe5ee1`（Range `7ac62a4a-366a-49ef-9117-7051b4996a2c`），GREEN原生TRX `c401caff-d4d2-4893-91ad-c0c2a62b7cda`（Range `be47ee9a-1054-48ba-b152-d003a94bbd3f`）。RED真实栈仅ObserveEnterEcho:193显式重抛→Won:487；首Main完成observer来源由冻结实测源码控制流绑定，不声称原生栈有来源帧或adapter原因/Main.Failure为空。发现栈条件不符先停，获父控一次SDK-free新分类后才恢复/进入GREEN；原helper未改，原失败/唯一镜像ErrorRunInfo保留。GREEN报告的旧静态stack-limit说明不作为RED栈证据。

GREEN唯一case原TRX stdout实际输出两次不同origin/call、`rewards=2 XP=104 hostCashGranted=192 prefixes=True/True outer=True/True`。公有adapter错/foreign/stale/replay拒绝、两处真实echo首Main完成快照、独立HP/MP/XP银行、原3次NOW preview/RNG隔离、prepared typed+两manifest声明闭包SHA及numeric完整初值、Dispose前后只读/正常原生释放，由冻结完整方法和完整case通过证明，不伪造逐字段独立日志。父控独立重核两轮各221源码/6工具、原TRX图/counters/真实root结果、680封存文件及恢复actualDLL/PDB，均通过；本树filtered marker和固定4包前后未变。证据 `C:/evidence/m6/execution/tickets/150/U16-enter-echo-red-green/`、`coordinator-u16-verification.json`。实测后只有本README结果文字；不冒称随后clean检查点重跑。异常preview lifetime、firstBattle active-exit、legacy新source-bound及最终shared影响仍待完成，不关闭或集成#150/#130/M6。

## #150 U20 生命周期反例与恢复验证（有界实测）

U17 的候选经父控修正并实际验证：U18 虽捕获指定 RNG 断言，但同时出现 runner 对已脱树 Main 重复 `RemoveChild` 的清理错误，不能计为有效指定 RED；原件保留。改为既有 StoryHost 的 container-owned runner 模式后，U19 又因父控错写 `GdUnit4.Api.ISceneRunner` 在编译阶段失败（仅 SDK/build 两命令，无 discovery/native/TRX），同样保留且不转签。核实际固定接口并仅更正两处为 `GdUnit4.ISceneRunner` 后，U20 在 `28552d25163c823d3bcddc8250387f96a912b825 + dirty`、patch SHA `a8c14c71ecb8de5971002c6f7fa370a5be9a1d413e28462e2b746286d2ea19b1` 上仅执行原公开 scene-realfull / Synthetic Won 完整 FQN 两次：Main active-only 漏释放自有 native RNG 产生唯一指定 `First-battle active exit must dispose its owned native RNG.`；精确完整 Main 新写恢复后，fresh 同例 **Completed/Passed1/RunInfo0**。没有 remove_child 或其它非指定诊断，RED 四命令 0/0/0/1、GREEN 全0，双 EOF/output/cleanup 完整且无 timeout/cancel，两轮各 discovery650、精确所选/实际1；时限、依赖、case库存和分区不变。

RED native `a4a92e58-b4d6-491b-80bd-1c7e153e5e7e`（Range `ebf3bda3-6211-4490-be12-7aa672cf03e7`）原生栈只有 Won driver:799 重抛及 GdUnit 帧；TreeExited 首标志/唯一 RNG throw/订阅/真实 RemoveChild/catch/rethrow 是冻结源码绑定的来源，不伪造 callback/Main 原生帧。GREEN native `50342d0c-90af-4114-b099-bff6ecab8a16`（Range `feb7636a-030a-447f-b82b-b737ff5f4c8d`）实际运行目录 DLL/PDB 与 RED 不同、和恢复源码/原TRX CodeBase/归档/current 相符，非仅恢复 bytes/mtime。父控独立复核两轮221源码/6工具、682个运行原件及838项全证据索引、实际TRX图/精确集合/四命令原双流/包及marker不变；另一次只读独立 Standards/Spec 审查无阻塞。实测后仅本 README 补结果，不称后续 clean checkpoint 重新运行。

唯一 case 的两循环 trace 直接记录 rewards2/XP104/hostCashGranted192/prefixes和outer均True，但位于新增退出段之前，不单凭该行签退出成功。同URI新 Main 首完成的独立初态、真实返回 preview lease 的 caller using 异常展开与 RNG 释放、实际 active TreeExited 的 RNG-first 清理/无假ACK/完整owner保持、末尾 fresh/container Free 及资源SHA不变，来自冻结无条件断言和完整方法 Completed；无独立末尾 trace，不虚构字段。这不是生产 StartCall 内部异常或 factory 构造 catch 的动态覆盖。父控收据见 `C:/evidence/m6/execution/tickets/150/coordinator-u20-verification.json`，原件在 `U20-active-exit-namespace-red-green/`。仍欠新 source-bound legacy/Main/Story 兼容、最终 shared 影响与整票独立审查/实际集成；#150/#130/M6、GPU/人工/private-real/CI 未整体验收。

## #150 U17 同 URI / active-exit / 返回 lease 异常（仅源码候选）

仅在原 Synthetic Won 方法内补生命周期观察，不新增 case/FQN、fixture、UID、metadata 或生产接口；原两次完整闭环、数字/回执/echo/三次普通 NOW preview/资源 SHA/正常 Dispose 断言保留。本单元**没有运行 SDK、Godot、Prepare、build、discovery、test、import 或 probe**，U16 GREEN 不转签这些新增断言，也不预告其编译或 runtime 通过。

- 第一 Main 正常 Dispose 后，仍使用本例同一 prepared URI 创建并配置第二个正常 Main，入树前订阅。首个实际 `Main.UpdateCompleted` 先置标志，再无条件核独立 owner/world/session/原生 RNG、HP100/100、MP20/5、XP0 与奖励 ledger0，以及冻结资源初值/SHA。driver 仅等待完成标志。
- 第二 Main 经真实 Enter/显示进入 active firstBattle，在真实 Main 完成回调捕获 pending/lifecycle/完整 owner 记录、world/source/call、adapter/display/overlay/实际 RNG；追加准备只给10s guard，不扩原120s/60s循环、2s WaitFor、280000ms方法或300000ms会话。真实 `RemoveChild` 触发 `TreeExited` 的首次观察先置标志，**先核 owned native RNG disposed**（命名错误文本），再核 Main/adapter/factory/world/overlay 引用清理、保留的 Core pending/完整记录和零奖励无假 ACK/Completion/外层返还；observer 异常捕获后在 driver 重抛，不变成 Main.Failure。RealFlowScene 仍负责自有输入、节点最终 free 与 globals 恢复。退出订阅释放责任另按生产源码核对，不声称直接枚举了 private/.NET delegates。
- 首 Main 初次完成中直接调用既有真实 `PreviewSessionFactory()`，在返回 native lifetime 的 `using` 内对真实 session 公开 `Start` 传不存在的 entry；按实际 `GetEntry` 契约精确核 `InvalidDataException` 消息，异常退出后核该真实 RNG fork 已释放、生产 RNG Seed/State 及完整业务快照不变。不替换 factory/session，不造假异常执行器；**只覆盖已转交 lease 的 caller 异常展开**，不宣称动态进入生产 `PalTriggerDialogue.StartCall` 内异常或 factory 构造 catch，这两处只做源码责任核对。

退出边界来自本地固定 GodotSharp 4.7.2 XML：`TreeExited` 在 `NotificationExitTree` 后，节点已不活跃，`_ExitTree` 在所有子节点离树后执行；不是 TreeExiting/idle 恢复或等 disposed 成功再采样。未应用的 active-only RNG 漏释放 mutation、完整恢复 SHA 和 source patch 随 `C:/evidence/m6/execution/tickets/150/U17-lifetime-source/` 封存，待父控实读后另授 native 预算。不能据源码设计声称反例已失败或恢复已通过。

U15 后续 route 更正（保留其历史原文）：heavy/light 不在 Test-Range allowlist **不等于必须重跑三轮 M0**。当前入口已允许绕过 Range 的正常 dotnet/VSTest，仍须固定 bundle bootstrap、批准 GODOT_BIN/PAL_TEST_GODOT、所需 build/import 与全局串行；具体 legacy/Main/Story 定点 compatibility 集合、命令及预算由父控另行冻结，不扩 allowlist、不伪装 Range/fullgate。本候选与新 source-bound compatibility/最终 shared 影响仍待实测及审阅；#150 未整票验收/集成，不代表 #130/M6、GPU/人工/private-real/CI 完成。

## #150 U15 公有回执拒绝 / Enter echo / prepared 闭包（仅源码候选）

仅扩展既有 `Synthetic_closure_full_flow_Won_continuation_and_changed_exploration`，不增加 case/FQN、metadata、UID 或生产接口。原两次真实 search / battle / owned ACK / 外层返回 / 首移动、独立 HP/MP/XP 数字、NOW preview 和真实 RNG 隔离断言保留。当前只读源码检查，**未运行 SDK、Godot、Prepare、build、discovery、test、import 或 probe**；U14 与旧 Synthetic 运行不转签本候选，仍待父控实读补丁后另批 scoped native 预算。

- 经真实公有 `main.Dialogue.AcknowledgeBattle`：首战所属 completion 已生成但 Main 尚未正常消费时，以错误 request 拒绝；Main 已消费后重复拒绝；第二真实 lifecycle 等待时，以第一 receipt 配当前正确 request 证明 foreign ownership 拒绝，以第一旧 request + receipt 证明 stale replay 拒绝，第二提交后再拒绝重复。每次同时核完整 owner 记录及引用、live view/HPMP、CallId/SourceRequestId/phase/PendingBattle/返回入口与阻塞状态、Main lifecycle/current/settlement/completion、外层 pending、探索位置和奖励账本不变；不抢发正常 ACK。两个不同 owned receipt 各为52XP/96cash，角色最终XP104；实际 `StoryCashGranted` 是 Main 的 grant ledger（96→192），不是新建持久钱包，trace 不再写死 cash192。
- 在首战 AttackMenu 与正常物理 Enter 后的 TargetMenu，注入 `InputEventKey { Pressed=true, Echo=true }`，通过 `Input.ParseInputEvent` / `FlushBufferedEvents` 进入原分发。入树前观察器在交付后的**首个 Main.UpdateCompleted** 先置完成标志，再无条件核完整业务快照；driver 只等标志，键在 finally 释放，合法下一次物理 Enter 仍驱动原流程。默认 WaitFor 实际为2s，原120s/60s driver、280000ms方法与300000ms批次均不放宽。不冻结允许自然推进的 idle 动画时钟，不冒称 GPU 像素核对。
- 复用 `NativeContentDependencies.ContentDependencies`，union firstBattle `ResourceSha256` 与 visual `ResourceSha256`，保存实际文件SHA：profile、static profile / PackedScene及其typed map/tile/sprite资源、palette、visual、numeric/global objects、完整事件index+files、双语语言、font/glyph、prompts及声明image/player/effect/number依赖（外部图像含已有.import remap）。在初始preview后、两次owned返回、两次移动preview后、runner Dispose前后核文件SHA及持有的原生numeric资源完整初始角色值不变。仅边界内prepared声明闭包：不扫目录、不写资源/缓存，不包括fixture私有author临时输入、scripts/engine或生成的 `.godot` import输出；文件SHA不证明GPU或所有内存对象不可变。

仍欠：本候选真实执行/负控、异常preview lease释放及firstBattle active-exit覆盖、legacy/Main/Story新source-bound兼容和最终shared影响。已有 `ActiveBattleExitReleasesOverlayAndBindingsBeforeOwnedNodesAreFreed` 属heavy-a、`NormalMainSearchBattleAndResultReturnThroughOwnedLifecycle` 属heavy-b/c、Main失败用例属light；当前 Test-Range allowlist不接受这些heavy/light批次，不能假填公开窄route，须父控另批既有完整门或后续合法范围方案。dialogue-only旧adapter在scene-dialogue、legacy无binding loader在native-map可用既有公开范围。完整映射随U15外部源码收据保存；未扩路由或执行任何后续case。#150仍未整票验收/集成，不代表#130/#126/#148、GPU/人工/private-real/CI或M6完成。

## #150 U13 Story 前缀消费者（仅源码候选，未运行 SDK）

原 `PartyTwoPlayersPickTargetsByCyclingExecuteQueueAndWin` 现在显式消费 `new AuthoredStoryFixture("PartyWin", partyHpMpPrefixAmount: -3)`；仍由原 Prepare、native profile 和 Main 真实 search 装配，不修改启动 HP100/95、MP20 或 prepared 文件。入树前已安装的 `StoryHost` 观察器在本次 search 交付且新 `Session.CallId` 出现后的首个 `Main.UpdateCompleted` 内无条件取样，记录前 CallId/request/phase 与新身份；不等 HP/phase 成功形状才采样，也不把尚未 Start 的排队回调当作前缀效果。原5s driver等待同时要求观察器已采样，避免FramePostDraw先建立display、但新CallId尚未经过Main完成回调时提前检查；不增加超时或等待正确数值。预期全队 HP97/92、MP17；实际 battle 入场先核当前 HP 与完整角色初值，再核 pending/lifecycle 引用，避免 current-value 数字负控被引用检查抢先截获。

owned 最终 ACK 发布后的首个适用 Main 完成回调按独立完整分支表核 HP：evade→123/121；命中 role0，damage20/21→113/121、22/23→112/121；命中 role1，damage21/22→123/110、23/24→123/109。两人 MP 均33，XP/cash 奖励0、level2、maxHP150/maxMP50不变；分支来自真实动作，数字不是由 owner/lifecycle 的实测 HP 反算。保留完整结算记录对接、同 owner/world/live view、party[0,1]、原真实动作/队列/目标/显示/后缀/探索返回及同 URI 新 Main 未 search 前原初值隔离检查。

本单元只冻结两路径 source patch、独立数值及实际行级 startup-value 消费负控方案；没有 SDK/build/discovery/Prepare/Godot/test/变异运行，不宣称新增断言已编译或通过，不复跑 U11 顺序 RED。原无参 FQN、RequireGodotRuntime、90000ms 方法、5s 观测和公开 scene-realfull 精确路由/300000ms 会话预算、650库存及所有分区不变。后续须父控实读 patch/数值/负控并另给原生预算；#150 其余验收及最终新 source-bound 影响证据仍欠，#155 准备436不是本候选 Main 证据。

## #150 U14 Story 当前值数字反例与恢复验证

U13 更正候选现已通过有界真实验证：临时仅在 Main 的 Story 入战消费点改为 startup lifecycle，prefix/owner/world/party 保持，公开 `scene-realfull` 精确 PartyTwo 单例产生指定 HP 数字 RED；精确恢复完整生产源码后，另一次 fresh 单例实际 `Completed / Passed1 / RunInfo0`，两轮 discovery650、所选/实际精确1，四命令均完整退出/双 EOF/清理（RED 0/0/0/1、GREEN 全0）。未重跑 U11 party-order RED，未修改 timeout/guard/allowlist。父控独立复核每轮221项源码、6项工具身份及原TRX/精确集合/实际DLL-PDB归档与恢复构建绑定；实测是 `4c11731 + dirty`、patch SHA `569d080ac5568ab69a3fae15d19493d761d16947c9f209a10a5f120b011f3f68`，不是后续 clean checkpoint 重跑。实测后仅本 README 补结果。

原 GREEN case stdout 记录 prefix HP97/92、MP17/17，以及动作34/44/敌对role1伤24/36、combatHP97/68；独立分支表的战后预期 HP123/109、MP33、完整 owned 提交与同URI新Main原初值，由冻结源码的无条件断言及完整方法通过作证，不称原日志独立输出了最终ACK/MP数值。RED原生栈定位 ObserveSharedOwner → StoryHost.Updated → Main._Process，未显示Wait/协程帧，不臆断adapter原因或动态证明该转抛。原GREEN证据helper错从dotnet成功摘要找prefix导致失败并停止；父控批准一次新SDK-free reader从原TRX唯一case stdout读取，原helper/STOP/日志保留，不重跑native。

父控收据见 `C:/evidence/m6/execution/tickets/150/coordinator-u14-verification.json`，原件在 `U14-story-current-value-red-green/`。这是独立复核后的当前值消费者小步，不是 #150 整票验收或集成；错/旧receipt、Enter echo/replay、完整资源hash、异常lifetime、legacy兼容及最终新源shared影响仍欠。#130/#126/#148/M6、GPU/人工/private-real/远端CI边界不变。

## #150 U21/U22 兼容失败与 U23 boost 预期修正候选

U21 使用既有正常 direct-dotnet 路由执行 heavy-a（5 Passed）及 heavy-b（5 Passed/1 Failed）；唯一失败为 `FriendDeathHookHealsCovererAndBattleStillEndsWon` 的 `panel=99/0`，原 Failed/唯一同文 Error RunInfo、四条 root 的 0/0/0/1 退出与完整双流保留，heavy-c/d/light 的35例未执行。没有把剩余预算自动续用。U22 在不含 #150 的已准备 `67be98f` 工作树，使用相同 heavy-b 六例 filter/expected 做一次独立基线：真实 5 Passed/1 Failed、同一唯一 `panel=99/0`，无其它原生诊断；两条 root 为 0/1，仍是 FAILED 而不是 GREEN。父控独立核验两轮原件、源码、实际 DLL/PDB、包/marker 与历史残留保留，分别见 `coordinator-u21-verification.json`、`coordinator-u22-verification.json`，证据位于 `C:/evidence/m6/execution/tickets/150/`。

旧断言的“native magicPower/flee 未声明、按零计算”前提未跟随 #127：Midbattle 原始现代 players 明确声明 attack40/magicPower20/dexterity100/fleeRate10，typed pipeline 已批准保留这些值。只读 SDLPal `script.c` 的 0x0030 按各 base word 乘百分比，面板是五个 boost 寄存器之和；独立脚本分支表为兄弟→2、我会胜→2、悲为力→10+5=15、回家→90+9=99。U23 仅把原方法的宽泛 `2/10/90` 枚举替换为与已观察对话分支一一对应的精确预期，仍逐项核两成员/仅caller非零，保留所有 HP/MP/Won/后缀/返回/cleanup 断言；不让被测实现生成预期，不修改生产、raw输入、fixture、Seed、timeout、分区或库存。

冻结时此两路径修正仅 source 候选；随后 U23 已实际完成原 heavy-b/c/d/light（6+3+4+28=41例）及 build/discovery：六条 root 全 exit0、四个独立 TRX 均 Completed/全部 Passed/RunInfo0，其余 counter0，实际 discovery650、完整输出/清理，无 timeout/cancel 或额外原生诊断。唯一 FriendDeathHook case stdout 直接记录回家分支 expected99、panel99/0、HP149/MP35/actions7；本 case 没有动态覆盖其余三个分支，不补写其通过。实际运行目录 DLL/PDB 与新 source/CodeBase 绑定，不借 U21 旧产物。

父控独立核518原件、221源码/6工具、1463 tracked、四 TRX 精确集合/完整身份图/原诊断/实际产物与包/marker不变；首次父控 helper 错从 dotnet 成功摘要寻找 engine banner 而停止，原 exit1 保留，另一次 SDK-free reader 从各原 TRX Summary/Output/StdOut 核对4.7.2 banner成功，未复跑 native，case trace仍只取唯一 UnitTestResult。收据见 `coordinator-u23-verification.json`。U21 heavy-a5按未变所选方法/helpers/生产依赖责任复用，联合兼容范围46不是同轮新46，也不是 clean checkpoint 重跑。运行后仅 README 补本实测结果；仍欠最终新 source-bound shared 影响、整票 review/实际 integration，不是 #150/#130/M6、GPU/人工/private-real/CI 验收。

## #150 最终 shared 原件与整票源码审查

U24 在 **clean `4363ae1a5c677ccfe3458702f98cb1eed2bc40ea`** 仅运行一次原公共 Range：`cli-integration/prepare/game`，Game 选择 `process/scene-dialogue/native-map`，无公共 Filter/Skip。原11条 SDK roots 全 exit0、完整输出/清理，无 timeout/cancel；CLI 为360 Passed＋仅两条合法平台 NotExecuted，Prepare89 Passed，Game **595 Passed＝85＋74＋436**，实际发现650，非完整647/646执行。三个原 TRX 均 Completed，只有 CLI 的两条实际 skip Warning RunInfo，Prepare/Game RunInfo0。正常完整 unfiltered native-map 由原入口自行刷新 marker，head4363；没有人工写入、复制或回退 marker。

原 Game Summary 的三条 `Profile.tres` Child ERROR 与四条 CaseMismatch WARNING 曾触发 STOP，不能凭595 Passed忽略。父控及 U25 纯 SDK-free 复核证明：前三条唯一绑定主动写坏资源的 `BrokenProfileFailsWithBothSelectedUrisAndNoReady:1 ("malformed", "PalExplorationProfile")`，实际自有子进程 exit1、指定拒绝原因/两URI且无READY；后四条唯一绑定主动将路径转大写的 Windows 规范化用例，requested/stored 仅case不同，四个直接原生 backtrace 均命名该测试。其 unique case stdout 本身无warning，warning来自 Summary；不伪造输出位置。历史 #153 原 TRX 存在相同七条诊断，五个直接文件与两轮实际编译输入hash一致。仅当前整TRX/语义Summary hash、精确case/owned GUID及四完整stderr块证明成立后分类，不是全局警告/ERROR白名单，也不证明跨平台export。

U25 保留三次 reader helper 失败（混合archive schema、warm空数组成员投影追加null、PowerShell `$Error` 自动变量碰撞），均先STOP、父控核原件、另新名 SDK-free 精确恢复；最终完整audit exit0，未新增任何SDK/native/import/metadata执行。原 U24 `audit.ps1` 从未执行，不称其failed。父控再独立重核 **4509** 原封存文件、289编译输入/1463 tracked、71实际runtime/36包文件、11root双流/身份/时间、原Version7发现/三TRX完整图/1046唯一execution、实际DLL/PDB/CodeBase与marker前后。31冻结身份中仅5项有单独归档，其余26是identity-only；Prepare宿主27项只核到prepare.test的原before/after，不虚构pre-Game bytes归档或环境before/after。编译日志为7条distinct warning、MSBuild7 Warning(s)、含重复摘要14个entry匹配/baseline14，计数不混用。

完整10路径 `67be98f…4363ae1` 已由两个独立fresh reviewer分别完成 Standards/Spec 源码审查，无可行动阻塞；source review不代替上述原生证明，也不要求为未变no-combat Dynamic路径凑跑库存。原件及父控收据见 `C:/evidence/m6/execution/tickets/150/{U24-final-shared-impact/,U25-shared-sdk-free-audit/,coordinator-u25-verification.json}`；独立review由 workflow `d750a3d1-ce3d-47ba-a846-aabe0863e43e` 保存。此段是运行后README结果补充，编译源码/工具与实际clean4363字节相同，不称随后文档checkpoint重跑。整票实际集成/Issue状态另以父控集成回执为准；#130聚合、#126/#148与完整M6、GPU/人工/private-real/远端CI/fullM0均不由本段自动验收。

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

### #210 outer five-enemy-slot migration (M6-A #138 leaf)

The dense/loaded enemy-slot acceptance widens from 1–2 to 5 across the outer chain, migrating the boundary #207 deliberately retained (not a rollback): `BattleDataProjector` projects up to five dense slots per team (source rows still 0..4; zero/empty/zero-objectId teams stay rejected), `NativeBattleContent.CreateChecked` and the no-receipt `Write(content, null)` enforce the same 1..5 loaded domain with teamId diagnostics, `BattleProjectionSourceInfo` slot rows accept 1..5 loaded per team, and `PrepareStoryPackage`'s author schema accepts 1..5 per-slot `visual.teams[].enemies` bindings matching the numeric enemy slots one-to-one. Repeated enemy definitions stay separate slots (id7 × 5 keeps independent HP 43..47); zero empty-slot/FFFF skip semantics stay with T13b (#140). Migrations: the projection `three` refusal row is replaced by the five-dense positive, native `enemies-overcapacity` and Prepare `three-slots` refusals move to six slots (`one to five enemy slots`), and the projection `three-loaded` defect now rejects through the projection/native slot-count mismatch (same `EnemySlots` cause). New: `BattleDataProjectionTests.Five_dense_enemy_slots_project_per_slot_identity_without_merging_duplicates` (CLI unit keeps 536: −1 refusal row, +1 positive; declared RED hit exactly at `BattleDataProjector.cs:185`), `NativeBattleContentTests.FiveLoadedSlotsRoundtripOnTheNonProjectedNativeChain`, `NativeBattleContentTests.ProjectionAcceptsFiveLoadedSlotsPerTeam` and `PrepareStoryPackageTests.AuthoredFiveSlotTeamsPrepareLoadAndKeepPerSlotIdentity` (five-enemy/five-slot authored package through the formal Prepare and strict Story/Visual loads; shared fixtures gained an opt-in `fiveEnemies` variant). Game inventory 992 = 989 positives + 3 controls (serial 85/108/2/5/6/3/4/30/709/37); Core 1433, CLI unit 536. Headless Extract Unit + native-map evidence only; formal-Main five-enemy loops, T13b empty slots and the #138 remainder stay open; no GPU/manual/CI/fullgate or parent-ticket completion claim. Review repair `projection-roles-domain-widening-unmigrated-consumers` migrates the two #137 leftovers: the CLI-integration `thirdRole` refusal row (red once the CLI accepted three ordered roles) now refuses four roles `--roles 4,0,1,2` with the unchanged `roles` diagnostic in `ProjectBattleDataCliTests`, mirroring the unit `fourRoles` pattern with an unchanged theory case count (the registered CLI-integration `419 passed + 2 skipped` shape stands), and the stale `ProjectionRoleIds` domain pin in `docs/design/modern-battle-data.md` moves from 1..2 to the committed `1..3 unique ordered roles in 0..4`.

### #141 mixed empty enemy slots: native round-trip and Main wiring (M6-A T13c)

`PalBattleEnemy` gains a default-false `IsEmpty` export and `NativeBattleContent.Write/CreateChecked/LoadChecked` transport #140's typed empty runtime slots symmetrically (the T13b-era "empty enemy slot" write refusal is intentionally removed, never a silent drop); the outer boundary keeps 1..5 runtime slots with teamId diagnostics. `BattleProjectionSourceInfo` slot rows accept the projector's `empty-0` disposition (objectId 0, absent loaded words, occupies a runtime slot; loaded rows keep compactSlot = runtime slot and battleId = runtime slot + 1) and bind rows per numeric slot: empty-0 ↔ IsEmpty id0, loaded ↔ non-empty battle id. `NativeBattleVisualProfile` keeps an empty slot's overlay (TargetId 0, authored position, slot order) while skipping its sprite/frame dependencies; a sprite/frame declaration on an empty slot, a loaded slot without a sprite or a mismatched TargetId still rejects exactly, and emptiness is never inferred from a missing sprite. `PrepareStoryPackage` authors per-slot presentation as one all-or-none group (an empty entry declares only targetId/enemyPosition) and skips empty sprite URIs in dependency collection; `PalStoryBattleView.Initialize` owns no textures for empty slots and the frozen first-mandatory chain explicitly refuses them — review repair closes the coverage debt with `NativeFirstMandatoryBattleProfileTests.FrozenFirstMandatoryChainRejectsNumericEmptyEnemySlots`, which rewrites the prepared chain's numeric slot 1 into #140's canonical empty spelling with an empty overlay slot (fresh content hash, slot order and authored position kept) and asserts the literal frozen refusal after every earlier stage accepts the mixed team. New coverage: native-map adds `NativeBattleContentTests.EmptyEnemySlotsRoundtripThroughNativeWriteSaveLoadAndCreateCore` (declared RED: the old T13b refusal hit exactly), two typed-boundary rows (`enemy-empty-nonzero-hp`, `enemy-id-zero-dense`), `ProjectionEmptyZeroRowsTransportMixedRuntimeSlots` and six `ProjectionEmptyZeroRowsRejectMisalignedOrMalformedRows` rows, `NativeBattleVisualProfileTests.EmptyNumericSlotsKeepOverlayOrderAndSkipSpriteDependencies` plus four `EmptySlotVisualOverlaysRejectWrongBindings` rows, and `PrepareStoryPackageTests.MixedEmptySlotsPrepareLoadAndKeepRuntimeSlotOrder` plus two `EmptySlotMetadataRejectsPartialPresentationFields` rows. scene-realfull adds `StoryRoundtripSceneTests.PreparedMixedEmptySlotsUseRuntimeIndicesTargetLoadedOnlyAndSettleOnceThroughFreshMain` (three-player PartyWin derivation with the five-slot team [empty, 7, empty, 7, empty] through the formal Prepare: default target starts at the first alive slot, cycling crosses empties, empty slots never draw/act/target, the last real enemy's death settles once with only the loaded rewards 3+3 exp / 5+5 cash, fresh Main second run and input isolation). Game inventory 1012 = 1009 positives + 3 controls (serial 85/108/2/5/6/3/4/30/728/38; scene-battle-light keeps its 30 members, the new Main flow lives in the range-executable scene-realfull alongside PartyTwo/PartyThree/ZeroHp); Core 1433, CLI unit 536 unchanged (the pre-existing Core 1433 pin vs #140's delivered 1451 is #140 registration debt, untouched here). Evidence classes are headless native-map plus the single range-executed scene-realfull Main flow; scene-battle-light's 30 registered members were compile-verified through the batch builds but not rerun for this slice; menu cancel semantics, Prepare process wrapper changes, GPU/manual/CI/fullgate and parent-ticket completion stay open.

### #139 battle-entry normalization and in-battle death identity (M6-A T13a)

`ClassicBattleLifecycleSession` now runs PAL_StartBattle's entry loop (battle.c 1568-1583) at the real battle start only: a participating member entering at HP=0 revives at exactly 1 HP and loses its puppet status (`kStatusPuppet`, statuses[4], any WORD spelling accepted for dead entrants only), while every member's hidden action counts reset there; validation/preflight (`TriggerBattleContent.Validate`, `ClassicBattleLifecycleSession.Validate`, `GetCurrentEntryRequirements`) accepts such an entry without side effects or RNG, a living member's puppet stays rejected, and in-battle death keeps identity (no per-action revive; Won half-recovery and the next entry are the only revives). Erratum recorded in `docs/design/classic-battle-session.md`: the old draft T13 sentence "死亡角色不被自动补成 1HP" contradicts the fixed reference and is not implemented. New coverage: Core adds `Zero_hp_member_enters_at_one_hp_with_puppet_cleared_and_counts_reset_at_start` and the three-position `In_battle_death_stays_dead_with_correct_identity_at_every_party_position` (`ClassicBattleLifecycleSessionTests`), two new rejection rows on `Unsupported_growth_rejected_before_start_changes_or_random` (living-member puppet; dead-entry non-puppet status) and `Battle_start_normalizes_zero_hp_puppet_participant_without_preflight_or_spectator_side_effects` (`TriggerScriptBattleBindingTests`: preflight side-effect-free, owner untouched before the owned ack, spectator never merged). scene-realfull adds `StoryRoundtripSceneTests.ZeroHpPuppetMemberEntersAtOneHpStaysDeadAndSettlesByIdentity` (PartyLoss-derived authored package through the real Prepare: member 0 enters 0 HP + puppet word, revives at 1 with puppet cleared while the shared owner keeps 0 until the ack, then dies for real and settles Lost 0 HP/zero statuses by RoleId). Game inventory 989 = 986 positives + 3 controls (serial 85/108/2/5/6/3/4/30/706/37); Core 1433, CLI unit 536. Headless SceneRunner/native evidence only; mid-battle puppet action, revival spells, other statuses and save/reload stay with their own tickets; no GPU/manual/CI/fullgate or #139 parent-spec completion claim.

### #137 three-player party domain (T11)

The living battle party domain widens from 1–2 to 1–3 members (battle.h `MAX_PLAYERS_IN_PARTY`; battle.c:27-31 `g_rgPlayerPos[2]` columns (180,180)/(234,170)/(270,146)) across the existing chain: `ClassicBattleSession`/`TriggerBattleContent` accept 1..3 living players with distinct RoleId 0..5 and still reject 0/4, duplicates and mismatched growth; the trigger-chain same-`nameObjectId` hook-seed check now covers every same-name member; `NativeBattleVisualProfile` authenticates three typed bindings at the reference columns and `PlayerOrigins(3)` returns them; `PrepareStoryPackage`'s author schema accepts 1..3 `visual.players` bindings; `BattleDataProjector`/`BattleProjectionSourceInfo` project and carry three ordered roles (four stay refused). New coverage: Core adds `Three_players_pick_targets_in_party_order_execute_queue_and_win` and `Party_domain_still_rejects_empty_four_and_duplicate_player_sets` (`ClassicBattlePartySessionTests`) plus `Three_roles_enter_the_same_owned_lifecycle_and_four_roles_are_rejected` (`ModernBattleContentTests`); the old "three players rejected" faults migrate to four-player refusals and `One_to_five_complete_enemy_slots_...` accepts parties of three. CLI unit migrates the `threeRoles` projection refusal to `fourRoles` and adds a three-role ordered positive. native-map adds `NativeBattleVisualProfileTests.ThreePlayerBindingsUseTheReferenceThreePlayerLayoutColumns`, `NativeBattleContentTests.ProjectionAcceptsThreeOrderedRolesMatchingThreePlayerContent` (roles-many now four roles) and `PrepareStoryPackageTests.AuthorPlayerSchemaAcceptsThreeReferenceColumnsAndRejectsAFourth`. scene-realfull adds `StoryRoundtripSceneTests.PartyThreePlayersPickTargetsByCyclingExecuteQueueAndWin` (PartyWin-derived three-player package through the real Prepare: three members confirm Attack→Target in party order, member1/2 cycle to slot1, queue5, one-round Won, per-RoleId damage bounds/actions and victory half-recovery 125/122/122 HP + 35 MP). Game inventory 988 = 985 positives + 3 controls (serial 85/108/2/5/6/3/4/30/706/36); Core 1426, CLI unit 536. Headless SceneRunner/native evidence only: death-at-entry rules, empty enemy slots, spells and new statuses stay with their own tickets; no GPU/manual/CI/fullgate or #137 parent-spec completion claim.

