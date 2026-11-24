# #193：逐敌位战斗视觉的消费链与完整交付预算

## 结论 / 本票边界

这是 #136 的必要 **SOURCE 前置**，不是 #136 实现、验收或故事完成。输入冻结为 `execution/m6-loop@a13548fc197b26227069ae6870b63312f2d06ad2`，开始时 clean/index 空。实时 #126/#136 正文、评论和原生依赖已核对：#136 原本无 blocker；#193 随后作为原生子票及 blocker 挂入，自身无 blocker。#192 明确不设计逐敌位数据，本报告不重做其逐角色研究。

- **已经实现**：逐槽 TargetId、SHORT bottom-center 位置、与选定敌队顺序一一匹配；重复现代敌 ID 不合并。不能再把这些基础当新交付。
- **尚未实现**：逐槽精灵/帧元数据、独立 WORD 待机状态、按实际行动槽和目标槽取数据、按槽寻址纹理。现有 PartyEnemies 还保留 HP 却没有死亡隐藏条件。这些是 SOURCE 可见缺口，不是本轮运行复现或根因证明。
- 下述完整候选路线 **A10 / B7 / C10，累计21个去重路径**，包括两种 Main 来源、正式 Prepare 进程测试、测试 fixture、双登记及必要文档；不是旧7–10路径估计可装下的整票。须确认新旧形态及标量投影策略后拆成实施叶，不能靠多个 commit 或漏测试压缩预算。
- 可复用当前已集成 full-profile sprite/dependency 校验，**没有证据证明 #136 必须依赖 #164 的 standalone checked overload**。不能因名称相似把 #164 变成新 blocker，也不能将其停放候选/477未跑GREEN视为已交付。

本轮只新增本文；不改 API、schema、资源、生产实现、测试、登记或 UID。所有 SDK/Unit/Game/Godot/Prepare/import/solution、真实内容、GPU/桌面/远端CI均 **NOT RUN**。外部本轮 `acceptance.md`、`source-inputs-final.json`、`source-final/binding.json` 保存实际检查/源身份；只读 scout 原回复另存，不替代外层票级 Standards/Spec。

## 固定参考与独立规则

| 只读参考 | 原字节 SHA256 |
| --- | --- |
| `reference/sdlpal/battle.c` | `45f38a01d08b864151390c99bcbb4622e3ba78a5da80a3657c276bf1c2fea33f` |
| `reference/sdlpal/fight.c` | `6d71434242de081d3cd2d5a0578d05485948e7c6cd507407805d0eafa3e761f5` |
| `reference/sdlpal/global.h` | `07f307f91af62291fba50a345075c54adf170a33d1eba1f389852abf9c44d28c` |

1. **身份**：`reference/sdlpal/global.h:198` 的 OBJECT_ENEMY.wEnemyID 同时指 DATA.MKF #1 敌记录和 ABC bitmap；这两个参考索引确实共值，不等于战斗对象ID。`reference/sdlpal/battle.c:1599` 从队伍对象ID取该敌记录，按战斗槽复制 ENEMY；`:913` 逐槽经对象取 ABC chunk、分配该槽 lpSprite。现代 `src/Pal.Core/ClassicBattleModels.cs:27` 的 ClassicBattleEnemy.Id 没有对象/敌记录/图号来源字段，不能把它直接当零售对象ID或SpriteId；`:50` 的 ActorIndex/TargetIndex 才是动作槽地址。重复Id也必须保留两个战内实例。
2. **位置/尺寸**：`reference/sdlpal/battle.c:935` 位置来自 EnemyPos 的槽/实际阵容列，Y加本槽 wYPosOffset；`:124` 绘制取本槽**当前帧**宽高，左上角为 bottomCenter−(width/2,height)。当前 author EnemyPosition 已是施加offset后的bottom-center，不应运行时再加一次。`reference/sdlpal/fight.c:2042` 玩家普攻也取目标槽当前帧高度，不能用整队/第一帧高度。
3. **独立待机**：`reference/sdlpal/fight.c:989` 对每个有效槽更新，睡眠/麻痹置帧0并跳过减计数；否则 WORD 预减速度计数，等于0才进一帧并从本敌记录重装初始速度，超出本槽IdleFrames归0。`:493` 的 Delay 仅 fUpdateGesture=TRUE 才推进有效且未睡眠/麻痹的各槽；FALSE动作不能顺带推进旁观槽。速度0会下溢65535，不能夹成1。这里记录参考状态分支，不借本票新增完整状态系统。
4. **物理动作也用MagicFrames**：`reference/sdlpal/fight.c:4987` 以本行动槽的 Idle+i 播MagicFrames，每帧2个FALSE节拍；再走3−MagicFrames次(−2,−1)及额外1个FALSE节拍。`:5029` AttackFrames=0取Idle−1并等待2；非零时i=0..AttackFrames（含端点），帧Idle+Magic+i−1，每帧等ActWaitFrames。零等待不补成1。声音留M7，不把“无敌方法术实现”解释成可丢掉MagicFrames。
5. **死亡不是通用死亡帧**：`reference/sdlpal/fight.c:739` 在战后动作检查中按(SHORT)Health<=0逐槽累计奖励并清ObjectID/释放该槽sprite，要求fade；`reference/sdlpal/battle.c:127` 只对ObjectID非零者实际blit。当前Core已提交HP不等于动画已经到死亡检查边界；不能一看live HP0就在命中反馈前隐藏。完整#136仍须证明死亡可见性/所需消失交接，不以本报告或HP数字代签，不暗删参考fade责任。

### 独立字面预期（SOURCE手算，未运行）

- 槽0(id7,sprite97,Idle3,Speed2)、槽1(id8,sprite98,Idle2,Speed3)，初始(frame,counter)为(0,2)/(0,3)。六次有效TRUE推进后依次为 **(0,1)/(0,2)、(1,2)/(0,1)、(1,1)/(1,3)、(2,2)/(1,2)、(2,1)/(1,1)、(0,2)/(0,3)**。FALSE期间只赋行动槽帧，不重置任一计数。两槽同id7/同sprite97时仍各自持有状态；其中一槽死亡不重置另一槽。
- Speed0初始counter0；第一次/第二次有效推进为65535/65534，frame0不进位。该下溢和上述计数不是机器调度时间断言。
- Idle2/Magic1/Attack2/Wait2：准备帧2等2，接近两步各1，再1；攻击帧 **[2,3,4]各等2**，命中边界为第11节拍。Idle3/Magic0/Attack0/Wait0则准备3+1，攻击帧2等2，命中边界6；不能把零Attack误作无动作。
- 敌位(100,120)，当前帧高30的玩家效果首帧bottom-center为(100,120)；目标另一槽当前帧高60则为(100,110)，不是沿用第一槽尺寸。宽20/高30、bottom-center(100,105)应绘制左上角(90,75)。这些是人工值，不是零售位置声明。

## 当前正式消费链及不能遗漏的调用方

### 资源与已检查视图

`src/Pal.Game/Scripts/Resources/PalBattleVisualEnemySlot.cs:10` 只有 TargetId/EnemyPosition；`src/Pal.Game/Scripts/Resources/PalBattleVisualTeam.cs:11` 持有 EnemySpriteUri 和五个帧/速度标量。无需为同一用途新增另一种sprite DTO或Resource类/UID；可扩现有slot，但具体字段/缺省策略尚未批准。

`src/Pal.Game/Scripts/Profiles/NativeBattleVisualProfile.cs:55` 已核slot非空元素、长度、Id顺序和位置；`:74` 保留legacy单敌pair，禁止多敌队坍缩为单pair。**有EnemySlots不等于声明了新逐槽精灵**：现有双敌旧资源已经使用只有身份/位置的EnemySlots，必须继续兼容。

该文件`:89` 依赖seeds仅每team的背景/单sprite；`:125` 只验证一份enemySprite的Idle+Magic+Attack所需帧。`:222` 的CheckedBattleVisualTeam仍持有标量EnemySprite/Idle等，`:248` 的CheckedBattleVisualEnemySlot只有Id/位置；readonly集合不等于Resource深不可变。新slot URI必须同时进现有ContentDependencies exact keyset及前后hash检查，逐槽帧/元数据也必须针对自己的sprite。复用私有LoadSprite与原NativeContentDependencies，不复制walker、不引入runtime MKF/JSON或新图像格式；声明/hash不是原始来源认证。

### 正式Prepare和两个宿主来源

| 路径 | 实际实现与缺口 | 必须保留的边界 |
| --- | --- | --- |
| Story author→native | `src/Pal.Game/Editor/PrepareStoryPackage.cs:255` 仍要求team级sprite/帧字段；`:271` 的enemies每行只容targetId/position。`:83` visualSeeds也只有一份team sprite | 新形态要同时进入严格键/重复字段验证、稳定leases/hash、staging/final两次Load（`:132`/`:140`），失败不发布假READY；不是只保存新字段 |
| Story正常Main | `src/Pal.Game/Scripts/Main.Story.cs:78` strict Load，选择reference/number能力后才创建map/random/session | 新声明不完整必须在副作用前拒绝；legacy加载合法不代表能绕过Main能力门 |
| 非Story首战 | `src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs:142` 已把完整enemyTeams传给同一Load（不同于#135缺完整party输入）；`:145` 却用team.EnemySprite.SpriteId与**单个**ImageEnemyId核首战闭包 | 不能把单图证明偷偷改成“仅验槽0”；新形态若不在冻结首战证明域内，必须明示拒绝/保持legacy-only，或另确认逐槽证明后再支持，不改真实首战来源声明 |
| 共用呈现 | `src/Pal.Game/Scripts/Main.cs:221` 把real.Visual交同一Story machinery | 两种来源都受同一view变化影响。合成首战应回归旧闭包/首个更新；不靠Story用例补签非Story或私有真实内容 |

`scripts/Prepare-StoryPackage.ps1` 只负责正式进程/参数包装，没有敌视觉业务，不必仅因同名而改它。直接Prepare测试在 `src/Pal.Game/Tests/PrepareStoryPackageTests.cs:21`，正式wrapper/editor代表在 `tests/Pal.Prepare.Tests/StoryPackagePrepareProcessTests.cs:30`；后者**计入**下文预算。`src/Pal.Game/Tests/SyntheticFirstBattleFixture.cs:436` 构造的是两槽7/8但**一图**的冻结首战人工闭包，不能被误认作异种精灵验收。

固定外部caller `C:/evidence/m6/loop/M6Loop.psm1`、`C:/evidence/m6/loop/Start-M6Loop.ps1` 对 EnemySlots/EnemySprite/NativeBattleVisual/PrepareBattle 的全文件搜索零命中；它们消费loop收据/状态而非此C# API，本票不改其公共契约。仓库和这两个脚本之外的未知C#调用方不在覆盖内，不能由零命中推断外部兼容。

### Main状态、实际绘制和释放

- `src/Pal.Game/Scripts/Main.Story.cs:44` 只有一个enemyIdle，`:148` 用整队metadata构造，启动两次Advance；`:166` outer、动作反馈/UpdateFighters都改此同一个状态。应复用 `src/Pal.Game/Scripts/Dialogue/StoryPlayerAttackPresentation.cs:29` 的现有WORD计数规则，为每槽拥有实例，而非新增第二时钟或让纹理Resource保存live计数。
- Main `:263` 已按ActorIndex取位置但仍传team帧参数；`:523` 的effectEnemyHeight取team单sprite及共享帧。必须分别由ActorIndex取动作metadata、TargetIndex取目标当前sprite/frame高度；TRUE段推进所有适用槽，FALSE只驱动行动槽。不能把所有Advance机械替换成“只推actor”或“永远推全队”。
- `src/Pal.Game/Scripts/Dialogue/PalStoryBattleView.cs:26` 纹理key为(bool Player,Frame,Shift)，`:67` 只装一份enemySprite。EnemyDraws在`:198` 留各槽HP/位置，却都取同idleFrame；`:222` foreach实际Sprite调用没有slot参数。新snapshot仅增加观察字段、但绘制仍用单sprite，不能算交付。
- view的BuildDrawSnapshot `:120` 是否采用party绘制由**玩家人数>1**决定，不是敌人数。`src/Pal.Core/TriggerBattleContent.cs:44` / `:60` 独立允许1–2角色与1–2敌，`src/Pal.Core/ClassicBattleSession.cs:483` 也没有要求两边人数相等。因此**单玩家双敌是当前Core已接受组合**，不是待选的新人数扩展；renderer不能因单玩家而只画第0敌。后续必须覆盖该组合，不能新增拒绝来掩盖现有绘制缺口。本文不放宽人数/空槽上限（五敌位另归#138）。
- 纹理key至少要有slot或等价的已核资源身份，同一帧编号的不同图不能碰撞；重复Id不等于共享动画状态。死亡可见性要随实际呈现阶段控制，当前foreach无HP/存在过滤只是静态证据。命中抖动/shift/数字仍定位TargetIndex，不能让另一个相同Id敌位同闪或同消失。
- Main `:144` 视图初始化异常会ReleaseTextures/Free；结束和ReleaseStory也释放view。view `:287` 的ReleaseTextures清理owned/frames/effects/numbers。新增每槽集合必须同一所有权收尾，部分初始化也能释放，不能清全局ResourceLoader/cache/UID掩盖污染。

## 已确定不变量与尚待确认的公共策略

| 主题 | 可行方案 / 取舍与推荐 | 当前权限 |
| --- | --- | --- |
| 新形态/版本/缺省 | A：v1给现有slot加成组可选视觉字段，所有槽全无新字段才继承team，任一槽声明则整队完整校验；少文件但要定义Resource哨兵/JSON字段存在性、空值、部分行、混写。B：v2新形态并保留v1 loader，分隔更明确但writer/兼容链增大。倾向A，不把已有EnemySlots本身当新形态开关 | **未批准**，不能由本文发布实现。选定后冻结确切字段、空/混写规则和诊断，legacy双敌身份位置数组继续有效 |
| checked标量投影 | A：统一成每槽checked数据，legacy标量仅legacy路径可访问，新形态明确拒绝标量投影；B：为纯同图同metadata证明后保留整队投影，额外等价判断成本。推荐A，防止非Story只校槽0 | **未批准**；FirstBattle调用者和测试不能漏掉。现有两个Load签名已有完整enemyTeams，不必为#136再造完整party API |
| native精灵身份/真实来源 | 人工声明区分现代TargetId和独立SpriteId；严格覆盖、帧及依赖hash可证明内部一致性。零售对象→记录/图号关系需已核现代输入来源根，不能只把两个数字写相等就宣称真实正确 | 推荐复用已准备数据及#191来源结论，不扩raw/重编码。具体可选expected字段与来源证明另待实施规格冻结 |
| #164 / 安全复用 | #136绑定的是整份visual已认证闭包，现有private sprite校验可复用；#164是新增standalone expected sprite完整对比契约，两者不是同一需求 | 不移植停放树，不宣称#164通过，不凭相似性增依赖。若实施时发现必须消费其新API，再核实际批准关系/身份，不自动扩权 |
| 死亡/已接受人数域 | 两槽独立死亡属于#136必验；fade/动作后隐藏的准确交接不能省略。单玩家双敌已由当前Core独立人数门接受，不能只用玩家人数决定敌数组绘制；何时移除可见sprite仍须对齐参考动作检查边界 | 不重新决策该人数域、不新增拒绝来规避。实现改变其他时间轴/状态/格式路径时先更新预算；本报告没有复现或修复这些运行行为 |

以上选择未获确认，不得把推荐误当已冻结API。没有新业务规则、敌方法术/召唤/变身、全状态系统、五槽/存档、资源自动准备或工具链升级授权。后续直接确认/实施，不再为同一缺口建同题SOURCE票空转。

## 完整候选影响集与语义拆分

下表为一条**建议路线**的21个去重路径，含所有已纳入A/B/C的必要测试，不是唯一最小方案或已批准的修改清单。本票实际仍只有本文1路径。复用已有Resource类，不新建cs/uid；若改方案新增类/uid/夹具/样本/时序文件，须追加预算而非假称已包含。

| # | 路径 | 路线职责 |
| --- | --- | --- |
| 1 | `src/Pal.Game/Scripts/Resources/PalBattleVisualEnemySlot.cs` | 成组可选slot视觉声明 |
| 2 | `src/Pal.Game/Scripts/Resources/PalBattleVisualTeam.cs` | 旧team继承/新旧声明边界及注释 |
| 3 | `src/Pal.Game/Scripts/Profiles/NativeBattleVisualProfile.cs` | 逐槽checked、hash/frame/capability、legacy |
| 4 | `src/Pal.Game/Tests/NativeBattleVisualProfileFixture.cs` | 两种人工图/元数据、外引/alias/hash控制 |
| 5 | `src/Pal.Game/Tests/NativeBattleVisualProfileTests.cs` | public Load/Saver/精确覆盖/拒绝/隔离 |
| 6 | `scripts/GameTestBatches.ps1` | 实际source/discovery/分区登记 |
| 7 | `tests/Pal.Game.Tests/GameBatches.Tests.ps1` | 独立登记预期 |
| 8 | `docs/design/native-battle-visuals.md` | 新旧native/checked/所有权契约 |
| 9 | `src/Pal.Game/Scripts/Profiles/NativeFirstMandatoryBattleProfile.cs` | 保留单ImageEnemyId证明；新形态明确拒绝或获准后全槽验证 |
| 10 | `src/Pal.Game/Tests/NativeFirstMandatoryBattleProfileTests.cs` | 非Story拒绝/旧首战兼容，不只验槽0 |
| 11 | `src/Pal.Game/Editor/PrepareStoryPackage.cs` | strict author、完整seeds/leases/hash/两次读回 |
| 12 | `src/Pal.Game/Tests/PrepareStoryPackageTests.cs` | 直接正式Prepare正/拒绝和未污染输入 |
| 13 | `tests/Pal.Prepare.Tests/StoryPackagePrepareProcessTests.cs` | 正式wrapper/editor新形态及诊断/退出，无假READY |
| 14 | `docs/design/story-package-preparation.md` | author新旧形态和输出契约 |
| 15 | `src/Pal.Game/Scripts/Main.Story.cs` | slot状态/ActorIndex/TargetIndex/效果尺寸/收尾 |
| 16 | `src/Pal.Game/Scripts/Dialogue/PalStoryBattleView.cs` | 实际纹理key/draw/slot状态/死亡可见性/释放 |
| 17 | `src/Pal.Game/Tests/StoryRoundtripSceneTests.cs` | 真Main真实输入/首完成/逐槽时序/legacy |
| 18 | `src/Pal.Game/Tests/AuthoredStoryFixture.cs` | 自有新形态异种/重复Idfixture经正式Prepare，不篡checked |
| 19 | `src/Pal.Game/Tests/SyntheticFirstBattleFixture.cs` | 正常非Story人工首战兼容身份/新拒绝控制 |
| 20 | `src/Pal.Game/Tests/SyntheticFirstBattleClosureTests.cs` | 共用renderer首战/cache/退出回归，不冒称真实资源 |
| 21 | `docs/design/main-story-roundtrip.md` | slot呈现/死亡/更新与未验层边界 |

- **A / native逐槽能力**：1–10，**10路径**。闭合现有typed Resource→public Load→逐槽checked、完整依赖/帧/覆盖/legacy测试；同步非Story消费边界，不能留标量误证漏洞。旧首战继续legacy，新首战形态是否支持由上表先选；不宣称Prepare或renderer已消费新能力。若超过10，先把非Story消费边界变成完整独立前置再重算，不遗漏调用者。
- **B / 正式准备能力**：6、7、8、11、12、13、14，**7路径**。依赖A冻结接口，经正式Prepare/staging/final读回并有wrapper/editor进程证据；不是正常Main呈现通过。
- **C / 正常Main呈现**：6、7、8、15、16、17、18、19、20、21，**10路径**。依赖A/B，独立idle/actor/target/纹理和死亡、首更新/失败释放、同URI与两来源兼容闭环。当前StoryEnemyIdleState/通用攻击timeline可复用，不计划修改其所在文件；若实际必须更改，则第11路径触发先拆而不是偷偷加。

**A∪B∪C=21**：不是10+7+10=27（共享6/7/8各算一次）。已列process1和Synthetic2不可在累计中排除。私有 `src/Pal.Game/Tests/RealFirstBattleFlowTests.cs` 现有案例可只读复用其原身份；本报告不承诺新HEAD通过。若新规格需要修改它/样本或新增GPU夹具，21基础上追加并拆票。A/B/C是完整能力切分建议，**未发布为ready实施叶，#136仍未完成**。

## 后续实际验收矩阵（不是本轮执行结果）

| 边界 / 独立错误反例 | 应验收结果与层 |
| --- | --- |
| 两种图使用相同frame编号、不同颜色/高宽/Idle/Speed；slot0/1重排；单玩家双敌 | public native/Prepare核各自图帧与hash；Main两敌都实际draw，图随槽而非玩家人数，ActorIndex取本动作metadata，TargetIndex取该目标当前高度/坐标，不串图/串位 |
| `[id7,id7]` 同图、两槽独立被击/行动/一槽死亡 | 重复Id合法；不按Id作为状态/纹理slot主键。命中shift/数字只落目标，死亡边界仅隐藏该槽；不能把已有HP列表断言当可见性证据 |
| 新形态漏槽、局部行缺字段、重复覆盖、错TargetId/额外slot、错SpriteId/缺帧 | 按已确认策略明确拒绝，不能回退整队图；重复TargetId本身不是重复slot错误；诊断定位team/slot/字段 |
| 改第二图字节、漏外引frame/Texture/import/opaque依赖、多hash、URI异常 | 复用精确graph/keyset/前后hash拒绝；不清cache、不自动import；hash仅完整性，不认证零售来源 |
| 本文两槽计数/Speed0、Attack0/Wait0/帧aliases、FALSE旁观槽 | 先确定性公开可观察规则/既有时间接口或纯presentation seam字面断言，再Game核真实更新连接；无Sleep/倍速/帧等待替代精确节拍 |
| 旧单敌pair、旧双敌身份位置数组、冻结非Story单图闭包 | 旧格式与原能力门不变；新形态不能借旧scalar检查只验第0槽。完整首战来源证明未扩展前明确拒绝不在域内的新形态 |
| 首个有效输入、部分初始化纹理失败、正常/活动中退出、重复Main同URI | Configure/订阅入树前；首个实际UpdateCompleted内无条件采样，不等待正确状态。finally释放自有输入/订阅/节点/纹理；资源字节/hash/初态不写脏 |
| GPU像素/私有真实/桌面/完整门 | 各自绑定实际候选、明确授权与原件，不能用SOURCE/native/headless互相代签；必要但缺少的证据继续保留未验，不以合成图或历史首战补签 |

选层沿用 `docs/testing/test-layers.md`、`docs/testing/game-test-layers.md`、`docs/testing/time-boundaries.md`。Profiles/Resources/Editor变更受 `scripts/GameTestBatches.ps1:318` 的native-map守卫；后续届时核完整未过滤native-map及当前runsettings，不复制旧marker/Skip绕过。计数按实际发现/运行同步，不能把当前source分区数473说成本轮执行通过数。

## 下一入口与保留限制

先由父控确认上表版本/缺省/混写、checked标量及非Story证明域策略，再按A/B/C预算发布实际实施叶；独立双轴接受本票只接受SOURCE结论，不自动批准这些接口。不要重做本报告、重复#192，也不把#164未验收实现移植过来当已有能力。#136、#126保持OPEN；#193待外层独立双轴→真实本地集成→current发布后才关闭。

本票不增加工程叶/故事：17工程叶、storyCredit0、fullM6=false保持。#129/#147阻塞，#164 SOURCE-only/477 GREEN未跑、#165原cold未解、#168 FAILED466/474、#179历史UNKNOWN/fixture失败及broadAST14均保留；allowRuntime不替代缺失的retryApprovalPath。#131现金声明/#133来源信任/#135公共party与装备能力仍待决定；#132宿主/活动战斗/setParty完整重放未闭合。不造自然save/隐式seed，不上传资产，不清cache/UID，不升级工具链，不写integration/canonical、不push/PR/main。
