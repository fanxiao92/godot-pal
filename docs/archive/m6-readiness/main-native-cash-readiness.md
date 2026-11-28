# #131 Main/native 现金接线：SOURCE 就绪性核对（#190）

## 结论与证据等级

调查基线：`execution/m6-loop@d4144358c382fec3909e829d520d11dd37a389f8`。本票只新增本文，不修改代码、公共接口、资源格式、测试计数或旧契约；**以下实现叶全部为建议，尚未发布/批准/实现/运行**。

实时 #131 正文/评论及原生依赖已读：#130/#177/#178/#179 CLOSED，#131 OPEN。已验收的 Core 钱包、现金命令与胜利入账无需重做。剩余跨两种原生上下文、两套 Prepare、静态闭包与正常 Main；不能只往 `Main.Story.cs` 加余额字段。#190 是单张必要 SOURCE 前置，不是 #131 或故事完成。

发现两个会改变施工计划的关键事实：

1. 生产 `StartCall` 已改用当前 Session 的 requirements；旧 preview 方法没有生产调用点，不能据历史叙述把它当成现行启动流程。
2. 首战冻结 envelope 没有现金；把新声明放在外层宿主绑定或扩展 envelope 是不同兼容方案，必须先确认。本文推荐前者，但不冻结名称、版本或 Godot 导出表示，也不把推荐当作批准。

## 已确认的当前调用图

以下 `路径:行` 均指上述固定基线，不是运行证据。

| seam | 实际代码与责任 | 当前缺口 / 必须保留 |
| --- | --- | --- |
| Story 作者资源 | `src/Pal.Game/Scripts/Resources/PalStoryProfile.cs:8` | 没有现金声明；缺失不能偷偷变为可用余额0。 |
| Story Prepare | `src/Pal.Game/Editor/PrepareStoryPackage.cs:20`、`:128`、`:232` | 正式 argv / public Prepare；严格 metadata→Resource→stage Load→final Load。新字段必须在生产者、严格键集合、完整 DWORD 解析及保存重载中成对贯通。现有 `Int` 是 Int32，不能用它截断完整 DWORD。 |
| Story native | `src/Pal.Game/Scripts/Profiles/NativeStoryProfile.cs:86`、`:120`、`:163` | 两语言各做静态 ValidateEntry，再产生 CheckedStoryProfile；现在未给现金能力。checked 内容只能携带不可变初始声明，不能持有被预检执行过的活钱包。 |
| Story Main | `src/Pal.Game/Scripts/Main.Story.cs:74`、`:93`、`:97` | 加载后创建同宿主 roles/world，再初始化 Dialogue；没有现金 owner。应在合法声明存在时创建一次并共享，而非每次 Start/入战重 seed。 |
| 生产 adapter | `src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs:76`、`:97` | FirstBattle / Story 两个 Initialize，各有 world/无world分支，当前均不传现金。须逐条保持旧未绑定行为，不遗漏无world分支。 |
| 当前预检 | `src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs:144`、`:159` | StartCall→当前 Session.GetCurrentEntryRequirements→ValidateResources→同一 Session.Start；预检不执行现金、battle或生产 RNG，不用旧 seed 的模拟结果。 |
| FirstBattle 作者边界 | `src/Pal.Game/Scripts/Resources/PalFirstMandatoryBattleProfile.cs:9`；`src/Pal.Game/Scripts/Profiles/NativeBattleEnvelopeAdapter.cs:12`；`src/Pal.Core/FirstMandatoryBattleProfile.cs:53` | 现有 typed envelope 与 exact-byte author 身份冻结，无现金字段。禁止改旧 author/provenance、伪称自然 save 或从角色数据反推钱包。 |
| FirstBattle Prepare | `src/Pal.Game/Editor/PrepareBattleContext.cs:36`、`:70`、`:82`、`:102`、`:163` | public Prepare、typed envelope Write、preparation校验、临时Resource Load、外部 RealClosureBinding严格键表均为实际生产链。RealClosureBinding 是本文件嵌套record，不是另一源码文件。 |
| FirstBattle native闭包 | `src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs:42`、`:123`；`src/Pal.Core/FirstMandatoryBattleClosure.cs:84` | 外层native→typed SHA→profile→真实闭包；双语静态 ValidateEntry同样不传现金。只改Story loader无法开放这条链；旧默认调用必须继续无现金。 |
| FirstBattle Main | `src/Pal.Game/Scripts/Main.cs:186`、`:193`、`:205`、`:214` | 创建roles/world/RNG→安装保留factory→生产Initialize→InitializeTrigger→CheckedStoryProfile facade复用正式Story呈现。未来现金应来自已核对声明，不来自facade的描述字符串或StoryCashGranted。 |
| dynamic消费者 | `src/Pal.Game/Scripts/Main.Dynamic.cs:34`、`:39` | 也调用NativeStoryProfile.Load，但实际构造DynamicEventSession，不走Story Dialogue。native能力扩展不等于dynamic已绑定现金；不可无声增加此模式支持，实施票须明确保留其未绑定/拒绝边界。 |
| 奖励展示 / release | `src/Pal.Game/Scripts/Main.Story.cs:493`、`:537`；`src/Pal.Game/Scripts/Main.cs:505`、`:531`、`:537`；`src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs:282` | Main累加展示奖励然后ACK，不能再次Credit；Failure和Exit均有释放链，新owner应由宿主归还引用，旧adapter不再作用于新Main。 |

### preview 的可达性更正

`PalTriggerDialogue.cs:191` 的私有 `CreatePreviewSession` 只有定义，没有调用点。`Main.cs:193` 给 `PreviewSessionFactory` 赋值不是执行它；生产 StartCall 的真实路径见上表。

保留的直接调用在 `src/Pal.Game/Tests/SyntheticFirstBattleClosureTests.cs:241`、`:1006`；`:615`、`:729` 检查清空。`src/Pal.Game/Tests/README.md:129` 明确只覆盖caller已获得native lease后的异常展开，不动态证明StartCall或factory构造catch。不要删除这些能力/测试，也不要为现金接线恢复未调用方法。当前factory没有现金绑定，因此不会共享或扣减生产现金；它并不具备预演现金命令的能力。

若未来另行批准让保留factory支持现金，必须从**调用时当前余额**构造独立owner，拒绝把生产owner借给执行型preview，也不能用原seed/默认0；这是额外能力，不是当前生产可达性要求。纯requirements无需执行型preview。

## 必须复用的 Core 提交边界

- `src/Pal.Core/CashLiveState.cs:9`：显式uint seed；TryChange signed WORD、DWORD回绕、Halve向下取整。共享同一实例，不新增GameState或另一份余额真值。
- `src/Pal.Core/TriggerScriptSession.cs:85`、`:129`、`:185`：已有可空owner参数、共享factory及静态ValidateEntry。静态检查只使用有无owner作为能力条件；**不能在缺失声明时一律传 new CashLiveState(0) 绕过门**。存在声明时可创建不对外发布、从声明值构造的验证上下文；只做requirements，禁止Start或预演获胜。运行时Main另建自己的唯一owner。
- `src/Pal.Core/ClassicBattleLifecycleSession.cs:127`、`:168`：真实Won调用SettleGrowth；其中`:174`已Credit，然后才执行enemy-end。`src/Pal.Core/TriggerScriptSession.cs:269`的所属ACK只合并角色并续执行，不再发现金。
- 固定只读参考 `reference/sdlpal/script.c:952`、`:2600` 与 `reference/sdlpal/battle.c:1054`、`:1334`、`:1809`：signed不足分支、减半、Won先入账再敌方战后脚本、非Won不走PAL_BattleWon。这里不重新实现规则，不编译/运行reference。
- 新显式Start是新调用，不是自动幂等请求。继续使用所属request/CallId门：`src/Pal.Core/NpcInteractionSession.cs:57`、`src/Pal.Core/FirstMandatoryBattleSession.cs:91`及当前trigger ACK。不得添加全局去重集或以任意相同金额拒绝合法第二次胜利。

仓库搜索边界为 `src`、`tests`、`tools`、`scripts`；另对固定 `C:/evidence/m6/loop/M6Loop.psm1` 与 `C:/evidence/m6/loop/Start-M6Loop.ps1` 搜索CashLiveState/InventoryLiveState/PartyPoisonState/CheckedStoryProfile/NativeStoryProfile/PalStoryProfile，无直接消费者。不推断未覆盖的仓库外已编译调用者或二进制ABI兼容。

## 待确认的公共边界（不是重新讨论现金规则）

| 方案 | 好处 | 代价 / 未决 |
| --- | --- | --- |
| A：新增可选**外层独立宿主现金声明**，Story metadata及FirstBattle外层binding各显式携带，经native校验到checked标量 | 不改被冻结v1 author/envelope/hash与来源tuple；旧无声明仍保持未绑定；与#131独立上下文要求一致。**推荐。** | 新公共字段的presence、完整DWORD导出表示、严格缺失/非法表示、是否保持FormatVersion1的兼容策略必须先确认并用ResourceSaver/Loader证实。不得默认可用0。 |
| B：把现金纳入FirstBattle typed envelope / snapshot及其来源矩阵 | 现金随同一个完整上下文做来源与散列约束 | 必须设计新版本/迁移边界，牵动Core profile/codec、native adapter、author工具及冻结字段来源；不能沿用下面小票预算，更不能直接修改旧v1。 |

需要的明确决定是：**是否接受A，并在实施票中确认其新声明/缺失/版本表示？** 本票不替操作者选择公共契约。旧未声明上下文保留无现金能力与原奖励展示是#131已定要求，不再次询问。真实私有上下文的现金数值/来源另须实际材料；不拿人工10或任何奖励总额替代，不阻碍先做明确标记的人工夹具。M6-C只承接owner/提交点/一次重建责任，不在此冻结save schema。

## 完整候选影响集：先确认方案，再逐票发布

下面按A列出**全部建议路径**，不是现行任务授权或已确认公共签名。每叶保持可编译、独立验收，不靠跨叶悬空声明凑数。若测试fixture无法通过现有公开helper就地表达，或检查发现其它必要调用者，先重算并拆，不追加第11路径。复用原文件，避免新增C#附带UID被漏算。

### S：Story 原生初始声明 / Prepare闭包，9路径

1. `src/Pal.Game/Scripts/Resources/PalStoryProfile.cs`
2. `src/Pal.Game/Scripts/Profiles/NativeStoryProfile.cs`
3. `src/Pal.Game/Editor/PrepareStoryPackage.cs`
4. `src/Pal.Game/Tests/NativeStoryProfileTests.cs`
5. `src/Pal.Game/Tests/PrepareStoryPackageTests.cs`
6. `docs/design/live-cash-state.md`
7. `src/Pal.Game/Tests/README.md`
8. `scripts/GameTestBatches.ps1`
9. `tests/Pal.Game.Tests/GameBatches.Tests.ps1`

用NativeStoryProfileFixture已公开Profile/SaveProfile和PrepareStoryPackageTests内Source.Metadata编辑已有人工元数据，不需改其fixture生产者。完整Resource→checked标量/严格metadata贯通即此叶提交边界；**不签Main**。CheckedStoryProfile构造应为旧facade调用保留兼容默认未声明值，若不能保持则该直接caller必须加入预算后重审。dynamic继续未绑定；不可把loader承认声明误称其executor已支持。

### H-S：正常 Story 宿主同一owner，10路径

1. `src/Pal.Game/Scripts/Main.cs`
2. `src/Pal.Game/Scripts/Main.Story.cs`
3. `src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs`
4. `src/Pal.Game/Tests/StoryRoundtripSceneTests.cs`
5. `src/Pal.Game/Tests/AuthoredStoryFixture.cs`
6. `docs/design/live-cash-state.md`
7. `src/Pal.Game/Tests/README.md`
8. `scripts/GameTestBatches.ps1`
9. `tests/Pal.Game.Tests/GameBatches.Tests.ps1`
10. `src/Pal.Game/Tests/StoryMainProcessTests.cs`

依赖S；Main字段/只读观察、构造与failure/exit释放、Story Initialize的显式绑定、正常SceneRunner及最小argv/READY/拒绝代表在同叶闭合。FirstBattle overload保持旧无现金；不把其尚未支持当Story测试通过。此处不新增钱包算法、不恢复preview。

### C：FirstBattle静态closure显式现金能力，5路径

1. `src/Pal.Core/FirstMandatoryBattleClosure.cs`
2. `tests/Pal.Core.UnitTests/FirstMandatoryBattleClosureTests.cs`
3. `docs/design/live-cash-state.md`
4. `scripts/GameTestBatches.ps1`
5. `tests/Pal.Game.Tests/GameBatches.Tests.ps1`

只增加兼容、显式可空验证能力，双语静态ValidateEntry使用同一声明上下文，无seed推断/执行/RNG；旧native caller不传参数仍无现金。明确新参数不改变冻结envelope，也不自动绑定Main。公开Core正反例形成独立闭环。

### F：FirstBattle外层原生声明 / Prepare，9路径

1. `src/Pal.Game/Scripts/Resources/PalFirstMandatoryBattleProfile.cs`
2. `src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs`
3. `src/Pal.Game/Editor/PrepareBattleContext.cs`
4. `src/Pal.Game/Tests/NativeFirstMandatoryBattleProfileTests.cs`
5. `src/Pal.Game/Tests/SyntheticFirstBattleFixture.cs`
6. `docs/design/live-cash-state.md`
7. `src/Pal.Game/Tests/README.md`
8. `scripts/GameTestBatches.ps1`
9. `tests/Pal.Game.Tests/GameBatches.Tests.ps1`

依赖C及外层方案确认；通过现有正式Prepare API/实际原生保存重载测试新binding，验证typed envelope/hash仍原样。fixture显式携带人工来源，不重写v1。脚本argv形状不变时固定PS wrapper无需变动；若选择新argv或需新增进程测试文件，则必须补进完整预算，不签未运行的进程层。

### H-F：FirstBattle正常Main消费，7路径

1. `src/Pal.Game/Scripts/Main.cs`
2. `src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs`
3. `src/Pal.Game/Tests/SyntheticFirstBattleClosureTests.cs`
4. `docs/design/live-cash-state.md`
5. `src/Pal.Game/Tests/README.md`
6. `scripts/GameTestBatches.ps1`
7. `tests/Pal.Game.Tests/GameBatches.Tests.ps1`

依赖F和H-S；复用同一Main owner字段/释放，不创建第二钱包。人工fixture的两个owned完整闭环、sameURI新宿主及当前request兼容在本叶取证；它不是私有真实首战验收。保留factory直接测试仍为未绑定现金能力，不宣称预演现金成功。若要改变它须另确认能力和测试影响，不以死代码问题阻塞生产requirements。

## 独立验收预期与将来的运行要求

以下全部 **NOT RUN**，实施票须先冻结源码/用例/确切指定RED断言位置、测试SHA、预期数字退出，再执行；此计划不拿编译/缺资源/超时当RED。

- **原生 / Prepare**：显式0与缺失不同；人工声明 `0`、`10`、`2147483648`、`4294967295` 经正式Prepare→ResourceSaver→IgnoreDeep Load→checked原样保留。`-1`（若不是明确批准的缺失编码）、`4294967296`、小数、字符串/null/重复键/未知键按已确认格式精确拒绝；不截Int32、不夹上限。缺声明选中现金闭包拒绝，非现金旧内容及原奖励展示保持。两语言和两个静态闭包分别取证；新/空目标、不覆盖/不写输入/链接拒绝沿现有生产规则。S的可编译shell指定RED应为10声明未往返而非setup错误；C指定RED应为明确绑定后仍拒绝现金命令的原诊断。
- **正常Main / 当前值**：独立人工场景可固定初值10，事件+3得到13，再减半得到6；第一真实胜利cash7，enemy-end必须能立即扣13得到0，返回事件+3得到3；第二真实胜利cash5得到8，展示累计仅12。值是手写规则预期，不由实现生成；正式lifecycle/action/display/input/owned ACK驱动，不手调SettleGrowth。H-S/H-F各冻结第一个适用`UpdateCompleted`回调内owner身份和余额断言为指定RED，不能等正确余额后再采样。已有StoryHost和Synthetic场景只作装配先例，不把它们现有仅展示累计的断言当钱包证据。
- **拒绝与兼容**：过早/null/foreign/wrong-ID/旧/重复completion/display回执及echo确认不改余额，不再次奖；真实合法新战斗应可再奖。Lost保持事件已经提交的现金副作用但不加胜利奖，不造回滚。新Main复用相同URI仍有独立owner且从显式seed开始；旧Main/adapter退出后不能污染新实例。prepared Resource/file/hash不被玩法写脏；未声明钱包仍null，不能读取StoryCashGranted冒充余额。纯requirements零现金写/零RNG/不改CallId；保留factory若只运行旧非现金测试，应保持生产余额和种子不变，不测试未批准现金能力。
- **层级**：C用普通Core `dotnet test tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj --settings tests/unit.runsettings`。S/F须正式Prepare/原生加载，H-S/H-F须正常Main/SceneRunner和必要真实argv代表；不可用Core或SOURCE补签。当前命令/分区/预算须届时从`src/Pal.Game/Tests/README.md`、runsettings和共享脚本核对，不复制过期数字。
- **调度与停止**：实际Godot/GdUnit/Prepare/import/solution全局串行；开始前确认旧租约已归还并只查全局Godot PID/Name。没有specific retryApproval就不恢复旧165/168或其它停止失败。新crash/abort/unknown/输出不完整保全STOP；不换入口规避、不升工具链/捕获、不放宽原生预算。首次适用更新同步取样，按`docs/testing/time-boundaries.md`区分状态错误与未完成。

## 本轮验证及保留限制

本票仅做SOURCE：实时Issue/原生关系回读、上述源码及固定参考定点读取、只读独立scout核对、引用路径/行锚与raw SHA检查、`git diff --check`、冻结clean候选。原scout关于preview的错误推断及其后续明确撤回原件均保留，不复制到当前事实；外层票级Standards/Spec仍须独立执行。

无SDK/Core/CLI/Game/Godot/GdUnit/Prepare/import/solution/完整门/私有资源/GPU/人工/CI运行，不给任何这些层新增通过。17工程叶、storyCredit0、fullM6false保持；#129/#147仍阻塞，#164 SOURCE-only/477GREEN未跑、#165 cold、#168 FAILED466/474、#179历史UNKNOWN/fixture失败及broadAST14失败保持。#132的所有Main/native/活动battle/setParty未闭合门不变。没有自然save、运行时JSON/MKF、真实资源提交上传、push/PR/main或integration/canonical写入。
