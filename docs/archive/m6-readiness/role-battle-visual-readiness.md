# #192：逐角色战斗视觉的来源、调用链与完整预算

## 结论与证据范围

这是 #135 的 **SOURCE 前置报告**，不是 #135 的实现或验收。冻结输入为 `execution/m6-loop@f1040a7526ca00a1a08d113f1978d6865f5f3c6d`，实时 #126/#135 正文、评论和原生依赖已核对；#135 起初无 blocker，#192 随后原生挂为其子票和 blocker。#136 无 blocker，但本报告不设计它的逐敌位数据。

1. 已批准的不变量：按 **RoleId** 严格绑定角色视觉，不按队伍位置合并，不把 RoleId 当 SpriteId；新声明缺失不能回退领队；旧未声明形态保留；攻击效果按实际战斗精灵解析；两人上限不扩展。
2. 当前视觉 loader、checked 投影、renderer 及效果缓存均为单份玩家数据。Story 包装入口还把完整 party 折叠为领队 ID + 人数；另一个公共入口没有足够信息认证第二个 RoleId。
3. “已有装备覆盖”只在投影能力层成立：Core 有最后非零覆盖投影，但当前开战门明确拒绝选中角色的非零覆盖。不能只改 renderer 或删除 Core 门就声称 #135 完整。
4. 下述 A/B/C 建议 typed 路线累计 **20 个去重路径**：17 个基础路径，加1个正式包装进程测试和2个非Story合成闭包测试/fixture；还不包含装备开战能力的必要链路及其它未决增量，超过十文件上限。应先确认兼容/API/装备边界，再分成完整能力叶；不能把本文推荐当作批准或再建同题 SOURCE 票循环。

本轮只新增本文，不修改公共契约、Core、Prepare、资源、测试、登记或 UID，不读取/提取 raw，不执行参考、SDK、引擎或业务测试。外部 `acceptance.md`、`source-inputs.json` 和 `source-final/binding.json` 绑定实际命令、原字节和候选；路径/锚点/字面匹配只证明结构与身份，不自动证明语义。独立只读 scout 的原回复也保存在本轮证据目录，不把它当独立票级双轴验收。

## 固定参考与独立规则

| 参考输入 | 本地原字节 SHA256 |
| --- | --- |
| `reference/sdlpal/global.c` | `6e7162374dca7af62e3e67b5ab1359f1dc0e040ac3a94612ae32f3ca37d82ddc` |
| `reference/sdlpal/battle.c` | `45f38a01d08b864151390c99bcbb4622e3ba78a5da80a3657c276bf1c2fea33f` |
| `reference/sdlpal/fight.c` | `6d71434242de081d3cd2d5a0578d05485948e7c6cd507407805d0eafa3e761f5` |
| `reference/sdlpal/global.h` | `07f307f91af62291fba50a345075c54adf170a33d1eba1f389852abf9c44d28c` |

- `reference/sdlpal/battle.c:856` 的 `PAL_LoadBattleSprites`：由 `rgParty[i].wPlayerRole` 调 `PAL_GetPlayerBattleSprite`，以结果寻址 F chunk，按队伍位置 i 保存 sprite。坐标来自 `reference/sdlpal/battle.c:27` 的人数/位置表；双人位置为 `(200,176)`、`(256,152)`，不是按 RoleId 选位置。
- `reference/sdlpal/global.c:1978`：先取角色基表 `rgwSpriteNumInBattle[role]`，再按寄存器顺序 `0..MAX_PLAYER_EQUIPMENTS`（包含额外寄存器）取最后一个非零覆盖。非零不是布尔化后的1，不是第一个覆盖胜出。
- `reference/sdlpal/fight.c:2029` 普攻调用语境：先由行动 party index 取 RoleId，`rgwBattleEffectIndex[PAL_GetPlayerBattleSprite(role)][1] * 3` 后顺序取三帧；攻击帧8→9，目标高度取目标当前帧。不能按攻击者 RoleId、队伍位置或 effect sprite 自身 ID 查组。
- `reference/sdlpal/global.h:444` 是 `WORD[10][2]`。`tools/Pal.Extract/DataTables.cs:256` 已按真实用途提供 `sprites[10]` 的 `preMagicEffectIndex` / `attackEffectIndex`；无需重新导出表或发明映射表。未落在十行内的实际精灵不能直接索引此表；人工 SpriteId41/profile group0 是既有 authored 声明，不是零售表认证。
- `reference/sdlpal/fight.c:940` 的经典规则：正常待机0，非傀儡死亡2，濒死/睡眠1，防御3；待施法5的特殊分支在 `#ifndef PAL_CLASSIC` 下，不应混入。`reference/sdlpal/fight.c:2387` 的列0×10+15属于施法前十帧，**不是本票普攻效果规则**。音效不前移 M7。

**独立字面预期（未执行）**：RoleId4、基图7、七寄存器 `[0,12,0,9,0,0,0]` 应解析 SpriteId9，不是4或12；人工表 sprite9 的列1=6，则普攻帧 `[18,19,20]`，列0=2则施法前帧 `[35..44]`，后者只作为列区别反例。RoleId4换到双人第二位置，图仍为9，位置改为 `(256,152)`。这不是当前 Core 已接受覆盖的正例。

## 当前生产消费图与缺口

### 1. 原生格式、公开入口与数值身份

- `src/Pal.Game/Scripts/Resources/PalBattleVisualProfile.cs:6`：v1，标量 `RoleId/PlayerSpriteUri/AttackEffectSpriteUri/AttackEffectIndex/PlayerPosition`，无逐角色数组。team/背景/palette/数字是既有共享内容，本票无需改 `PalBattleVisualTeam` 或 `PalBattleVisualEnemySlot`。
- `src/Pal.Game/Scripts/Profiles/NativeBattleVisualProfile.cs:22` 的 Story overload 可读 `BattleContent.Players`，但立即转成 `Players[0].RoleId + Players.Count`；`:31` 的 public overload 只接收这两个数和 enemyTeams/requirements/palette。它至多认证领队，**不是完整 party 认证接口**。
- loader 在 `:43` 校验领队；`:89` 种子只含一个 player/effect；`:104` 只校验单玩家0..9；`:166` checked 类只保留单份 PlayerSprite、AttackEffectFrames。`PlayerOrigins` 仍按人数/位置给坐标，不能以它代替角色身份。
- `src/Pal.Core/ClassicBattleModels.cs:10` 的玩家及 `src/Pal.Core/FirstMandatoryBattleModels.cs:18` 的角色快照都没有 base battle-sprite 字段；`src/Pal.Core/ModernBattleContent.cs:168` 的严格玩家 JSON 也没有。不能说传一个 ClassicBattlePlayer 列表就已认证零售 base sprite。可用完整 RoleId 集合核覆盖，图号/效果对应的信任根仍要显式说明。
- `src/Pal.Core/BattleEquipmentState.cs:328` 已实现投影；`src/Pal.Core/ClassicBattleSession.cs:491` 的 `equipmentAgrees` 要求 `!equipment.OverridesBattleSprite(role)`。`src/Pal.Core/FirstMandatoryBattleClosure.cs:109` 的旧注释不能推翻真实门行为。不得为视觉正例洗掉覆盖字或悄悄放行它。

### 2. 两个 Main 来源与正式 Prepare

| 路径 | 当前实际行为 | 后续必须保留/补足 |
| --- | --- | --- |
| Story | `src/Pal.Game/Scripts/Main.Story.cs:78` strict Load 后选择 reference/number capability，再创建 map/random/session | 新绑定必须在发布节点/RNG前完成覆盖和依赖校验；legacy仍按原能力门，不因“旧格式可加载”伪称 Main 任意缺效果也可运行 |
| 非 Story / FirstBattle | `src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs:129` 分别校验 ImagePlayerSpriteUris 的帧与 Source.identity；`:142` 视觉 Load 却仍只传领队+人数 | 图像闭包列表不是 renderer 的 RoleId绑定。新形态要交完整 party 身份；旧 overload 不得猜第二角色。首战声明/real closure冻结不重解释 |
| 共用宿主 | `src/Pal.Game/Scripts/Main.cs:208` 建 Story facade，`:221` 把 real.Visual 交给同一 Story machinery | 修改共用 view 必须回归两种入口；不建第二 renderer/VM/loader。Main.cs本身可能无需修改，不能因它是间接 caller 就不核查 |
| Story author→native | `src/Pal.Game/Editor/PrepareStoryPackage.cs:246` 严格 JSON键/成对效果/队伍形态；`:83` 只收集标量 player/effect seeds；`:132` / `:140` staging/final strict读回 | 新数组必须同时进入 strict metadata、完整 dependency seeds、稳定读取 leases/hash、保存及两次读回，不只序列化数组 |
| FirstBattle author→native | `src/Pal.Game/Editor/PrepareBattleContext.cs:127` 绑定已准备 VisualUri 与 image列表，`:143` 发现完整依赖 | 若 VisualUri 的新图全部在根外依赖图内，可复用现有递归闭包；不能无证据修改它或冒称无需新原生读回验证 |

`scripts/Prepare-StoryPackage.ps1` 与 `scripts/Prepare-BattleContext.ps1` 是既有正式包装，不含角色视觉业务。定点全文件搜索本机固定 caller `C:/evidence/m6/loop/M6Loop.psm1`、`C:/evidence/m6/loop/Start-M6Loop.ps1` 对 NativeBattleVisualProfile/PalStoryBattleView/PlayerSprite/AttackEffectIndex **零命中**；它们消费结果/收据/状态，不消费该 C# API。本轮不改这些契约；搜索边界之外的外部 C# 调用者未知，公共 overload 改名/删除不能靠仓库零命中宣称外部兼容。

### 3. Main 呈现、纹理和效果所有权

`src/Pal.Game/Scripts/Dialogue/PalStoryBattleView.cs:26` 的纹理键 `(bool Player,Frame,Shift)` 没有 RoleId/资源身份；`:61` 只从 `visual.PlayerSprite` 构造一次，`:75` effects 只用相对帧0/1/2作为键。即使保存两份新资源，现 renderer 仍画同一份。必须让实际绘制消费角色索引及行动角色效果，不加仅供测试读取、绘制不用的 observer。

`PlayerDraws` 在 `:183` 使用 party member 的 HP/rest/行动姿势，`_Draw` 在 `:220` 对每个 member 仍调用同一 `Sprite(true,...)`。还需逐成员验证受击/调色：当前该函数仅处理掩护、逃跑、己方行动及 rest，没有把 EnemyPresentation 的 PlayerFrame/shift套给被击成员。这里是 SOURCE 缺口，不是本轮已复现/修复的 runtime bug。`src/Pal.Game/Scripts/Dialogue/StoryEnemyAttackPresentation.cs:36` 已有命中帧4、shift6与掩护区别，优先复用而非改伤害规则。

`src/Pal.Game/Scripts/Dialogue/StoryPlayerAttackPresentation.cs:10` 的效果帧是相对0..2；这是通用序列，不必为不同图再造时间轴。Main 可以在 `src/Pal.Game/Scripts/Main.Story.cs:262` 的真实 action.ActorIndex处选择已核角色/effect；`:523` 的 enemy高度仍是#136未来逐敌位问题，#135不顺带改敌方共享模型。Main owns action/lifecycle，view owns ImageTexture，`:144` 初始化失败调用 ReleaseTextures，结束释放 view，退出 `PalStoryBattleView.ReleaseTextures` 清空自有缓存。新增多角色字典也必须进入同一清理，不全局清缓存/UID掩盖污染。

## 兼容与接口：已确定不变量、尚待选定的契约

| 问题 | 方案与取舍 | 推荐 / 授权状态 |
| --- | --- | --- |
| 新 native 声明及歧义 | A：v1增加可选 typed players，数组非空才是新形态，标量必须满足明确中性/兼容规则。无额外版本，需定义空数组和仍有旧标量的拒绝；Resource默认值/省略字段不等于JSON字段存在性。B：v2专用新形态，同时保留v1加载；隔离清楚但更多生产 writer/兼容案例 | 倾向A，**未批准**。新形态不能缺一人回退领队；旧资源不能被重新解释为新形态。必须先冻结“空/缺失/混写”含义 |
| 完整角色输入 | 保留原 public overload 仅服务legacy，新公开入口接受完整party身份，Story据内容传入；FirstBattle随后明确迁移。相比破坏性替换原签名，多一条受限兼容路径，但不破坏外部历史 caller | 推荐新增完整身份入口、旧入口对新形态拒绝而非猜测，**签名/shape未批准**；不在本报告新增任何API |
| sprite/effect来源 | 人工声明可延续既有 SourceIdentity“声明而非认证”；零售内容需现代已验证图像/表的实际身份根，不能 RoleId→SpriteId推断，也不能把hash完整性当来源认证。#133/#191投影信任决策仍独立待定 | 复用已有表/图像契约，不读raw绕过。新typed行可保留预期SpriteId以核PalSprite.SpriteId，但这也只是内部一致性，非来源认证；具体字段待定 |
| 装备非零覆盖 | #135最终不能忽略这条验收。视觉基础叶先保持当前拒绝，单独在完整上下文能力门中证明投影、图像/效果闭包及拒绝先于副作用，再移除对应限制；不能给Core注入Godot对象或全局disable门 | **拆分/能力证明契约需确认**；不能仅删除 `OverridesBattleSprite` 拒绝，也不应让纯Core承担渲染I/O。不是重开最后非零规则的讨论 |

以上是设计建议而不是实施授权。可确认的是“不猜身份、不回退、不漏caller、不超预算、不移除无替代证明的安全门”；确认具体公共签名、版本/混写规则及装备能力边界之前不得实施推荐路线。

## 完整影响与建议拆分

A/B/C 建议 typed 路线的完整去重清单共 **20 路径**（不是唯一最小实现，也不是已批准改动列表）；1–17是基础清单，18–20是下述建议叶已经纳入的必要测试，不是可以从累计预算排除的条件增量。装备能力尾项及其它未决增量另计：

| # | 路径 | 原因 |
| --- | --- | --- |
| 1 | `src/Pal.Game/Scripts/Resources/PalBattleVisualProfile.cs` | 引用新逐角色声明 |
| 2–3 | 拟新增 `src/Pal.Game/Scripts/Resources/PalBattleVisualPlayer.cs` 与 `src/Pal.Game/Scripts/Resources/PalBattleVisualPlayer.cs.uid` | typed Resource和真实登记身份，不能漏算uid；名称待定 |
| 4 | `src/Pal.Game/Scripts/Profiles/NativeBattleVisualProfile.cs` | 完整party输入、checked投影、帧/图/散列/能力门及legacy |
| 5 | `src/Pal.Game/Editor/PrepareStoryPackage.cs` | strict author schema、seeds/leases、native发布/读回 |
| 6 | `src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs` | 非Story完整party caller，不将image列表当绑定 |
| 7 | `src/Pal.Game/Scripts/Main.Story.cs` | 当前角色/effect选择、失败/结束生命周期 |
| 8 | `src/Pal.Game/Scripts/Dialogue/PalStoryBattleView.cs` | 实际纹理键、每名角色绘制、effect选择与清理 |
| 9 | `src/Pal.Game/Tests/NativeBattleVisualProfileTests.cs` | ResourceSaver→public Load正/拒绝/legacy/双入口 |
| 10 | `src/Pal.Game/Tests/NativeBattleVisualProfileFixture.cs` | 两个不同人工精灵/组、相同图别名与hash变化 |
| 11 | `src/Pal.Game/Tests/PrepareStoryPackageTests.cs` | 正式Prepare schema/依赖/输出拒绝/读回 |
| 12 | `src/Pal.Game/Tests/NativeFirstMandatoryBattleProfileTests.cs` | 非Story新输入与历史闭包兼容、不能串role |
| 13 | `src/Pal.Game/Tests/StoryRoundtripSceneTests.cs` | 真Main真实输入/更新内采样、姿势/两Main复用 |
| 14 | `src/Pal.Game/Tests/AuthoredStoryFixture.cs` | 通过正式Prepare构建测试自有双精灵内容，不直接篡改checked结果 |
| 15–16 | `scripts/GameTestBatches.ps1` 与 `tests/Pal.Game.Tests/GameBatches.Tests.ps1` | source/discovery/批次身份和独立字面登记 |
| 17 | `docs/design/native-battle-visuals.md` | 当前新旧契约/所有权/证据边界文档 |
| 18 | `tests/Pal.Prepare.Tests/StoryPackagePrepareProcessTests.cs` | B已纳入的真实包装进程契约，覆盖schema/CLI失败诊断与退出 |
| 19 | `src/Pal.Game/Tests/SyntheticFirstBattleFixture.cs` | C已纳入的正常非Story双精灵内容；B若也需调整则共享此路径 |
| 20 | `src/Pal.Game/Tests/SyntheticFirstBattleClosureTests.cs` | C已纳入的正常非Story实际呈现/同URI闭包；B若也需调整则共享此路径 |

累计去重为 **17 + 1 + 2 = 20**，即 A∪B∪C。B是否同时修改19–20只影响B自身为8还是10路径，不影响累计20，因为C已经包含它们。不删测试或以“可能不改”排除上述三项来保住17。`src/Pal.Game/Tests/RealFirstBattleFlowTests.cs` 是私有真实入口，不能由合成案例补签；若后续实际实现须修改它或新增样本/接口路径，须在20基础上追加并重新核算，不声称其已包含。装备能力链尚未冻结，另见下项。现有wrapper、NativeContentDependencies、Core数值模型、通用时间轴不是仅因名字相关就必须修改的文件。

建议拆分（均为**待批准的后续叶**，不是现在发布的ready实施票）：

- **A / 原生逐角色能力**：上述1–4、9–10、15–17，共 **9路径**（含新cs+uid）。闭合 typed ResourceSaver→完整party public Load/checked view、完整hash/frame/角色集合/legacy兼容与独立测试。Story wrapper可自然传完整party；旧 public入口保留legacy，对新形态明确拒绝。此阶段FirstBattle新形态不支持、Prepare/renderer未接，绝不签#135；不把新数组保存成功当可游玩。
- **B / 正式准备和非Story caller**：上述5、6、11、12、15–18，共 **8路径**；若同时需更新上述19–20，则正好 **10**，两项在C与累计清单中始终保留。绑定已批准A接口，通过生产Prepare/strict Load证明完整身份；保留旧首战闭包。需更多路径则先重算/再拆，不能漏真实调用测试。
- **C / 正常Main消费**：上述7、8、13–17、19–20，共 **9路径**，真实逐角色绘制/effect/受击/所有权闭环。若必须改变已有 snapshot/时间轴接口，先核调用方再重算；不得加只观察不参与绘制的测试入口。此叶不偷偷包含装备门放行。
- **装备能力前置/尾项**：当前只能给“需要Core公开证明 + 外层完整视觉闭包承接”的边界，不能在未选能力契约前声称其精确文件预算已冻结。先选不含Godot依赖且不依赖test-only许可的能力证明，再核Core/modern/native/调用方/测试完整预算；这不是删掉#135原验收，也不是拆成多个commit规避上限。

A→B→C 不表示装备问题消失，也不意味着#133信任根、#161/#165/#168已解除。本轮只交付#192报告，以上路径都**未实现**。

## 后续验收矩阵（独立预期，不是本轮执行结果）

| 边界 | 字面输入/最小错误反例 | 应验收结果 / 适用层 |
| --- | --- | --- |
| 身份与重排 | party `[4,1]`，人工role4→sprite9/group6，role1→sprite7/group2；交换成 `[1,4]` | 图仍随role；位置按队伍列；效果分别 `[18,19,20]` / `[6,7,8]`，不被effect自身SpriteId影响。public native Load + 正式Main |
| 精确覆盖 | 新绑定只role4、重复role4、额外role5、非法role6；同图但不同role | 前四项拒绝，不回退；不同role共享同一合法图可以存在，不能按图号去重掉角色。native/Prepare |
| 新旧策略 | 旧单人/双人共享scalar无新绑定；新绑定同时有旧标量；空数组 | 旧行为兼容；后两项依确认策略给明确拒绝/解释，不从Resource缺省推断作者是否显式写值。native/Prepare + normal legacy |
| 帧与效果 | 玩家nominal0..8并有合法alias9；缺9；group6缺18/19/20之一；高WORD组65535 | alias9仍可寻址；缺帧拒绝；高组使用int196605..196607校验，不能wrap到WORD/frame0。native |
| 精确依赖 | 只改第二角色图字节；漏其外引frame/Texture/import；extra hash；错SpriteId | preflight/前后散列/内部身份检查拒绝；加载不清cache、不自动import；Source/hash不认证零售来源。native/Prepare |
| 呈现与受击 | 行动role不同、未行动成员、受击/掩护、HP0/濒死；相同frame编号的两种图 | 每人实际绘制自身图，effects随行动角色；受击4/shift6仅作用正确成员，掩护不串位；死亡/濒死2/1。既有通用时间轴与Core伤害不复制 |
| 首个更新/失败释放 | Configure+订阅在入树前；首个有效输入后首次UpdateCompleted；纹理创建中途失败 | 正常生产输入链真实完成后采样；失败不发布半视图，释放自有纹理/输入/订阅/节点，不能只等“正确状态出现” |
| 两Main/cache | 第二Main复用相同URI；退出/部分初始化后重建 | 资源hash/初始图帧不被上例写脏，所有新增纹理集合清空；不全局清ResourceLoader/cache/UID |
| 装备门 | 上文最后非零例；未有已证明的完整上下文能力时给非零覆盖 | 当前必须仍拒绝且无RNG/玩法副作用；未来仅在已批准能力闭包下覆盖正例可游玩。不能用无装备正例补签 |

选层沿用 `docs/testing/verification-matrix.md`、`docs/testing/game-test-layers.md`、`docs/testing/time-boundaries.md`（历史记录中的 `docs/testing/test-layers.md` 与 `scripts/GameTestBatches.ps1` 已于 M7 退役，现由 `tests/affected-map.psd1` 与 `./pal.ps1` 承接）。检查点A的native资源测试不能代签B/C；headless snapshot不能代签GPU像素。本报告不改变历史473等源码计数，也不将其当本轮测试结果。

## 下一合法动作与未运行

父控/操作者先确认上表中的版本/混写、完整party接口及装备能力拆分边界，再依据最新HEAD发布有完整路径预算的A/B/C实施票；不重复生成本报告，不以SOURCE检查代替这些决定。#135仍OPEN，正常准备/生产加载/Main/原生、GPU与真实内容验收均未完成。本轮所有SDK/Unit/Game/Prepare、完整门、私有真实数据、GPU/人工桌面/远端CI均 **NOT RUN**，没有消耗或持有执行租约。

保留17工程叶、storyCredit0、fullM6=false。#129/#147阻塞；#164 SOURCE-only/477 GREEN未跑，#165原cold未解，#168原FAILED466/474及#179历史UNKNOWN等不改写。request.retryApprovalPath为空，AllowRuntime不是旧崩溃/abort重试批准。#190/#191已交付但其现金声明/投影信任选择仍未批准；本报告既不重做也不替它们签字。canonical不由worker直接写入，#192关闭须等外层独立两轴通过、实际本地集成及current发布后执行。
