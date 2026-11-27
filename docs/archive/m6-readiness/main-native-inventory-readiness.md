# #194：Main/native 库存、装备与毒状态的来源及生命周期门

## 结论 / 本票不是 #132 实现

固定输入 `execution/m6-loop@7fb900f8d2ca5c4d404c2fda6c3bd0d89bd4f5df`，开始时 clean/index 空。实时 #126/#132 正文、评论及原生依赖已读，#132 的 Core blockers 均 CLOSED；#194 随后作为 #132 的原生子票和 blocker 建立，自身无 blocker。本票仅新增本文，**无 API、schema、生产、测试、资源、UID、登记或运行行为变化**。

这是 #132 尚缺的 Main/native 初始来源和同一生命周期接线的必要 SOURCE 前置，不重做 #180–#189 已验收 Core；也不重做 #190 现金、#191 投影、#192/#193 视觉调查。主要新发现：

1. **装备声明已有，完整库存/活毒来源没有**。不能把全部初始来源说成空白，也不能从角色快照缺字段推断空背包/无毒。
2. 运行期 `ForSharedRoles` 能绑定四个 owner，但静态 `ValidateEntry` 只有可选库存，没有装备/角色/毒参数。仅改 Main 或 native loader 传库存，仍无法让 Event `removeItem` 通过加载前置。
3. Story/FirstBattle 都已有共享角色/world；实际 Dialogue 的两组 world/无world分支却都没有绑定库存、当前装备与毒。未绑定战斗仍可每次从 author 初始化装备，必须避免移除后旧装备复活。
4. 下述窄接线方案建议 **C7 / S10 / H-S10 / F-C5 / F10 / H-F7，累计29个去重路径**。这只是条件化实施路线，不是整张 #132 的预算，更不是已批准的新公共契约。活动战斗/毒执行和完整 setParty 链另有未闭合责任，本文不把它们估成零文件或静默排除故事。

所有 SDK、Unit、CLI、Godot/GdUnit、Prepare/import/solution/共享门、真实资源、GPU/桌面/远端 CI **NOT RUN**。SOURCE 定点读取、路径/行锚/原字节核对不证明运行成功；外部本轮 `acceptance.md` 及冻结 binding 绑定实际命令/结果。只读 scout 不是票级双轴验收。

## 当前来源与实际消费链

以下行号均指上述基线。

| seam | 已确认的实现 | 当前缺口 / 不可替代项 |
| --- | --- | --- |
| 库存 Core | `src/Pal.Core/InventoryLiveState.cs:7`：显式有序 seed；256槽、非零唯一WORD物品、0..99数量、in-use WORD；耗尽槽仍占用 | initial `[]` 是明确声明而不是缺字段的默认值。保留顺序/耗尽记录/in-use；不得隐式压缩、重 seed 或添加预留算法 |
| 当前装备 | `src/Pal.Game/Scripts/Resources/PalBattleContent.cs:13`、`src/Pal.Game/Scripts/Profiles/NativeBattleContent.cs:50`、`:71`、`:97` 搬运六角色装备网格及效果对象；`src/Pal.Core/BattleEquipmentState.cs:51` 初始化 | 这些是可复用的初始装备声明；host只需在合法上下文初始化一次，不新增另一套装备格式。#168尚未交付的新WORD transport不可偷用停放树 |
| 当前角色 | `src/Pal.Core/FirstMandatoryBattleModels.cs:18` 的快照有Equipment/Statuses；`src/Pal.Core/FirstMandatoryBattleEnvelope.cs:7` 与 `:72` 编码这些记录 | 没有库存/毒ID+脚本对；typed envelope/hash/来源tuple冻结，不得冒称包含完整玩家状态或自然save |
| 活毒 | `src/Pal.Core/PartyPoisonState.cs:7`：显式当前角色顺序、每行16对ID/脚本WORD及等级表，校验首空槽后的非零ID也有等级 | `NativeBattleContent.cs:111` 的Player.Poisons只是ID列表，不是活毒脚本槽/等级根；不能补零脚本或抹掉隐藏尾项。当前战斗的非零毒支持仍受既有能力门限制 |
| Story作者/Prepare | `src/Pal.Game/Scripts/Resources/PalStoryProfile.cs:9` 无库存/毒声明；`src/Pal.Game/Editor/PrepareStoryPackage.cs:232` exact键集→Resource；`:132`/`:140` staging/final严格Load | 新初始声明需进入metadata的缺失/重复/未知/类型校验、原生保存读回及checked投影；只增加Main字段不能形成来源 |
| Story native | `src/Pal.Game/Scripts/Profiles/NativeStoryProfile.cs:86` 双语静态ValidateEntry，`:120` 产出checked，`:163` checked类 | 现在没有库存能力；checked只应保存不可变初始声明，不能持有被预检执行过的生产owner。调用静态要求检查不等于执行脚本 |
| Story host | `src/Pal.Game/Scripts/Main.Story.cs:93` 先建RoleLiveState，随后world与Dialogue | 唯一库存/装备/毒owner尚不存在；必须与world共享角色，不每次Start、ACK或入战重建 |
| FirstBattle Prepare | `src/Pal.Game/Editor/PrepareBattleContext.cs:36` public Prepare，`:76` Write冻结envelope，`:82` preparation校验，`:102` 保存后Load；`:163` 嵌套RealClosureBinding | 外层binding也是公开严格输入，不是任意扩展字典；在这里加声明与改typed envelope是不同方案。不得混写或改旧provenance |
| FirstBattle native | `src/Pal.Game/Scripts/Resources/PalFirstMandatoryBattleProfile.cs:8` 无库存/毒；`src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs:42` typed SHA和profile校验，`:123` Verify真实闭包 | `src/Pal.Core/FirstMandatoryBattleClosure.cs:84`/`:85` 双语静态ValidateEntry同样无完整移除能力。只改Story不能替首战签收 |
| FirstBattle host | `src/Pal.Game/Scripts/Main.cs:186` roles→world→RNG→Dialogue，`:206` InitializeTrigger；`:214` checked Story facade | facade是共用呈现适配，不是库存/毒来源；首战静态绑定与request所有权仍须维持，不能重新解释冻结@09C3 |
| 生产Dialogue | `src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs:76`/`:97` 两个Initialize，各含world/无world分支 | 全部仍用旧构造，不传库存，也不走完整ForSharedRoles；新能力应显式注入，未声明的旧caller仍未绑定，不制造空owner绕门 |
| 实际启动预检 | `PalTriggerDialogue.cs:144` StartCall→`:159` 当前Session.GetCurrentEntryRequirements→ValidateResources→同Session.Start | 纯requirements不执行库存/装备/毒、不消耗RNG、不改CallId；不能用从旧seed重播的preview代替当前状态 |
| 其它模式 | `src/Pal.Game/Scripts/Main.Dynamic.cs:34` 用同NativeStoryProfile，但构造DynamicEventSession；`PalTriggerDialogue.cs:26` 旧NPCInitialize只有基础trigger | loader接受新来源不意味着这些executor已支持它。建议明确保留未绑定/拒绝，不扩大dynamic或旧NPC能力 |
| 释放/失败 | `src/Pal.Game/Scripts/Main.cs:505` Fail、`:531` 清factory/RNG、`:537` Exit；`Main.Story.cs:537` ReleaseStory；`PalTriggerDialogue.cs:282` Exit清session/world/订阅 | 新owner是managed业务状态，不需要假造Dispose，但必须失去旧host/adapter可达引用；失败与第二个同URI Main均需真测试，不能以现有RNG释放推断新owner已安全 |

### 必须复用的 Core 接缝与局限

- `src/Pal.Core/TriggerScriptSession.cs:129` 的 `ForSharedRoles` 已接受可选inventory/equipment/partyPoisons，要求world使用同一RoleLiveState。不需要新增总状态、VM或运行中rebind。
- `TriggerScriptSession.cs:185` 的静态 `ValidateEntry` 只有cash/inventory；`:365` 的当前校验才向闭包提供equipment、完整owner能力和world/poison队伍顺序。`:402` 的0020要求完整绑定且只能Event；`:558` 拒绝同时可达setParty。**静态能力扩展是实际额外前置，不是现成可调用API**；下文C只为此建议可兼容显式参数方案，具体签名尚未批准。
- `TriggerScriptSession.cs:673` 开战使用当前equipment或回退Initialize(author)，`:686` 执行TryRemoveItem，`:269` 所属battle ACK仅合并一次角色结果并续脚本。网格/效果寄存器为装备真值，角色Equipment数组是同步记录；不得作为另一可独立编辑的网格。#187已证明下一场战斗消费当前装备，不重做算法。
- 当前 `ClassicBattleSession.cs:491` 附近仍拒绝非零毒/未支持状态、attack-all及装备精灵覆盖；把PartyPoisonState接入外层**不**开放毒执行/战斗投影。不得用另一个零列表掩盖初始非零活毒是否可入战的问题；完整毒消费须后续真实能力和拒绝前置。
- `src/Pal.Core/FirstMandatoryBattleSession.cs:75` 的InitializeTrigger只绑定生产world一次，`:91` 的BindTrigger与后继CompleteTrigger仍有所属request门；接线不新建全局去重集。新的合法Start是新调用，不是自动忽略同类库存命令。

### preview：保留能力不等于生产执行

`PalTriggerDialogue.cs:191` 的CreatePreviewSession只有定义，无生产调用点；`Main.cs:193` 仅设置PreviewSessionFactory。已有直接消费者是 `src/Pal.Game/Tests/SyntheticFirstBattleClosureTests.cs:241` 和`:1006`，退出断言在`:615`/`:729`。本轮不删除factory/测试，也不恢复旧预演。

建议保留factory原有未绑定库存能力，只要求调用/异常展开不影响生产owner；若后续要支持执行型库存preview，必须明确批准从**调用时当前**库存/网格/角色/毒对构造完整隔离副本，不借用生产owner、不拿初始seed或空毒替代。`world.CreateIsolatedCopy()`本身只证明world/角色相关隔离，不替新库存/装备/毒副本背书；复制时不能从外部可写Resource获取另一份真状态。这是额外能力，未算入下文窄接线预算。

## 固定参考：规则已交付，宿主不能改变

只读参考 `reference/README.md` 导航，未编译运行。

- `reference/sdlpal/script.c:970` 的001F强制signed WORD且忽略库存返回；`:977` 的0020计数含装备、零量转1、stock优先，再按当前party/slot顺序回退清效果与装备；`:1859` 的0058只查库存并signed比较。不新增失败退款/全脚本事务。
- `reference/sdlpal/global.c:957` 的CountItem只计当前party装备；`:1372` 的移除清寄存器、hand实际dualAttack、wear保留level<99的ID/脚本对并清尾。不重放其它装备以“修复”本次效果。
- `reference/sdlpal/script.c:2164` 的0075重排队伍（全零操作数保留role0），`:2195` 清**整张**毒表，再`:2196` PAL_UpdateEquipments。`reference/sdlpal/global.c:1333` 后者先清整张效果寄存器，遍历全部6角色/6槽，对非零装备执行wScriptOnEquip并写回返回入口。不是只对当前队员拷贝静态effect。
- #189的 `PartyPoisonState.ResetForPartyChange` 已交付明确清零，不是保留同成员毒迁移；当前 `TriggerScriptSession.cs:807` 只调用world.SetParty。尚无完整装备脚本/入口写回/装备毒再生链，不能仅接Reset或Initialize就移除#188拒绝。现有“不含0020”的setParty路径保持既有边界，不宣称已忠实完成全链。

参考原字节SHA256在本轮外部source输入记录中；报告不复制原始游戏资源或把hash内部一致性冒称真实来源认证。

## 要先确认的公共契约 / 来源决定

| 问题 | 方案、利弊与推荐 | 未批准边界 |
| --- | --- | --- |
| 新状态放哪一层 | A：Story metadata与FirstBattle外层binding各显式携带可选独立宿主初始声明，保留被冻结v1 envelope/hash；旧缺声明仍无能力。B：新版本完整上下文/envelope纳入全部新字段，统一来源矩阵但需要更多Core codec/adapter/author迁移。**倾向A**，与#190可同层但不合并两种真状态 | presence、显式空与缺失、允许的成组组合、版本/混写/拒绝、Godot export表示尚须确认。A可利用现有Resource文件表达，不在本文发明字段名或新的Resource类；若批准B，下面预算失效，先重算 |
| 库存与毒的能力粒度 | A1：独立stock声明足够开放add/shortage；完整stock+poison/levels+同装备/role绑定才开放Event remove。A2：一组全有全无声明，门简单但add也需完整毒输入。推荐A1，更忠实于现有可选能力，不把缺毒当无毒 | 两种都不能把旧缺字段转空seed；需决定部分/畸形声明在Load时如何拒绝，不以未选中命令掩盖损坏的已声明数据 |
| 静态移除预检 | C：在现有静态ValidateEntry及FirstMandatoryBattleClosure.Verify兼容增加显式初始绑定能力，继续共用闭包walker且不执行。另一方案是另建临时session/world/RNG再取requirements，依赖更多并有副作用/映射语境差异。倾向C | 尚未批准具体签名/组合；不可仅传hasRemoval=true的布尔假证明，必须有校验过的同一初始角色/网格/party毒关系。旧调用不传仍拒绝0020；返回receipt不是生产owner |
| 毒来源与入战 | 独立声明完整ID/脚本对和等级；现代Poison ID列表或role缺字段不能还原。零对seed可以人工明确author，不能冒称真实自然save。非零毒在当前战斗不支持域内，必须由后续消费链处理/拒绝 | 不把允许库存移除等同完整毒战斗能力；存在Poison owner不授权隐藏毒或清空以通过战斗。#132完整活动battle/setParty门仍开放 |
| 保留preview | 当前生产只用requirements，无需执行型preview。建议保留其旧未绑定库存能力及异常释放，不做完整snapshot-copy API | 若要求其新增库存执行能力，需另确认可达用途、复制边界、全部测试与额外路径；不默默扩进本文预算 |

这些是需要操作者明确选择的输入/兼容/API边界，不是再次讨论已确定的库存/移除忠实规则。外层双轴接受本文也不批准表中的实现建议。真实私有初始库存/毒声明及来源材料仍缺；人工夹具可以独立验能力，但不替真实上下文签收。下一步应直接决策/发布实施叶，不再以同题SOURCE票重复该缺口。

## 窄接线的完整建议影响集（未发布实施叶）

仅对**方案A、可在既有Resource文件表达字段、保留preview未绑定**这一条件成立。不新增.cs/uid、样本或新的框架；需新增时先重算/拆分。公共argv形状不变时两个Prepare包装脚本只传路径，无库存业务，已核无需计划改；如果改变argv则包装和进程测试都补入，不偷偷作为第11路径。两个包装的既有timeout清理分支确有无参WaitForExit，本文不声称其有界性通过；保留现有全局诊断/停止约束，不据SOURCE计划忽略这项风险或改脚本取得运行绿灯。以下各叶包含其完整测试/文档/登记，不把登记拆出去凑≤10。

公共路径用精确名称缩写：D=`docs/design/live-inventory-state.md`，R=`src/Pal.Game/Tests/README.md`，L1=`scripts/GameTestBatches.ps1`，L2=`tests/Pal.Game.Tests/GameBatches.Tests.ps1`。L1/L2是已有测试登记，**不是canonical current.json**。

| 叶 / 独立可验提交 | 完整路径（每项为文件） | 计数 |
| --- | --- | ---: |
| C：静态初始完整绑定可做纯requirements，旧调用仍拒绝；不改任何native caller | `src/Pal.Core/TriggerScriptSession.cs`；`tests/Pal.Core.UnitTests/TriggerScriptRemovalTests.cs`；`tests/Pal.Core.UnitTests/TriggerScriptInventoryTests.cs`；`tests/Pal.Core.UnitTests/TriggerScriptBattleBindingTests.cs`；D；L1；L2 | 7 |
| S：Story正式metadata→Resource→checked初始声明，双语闭包，依赖C；不签Main | `src/Pal.Game/Scripts/Resources/PalStoryProfile.cs`；`src/Pal.Game/Scripts/Profiles/NativeStoryProfile.cs`；`src/Pal.Game/Editor/PrepareStoryPackage.cs`；`src/Pal.Game/Tests/NativeStoryProfileTests.cs`；`src/Pal.Game/Tests/PrepareStoryPackageTests.cs`；`tests/Pal.Prepare.Tests/StoryPackagePrepareProcessTests.cs`；D；R；L1；L2 | 10 |
| H-S：Story正常Main同一四owner/后续battle，依赖S；不开放活动battle或完整setParty | `src/Pal.Game/Scripts/Main.cs`；`src/Pal.Game/Scripts/Main.Story.cs`；`src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs`；`src/Pal.Game/Tests/StoryRoundtripSceneTests.cs`；`src/Pal.Game/Tests/AuthoredStoryFixture.cs`；`src/Pal.Game/Tests/StoryMainProcessTests.cs`；D；R；L1；L2 | 10 |
| F-C：FirstBattle现有纯closure兼容承接C，双语新显式能力与旧拒绝；不改变envelope/来源 | `src/Pal.Core/FirstMandatoryBattleClosure.cs`；`tests/Pal.Core.UnitTests/FirstMandatoryBattleClosureTests.cs`；D；L1；L2 | 5 |
| F：FirstBattle外层声明/正式Prepare/native闭包，依赖F-C；不签Main | `src/Pal.Game/Scripts/Resources/PalFirstMandatoryBattleProfile.cs`；`src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs`；`src/Pal.Game/Editor/PrepareBattleContext.cs`；`src/Pal.Game/Tests/NativeFirstMandatoryBattleProfileTests.cs`；`src/Pal.Game/Tests/SyntheticFirstBattleFixture.cs`；`tests/Pal.Prepare.Tests/BattlePrepareProcessTests.cs`；D；R；L1；L2 | 10 |
| H-F：FirstBattle正常Main同一owner/连续性与保留factory隔离，依赖F和H-S | `src/Pal.Game/Scripts/Main.cs`；`src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs`；`src/Pal.Game/Tests/SyntheticFirstBattleClosureTests.cs`；D；R；L1；L2 | 7 |

**算术**：逐叶共49项，去重29路径。公共D/R/L1/L2共4；独有生产/测试分组C4、S6、H-S6、F-C2、F6、H-F新增1，共25，合计29；Main/Dialogue已计入H-S，H-F不重复计为新增。不是单张≤10文件的#132；本文也不计为未来每叶必改文件。

fixture/caller核对：S可用 `NativeStoryProfileFixture.cs:17` 现有Profile及`:141` SaveProfile，不需改fixture；PrepareStoryPackageTests.cs:706已有Preparation.Metadata/Native，故可在原测试文件构造新metadata与事件。H-S确需改AuthoredStoryFixture的显式选项/owned临时输入，原sample字节保持；不能靠直接写私有Main字段验收。F的SyntheticFirstBattleFixture.cs:169已有real binding JSON→public Prepare，不能遗漏它；原生单测可复用此helper与NativeProfileFixture，不需改NativeBattleProfileFixture。正式进程两个路径已计入：StoryPackagePrepareProcessTests的wrapper/editor、BattlePrepareProcessTests的正式脚本。后者目前主要使用无real绑定的profile，**必须补新声明的实际进程正反例所需人工输入，不能以其旧通过替代**；若不能在现有文件与既有fixture设施内表达，先重算F并拆分，不能删掉进程覆盖凑10。H-F人工双闭环不补签RealFirstBattleFlowTests私有真实来源/完整阶段验收，后者仍需独立适用票。

只读scout的原建议原件保留；其中把canonical当测试ledger、无条件改typed envelope、漏登记后的≤10计数不采用。父控已查明L1/L2、wrapper及公开fixture能力，以上是补足后的建议，而非照抄其未完整估算表。

### 本预算之外的真实开放契约

完整装备脚本重放/返回入口、毒添加/排序/抗性/执行与战内投影、RoleHook/BattleEnd的0020、完整setParty/world协调、真实初始来源、适用完整门/私有/GPU/人工/远端均未交付。Main当前world初始party取BattleContent.Players；`src/Pal.Core/DynamicEventSession.cs:130` 接受1–3名明确角色，但当前战斗仍1–2名，毒owner可表达最多5名探索队伍也不扩大战斗/host容量。不能从owner记录数反推party或强行制造六个live角色。需按实际消费点另拆已批准能力叶，**本轮未调查其完整路径，不给虚构总预算，也不据这29路径承诺#132整票可关闭**。不得删#188 gate或从静态效果拷贝推定PAL_UpdateEquipments已实现。

## 独立未来预期、层级与停止边界

以下是未来实施的独立字面验收建议，全部NOT RUN，不能当作本轮RED/GREEN：

- **来源/兼容**：缺声明≠显式空。合法库存例 `[item163,2,inUse65535]`、耗尽非零ID槽、末256槽、WORD65535及数量99严格往返；重复ID/零ID占用/257槽/负数/100数量/65536字、错误类型/重复JSON键/不成组声明拒绝。毒完整16对保留 `(id0,script9)`及其后非零陈旧槽，不自动规整；所有非零ID（含隐藏后缀）须有等级，99/100/65535保持WORD。等级是静态声明，不由被测输出生成。来源缺失不得new empty owner让物品闭包通过。
- **纯预检**：C/F-C从明确初始四owner检查两语言/两分支闭包，不Start、不抽RNG、不改seed、CallId/库存/网格/毒对。stock-only可add/shortage但remove拒绝；完整绑定的Event remove可检查，不开放RoleHook/BattleEnd或remove+setParty。保留非库存legacy及未选中内容能力边界。
- **正常Main**：人工夹具A显式全零毒行，手写stock2+hand1移除3，应stock变0且ID/in-use保留，hand实际/overlay双击清0，余wear贡献attack10与base40在下一场得50；真正完成/ACK后新调用移除wear，再入战不得恢复旧装备。独立夹具B只执行Event移除wear，不进入战斗，毒 `[low98/script11, equip99/script12, low1/script13]` 变 `[low98/script11, low1/script13, 0/0…]`，不是重新seed或按ID去重；非零活毒的后继入战必须等真实毒能力闭合或明确拒绝，不能让内容的空Poisons掩盖活毒。本预期不宣称当前host已支持这种毒拒绝。允许不足已耗库存不退款，带shortage目标的总数不足保持状态并跳转，ScriptSuccess不被0020改写。用正式输入/所属request驱动，不私调settle或改owner字段。
- **身份/生命周期**：首次适用UpdateCompleted同步采样owner身份与值，不能等“余额/库存正确”再断言；echo、早/空/外来/重复/旧display/battle/trigger completion不二次移除。合法新Start是另一操作。活动battle/显示暂停时不外部写共享owner。失败/Exit后第二个同URI Main有新owner且来自明确seed，旧adapter不能污染；prepared Resource/hash保持只读。
- **preview**：保留原factory直接测试与退出null断言；调用/异常释放不碰生产库存/装备/毒/角色/RNG。只验未绑定库存的原能力，不能以它拒绝新命令声称支持执行型库存preview。若批准新增执行型能力，另补当前值全隔离快照与反例。
- **层级**：C/F-C普通Core原生dotnet反馈；S/F需正式Prepare及ResourceSaver/IgnoreDeep加载并核两语言；H-S/H-F需正常Main/SceneRunner及适用最小真实argv代表。Core/SOURCE不签Game，synthetic不签私有，headless不签GPU/桌面；真实首战author/provenance不能手造自然save。
- **执行前**：届时再读当前脚本/runsettings/包基线，独立SOURCE gate和指定RED准确失败位置/消息/测试SHA先冻结；全局Godot只查PID/Name，确认无未知进程/已归还旧租约。原生预算/capture不改。无retryApproval不恢复旧165/168；新crash/abort/unknown/不完整输出保全STOP，不拼TRX/热cache重跑/清UID/升级/加时。

仓库搜索覆盖 `src`/`tests`/`tools`/`scripts`，另核固定 `C:/evidence/m6/loop/M6Loop.psm1` 和 `C:/evidence/m6/loop/Start-M6Loop.ps1`：InventoryLiveState/PartyPoisonState/BattleEquipmentState/PalStoryProfile/CheckedStoryProfile无直接消费者。本文不改变它们收据契约；未覆盖外部已编译C#caller，未来兼容默认参数也不是二进制ABI承诺。

## 交付限制

仅SOURCE报告/独立可读前置，不是#132、任何故事或完整M6交付。17工程叶、storyCredit0、fullM6false不变；#129/#147仍阻塞，#164 SOURCE-only/477 GREEN未跑、#165原cold未解、#168 FAILED466/474、#179 UNKNOWN/fixture及broadAST14失败原件不变。#131/#133/#135/#136已有待定决定不由本文批准。未运行任何SDK/业务测试/资源准备，不给重试/来源/人工签字。未写integration/canonical、未push/PR/main、未关闭票；外层独立Standards/Spec、实际本地集成和current发布后才可关闭#194。
