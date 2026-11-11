# #134 投影来源进入正常 Main：SOURCE 接缝核查（#197）

## 身份、结论与证据层

调查基线：`execution/m6-loop@e3484e1fdeb7c3927a51da3db8f74f5fdbce591e`，开工时 clean/index 空。本报告是 [#197](https://github.com/fanxiao92/godot-pal/issues/197) 的完整 SOURCE 交付，原生归属 [#134](https://github.com/fanxiao92/godot-pal/issues/134)，不是 #134 实现、公共 API 批准或运行验收。引用行号均针对该基线；本票只新增本文一条 tracked 路径。

**结论：正常 Story Main 已有 T04 状态接线，不应重造宿主；新增工作首先是来源收据与实际数值/原生字节之间的准备期绑定。** #133 的数值可以进入现有单文档 Prepare，但收据没有生产消费者。只把 `receipt.json` 放旁边、把 hash 写进 `SourceIdentity` 或跑现有 authored Win，均不能闭合 #134。完整影响不能只数 Main/Prepare 两个文件，应先拆来源契约、准备运输和正式场景验收。

实时核查：#134 原直接 blockers #133/#127/#130 均 CLOSED；#133 的交付 tip 是本基线祖先（本身即 tip）。本轮为防止下游绕过此必要核查，新增原生 `134 blocked_by 197`，`197 blocked_by 133(CLOSED)`。外层独立双轴、实际本地集成和关闭 #197 之前，#134 不应开始写入。#126/#148/#124 保持 OPEN；本报告不增加工程叶18、storyCredit0 或 fullM6false 的计数。

权威决定：
- [#133 信任 A](https://github.com/fanxiao92/godot-pal/issues/133#issuecomment-6799410951)：上游已验证是调用者前提；不重认证现代 JSON 与 MKF 相符，不增加第五个投影 CLI 参数。
- [#126 统一格式/受控迁移](https://github.com/fanxiao92/godot-pal/issues/126#issuecomment-6799964973) 适用于 #131/#132/#135/#136 活跃链。它不是让 #134 重解释冻结 M4 author、偷偷引入旧资源 fallback 或删除玩法验收的许可。
- #148 的 D3/D4 已批准；#134 正文中的历史“未冻结”措辞不重开决定，本片也不设计保存格式。

## 1. 现有生产链和准确缺口

| 来源（路径 / 符号；基线行） | 已有事实 / 对 #134 的含义 |
| --- | --- |
| `tools/Pal.Extract/BattleDataProjector.cs` / `Project`，24–247 | 纯内存读取完整14个现代文件字节；校验后生成数值与收据。`output.file` 固定 `numeric/battle-data.json`，`byteLength`/SHA 是实际 UTF-8 输出字节，含末尾换行。`modernInputs` 是本次现代文件字节身份，不是原 chunk hash。 |
| 同文件 / `Project`，81–220 | 角色选序保留；最多2人，role5因成长来源缺列拒绝；非零装备/名字钩子/attackAll等拒绝。敌队最多2个密集有效槽，FFFF跳过并保留来源槽，0空槽拒绝，目标 id=紧凑槽+1。收据不是图片/场景/语言映射。 |
| `src/Pal.Game/Editor/PrepareBattleData.cs` / `Prepare`、`ValidatePaths`，35–85、120–134 | **只**接物理单文档目录；多放收据会拒绝。稳定读 lease、严格 UTF-8/16MiB、`ModernBattleContent.Parse`→`NativeBattleContent.Write`→保存/正式回读→无覆盖发布。没有收据参数或身份输出，输入 hash 当前只证明读稳定性。 |
| `src/Pal.Game/Scripts/Resources/PalBattleContent.cs`；`Profiles/NativeBattleContent.cs` / `Load/CreateCore/Write`，14–76 | native 格式1运输数值，Load返回 `TriggerBattleContent`；没有投影来源字段或 checked-provenance 返回类型。不认证来源、不授予事件闭包能力。不能把 Core 数值 DTO 当原始 JSON 字节身份。 |
| `src/Pal.Game/Editor/PrepareStoryPackage.cs` / `Prepare`，49–99、130–143；`ReadMetadata`，231–245 | 输入恰为 metadata+完整 events，仅允许 events 子目录；根字段严格白名单，无 projection 声明。数值依赖是**已准备**的 `battleContentUri`。稳定锁定精确 native graph，先严格 Load，最终 Story 作为完成入口；不负责数值投影或原档提取。 |
| `src/Pal.Game/Scripts/Resources/PalStoryProfile.cs`，8–25 | `SourceIdentity` 是声明；`ResourceSha256` 绑定本地依赖字节。没有把收据输出 SHA 与 numeric 源/实际 native 内容关联的字段。 |
| `src/Pal.Game/Scripts/Profiles/NativeStoryProfile.cs` / `Load`，19–92、103–132 | 验证规范URI、完整 events/native graph 的精确 hash 集合、owner/slot/address、两个语言的 `ValidateEntry`、字形和画像。随后检查静态场景/role0，并返回已校验对象；没有投影来源检查。 |
| `src/Pal.Game/Scripts/Profiles/NativeBattleVisualProfile.cs` / `Load`，22–145 | 当前角色由 head RoleId 绑定，共享 player sprite；敌位保持独立目标但一队共享 enemy sprite。实际需求队集合精确匹配，palette、帧、effect/数字资源另验。不能把数值team/目标ID当 spriteID。#135/#136 尚未交付的统一视觉不在本票假造。 |
| `src/Pal.Game/Scripts/Main.Story.cs` / `InitializeStory`，74–123 | 两个正常 checked profile、表现能力与UI校验先于发布节点/READY；宿主只在初始化创建 `RoleLiveState`，同一个 owner 交给 `DynamicEventWorld.ForHost` 和 dialogue/session。原生资源已经是正确消费入口，不需要运行时 JSON loader。 |
| `src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs` / `Initialize/StartCall/AcknowledgeBattle`，91–163 | 正常 checked Story 初始化沿同 world 建 session；Start须所属 search request、所选entry及当前requirements；battle ACK 复用正式 session 的所属回执，不复制角色所有者或战斗引擎。 |
| `src/Pal.Game/Scripts/Main.cs` / `Configure/StartAfterSceneDraw/CompleteTrigger`；`Main.Story.cs` | 配置在入树前；真实搜索→trigger→lifecycle→表现→所属完成→探索已存在。新来源接线若不需改这些函数应保持不变，用正常场景验证，而不是为凑纵向链先改 Main。 |

`NativeContentDependencies` 的 file hash 图已经可复用，不能另造第二套 native 图遍历；`PreparationPathSafety.OpenStableRead/MatchesStableRead` 已提供准备期安全读。`ModernBattleContent.Parse/Validate` 已提供数值合法性，`ValidatePhysicalBattle` 是 host-free 数值能力门；它们**不**证明完整 Story/Main/语言/视觉支持。工具的 `StrictJson` 和 `BattleDataProjector` 位于离线程序集；Game `.csproj` 只引用 Core，不能为了读收据给 Core/Game 添加 Pal.Extract 项目依赖。

搜索范围：`src`、`tools`、`tests`、`scripts`、相关 `docs/design`；没有发现生产收据消费者。仓库外固定调用者 `C:/evidence/m6/loop/M6Loop.psm1` 与 `Start-M6Loop.ps1` 只消费执行状态/交付收据，未发现 `BattleDataProjector`、`PrepareBattleData`、`PrepareStoryPackage` 或投影收据 schema 的直接调用。未来改公开参数仍要重核，不承诺未知外部调用方。

## 2. 必须区分的四种身份

1. **上游归档/chunk声明**：`declaredSources/declaredTables`。保持 A 的信任边界；不能与现代 JSON 的 SHA 混比，更不能声称原档独立认证。
2. **实际现代输入身份**：14项 `modernInputs`。绑定本次投影输入；不是完整运行时上下文。fieldSources 的文字来自生产者，消费者可以校验结构/覆盖，但不靠自报文字重新证明原始字段真值。
3. **数值源字节身份**：收据的 `output` 对实际输入数值文件逐字节 SHA/长度。即使语义相同，改空格/BOM/换行也不再是收据绑定的文件；应拒绝而不是重序列化后混比。
4. **准备后的 native 身份**：`.tres` 与实际依赖文件 SHA。格式/编码变化使它不可能等于 JSON SHA。必须由一次完成的准备链把(3)和(4)关联，再由 Story 原生图锁定(4)；不能只判断两个字符串长得像hash。

绑定必须覆盖同次稳定读的 receipt+numeric；再验证 receipt roles 的**有序**角色身份、teams集合/顺序约定、loaded/FFFF槽映射与实际 `TriggerBattleContent`，保留完整输入身份/来源追溯。不要仅比较HP/单个team；不要重新实现投影算法来“证明自己”。篡改收据并同步改hash仍可伪造自声明，以上提供一致性与追溯，不提供认证或防作弊。

准备后运行时只消费批准的 typed 来源声明和 native graph；不读收据目录、现代数值JSON或MKF，不把构建机绝对路径写成运行时依赖。若持久化完整收据，仅作离线旁证并绑定其原字节身份；不能因此引入运行时 JSON 数值读取。

## 3. 最小正常 Main 支持域和独立预期

**可以选择的最小域，不是扩大 #134**：role0为首的单人、一个有效敌位、普通物理能力；用既有正式静态场景、现代触发事件、双语资源、可见glyph和原生战斗视觉。#133允许的所有角色/双人/多队组合不因此成为 Story 支持域；尤其单选role4虽然投影合法，当前 Story 的静态leader门仍拒绝。不为“尽快绿”清零非零钩子/装备、改敌人HP或把空槽压成合法队。

- 成功链必须是完整人工M5表→正式投影CLI→来源核对→显式正常Prepare→正常Story/Visual加载→正常Main，不能在测试中绕过收据或直接 `Write` 一份新的数值代替。
- 人工现代表独立设计可赢/可输值，预期在运行前手算；现有 `src/Pal.Game/Tests/AuthoredStoryFixture.cs` 只从样例 `Numbers` 准备并改写story metadata（该类32–92），没有调用投影器，不能复用它的旧通过当作新链证据。
- 保留T04同一owner：脚本HP/MP前缀→当前值入战→真实结果/结算→所属ACK→探索→再次入战使用上场结果；新实例同URI不共享可变状态。收据是起始内容事实，不应每次触发重新seed。
- 前置拒绝：缺/损坏收据、版本/kind/snapshotKind不支持、重复/未知字段、坏UTF-8、字节长度/SHA不匹配、roles顺序/teams/槽映射不符；native错身份、缺依赖/错语言/缺glyph/未支持事件闭包均须定位且不发READY。不把生产异常包装成原版脚本正常失败。
- 当前 M3/M4 非投影入口不强加投影收据。#134的可选来源声明须明确：未选投影模式不要求；显式选择后缺失/半声明严格失败。#131/#132/#135/#136后续统一迁移按其已批准规格处理，不把这里的局部兼容误用成双loader总设计。

固定参考只读依据：`reference/sdlpal/global.c:400–470` 的 Windows object/default PlayerRoles与EXP/毒初始化，说明基表独立上下文不等于自然剧情快照；`reference/sdlpal/battle.c:1531–1620` 的 party→RoleId、入场规范化、FFFF与enemy-object视图；`reference/sdlpal/battle.c:1799–1831` 的真实结果、Won结算及清理；`reference/sdlpal/script.c:3316–3339` 的0x0007 team/boss/loss/flee分支。现代来源绑定是新资源安全契约，**不是**声称SDLPal有JSON收据机制。参考未修改、编译或运行。

## 4. 两条可行运输路线：尚未批准的新公共契约

| 方案 | 变化 / 优点 | 成本与风险 |
| --- | --- | --- |
| A：在数值 Prepare 显式接收独立收据，native数值携带成对来源声明；Story选择投影绑定时严格校验 | 最靠近JSON→typed的实际转换点，容易证明同次读字节与产物一致；保留numeric目录只有单文档，不改变#133四参数。**推荐** | 需明确 public Prepare额外参数、Editor/PS argv、typed来源可选/必填形状及Story期望绑定；完整调用链与负例不能省。Load目前只返回Core，需决定如何给Story提供已校验native来源，不可静默丢弃。 |
| B：在Story Prepare显式提供receipt+numeric来源，与已准备native数值完整核对并运输来源绑定 | 不改现有数值Prepare的public参数；绑定集中在内容装配边界 | 多一次数值原文/native完整语义核对，需稳定读三方；不能仅比较序列化顺序或重复映射。Story package严格输入/路径与wrapper也可能扩大；资源准备不再只消费既有native依赖。 |

两者均不要求修改冻结 author、runtime读JSON或原档认证，也不自动授权另造converter。**需要确认的是来源验证/运输位置和它的public形状，不是重开已批准信任A或兼容总决定。** 推荐 A；在选择前不发布自称可直接编码的typed字段/参数任务。若批准B，应重算完整影响，不把下面A路线预算照抄。

## 5. 完整影响集与拆分边界（A路线条件预算）

以下是A路线一个可行设计的**条件影响集**，不是“所有实现必然至少22文件”的证明。既有能力能省掉修改时应删除对应路径并解释；新增夹具/资源/UID须相应增加。不能把首次估计8–10当豁免，也不能把多个commit当多张票。

| 组 | 精确候选路径 | 数量 |
| --- | --- | ---: |
| typed来源运输/消费 | `src/Pal.Game/Scripts/Resources/PalBattleContent.cs`；`src/Pal.Game/Scripts/Profiles/NativeBattleContent.cs`；`src/Pal.Game/Scripts/Resources/PalStoryProfile.cs`；`src/Pal.Game/Scripts/Profiles/NativeStoryProfile.cs` | 4 |
| 收据检查及生产准备 | `src/Pal.Game/Editor/ProjectedBattleSource.cs`（拟新增）；同名 `.cs.uid`（新增需登记）；`src/Pal.Game/Editor/PrepareBattleData.cs`；`src/Pal.Game/Editor/PrepareStoryPackage.cs`；`scripts/Prepare-BattleData.ps1` | 5 |
| native/Prepare测试 | `src/Pal.Game/Tests/NativeBattleContentTests.cs`；`src/Pal.Game/Tests/NativeStoryProfileTests.cs`；`src/Pal.Game/Tests/PrepareBattleDataTests.cs`；`src/Pal.Game/Tests/PrepareStoryPackageTests.cs`；`tests/Pal.Prepare.Tests/BattleDataPrepareProcessTests.cs` | 5 |
| 端到端人工夹具/正式宿主测试 | `src/Pal.Game/Tests/ProjectedStoryFixture.cs`（拟新增）；同名 `.cs.uid`；`src/Pal.Game/Tests/StoryRoundtripSceneTests.cs`；`src/Pal.Game/Tests/StoryMainProcessTests.cs` | 4 |
| 登记与契约 | `scripts/GameTestBatches.ps1`；`tests/Pal.Game.Tests/GameBatches.Tests.ps1`；`src/Pal.Game/Tests/README.md`；`docs/design/modern-battle-data.md`；`docs/design/story-package-preparation.md` | 5 |
| **去重合计** | 各组无重复，未计本SOURCE报告 | **23** |

Main生产文件、Visual schema、Core、投影器、现有Editor `.tscn`/`.uid`在此方案**预期不改**：正常入口已可复用。若实际需改，纳入对应叶预算而非隐去。上述测试夹具应生成自有人工M5字节，使用正式CLI产物，不引用Pal.Extract进运行时；不得直接链接当前CLI Unit夹具，其 `DataTables` 等类型依赖工具程序集。可提交人工内容若另落文件，每个都计数；真实派生资源只本地新/空目标，不能上传或覆盖已有资源。

### 推荐首叶：typed来源绑定能力（条件7路径）

仅在路线A及typed形状确认后，先发布一个独立可验收叶：`PalBattleContent.cs`、`NativeBattleContent.cs`、`NativeBattleContentTests.cs`、`scripts/GameTestBatches.ps1`、`tests/Pal.Game.Tests/GameBatches.Tests.ps1`、`src/Pal.Game/Tests/README.md`、`docs/design/modern-battle-data.md`，共**7**路径。复用现有类/UID，不新建Resource类型。公开数值读取保持Core纯净；外层通过已确认的checked来源视图获取同次native事实，不能给未投影资源伪造默认来源。

验收：人工非默认SHA/长度/有序角色/槽信息经保存/正常Load完整保留，旧非投影路径仍合法；半声明、坏hash/长度/版本/选择/重复槽拒绝；修改原生对象不写脏已取得的只读来源值。仅typed人工来源证明，不宣称来源真实性或Prepare/Main。若视图设计还需新文件/UID，必须在开工前增加预算，超10则重新拆。

后续按已接受首叶的实际接口再细化：
1. receipt严格检查与数值Prepare绑定（含公开Editor/PS/argv与拒绝/路径/owned cleanup）。按上表完整组装可能超过10，必须继续拆parser/绑定与进程入口，不能省登记。
2. Story来源期望/实际native核对，正式Prepare回读与内容拒绝；Main生产逻辑若无需改则复用，不额外“注入初值”。
3. 人工投影→真实Prepare→正式Main/SceneRunner与必要进程代表，回填#134全部原验收。建议先最小单角色/单敌；不能据此关闭#135/#136或宣称完整战斗。

不批量发布只带标题的后续票，也不把本报告当新contract已经验收。若操作者不选择路线，下一轮应带两个方案和上述证据返回blocked，不再创建同义readiness票制造进展。

## 6. 验证入口与剩余条件

本轮实际：只读Git/Issue/原生关系/源码、本文路径与预算核对、diff whitespace及派生状态检查；**SDK/Unit/CLI执行/Prepare/Godot/Game/GPU/人工/远端CI均 NOT RUN**。精确命令、退出与本轮完整源绑定在worker `acceptance.md`/`source-final/binding.json`，由外层独立审查后才可发布状态。本文没有新测试计数。

后续运行层必须分开：
- 来源parser（若纯内存且有公开契约）：适用Unit；文件/argv用Integration或既有Prepare进程层。`GameTestBatches.ps1:176–181`及其回归中Core/CLI/Prepare计数是源登记，不是本轮实测。
- typed/Prepare：现有native-map与Prepare正式入口；Profiles/Resources/Editor变更触发既有affected-batch guard，不能先跑场景再事后补门。沿当前脚本/runsettings和预算，不清cache/UID、不升级捕获。
- Main：沿 `StoryRoundtripSceneTests`/`SyntheticFirstBattleClosureTests` 的公开配置、真实输入与 `UpdateCompleted`同步采样先例；fixture只组装生产资源，禁止第二宿主。两次完整战斗/再次返回不能由第一场的早期更新或仅创建第二个request补签。执行前读取时序与Game规范。
- 进程：复用 `StoryMainProcessTests.NormalStoryShippedMainPreloadsThenReportsReadyLast` 等argv/READY/诊断代表，新增投影成功和错绑定无READY的必要代表即可，不为每个内容错误起独立业务进程。

#165/#168原失败、后续NONREPRODUCTION与机制UNRESOLVED均保留；request无具体retryApproval。本票未触碰旧dirty树/缓存/资源/全局配置，未恢复运行租约。将来若选中历史失败入口，allowRuntime不代替具体重试批准与事前独立SOURCE gate。私有资源、GPU、人工、远端CI亦不因本SOURCE票获得权限。
