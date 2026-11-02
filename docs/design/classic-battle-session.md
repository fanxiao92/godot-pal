# ClassicBattleSession：首个 Core 普攻检查点

状态：#46 的 **combat-only 内部检查点**。单人单敌经典普攻、选令后随机排序、逐动作推进和真实 Won/Lost 已实现；不是整张 #46 或 M4 的交付。事件往返、Main/真实输入、CLI/Prepare/native 人工内容、表现、战后恢复/清理尚未接通。

## Interface 与数据

`ClassicBattleSession(ClassicBattleDefinition, IBattleRandom)` 正常构造后处于 Selecting；`SelectAttack(enemyId)` 提交本轮攻击及唯一目标，之后才消耗排序随机数；`ResolveNextAction()` 每次推进一个完整业务动作，双方存活且本轮两个动作完成后返回 Selecting，再次选令重新排序。没有业务 deadline，因此不接收无效时参，不读取墙钟。未来 Main 必须成为唯一推进所有者，在动作表现真实完成处推进；这不是一帧一条事件命令的调度器。

`Current` 是不可变 view，公开 phase、round、双方 HP、最近动作与战内 attack/health 动作计数。Damage 保留参考规则的命中伤害（玩家对敌可过量，敌对玩家先截至剩余 HP）；Evaded 与 Critical 独立表示。输入定义/外部角色不原地写入，旧 view 不随更新改变。战内计数按 WORD 增加并保留，不因奖励为零省略；不是永久属性或经验升级。其他动作尚不可选。

只有真实 KO 才生成 `CompletionReceipt`，没有公共构造器或接受任意 outcome 的完成接口；终态保持同一个 receipt，不能重开或重复结算。后续事件请求须持有其唯一 active session，并比较该 session 的非空 `Completion` 的**对象身份**，再读 Outcome；不能仅根据相同结果、枚举、调用者或地址接续，也不能接受另一 session 的 receipt。本检查点尚未实现请求/事件续接。

`IBattleRandom` 是正常业务依赖，不是测试开关。整数和 float 区间均含端点，退化区间必须不消耗底层熵；Core 仍如参考请求敌目标 `0..0`。float 使用单精度。此处规定规则所请求的区间及顺序，不提供宿主随机适配器或承诺与原程序按 seed 重放。外层以后提供生产随机源；Unit 只在这个外部 seam 提供逐项可核的随机输入，不复制战斗引擎。

新紧凑数值 DTO 必要：#45 的 `BattleRoleSnapshot` 保存角色/装备/经验上下文，但没有战斗 attack/defense/dexterity，行走 profile 也不是战斗数值来源。玩家数值在本支持范围内就是零装备效果的有效值；敌 attack/defense/dexterity 是**有符号原表 offset**，level 加成由 engine 计算，不把 offset 当有效面板。新类型无既有序列化调用方，不修改旧 snapshot、codec 或真实 v1。

构造器在创建可变战斗状态、消耗随机数之前预检：

- 恰好一个存活玩家、一个存活敌人；玩家 role0..5，敌 ID 非零 WORD。
- 当前 HP 为 1..32767（本有界能力不支持更高当前 HP）；玩家 maxHP 为正 WORD 且不低于当前 HP，攻防身法为 WORD；敌 level/physical resistance 为 WORD，三个 offset 为 signed WORD。
- 装备/status 可以为空或分别为六/九项全零；poison 必须全零。非零装备、状态（包括双击）、毒、角色/敌方钩子、cover、attack-all、敌魔法/双动/等效道具及其概率、经验/现金均明确拒绝，不静默忽略。
- 非 Selecting 不能提交选令；错误 target 在状态变化和 RNG 前拒绝。非 Resolving 的推进返回 false。

这是纯战斗数据，不接收存档/成长表，不宣称零 Exp 就完成了全部战后规则。#50 将复用这些真实计数；后续 #46 事件接通前仍须提供合法初始经验、无待学法术的人工数据，以及适用的基础恢复/清理，不能用人工 profile ID 特判跳过。

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
