# ClassicBattleSession：首个 Core 普攻检查点

状态：#46 的 **Core 内部检查点**。单人单敌经典普攻、选令后随机排序、逐动作推进和真实 Won/Lost、下述零成长正式生命周期及 trigger 事件 binding 已实现；不是整张 #46 或 M4 的交付。Main/真实输入、CLI/Prepare/native 人工内容与表现尚未接通。

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

既有 `BattleRoleSnapshot` 提供 MP/maxMP、primary/七 hidden EXP、32槽已学法术、六装备与九状态；私有可变角色复用 `BattleRoleState`，外部只得到深拷贝、只读集合的快照。角色和 compact player 的 role/HP/maxHP 必须一致，零装备/status/cover 及 combat 既有全部能力约束仍预检拒绝。新增 `ClassicBattleGrowthData` 仅接收显式完整100项 level-indexed WORD 阈值和本 role 全部 `BattleLevelUpMagic(Level, MagicId)` 行；没有 native codec、人工 profile ID 特判或默认补齐数据；纯数值 JSON 读取现见[现代战斗数据 v1](modern-battle-data.md)。成长表只在纯预检使用，构造后不保留外部引用；角色数据实际深拷贝。原 frozen v1/profile/envelope 不变。

无成长闭包在创建可变角色/抽 RNG 前证明：role level 与七 hidden level 均0..99，所有 XP/level/count 为 WORD，primary XP 严格小于 **role.Level** 对应阈值，七 hidden XP 各严格小于自身 level 对应阈值；阈值完整且正 WORD。七项 hidden 名称按 Ordinal 精确匹配，不因调用方字典使用大小写不敏感比较器而接受别名；规范键配合该比较器仍合法。不是 `reward==0` 就跳过规则：旧 XP 已达阈值、需要 level clamp、任意可新增法术等均拒绝。逐行检查当前等级可学法术，已知/零 ID/未来等级不新增；32槽全满时按 `PAL_AddMagic` 不会新增。非零奖励仍由 combat 预检拒绝。这是合法数据下没有永久成长的封闭子集，不是 #50 通用成长实现。

参考 `PAL_StartBattle` 开场将 HP0 抬至1（零 status 所以 puppet 已为零），七 hidden count 归零而 primary count 保留；不放松底层 combat HP1..32767。每个真实动作后将真实 attack/health counts 写回角色，包括最后 **Lost**；其他五项仍为开场0。Won 在零结束钩子后按 `(max-current)/2` 截断恢复 HP/MP；Lost 不恢复，保留 HP0。清理真实 owned role status（参考 `<=999` 条件），并释放 active combat owner；本子集从不接收/产生 inventory-in-use、毒或 extra-equipment effect，不声称验证这些非零系统的清理，也不删除 hand-slot/dual。

独立 `ClassicBattleLifecycleSession.CompletionReceipt` 仅在真实 KO、适用结算及清理之后发布，与 raw combat receipt 类型不同，无公开构造或任意结果入口。终态推进返回 false，选令拒绝，快照和 receipt 身份不变；`OwnsCompletion` 只对自身非空 receipt 的对象身份返回 true（两个 null 不匹配）。它不消费 receipt；后续事件请求仍须绑定准确 active lifecycle session，并自行实现一次性继续，不能接受另一 session、枚举或 raw KO。下述 trigger binding 复用此入口，不以 raw combat KO 绕过结算。

下一场显式传入上场 `Current.Role`，正常承接 HP/MP/XP/法术与动作计数，然后按开场规则 reset；不静默重置 HP。Lost 的0HP只在下一场开场复活，不在战后伪恢复。公开 Core 测试包含：缺HP50/49→125、缺MP30/29→35；Lost HP0/MP20 不恢复；其后开场HP1、胜后HP75/MP35；双回合 rawHP180→结算HP190、attack2/health5，XP值与level不变；预检、深隔离、终态与跨会话身份。它们不是事件/Main/引擎接线证据。

新增只读参考位置：battle.c:991–1390（主/hidden阈值、逐行学法术、半恢复），1531–1603（开场复活/reset），1780–1860（结果/清理/退出）；global.c:2084–2135（已知/空槽法术规则），2311–2348（状态清理）；palcommon.h:66/82（32槽/99级）。毒清除仅 party（global.c:1567–1636）、装备 extra slot 非 hand slot，均保留后续能力边界。

## Trigger 事件binding（#46 有界检查点）

正常显式入口：`TriggerScriptSession(events, language, TriggerBattleContent, IBattleRandom, inheritedColor = ...)`。既有 `(events, language, inheritedColor = ...)` 保持原有 M2/M3 行为及 enum 数值 Ready=0 / WaitingForDisplay=1 / Completed=2，无战斗配置时依然拒绝具名 `startBattle`，raw7 从不执行。WaitingForBattle 追加为3；两种等待均阻塞 `BlocksSceneUpdates`，且 `Start` 拒绝重入，不改变原调用。

`TriggerBattleContent(role, player, growth, teamId→enemy)` 是纯 typed 正式内容，不是测试 profile、工厂或注入结果的 delegate；后续有界检查点已补充[严格现代数值 JSON 读取](modern-battle-data.md)，尚无 native/package schema。role 提供初始持久状态，player 提供无装备效果的有效面板，growth 是该 role 的完整阈值/法术表，每个 team 映射本支持域的一个真实 enemy。初始 role/player 的 roleId、HP、maxHP 必须一致；所有之后仍要使用的嵌套 role、player、enemy、growth 集合均防御拷贝，不保留外部可变 alias。公开 `BattleRole` 是只读、不可变快照，不是可写全局状态库。

同一 trigger session 持有唯一持久角色快照；每场以其当前 HP 覆盖初始 player template HP，正常构造准确且唯一的 `ClassicBattleLifecycleSession`。会话不把 rawHP 当 settledHP，也不在请求创建/真实KO时提前应用角色；**只有合法 ack** 将 lifecycle 的 settled `Current.Role` 一次应用。多次开战和后续 `Start` 承接 HP/MP/XP/法术/counts；开场复活/reset 只发生在下一场 lifecycle 内。独立新 session 从内容初始状态开始。为了保证任意多战 Won 半恢复后都仍在底层 combat HP1..32767 范围，此 VM 的保守支持域额外要求 **maxHP<=32767**；不更改 lifecycle/combat 原有 maxHP WORD 合同。

`PendingBattle` 保存唯一 Guid、原 Caller、调用 Position、具名 teamId/IsBoss、原 NextAddress、LossAddress/FleeAddress 及准确 `Lifecycle`。等待时 `CurrentPosition` 留在开战指令。外层通过该生命周期的 `SelectAttack` / `ResolveNextAction` 进行真实战斗，结算/释放后才能得到其独立 Completion。`AcknowledgeBattle(requestId, completion)` 同时匹配当前 WaitingForBattle、准确 Guid 和该 active lifecycle 自己非空 completion 的对象身份；null、未完成、错 Guid、跨 lifecycle/脚本、旧场/重复或 display 回执都 inert，不改 PC/caller/context/持久角色、不重结算、不抽随机。

合法 ack 按参考 `script.c:3316–3335` 的 Won→next、Lost→非空 lossTarget 否则 next 续接，持续执行至真实 display/battle/end(keep)/零地址边界，而非每 tick 一 opcode；原保存的 `ReturnEntry` 不因开战/结果分支改写。表现结束后何时 ack 是外层未来唯一推进者的责任，本接口没有 clock、动画或第二套循环。当前没有 autoBattle 可变 owner，不添加假标志宣称清除该尚未建模状态；引入实际 owner 时须补齐参考清除副作用。

`Start` 在任何 caller/显示/角色状态副作用前，用 visited 工作表验证全部可能的 next/loss/flee 分支（跨文件、循环及真停止边界），复用 internal 纯 lifecycle/combat validation 和原语言/control preview。它不建战斗实例、不模拟胜利、不抽生产 RNG；每个可达 team/敌方数据、role/growth 和语言范围/control 都必须合法。即使当前无逃跑输入，也不得隐藏 flee 分支的不支持命令。IsBoss/FleeAddress 保留，但当前 lifecycle 仅产生真实 Won/Lost，**不宣称已经实现 Fled/逃跑 UI**；该能力仍属 #51。

公开 Core binding 用例经真实KO验证：Won rawHP100→settled125/MP35，第二场承接125→137/MP42；Lost HP0/MP20 保留，下一场才抬HP1/reset；结果保持原 Caller/ReturnEntry并继续至完整停边界。另核对请求身份隔离、display交叉、跨文件 loss/循环、所有编码分支预检、规范 hidden 键、支持域及深隔离。这只是 Core 证据，不签 native/Main/表现、GPU、人工或 CI。

## 现代数值读取（#46 有界检查点）

`ModernBattleContent.Parse(string)` 仅严格读取纯数值并返回上述同一 `TriggerBattleContent`；字段和数组演进边界见[现代战斗数据 v1](modern-battle-data.md)。复用既有 role 转换及 combat/growth 类型，不增第二套规则。格式合法不等于选中入口可执行：合法非零能力字段、含0阈值和有序重复法术行均保留，再由正式 `Start` 纯预检拒绝不支持闭包，不消耗RNG。`InitialRole/Player/Growth/Teams` 为public只读投影，全部嵌套集合沿现构造器防御拷贝。公开 Core 字符串用例已真实经Won/Lost与原事件续接；不是已交付的人工资源链，native/Editor/CLI/Main仍待后续。

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

#45 `first-mandatory-battle-pre-trigger-v1` 仍冻结 scene2/map12/owner28/@09C3、team18/enemy97×2、六 role 装备及来源，九项 closure 均 contract-only，真实搜索 gate 保持拒绝。人工数据不能塞进真实 v1、伪称 original-base 或用假 battle-data 开放真实入口；#53 将真实上下文适配到同一个 engine，不引入第二战斗循环。此处不预造全局总线、状态库或 profile 注册框架。
