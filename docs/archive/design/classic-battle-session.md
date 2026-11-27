# ClassicBattleSession：Core 普攻与整轮选令

## 当前 #145：普通战斗逃跑（T17）

非 boss 战斗的逃跑成功域经同一选令/动作队列接通（fight.c 4119-4152；battle.c 1458-1527 + 774），不新增队列、随机源或第二套状态。公开 seam：`ClassicBattleSession.SelectFlee` → `AttemptFlee` 观察面 `ClassicBattleView.LastFleeAttempt`（`ClassicBattleFleeAttempt.EnemyFleeDefense/Draw/Escaped`）与新终局 `ClassicBattlePhase.Escaped`；经 `TriggerScriptSession.AcknowledgeBattle`（script.c 0x0007 operand[2] → fleeTarget 分支）与正常 Main 表现接通。

- **提交与传播**：`SelectFlee(partyIndex)` 要求 Selecting 且当前选令成员；提交者与其后全部存活成员一起逃跑（CLASSIC kKeyFlee 回喂，fight.c 1965-1978 + 1797-1799）；逃跑减半队列速度（fight.c 1546）。成功即战斗立刻结束于 `Escaped`（主循环中断，队列余项不执行、无胜利现金/经验/物品、无逃跑隐藏经验计数）；失败只消耗该动作并加 2 到 flee 计数。boss 战即使数值够也必败，但抽取照常消费。
- **判定数学（WORD 累加器）**：def 在 fight.c 3597 声明为 WORD，逐敌 `def += dex` 与 `def += (level+6)*4` 每次存回即 mod 65536（与 AttackEnemy 的 WORD 回绕先例同款），最终仅 `(SHORT)def < 0` 归零后进入 `RandomLong(0, def)`。回归例：单敌 level 65535 → 262164 回绕为 20（fleeRate 25 必逃脱）；双敌 dex −32768 → 逐项回绕为 48（非钳 0、非负上界；其 WORD 回绕队列速度 32786 在 SHORT 嵌套比较下为 −32750 排最后）。既有求和/负 SHORT 例（52、40024）不越界，行为不变。
- **回执路由**：Escaped 回执按 startBattle operand[2] 路由到 fleeTarget；Lost 仍走 lossTarget；过期/重复/非 owned 回执保持惰性，不二次执行剧情。
- **Main 表现**：成功逃跑播放 16 姿势队列步进行走（仅活人，battle.c 1493-1516），最终移除帧把全部成员（含死亡——战中死亡成员持续可见，fight.c 952-957）移出画面 (9999, 9999)（battle.c 1519-1526）；随后经自有回执释放战斗并续跑 fleeTarget 剧情。
- **覆盖**：`ClassicBattleSessionTests` 逃跑族（boss 抽取/必败、有效面板、成功无奖励、边界相等、失败计数、存活敌求和、负 SHORT 钳零、WORD 回绕、后续队员不再尝试、经典提交覆盖）+ `TriggerScriptBattleBindingTests`（fleeTarget 路由与惰性重复回执）+ scene-realfull 两个 `NormalBattleFlee` 测试（正式 Prepare 链 Main 流程与最终帧全员离屏表现）。计数：Core 1473、Game 1015 positives（scene-realfull 44）。敌方逃跑、脚本强制逃跑、战败读档不在本票。

## 当前 #143：普通攻击全体（T15）

有参考全体攻击能力的队员经同一攻击选令与动作队列提交一次全体目标选令，不新增队列、随机源或第二套状态。能力来源 = 授权玩家字 `ClassicBattlePlayer.AttackAll`（正式 authored/native 链可运输的声明，PLAYERROLES rgwAttackAll；固定参考引擎本身只扫装备效果）∨ 任一非零装备 `AttackAllWord` 寄存器（`PAL_PlayerCanAttackAll`，global.c 2048-2080 原文）。旧拒绝面（玩家字 + 装备字）迁移为能力正例；cooperativeMagic 装备字仍拒绝。

- **选令**：`ClassicBattleSession.SelectAttackAll(partyIndex)`（`ClassicBattleLifecycleSession` 同名转发）要求 Selecting、当前选令成员且具备能力，否则无副作用拒绝；提交即参考 sTarget -1，全员选完才建队。有能力的成员用 `SelectAttack(partyIndex, enemySlot)` 提交具体目标时按 `PAL_BattlePlayerValidateAction`（fight.c 3489-3494）归一化为全体；视图发布 `Players[i].CanAttackAll/SelectedAttackAll`（全体提交时 `SelectedEnemySlot` 保持 null）。`CancelSelection` 清全体承诺与其余承诺一致。
- **执行**（fight.c 3686-3735）：一击一次 `RandomLong(0,5)` 会心掷骰（bravery 保留掷骰恒取会心）覆盖整击；命中序固定 {2,1,0,4,3}；命中集在动作开始时冻结（存活非空槽，空槽与先前动作已杀槽既不命中也不推进除数；PostActionCheck 在整动作后才清 KO，双击第二击仍打第一击击杀目标，applied 伤害 0、不重复计奖）。逐目标：`BaseDamage`→抗性整除（SHORT）→float ×3（会心）→ `/division`（1,2,4,8,16 逐目标加倍，float 除）→ ≤0 抬 1 → WORD 截断扣血（截断可观察，如 1000-10.5→989）；无 +1..2、role0 加成或末尾 float 摆动。双击每击重掷会心并重置除数；attack/health 计数与 KO 奖励每动作一次；胜负与回合只在整动作后结算。
- **观察面**：`ClassicBattleView.LastActionTargets` 逐目标记录（单体/敌方动作单元素；全体一击每命中槽一条，按命中序）；`ClassicBattleAction.AttackAll/DualAttack` 标记全体语义与双击动作；全体记录的 `Damage` 是 applied WORD 差（前 HP 减截断后 HP），区别于单体分支可能过量的原始伤害。
- **Main 接线**：AttackMenu 确认时选令成员 `CanAttackAll` 则直接 `SelectAttackAll`（uibattle.c 1094-1099 + 1611-1617，经典全体成员不进目标选单）；一击一次表现动画：非双击走向固定 (150,100) 中心，双击+全体钉在本位（fight.c 2080-2101：首击 -8/-4、第二击 -20/-12），FX 同帧绘在每个命中敌位（2173-2190，无逐帧偏移），全体受击色偏（8-9 拍）与纯水平震动（10-12 拍 -8/-4/-6，2198-2256），逐目标伤害数字在各命中敌位同时入队（DisplayStatChange 语义）。
- **覆盖**：Core 新增四例（`ClassicBattlePartySessionTests`：全体顺序/除数/截断字面值、无能力拒绝与提交边界、会心单掷与双击重掷/计数一次、冻结命中集与单次奖励；装备闭包同时携带 rgwAttackAll 与双击状态）；`ClassicBattleEquipmentSessionTests` attackAll 字（1/65535）迁移为四入口能力正例；`TriggerScriptBattleBindingTests` attackAll 迁移为可开战 + 卸装备后能力消失；`ModernBattleContentTests` 两事实与装备闭包理论行迁移为可执行；scene-realfull 新增全体成员 Main 流程与全体攻击表现字面表（两个新增测试均注册于 range 可执行的 scene-realfull，表现字面表由此获得执行证据；scene-battle-light 保持其原有 30 例）。法术、道具、自动选令与法术型全体（kMagicTypeAttackAll）不在本票。

## 当前 #142：经典选令取消与目标返回（T14）

选令编辑接入固定参考的 PAL_CLASSIC 取消分支，不新增队列、随机或第二套状态。公开 seam：`ClassicBattleSession.CancelSelection()` → `ClassicBattleLifecycleSession.CancelSelection()`（同一 active combat owner，发布视图跟随重开成员）。

- **主选单取消（uibattle.c 1224-1252）**：仅 Selecting 阶段可用；一次取消恰好回退最新已提交且可重选的一名成员，使其重新选令；参考回退循环跳过死亡/不可选者（本域只有死亡成员，且死亡成员从未提交选令）；首位可选成员之上无可编辑项，请求为无副作用 no-op（不 Publish、不消费随机、不动队列）；其他成员已提交目标保持不变。最终成员提交后进入 Resolving，编辑关闭（InvalidOperationException）；已结束的 lifecycle 同样拒绝。已提交逃跑与可编辑选令不会共存（它提交后续全部成员并直接开轮），故清目标即完整回退；本域尚无物品预留。
- **目标选单取消（uibattle.c 1512）**：返回动作选单，未提交任何内容，无 Core 调用；重进目标选单从首个存活槽重新开始（无 previous-target 记忆，battle.c:1771 初始 -1 语义不变）。
- **Main 接线（input.c 66-70）**：`_Input` 捕获 kKeyMenu 键族（Escape/Insert/Alt/KP_0），仅在 story 战斗 AttackMenu/TargetMenu（不含 HookDialogue/Feedback/Settlement，不含探索）；每帧丢弃取用，stale/echo 取消不会入队；`ProcessStoryBattle` 的 AttackMenu 分支调 `lifecycle.CancelSelection()`，TargetMenu 分支仅切回动作选单并清 cycle。
- **覆盖**：`ClassicBattlePartySessionTests` 三例（新建承诺回退/首位边界、死亡成员不重开/末员提交关闭、混合空槽下回退与重选仅限存活槽）、`ClassicBattleLifecycleSessionTests` 一例（经 lifecycle 视图回退并在结算后关闭），及 scene-realfull 的正式 Prepare 链 Main 流程（见 `src/Pal.Game/Tests/README.md` #142 小节）。

## 当前 #207/#210：一至五个实有敌位贯通 Core 与外层门（#138）

`ClassicBattleSession` 与 typed `TriggerBattleContent.EnemyTeams` 接受 **1..5 个完整、非 null 的敌位**；玩家接受域为 1..3（#137）。沿用正式 trigger → `ClassicBattleLifecycleSession` → combat 链，不新增接口或第二套战斗引擎。0、6、null 槽和已有不支持能力仍在 RNG/玩法副作用前拒绝，typed team 容量诊断带 teamId。此节更新后文历史检查点的 Core 敌方上限，不覆盖其他能力限制。

- `EnemyTeams[teamId][slot]` / `Current.Enemies[slot]` 的 ordinal 是实例地址；重复 ID7 的五槽仍各有独立 HP、队列与目标。typed 内容深拷贝全部槽及嵌套状态/毒数组；调用者替换输入数组或后续推进不改变已发布快照。
- 不改既有排序与重定向：选齐存活玩家后才按 E0..E4、P0..P1 抽身法；完整13项参考队列作 WORD 截断、SHORT 严格嵌套比较，不替换为稳定排序。死亡队列位置 pass、不消费动作随机；执行时目标死亡，从原 slot 循环找存活目标。最后一敌 KO 才 Won，终态保留唯一回执。
- 固定只读参考：`palcommon.h:60` 的 `MAX_ENEMIES_IN_TEAM=5`；`fight.c:87–129` 自动目标、`1450–1601` 经典队列、`1705–1770` 死亡 pass、`3624–3670/3753–3757` 普攻和计数；不编译或运行参考。

公开 Unit 独立字面预期位于 `ClassicBattlePartySessionTests` 与 `TriggerScriptBattleBindingTests`：

| 行为 | 独立输入 / 预期 |
| --- | --- |
| 容量、身份与拒绝 | 1..5 敌 × 1/2 玩家可构造；重复 ID7 的0..4均可选；-1/5拒绝且快照/RNG不变；0/6/null槽、三玩家和槽4法术仍拒绝 |
| 同速与非稳定反例 | 五敌 level0/dex0、两玩家 dex18、七次倍率1 → `E0:18,E1:18,E2:18,E3:18,E4:18,P0:18,P1:18`；玩家1/100 → `P1:100,E1:18,E2:18,E3:18,E4:18,E0:18,P0:1`，另有不同倍率截断例 |
| 死亡目标与回执 | attack40、防御有效24、+1、无暴击、倍率1 → 每击43；五敌HP43，两人同选4，先杀4再绕至0，1/2/3仍存活；后续公开回合杀3/1/2才Won，死敌pass无额外随机，旧快照保留HP43 |
| 五敌逐槽行动 | 敌速度118..114先于玩家100/90；敌强度36..40对防20，目标0/1/0/1/0 → 伤害20..24，玩家最终HP134/156；每个 ActorIndex 及敌自身HP100..104独立 |
| trigger / lifecycle / continuation | 单角色HP300/max350、五敌HP43，按4/0/1/2/3依次击杀，剩敌合计10次伤害20；rawHP100，五个战后钩子按 caller0..4真实执行后半恢复HP225/MP35，仅 owned ACK 提交并进入新五槽战斗；旧/重复回执无效。五轮随机请求数30/24/18/12/6合计90，无成长抽取 |

存量 `ClassicBattleSessionTests.UnsupportedDefinitions` 和 `ModernBattleContentTests` 的解析容量负例原以三敌代表超限，同步改为六敌，并独立覆盖解析后1..5槽的顺序、重复ID及HP。`ModernBattleContent.Parse` 委托 `TriggerBattleContent` 构造并验证，因此该纯Core解析入口也获得1..5容量，不是仍停留在双敌的外层门。这是同一容量契约必要的调用方/测试迁移，不能保留与新接受域矛盾的断言。

**#210 外层迁移与证据边界**：#207 的 Core 1..5 接受域已贯通外层链，这是对 #207 期间按票面边界刻意保留的双敌门的有意迁移，不是回退：`BattleDataProjector` 投影 dense 实有敌位 1..5（源槽仍仅 0..4，零槽、零 objectId 与空队仍拒绝）；非投影 native 链在 `NativeBattleContent.CreateChecked`（读回）与 `Write(content, null)`（无 receipt 的 Prepare 写出）按同一 1..5 loaded 域拒绝 0/>5（诊断带 teamId）；投影槽行 `BattleProjectionSourceInfo` 每队 1..5 loaded；`PrepareStoryPackage` author schema 的 `visual.teams[].enemies` 接受 1..5 逐槽绑定并与数值敌位一一对应。重复敌人定义多槽仍不合并。测试迁移：投影 `three` 拒绝例删除并由五槽正例取代、native `enemies-overcapacity` 与 Prepare `three-slots` 拒绝例迁至六槽，投影 `three-loaded` 缺陷例改经投影/native 槽数不匹配路径拒绝；新增非投影、投影槽行与 Prepare 五槽正例。零空槽/FFFF skip 与数据表零槽语义仍归 T13b（#140）；正式 Main 的五敌目标循环、所选敌队参考布局与 #138 余项未在本叶验收，不解除 #140 的父依赖。Extract Unit 与 native-map headless 证据不替代 Prepare 进程/私有真实资源/GPU/Windows人工/远端CI。空槽、三角色、新动作、敌方法术/双动与旧失败支线不在本叶。敌方 HP/目标/队列仍由 combat 唯一持有，lifecycle 结算角色、trigger 在 owned ACK 后提交；不新增持久化 schema 或另一份真状态。

## 当前 #140：敌队 0xFFFF 跳过项与 0 空槽的数值/Core 契约（T13b）

本节更新上文 #210 刻意保留的零槽边界，不是回退。`ClassicBattleEnemy` 新增可选 `IsEmpty`（缺省 false，typed/JSON/record-with 均保留）：仅 `IsEmpty=true` 允许 canonical `id=0`、`hp=0` 与全零能力/奖励/钩子/状态/毒，`ClassicBattleSession.ValidateEnemy` 与 `ModernBattleContent.Validate` 在 RNG/玩法副作用前拒绝其它拼写；`ClassicBattleEnemyView.IsEmpty` 发布明确空状态，战中 HP=0 的实有敌保留原战内 id 且 `IsEmpty=false`，两类零值不合并。团队仍要求 1..5 槽且**至少一个非空敌**（全空队拒绝）；空槽不可选为目标、不抽行动随机、不进队列、不运行钩子、不贡献奖励，死亡目标循环重定向与最后一实有敌 KO→Won 沿用既有规则。

投影按 `PAL_StartBattle`（battle.c:1617-1717）：FFFF 不占运行时槽，0 保留 `wObjectID=0` 运行时槽；数值 `id` 仍是战内 target ID = 运行时槽+1，重复对象不合并，收据逐行保留 sourceSlot→运行时槽（`compactSlot`/`battleId`）映射与 `empty-0` disposition（仅四个共享键），dense 队输出与 #210 逐字节一致。外层 native/Main 接线域在 T13c 前仍显式拒绝空槽：`NativeBattleContent.Write` 遇 `IsEmpty` 敌位带 teamId/slot 拒绝（不静默丢字段），`ProjectedBattleSource`/`BattleProjectionSourceInfo` 对 `empty-0` 槽行按 unknown disposition 拒绝，非投影 1..5 loaded 域不变。本票不宣称 native/Prepare/Main 已支持空槽；Core/Extract Unit 证据不替代引擎层验收。

## 当前 #160：live Name 角色钩子所有权（Core 窄契约）

本节同步 [#160](https://github.com/fanxiao92/godot-pal/issues/160) 的当前 Core 实现；**后文 #47/#48/#49/#46 等是历史检查点**，其中单人、零钩子及静态初始入口等描述不覆盖本节的现行 seam。本票仍未整票验收、未集成，不代表原生资源/Main 或 M6 交付，也不增加故事验收计数。

### 唯一 bank、声明与启动顺序

- 唯一可变入口存储是既有 [`DynamicEventWorld.ObjectScripts`](../../../src/Pal.Core/Events/DynamicEventSession.cs)，键为显式 `(NameObjectId, slot)`：slot0=`FriendDeath`，slot1=`Dying`。`NameObjectId=null` 是未映射的 legacy 静态路径；Name WORD `0`、`0xFFFF` 都是真实名字对象身份，与脚本 entry0 的禁用语义不同。相同 Name 的角色 alias 同一双槽，且声明的 FriendDeath、Dying seed **各自**必须相等；不同 Name 即使 seed 相同也不 alias。
- [`TriggerScriptSession`](../../../src/Pal.Core/Events/TriggerScriptSession.cs) 对选中闭包中可达的 mapped battle 做小型、纯 typed 能力检查：只要有玩家声明 Name，就要求全体玩家映射、Name 在 WORD 范围内、同 Name seeds 一致，并且实际 Start/当前查询绑定 world。不是调用完整 modern 数值格式 validator；legacy 简写数组及既有格式诊断顺序不变。
- 构造器、`ForSharedRoles`、纯查询均不 seed、不重绑 world/角色 owner。成功 `Start` 先完成全部纯预检，再为实际结构可达 mapped battle 的相关双槽**仅补缺失键**，之后才创建 CallId、改 caller/success/PC、执行前缀及消耗生产 RNG。已有值0优先于声明 seed；无 battle 或 entry0 不因传入 content 而 seed。基本、world-only、battle-only、shared+world 绑定仍各守原能力边界，不从未映射内容制造 owner。

### 实际参与者的 live reader 与真实完成回写

- 正式路径为 Trigger `Continue(startBattle)` → [`ClassicBattleLifecycleSession`](../../../src/Pal.Core/Battle/ClassicBattleLifecycleSession.cs) → [`ClassicBattleSession`](../../../src/Pal.Core/Battle/ClassicBattleSession.cs)。仅 internal overload 借用 `Func<ClassicBattlePlayer, ClassicBattleHookKind, int>? readRoleHookScript`，每次真实检测按实际 player 的 Name 与 kind 读当前 bank：Dying 读濒死本人，FriendDeath 读实际 coverer，而非死者。live0 是禁用，不是缺失。既有 public 独立 combat/lifecycle 构造器仍传 null、读静态声明，不创建新 bank。
- 钩子 caller 是实际 `RoleId`，0 合法，不是 Name、party index 或 EVENTOBJECT count owner。只有外层 `Start` 的 caller `0xFFFF` 继承 `_lastCaller`；Name `0xFFFF` 与根 RoleId0 都不是继承标记。这不引入 [#129](https://github.com/fanxiao92/godot-pal/issues/129) 的嵌套 VM/call、保存入口或 count 语义。
- `StartBattleHook` 仍先预检**实际 pending entry X**，通过后捕获不可变 `(NameObjectId, slot, X)`；X 不取声明 seed、此刻 bank、返回 PC 或外层 `ReturnEntry`。真实 hook 内 `0090/setObjectScript` 写自身槽Y后等待 display 时保持Y；只有正常根 `keep` 或真实到达 PC0 的完成才把X写回捕获槽，**先写 bank，再清临时 hook owner，再 lifecycle `AcknowledgeRoleHook`**，随后恢复外层 caller。外层 `ReturnEntry` 不是 hook bank；其他 Name/slot 不受该回写影响。
- 真实用例覆盖 pending5→另一个 executor 写 bank31→hook 自写0→display 等待仍0→真实 display 完成且 owned ACK 后终止→回写5、恢复 outer caller28。仅 display 完成而未 ACK、早到/foreign/stale/重复 display 或 battle 回执均不提交回写；重复回执也不能覆盖后来真实写入。跨文件位置仅用于诊断和寻址，文件名不是 bank key。enemy battle-end 按实际 enemy slot 作 caller，丢弃脚本返回 WORD，没有 Name owner。
- 同一 world 的后续 battle/新 executor 都读当前 bank；`CreateIsolatedCopy` 复制**当前**入口值且后续变更隔离，再次复制也取复制时的当前值。共享入口不自动共享 legacy executor 的角色 HP；真正结束 battle 的 settled roles 仍仅由该生命周期 owned completion 的合法 ACK 发布给事件，再用于下一场。

### 纯结构闭包及有界性

现有 walker 从选中 Event 根及结构可达 battle 出发，以当前 world 值（包括0）覆盖缺失键的声明 seeds；把每条结构可达 `0090` 对相关 Name/slot 的 literal 写值加入未来 hook 根，迭代至有限不动点。实现按 key 记录先发现的写入，相关 key 每个仅激活一次；后来发现的写入直接入工作表，根按 `(key, entry)` 去重。因此覆盖写在发现 battle/key 之前、初始0后来启用、随机双分支、hook 再写另一 hook、backedge 及 alias。相关闭包内坏/越界入口、raw、content-only、不支持命令或缺失语言在 CallId/caller/success/PC/前缀/角色/music/RNG/seed 副作用前失败；`source` 只作诊断，不路由执行 opcode。无关 Name 与 slots2..5 仍只是存储，不成为玩家 hook 根。

设N为事件集合命令数、W为可达 literal 写候选数、K为相关 Name/slot 键数。现有最多两角色×两槽，K≤4；`visited(Address, Context)` 的 Context 仅 Event/RoleHook/BattleEnd，至多3N。按 key 索引的工作表开销为 O(N+W+K)，加上既有显示解析/校验代价D，总计 O(N+W+K+D)。这不是实测 benchmark、显示恒定成本或脚本运行终止证明；没有 WORD 状态笛卡尔积枚举、深度截断、新 VM、运行时递归推进或推测性 cache。

### 当前纯查询与静态入口的边界

```csharp
public TriggerEntryRequirements GetCurrentEntryRequirements(string fileId, string entryId, int caller);
```

该既有公开方法绑定当前 `BattleRoles`、`Context.Color`、events/language/content 及相关 world keys，返回既有深不可变 [`TriggerEntryRequirements`](../../../src/Pal.Core/Events/TriggerEntryRequirements.cs)（`DisplayBlocks`/消息引用、`PortraitIds`、`BattleTeamIds`），不创建生产调用、seed、重绑、消耗 RNG 或改变 CallId/caller/PC。它与 `Start` 共用 `ValidateSelectedStart` 纯 guard：阻塞中的 active call 抛 `InvalidOperationException`，非 WORD caller 抛 `ArgumentOutOfRangeException`，入口/Name 映射/world 可用性/闭包/语言问题抛带文件或命令位置的 `InvalidDataException`。caller `0xFFFF` 仅保留对 `_lastCaller` 的继承约定，不更新它；查询结果不是跨后续更新有效的授权，真正 `Start` 必须重查。

静态 `ValidateEntry` 仍是 initial-content、纯且无计数器的预检，以初始角色及声明入口保守纳入相关未来 `0090` 写根，不要求 live world、不读取当前 bank。它不获得 #129 的 call/goto/count 能力或 `ValidateReturnEntryPreserved` checker 权限；当前查询也不扩大既有构造绑定的执行能力。

### 已有 Core 用例与尚未交付的外层

[`MidbattleRoleHookTests`](../../../tests/Pal.Core.UnitTests/MidbattleRoleHookTests.cs) 经公开构造、选令、真实动作、display.Update 与 owned ACK 覆盖上述行为，不注入假 hook/receipt：例如 `Current_entry_requirements_are_pure_and_do_not_seed_or_advance_last_caller`、`Aliased_Name_zero_Dying_hooks_use_the_shared_current_entry_and_keep_the_captured_return`（Role0/1 同 Name0）、`Dying_root_keep_restores_the_captured_actual_Name_entry_after_its_own_write`，以及 `Ended_battle_owned_ACK_starts_the_next_battle_with_settled_roles_and_the_current_mapped_Dying_entry`（第一场真实 Won/结算及 owned ACK 后，第二场真实检测当前入口；不是第二场结算证据）。同文件另有实际 coverer FriendDeath、PC0、跨文件、真实 foreign display、同 world 新 executor 与当前副本/local keep 用例；预期来自独立 authored 字面值及参考核对，不由被测实现生成。

D1 的生产 Start 前当前查询与完整资源预检、D2 的正常 Main/资源/真实输入/`UpdateCompleted`+ACK，以及未来 #129 集成、完整 M6 保存/66条C故事，均不由本 Core 前置票实现或验收。[`NativeStoryProfile`](../../../src/Pal.Game/Scripts/Profiles/NativeStoryProfile.cs) 的静态双语检查和 [`PalTriggerDialogue`](../../../src/Pal.Game/Scripts/Dialogue/PalTriggerDialogue.cs) 到 battle 即停的 preview 不能证明当前 live 资源闭包；Core 反馈不替代 Game/native/Main、GPU、人工、私有真实资源或远端 CI 门禁，`.packages` 正式 Source 基线要求不豁免。

## #47 纯 Core 多人检查点

同一 `ClassicBattleSession` 现支持每侧1或2个初始存活参与者；不是全游戏战斗或 #47 整票交付。下文 #46 的 lifecycle/trigger/modern/native 入口仍保持单人单敌，后继 unit 才扩展这些正式 typed 契约与 Main。装备、双击、掩护、状态、钩子、奖励和成长的非零能力仍在副作用前拒绝。

- `Players` 顺序是 party index，`RoleId` 是独立角色身份（0..5，不重复）；`Enemies` 顺序是 enemy slot，旧 `ClassicBattleEnemy.Id` 意义不变且允许重复，不能用它代替战场 actor 身份。不把 role0/1 隐式转换为 party index。
- `SelectAttack(partyIndex, enemySlot)` 仅接受 `Current.SelectingPlayerIndex`，按 party 顺序为存活队员选令；目标须为存活 slot。第一人选完仍 Selecting、Round 不增、没有 queue 或排序 RNG。全员选完才按敌 slot、party index 顺序请求随机身法并建立整轮队列。
- 旧 `SelectAttack(targetId)` 只适用于每侧恰1人的原调用；任一侧多人时明确拒绝歧义。旧 `PlayerHp/EnemyHp/Counts` 是 index0 的兼容投影，**不是全队合计**。旧 action 的 `TargetId` 仍为被攻击敌人的 Id 或玩家 RoleId；新增 `ActorIndex/TargetIndex` 由 side 决定其索引域，single 默认0，保留旧 action 构造与相等比较。
- `Current.Players(PartyIndex,RoleId,Hp,Counts,SelectedEnemySlot)`、`Enemies(Slot,Id,Hp)`、`ActionQueue(Side,ActorIndex,Dexterity)` 是深只读快照；输入列表已拷贝。每人选中目标及 `LastAction` 是真实选令/执行历史；新回合清选令。没有新可写 UI previous 状态：固定参考 battle.c:1771 初始 `iPrevEnemyTarget=-1`，uibattle.c 两处赋值均注释禁用。因此失效目标从原 slot 循环寻找存活敌人，不擅自记忆另一队员的目标。
- `ResolveNextAction` 一次消费一个可执行队列位置。已死玩家/已清除敌人的位置只 pass，不抽动作 RNG、不重复 LastAction 或增加计数。敌每次执行先在完整 party 索引区间抽目标，对死者重新抽；全敌 KO 才 Won，全队 HP0 才 Lost，终态不再执行，回执仍唯一。
- 严格照 fight.c:1450–1601 对完整13槽（battle.h:166，3+5×2）执行嵌套 SHORT 比较，非 stable sort。公开 queue **仅含首个 WORD `0xFFFF` 之前的可执行前缀**，不暴露虚构空 actor。空槽 SHORT -1 会排在负 SHORT 身法之前，actor 的 WORD65535 本身也结束本轮。这是经参考证实、获准修正的旧 single 两项比较遗漏；合法 numeric 输入仍接受，普通 single 路径 API/伤害/RNG 顺序不变，不宣称极端负 SHORT 路径行为完全兼容旧实现。

参考：fight.c:87–130（失效目标循环），1384–1586（选令/WORD/SHORT队列），1705–1770（执行时死亡 pass），3504附近（普攻目标验证），4520–4581（敌随机目标及死者重抽），744–776/1116–1160（敌清除、整队胜负）；battle.c:1771（previous初值）。所有测试通过正常公开选令/执行到达死亡与失效，不写私态、不注入战果。
## #48 unit 2：装备执行语义、双击与掩护接入 session/Main

`ClassicBattleSession(definition, random, BattleEquipmentState?)` 追加可选装备闭包参数；声明的闭包（`Declared`）即所穿对象的正式执行语义，session 消费投影后的有效面板与状态覆盖，从不执行 source opcode：

- 有效 attack/defense/dexterity 由 `EffectivePlayer` 投影后进入伤害、身法与 `Current.Players[i]` 的每成员发布字段（attack/defense/dexterity）；未声明闭包时保持旧零装备域。
- `kStatusDualAttack`（fight.c:3628）：一个攻击动作两次打击，`ResolveNextAction` 每次推进一击，两次都发布独立 `LastAction`（`SecondStrike` 标记第二击）；第二击保持已验证目标槽（首击击杀后不再换目标，HP 也不低于0再扣），胜负与回合只在整动作后结算，attack/health 计数每动作一次（fight.c:3753）。
- `kStatusBravery` 恒走会心分支但保留掷骰（fight.c:3633）；`kStatusProtect` 在 HP 截断前将敌方伤害减半（fight.c:5086）；`kStatusHaste` 按 CLASSIC 将队列身法×3 后再取999上限（fight.c:336）。
- cover（fight.c:4943–4973）：目标濒死/失控且防御掷成功时，由 `CoveredByRoleId` 对应的在场且可响应（自身不濒死、无 confused/paralyzed/sleep）队友掩护；掩护动作 Damage 0、Evaded false、`CovererPartyIndex` 指向掩护者，友亡对白与本回调无关。`ValidatePlayer`/`ValidateNoGrowth` 打开非零装备与 0..5 cover 的拒绝面；装备 battleSprite override 因视觉链仅绑定一个 authored 基础精灵而仍按"缺少/未支持装备依赖"在副作用前拒绝（`OverridesBattleSprite`）。
- 战中角色钩子（fight.c 768–880，`PAL_BattlePostActionCheck` 仅在敌方攻击动作后带 `fCheckPlayers=TRUE`）：先友亡后濒死按队列顺序扫描，每次动作至多一个脚本。友亡=成员 HP 降至 0 时由**掩护者**的 `FriendDeathScript` 以掩护者角色为 caller 运行（掩护者须在场、存活、可响应）；濒死=成员 HP 下降且 >0、新 HP 低于 `min(100, maxHP/5)`、且**原 HP ≥ maxHP/5**（原始整除，不用 min）时由本人 `DyingScript` 以本人角色为 caller 运行，掩护者不可响应或不在场则跳过；濒死条件命中后即使无脚本也终止扫描（fight.c 880）。命中后 combat 进入 `RoleHook` 阶段并发布 `PendingRoleHook`（kind/partyIndex/roleId/scriptAddress），拒绝推进直至 `AcknowledgeRoleHook` 回到保存的队列位置，Won/Lost/回合结算在钩子之后才评估（g_Battle.BattleResult pause）。
- 钩子副作用按脚本 opcode 落到活体战斗状态：`changeHp/changeMp`（script.c 0x001B/0x001C，仅 caller 域、PAL_IncreaseHPMP 只改活人并按 [0,max] 封顶）、`boostStat`（0x0030，`kBodyPartExtra` 寄存器写 `基面板×percent/100` WORD 截断覆盖写；攻击/灵力/防御/身法/逃跑五字段，非零 `roleOperand`/`scope` 及其他字段在副作用前拒绝）。临时加成只在本场 combat 会话内（战后随会话释放，battle.c 1829 同域清理），装备持续双击不受影响；有效面板/队列身法/敌伤防御实时包含 extra 寄存器，并在玩家 view 以 `Boost*` 五字段公开。`TriggerScriptSession.StartBattleHook` 预检钩子完整随机闭包（两个分支全覆盖，含 startBattle 嵌套拒绝）后才消耗正式随机并执行；挂起的外层调用保留自身 caller/ReturnEntry。角色钩子非零 `DyingScript/FriendDeathScript` 的旧拒绝面已打开为正式执行；入口预检（`ValidateEntry`）同样遍历 hook 脚本全部分支与显示/画像需求。
- 生命周期把闭包好状态覆盖进角色状态（`ApplyStatuses` 取大），战后基础清理（<=999 清零）因此保留 >999 持续装备状态；再次初始化/新会话按 max 不叠加。普通有期限状态与 extra 临时效果仍为零域；每回合状态递减（fight.c:1636）未建模，属后续边界。

Main/View：两击各走一次正式 Feedback 表现；掩护时掩护者以 frame3 站到目标侧前（fight.c:5012–5022），目标不再取闪避/受击帧。`samples/authored-battle/PartyEquip` 提供 role1 五对象闭包（有效 46/52/119，对象170持续双击）+ 濒死 coveredBy 与双快敌槽的人工场景；SceneRunner 增两例（两击表现/删除双击反例），scene 批次 109→111、库存 604→606、game 601→603、Core 831→847。

#49 unit 1（Core 链）：`MidbattleRoleHookTests` 以原钓 authored 钩子流（四随机带、零售结构同构）覆盖检测/暂停/恢复、全部随机分支、逐字副作用与继续位置，Core 批次 847→872；Extract CLI 批次 57→58（角色钩子动词双向往返与严格校验）。不包含资源/Main/SceneRunner（unit 2）。

参考：fight.c:336–349（haste×3）、3628–3673（双击循环/目标保持）、3753–3755（计数一次）、4943–4973/5008–5028（掩护条件与表现）、5086–5090（protect 减半）；global.c:2255/2304（好状态取大/清理保留 >999）。

状态：#46 的 **Core 内部检查点**。单人单敌经典普攻、选令后随机排序、逐动作推进和真实 Won/Lost、下述零成长正式生命周期及 trigger 事件 binding 已实现；不是整张 #46 或 M4 的交付。独立现代数值 native codec 及完整显式离线数值 Prepare/Editor/PS 已追加（见[数值契约](../../design/modern-battle-data.md)），但完整人工 package、Main/真实输入与表现尚未接通。

## Interface 与数据

`ClassicBattleSession(ClassicBattleDefinition, IBattleRandom)` 正常构造后处于 Selecting；`SelectAttack(enemyId)` 提交本轮攻击及唯一目标，之后才消耗排序随机数；`ResolveNextAction()` 每次推进一个完整业务动作，双方存活且本轮两个动作完成后返回 Selecting，再次选令重新排序。没有业务 deadline，因此不接收无效时参，不读取墙钟。未来 Main 必须成为唯一推进所有者，在动作表现真实完成处推进；这不是一帧一条事件命令的调度器。

`Current` 是不可变 view，公开 phase、round、双方 HP、最近动作与战内 attack/health 动作计数。Damage 保留参考规则的命中伤害（玩家对敌可过量，敌对玩家先截至剩余 HP）；Evaded 与 Critical 独立表示。输入定义/外部角色不原地写入，旧 view 不随更新改变。战内计数按 WORD 增加并保留，不因奖励为零省略；不是永久属性或经验升级。其他动作尚不可选。

只有真实 KO 才生成 `CompletionReceipt`，没有公共构造器或接受任意 outcome 的完成接口；终态保持同一个 receipt，不能重开或重复结算。后续事件请求须持有其唯一 active session，并比较该 session 的非空 `Completion` 的**对象身份**，再读 Outcome；不能仅根据相同结果、枚举、调用者或地址接续，也不能接受另一 session 的 receipt。下述 trigger binding 实现了该约束；它只接受 lifecycle 而非 raw combat 的完成回执。

`IBattleRandom` 是正常业务依赖，不是测试开关。整数和 float 区间均含端点，退化区间必须不消耗底层熵；Core 仍如参考请求敌目标 `0..0`。float 使用单精度。此处规定规则所请求的区间及顺序，不提供宿主随机适配器或承诺与原程序按 seed 重放。外层以后提供生产随机源；Unit 只在这个外部 seam 提供逐项可核的随机输入，不复制战斗引擎。

新紧凑数值 DTO 必要：#45 的 `BattleRoleSnapshot` 保存角色/装备/经验上下文，但没有战斗 attack/defense/dexterity，行走 profile 也不是战斗数值来源。玩家数值在本支持范围内就是零装备效果的有效值；敌 attack/defense/dexterity 是**有符号原表 offset**，level 加成由 engine 计算，不把 offset 当有效面板。新类型无既有序列化调用方，不修改旧 snapshot、codec 或真实 v1。

构造器在创建可变战斗状态、消耗随机数之前预检：

- 恰好一个存活玩家、一个存活敌人；玩家 role0..5，敌 ID 非零 WORD。
- 当前 HP 为 1..32767（本有界能力不支持更高当前 HP）；玩家 maxHP 为正 WORD 且不低于当前 HP，攻防身法为 WORD；敌 level/physical resistance 为 WORD，三个 offset 为 signed WORD。
- 装备/status 可以为空或分别为六/九项全零；poison 必须全零。非零装备、状态（包括双击）、毒、角色/敌方钩子、cover、attack-all、敌魔法/双动/等效道具及其概率、经验/现金均明确拒绝，不静默忽略。
- 非 Selecting 不能提交选令；错误 target 在状态变化和 RNG 前拒绝。非 Resolving 的推进返回 false。

这是纯战斗数据，不接收存档/成长表，不宣称零 Exp 就完成了全部战后规则。#50 将复用这些真实计数；上层必须经下述生命周期提供合法初始经验、无待学法术的人工数据及适用的基础恢复/清理，不能用人工 profile ID 特判跳过。

## 零成长正式生命周期（#46 有界检查点）

事件后续不能直接以 raw combat KO 恢复。正常业务入口是 `ClassicBattleLifecycleSession(BattleRoleSnapshot, ClassicBattlePlayer, ClassicBattleEnemy, ClassicBattleGrowthData, IBattleRandom)`；它私有持有唯一 `ClassicBattleSession`，只转交 `SelectAttack` / `ResolveNextAction`，不另建伤害、排序或推进循环。`Current.Combat` 是战内快照，`Current.Role` 是角色持久快照：例如 raw HP180 胜利后可结算为 HP190，两者不得混用。`Current.IsInBattle` 取自真实 active combat 引用；结算及清理后释放该引用，不再接受动作。

既有 `BattleRoleSnapshot` 提供 MP/maxMP、primary/七 hidden EXP、32槽已学法术、六装备与九状态；私有可变角色复用 `BattleRoleState`，外部只得到深拷贝、只读集合的快照。角色和 compact player 的 role/HP/maxHP 必须一致，零装备/status/cover 及 combat 既有全部能力约束仍预检拒绝。新增 `ClassicBattleGrowthData` 仅接收显式完整100项 level-indexed WORD 阈值和本 role 全部 `BattleLevelUpMagic(Level, MagicId)` 行；没有 native codec、人工 profile ID 特判或默认补齐数据；纯数值 JSON 读取现见[现代战斗数据 v1](../../design/modern-battle-data.md)。成长表只在纯预检使用，构造后不保留外部引用；角色数据实际深拷贝。原 frozen v1/profile/envelope 不变。

无成长闭包在创建可变角色/抽 RNG 前证明：role level 与七 hidden level 均0..99，所有 XP/level/count 为 WORD，primary XP 严格小于 **role.Level** 对应阈值，七 hidden XP 各严格小于自身 level 对应阈值；阈值完整且正 WORD。七项 hidden 名称按 Ordinal 精确匹配，不因调用方字典使用大小写不敏感比较器而接受别名；规范键配合该比较器仍合法。不是 `reward==0` 就跳过规则：旧 XP 已达阈值、需要 level clamp、任意可新增法术等均拒绝。逐行检查当前等级可学法术，已知/零 ID/未来等级不新增；32槽全满时按 `PAL_AddMagic` 不会新增。非零奖励仍由 combat 预检拒绝。这是合法数据下没有永久成长的封闭子集，不是 #50 通用成长实现。

参考 `PAL_StartBattle` 开场循环（battle.c 1568-1583）将 HP0 参战队员抬至1 并同时清其 puppet 状态（kStatusPuppet，statuses[4]）；任何 WORD 拼写的死入场 puppet 都可被接受，但只在真实开战构造内清零，预检/验证不产生该副作用。活成员携带 puppet（未开放的战中傀儡行动域）及死入场的其它非良好状态仍拒绝；七 hidden count 归零而 primary count 保留；不放松底层 combat HP1..32767。#139 勘误：旧草案 T13 “死亡角色不被自动补成 1HP”与固定参考不符，已按参考执行入场恢复；战中死亡仍按正确身份/目标处理，不在每帧或每个动作中补 1HP，仅 Won 半恢复/下一场入场规范化触碰它。每个真实动作后将真实 attack/health counts 写回角色，包括最后 **Lost**；其他五项仍为开场0。Won 在零结束钩子后按 `(max-current)/2` 截断恢复 HP/MP；Lost 不恢复，保留 HP0。清理真实 owned role status（参考 `<=999` 条件），并释放 active combat owner；本子集从不接收/产生 inventory-in-use、毒或 extra-equipment effect，不声称验证这些非零系统的清理，也不删除 hand-slot/dual。

独立 `ClassicBattleLifecycleSession.CompletionReceipt` 仅在真实 KO、适用结算及清理之后发布，与 raw combat receipt 类型不同，无公开构造或任意结果入口。终态推进返回 false，选令拒绝，快照和 receipt 身份不变；`OwnsCompletion` 只对自身非空 receipt 的对象身份返回 true（两个 null 不匹配）。它不消费 receipt；后续事件请求仍须绑定准确 active lifecycle session，并自行实现一次性继续，不能接受另一 session、枚举或 raw KO。下述 trigger binding 复用此入口，不以 raw combat KO 绕过结算。

下一场显式传入上场 `Current.Role`，正常承接 HP/MP/XP/法术与动作计数，然后按开场规则 reset；不静默重置 HP。Lost 的0HP只在下一场开场复活，不在战后伪恢复。公开 Core 测试包含：缺HP50/49→125、缺MP30/29→35；Lost HP0/MP20 不恢复；其后开场HP1、胜后HP75/MP35；双回合 rawHP180→结算HP190、attack2/health5，XP值与level不变；预检、深隔离、终态与跨会话身份。它们不是事件/Main/引擎接线证据。

新增只读参考位置：battle.c:991–1390（主/hidden阈值、逐行学法术、半恢复），1531–1603（开场复活/reset），1780–1860（结果/清理/退出）；global.c:2084–2135（已知/空槽法术规则），2311–2348（状态清理）；palcommon.h:66/82（32槽/99级）。毒清除仅 party（global.c:1567–1636）、装备 extra slot 非 hand slot，均保留后续能力边界。

## Trigger 事件binding（#46 有界检查点）

正常显式入口：`TriggerScriptSession(events, language, TriggerBattleContent, IBattleRandom, inheritedColor = ...)`。既有 `(events, language, inheritedColor = ...)` 保持原有 M2/M3 行为及 enum 数值 Ready=0 / WaitingForDisplay=1 / Completed=2，无战斗配置时依然拒绝具名 `startBattle`，raw7 从不执行。WaitingForBattle 追加为3；两种等待均阻塞 `BlocksSceneUpdates`，且 `Start` 拒绝重入，不改变原调用。

`TriggerBattleContent(role, player, growth, teamId→enemy)` 是纯 typed 正式内容，不是测试 profile、工厂或注入结果的 delegate；后续有界检查点已补充[严格现代数值 JSON 读取](../../design/modern-battle-data.md)，已有独立 typed numeric native schema，已有显式数值准备入口，尚无完整 storypackage schema/准备链。role 提供初始持久状态，player 提供无装备效果的有效面板，growth 是该 role 的完整阈值/法术表，每个 team 映射本支持域的一个真实 enemy。初始 role/player 的 roleId、HP、maxHP 必须一致；所有之后仍要使用的嵌套 role、player、enemy、growth 集合均防御拷贝，不保留外部可变 alias。公开 `BattleRole` 是只读、不可变快照，不是可写全局状态库。

同一 trigger session 持有唯一持久角色快照；每场以其当前 HP 覆盖初始 player template HP，正常构造准确且唯一的 `ClassicBattleLifecycleSession`。会话不把 rawHP 当 settledHP，也不在请求创建/真实KO时提前应用角色；**只有合法 ack** 将 lifecycle 的 settled `Current.Role` 一次应用。多次开战和后续 `Start` 承接 HP/MP/XP/法术/counts；开场复活/reset 只发生在下一场 lifecycle 内。独立新 session 从内容初始状态开始。为了保证任意多战 Won 半恢复后都仍在底层 combat HP1..32767 范围，此 VM 的保守支持域额外要求 **maxHP<=32767**；不更改 lifecycle/combat 原有 maxHP WORD 合同。

`PendingBattle` 保存唯一 Guid、原 Caller、调用 Position、具名 teamId/IsBoss、原 NextAddress、LossAddress/FleeAddress 及准确 `Lifecycle`。等待时 `CurrentPosition` 留在开战指令。外层通过该生命周期的 `SelectAttack` / `ResolveNextAction` 进行真实战斗，结算/释放后才能得到其独立 Completion。`AcknowledgeBattle(requestId, completion)` 同时匹配当前 WaitingForBattle、准确 Guid 和该 active lifecycle 自己非空 completion 的对象身份；null、未完成、错 Guid、跨 lifecycle/脚本、旧场/重复或 display 回执都 inert，不改 PC/caller/context/持久角色、不重结算、不抽随机。

合法 ack 按参考 `script.c:3316–3335` 的 Won→next、Lost→非空 lossTarget 否则 next 续接，持续执行至真实 display/battle/end(keep)/零地址边界，而非每 tick 一 opcode；原保存的 `ReturnEntry` 不因开战/结果分支改写。表现结束后何时 ack 是外层未来唯一推进者的责任，本接口没有 clock、动画或第二套循环。当前没有 autoBattle 可变 owner，不添加假标志宣称清除该尚未建模状态；引入实际 owner 时须补齐参考清除副作用。

`Start` 在任何 caller/显示/角色状态副作用前，用 visited 工作表验证全部可能的 next/loss/flee 分支（跨文件、循环及真停止边界），复用 internal 纯 lifecycle/combat validation 和原语言/control preview。它不建战斗实例、不模拟胜利、不抽生产 RNG；每个可达 team/敌方数据、role/growth 和语言范围/control 都必须合法。即使当前无逃跑输入，也不得隐藏 flee 分支的不支持命令。IsBoss/FleeAddress 保留，但当前 lifecycle 仅产生真实 Won/Lost，**不宣称已经实现 Fled/逃跑 UI**；该能力仍属 #51。

公开 Core binding 用例经真实KO验证：Won rawHP100→settled125/MP35，第二场承接125→137/MP42；Lost HP0/MP20 保留，下一场才抬HP1/reset；结果保持原 Caller/ReturnEntry并继续至完整停边界。另核对请求身份隔离、display交叉、跨文件 loss/循环、所有编码分支预检、规范 hidden 键、支持域及深隔离。这只是 Core 证据，不签 native/Main/表现、GPU、人工或 CI。

## 公开纯入口预检（#46 native binding 前置检查点）

`TriggerEntryRequirements TriggerScriptSession.ValidateEntry(TriggerEventCollection events, DialogueLanguagePackage language, TriggerBattleContent battleContent, string fileId, string entryId, int inheritedColor = DialogueTextTokenizer.DefaultColor)` 为外层严格加载提供纯 capability seam。三个依赖不能为 null，颜色须为0..255，入口必须存在且 kind 为 trigger；禁用零入口沿既有语义不执行任何命令。它只针对 `battleContent.InitialRole` 校验选中入口的完整 next/loss/flee 保守闭包，复用与 `Start` 相同的私有静态 walker、消息范围读取及独立 display/control preview。遇不支持数据或命令抛出带原位置的 `InvalidDataException`，不创建 trigger session、战斗或 lifecycle，不接收/消耗 RNG，不调用 `Start` 或模拟胜利，也不改变任何现有会话、请求或初始角色。 返回的深不可变requirements包含选中闭包的真实DisplayBlocks/MessageReferences、非零PortraitIds和BattleTeamIds；native实际消费者见[原生story内容绑定](native-story-content.md)。

`Start` 的调用/副作用时机保持不变：仍以该会话当前 settled `BattleRole` 和 `Context.Color` 在 caller/PC/request 改变前调用同一 walker；后续 Start 不因静态预检而重置为初始角色。旧无 battle 配置的路径仍在原边界拒绝 `startBattle`。数据格式合法与选中入口能力不混淆：仅沿该闭包验证，归档的非零能力/未引用 team 不因此变成格式错误；本入口不验证来源认证、native hash、scene/owner identity、字形/画像或战斗视觉资源。完整 static native story profile、production package preparation 和正常 Main/输入/显示仍未交付。

## 现代数值读取（#46 有界检查点）

`ModernBattleContent.Parse(string)` 仅严格读取纯数值并返回上述同一 `TriggerBattleContent`；字段和数组演进边界见[现代战斗数据 v1](../../design/modern-battle-data.md)。复用既有 role 转换及 combat/growth 类型，不增第二套规则。格式合法不等于选中入口可执行：合法非零能力字段、含0阈值和有序重复法术行均保留，再由正式 `Start` 纯预检拒绝不支持闭包，不消耗RNG。`InitialRole/Player/Growth/Teams` 为public只读投影，全部嵌套集合沿现构造器防御拷贝。公开 Core 字符串用例已真实经Won/Lost与原事件续接；独立 typed native codec 已追加；不是已交付的完整人工资源链，数值 Prepare/Editor/PS 已交付，完整 storypackage/CLI 作者链/Main 仍待后续。

## 固定参考与独立算例

仅只读核对 `reference/sdlpal`，不编译运行：

| 规则 | 固定参考位置 |
|---|---|
| 濒死 `HP < min(100,maxHP/5)` | fight.c:29–48 |
| 三段物理基础伤害及 +0.5 截断；抗性为0时不除 | fight.c:131–172、253–285 |
| 敌身法 `(level+6)*3 + signed dex`；经典玩家 cap999 | fight.c:289–333、336–389 |
| 整轮选令后敌先入队、敌/玩家依次 float0.9..1.1、濒死玩家再减半、整数队列、signed严格降序比较（同速敌先） | fight.c:1384–1586 |
| 单目标普攻：敌防御加 `(level+6)*4`，int1..2，int0..5==0 三倍，role0 再 int0..11==0 双倍，float1..1.125，最小1；attack++及health int2..3 | fight.c:3624–3670、3756–3757 |
| 敌目标选择 int0..maxPartyIndex；无魔法不请求魔法概率 | fight.c:4521–4547、4576–4581、4656–4659 |
| 敌攻加 `(level+6)*6` 后截零，int0..16>=10闪避；命中时 strength int0..2，基础伤害/2，再 int0..1；截至HP再最小1 | fight.c:4913–4938、5052–5074 |
| 命中后即使等效道具概率0，仍请求 int1..10；短路不请求毒抗 int1..100 | fight.c:5139–5146 |
| 入场规范化：HP0→1 并清 kStatusPuppet（仅死入场队员），全队七 hidden count 归零、primary count 保留；战后恢复另算 | battle.c:1568–1583 |
| KO、零奖励累计；战内动作计数不等于成长 | fight.c:744–776；battle.c:1228–1262 |
| 整数/float包含端点、退化区间不抽熵 | util.c:222–278 |
| 玩家有效攻防身法来自原属性加装备效果（本检查点装备全零） | global.c:1736–1765、1800–1867 |

Unit 的 expected 为下述独立输入/参考手算字面值，不调用生产伤害/排序 helper：

- 敌 level0/defense0 → 有效防24；玩家 attack40 → `80−38.4+0.5` 截短为42；加1、无暴击、倍率1 → **43**。敌 HP43 当场 Won。level0 基础敌速度是 **18**，不是21；玩家速度100先手。
- 同一基础42，加2、三倍暴击、float1.125 → **148**；role0 加1、无三倍但双倍、float1.125 → **96**；同时三倍/双倍、float1 → **258**。
- 玩家 attack20 对防24 → 基础6，抗性2后3、加1 → **4**；attack14 不超过24×0.6 → 基础0、加2 → **2**。
- 敌 level0/attack0 → 强度36；玩家防20 → 基础40，除2加1 → **21**；玩家剩20则截至20形成真实 Lost。足够负的敌攻 offset 先截零，仍最小伤害1。
- 专门排序用例显式 **level1** → 敌基础速度21；玩家24×0.9=21.6 截为21，同速敌先；enemy offset979 →1000，玩家2000先cap999，敌先。HP39/max200才减半，HP40不减半；HP99/max1000减半，HP100不减半。
- 双回合：level0敌18×1.1与玩家22×0.9都截19，首回合敌先造成20、玩家造成43；下回合重新随机，玩家先暴击132获胜，保留attack2/health5，不固定双方交替。

## 内容衔接（已批准方向，尚未交付）

后续最小独立人工 content/native profile 应明确人工来源，绑定静态场景/profile、完整 faithful events、typed battle definitions、语言/图像文件与 hash；正常 Main 共用加载、真实搜索/选攻击目标输入与本 engine。CLI 必须添加真实具名开战与必要分支的双向语义编码（保留 source/next/保留位），而不是执行 raw 或测试直装。不能只复制 owner60 NPC 切片并换常量。

#45 `first-mandatory-battle-pre-trigger-v1` 仍冻结 scene2/map12/owner28/@09C3、team18/enemy97×2、六 role 装备及来源；九项 closure 的 `status` 只能统一为 `contract-only` 或重绑真实现代资源后的 `prepared`，且真实搜索 gate 只认 `FirstMandatoryBattleClosure.Verify` 的能力预检回执——回执缺失时依旧在副作用前拒绝，声明状态本身不开门。人工数据不能塞进真实 v1、伪称 original-base 或用假 battle-data 开放真实入口；#53 将真实上下文适配到同一个 engine，不引入第二战斗循环。此处不预造全局总线、状态库或 profile 注册框架。
