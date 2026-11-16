# Native battle visual binding（#46）

## #136 / #204：checked 逐槽消费边界（中间实现叶）

`CheckedBattleVisualTeam` 只暴露 `TeamId`、`EnemySlots`、`Background`。每个
`CheckedBattleVisualEnemySlot` 持有 `TargetId`、bottom-center `EnemyPosition`、
`EnemySprite` 及 `IdleFrames/IdleAnimSpeed/MagicFrames/AttackFrames/ActWaitFrames`；
没有 team 级敌精灵、帧元数据或单槽身份/位置投影。ordinal 是实例地址，重复 TargetId 不合并。
标量是独立值快照；sprite 仍是本次加载的原生 Resource，允许共享但须只读消费，不保证深不可变。

Main 为每槽创建独立 idle/current-frame WORD 状态。TRUE gesture/UpdateFighters 推进所有槽，
FALSE 动作只赋行动槽的帧；行动 metadata 取 ActorIndex，玩家效果的尺寸取 TargetIndex 的
当前 sprite/frame。renderer 的 enemy texture key 包含 slot，并按该槽实际绘制；同一 slot 的
自有纹理沿原部分初始化、退出及结束路径释放，不清全局 cache。绘制快照保留逐槽 idle 帧/计数值，
不持有 live state。非 Story FirstBattle 逐槽核 `ImageEnemyId`，错误定位到具体槽，不能以槽0代全队。

**中间实现的明确限制**：native 逐槽 URI/帧 metadata 叶已迁移原生地址及元数据，但离线 author
尚为整队输入；Prepare 显式展开至 native 槽，checked 不再读取整队精灵或帧 metadata。此分步施工状态不是
最终兼容承诺，也不代表异种 author 输入已经可接受。后续 #136 叶仍须补齐逐槽 SpriteId/author 声明，迁移活跃
输入和正式进程测试并拒绝旧/混写形态，不能把这里的中间状态作为最终兼容承诺。独立命中/死亡
fade、异种 Prepare→native→Main、完整来源证明和失败释放仍须相应验收；下面 #46 历史 checkpoint
不覆盖 #126 `m6-no-legacy-compat-20261111` 的最终统一格式决定。当前叶的定点 headless 结果不替代
完整 native-map、独立 Prepare 进程组、全 Game、真实资源、GPU/桌面、远端 CI 或独立评审。

`PalBattleVisualProfile : Resource` 是独立、显式作者配置；`ResourceSaver.Save` 保存 `.tres`，`NativeBattleVisualProfile.Load(uri, CheckedStoryProfile story)` 返回 `CheckedBattleVisualProfile`。它只绑定已校验 story 所选闭包需要的逐角色、逐槽队伍视觉，不创建 Node、trigger、battle、RNG，不推进动画或改变数值规则。未选 numeric teams 可以继续保留合法非零数据；这不是对它们的执行能力授权。

## #136 / #205：原生身份与位置必须逐槽（中间实现叶）

所有 native team（包括单敌）必须显式声明非空 `EnemySlots`，按 ordinal 与所选敌位一一对应。
缺槽不再从 team 的 `TargetId/EnemyPosition` 合成；含槽时混写任一旧标量也拒绝，诊断包含 TeamId。
旧两个导出字段仅保留默认哨兵（-1 / `(int.MinValue,int.MinValue)`）用于定位拒绝，不是兼容入口。
当前开发期 `FormatVersion=1` 收紧此要求，未新增双 loader；旧单敌原生资源须显式重新 Prepare。
Prepare 可读取既有离线 author 来源，但写入的原生身份/位置始终是槽数组，并执行 staging/final 严格回读。

**尚未完成**：离线 author 仍为 team 中间生产契约；native URI/metadata 已在下文逐槽化，逐槽 SpriteId 声明、活跃 author 样例迁移、
异种正式链与独立命中/死亡/失败释放验收仍属后续叶。本节不是 #136 完成或完整门通过证明。

## #136 / #206：非 Story 首战的敌图来源声明一致性

`NativeFirstMandatoryBattleProfile` 对每个已选 `EnemySlots` 同时核对敌图 `SpriteId == ImageEnemyId`
和 String `Source["identity"] == ImageEnemyArchiveEntry`（冻结首战为 `97` / `ABC97`，精确区分大小写）。
缺失、非 String、空值或不同声明即使有正确的新文件 hash 也拒绝；诊断包含 visual URI 与 slot ordinal。
Prepare 的发布前校验、保存回读与正常 Main 都复用该加载边界；没有新增 author 字段、loader 或证明域。
这里仅核声明一致性，hash 仅绑定文件 bytes，均不认证原始归档来源；一般 Story 的任意非空 Source
声明规则不因此收紧。此历史叶当时的 native team 仍共享敌图输入；下面 native 逐槽 URI 叶补充了
“槽0合法而异源槽1越域”的独立拒绝，不能将本处历史回归或静态循环检查当作该新增用例证据。

## #136：native 逐槽精灵 URI 与第二槽来源证明

`PalBattleVisualEnemySlot.EnemySpriteUri` 现在必填，单敌也不能省略。native team 的旧
`EnemySpriteUri` 仅保留空字符串拒绝哨兵；旧整队声明/混写明确拒绝，绝不在运行时补槽。
每个槽的 URI 都参与完整 native dependency/hash 闭包，并独立执行 sprite 结构及所需帧校验；
checked 保留对应 sprite（含实际 SpriteId），同 TargetId 两槽可以指向不同图，不从槽0投影。
非 Story 冻结首战对槽1的新文件同样检查 SpriteId/Source；即使槽0合法且新文件hash正确，
槽1 `Source.identity=ABC98` 仍精确拒绝为 `enemy slot 1`，恢复同URI声明后可重新加载。

正式 Prepare 从当前离线 author 的共享 enemySpriteUri 显式写入每个 native 槽，依赖收集、
staging/final strict 回读同样只读槽URI。旧 native 产物须显式重新 Prepare；没有双 loader。
此叶不改离线 author schema，故8个活跃 author 输入继续作为只读生产输入，并非 runtime fallback。
完整逐槽 author/SpriteId 声明和异种正式 Prepare→Main 仍待后续叶；native 帧 metadata 已由下节迁移，
不能将本节当作#136完整格式或父票验收。
直接消费者、synthetic首战生产者及GPU fixture的逐槽取图同步迁移；headless不证明GPU。

## #136：native 逐槽帧 metadata

每个 `PalBattleVisualEnemySlot` 必须显式持有 `IdleFrames/IdleAnimSpeed/MagicFrames/AttackFrames/ActWaitFrames`。
五字段默认 -1 用于拒绝缺失；IdleFrames 为1..65535，其余为0..65535。speed0/ActWait0保持原值，
不夹为1。旧 team 五字段只保留 -1 拒绝哨兵；旧格式或混写拒绝，无 runtime 展开/标量回退。
当前开发期仍为 FormatVersion1；旧 native 产物必须显式重新 Prepare。
逐槽校验所需帧索引0..Idle+Magic+Attack−1，错误包含TeamId和槽ordinal；checked投影取该槽五字段。
物理动作仍消费MagicFrames段（固定PAL_CLASSIC/Windows fight.c:4987–5050），不是新增敌方法术。
Prepare 从现有离线 team author 显式向每槽生产五字段并严格回读；author 的idleAnimSpeed现在必填，
不为缺失作者值补0。8个活跃author均已有speed2，本叶不改其来源/格式；完整author逐槽化另叶迁移。
定点覆盖同TargetId/同URI异metadata、槽1独立帧地址拒绝、五字段缺失/混写和WORD边界；
不新增Resource/DTO或改动Core/Main时序，不将此叶冒签异种正式生产链或父票完成。

## #136：致死反馈结束后的逐槽停止绘制

正常 Main 的绘制快照按槽保留 `Visible`，不从 TargetId 或共享 sprite 推断存亡。
Core 在反馈前已提交伤害，所以致死攻击的目标仍绘制完整物理反馈；反馈结束后的首次实际更新起，
仅该死亡槽停止提交 sprite 绘制，其他同 ID 槽继续绘制、行动及参与目标选择。槽数组、HP 面板和
纹理键不压缩、不重编号；纹理仍由整个 view 的原有退出路径释放，不提前释放共享源资源。
单敌使用相同可见性规则；新 Main 复用同 URI 从新战斗状态重新计算，不写回 Resource。

依据为 `fight.c` post-action check 清除死亡实例的 `wObjectID`，以及 `battle.c` 仅绘制非零对象。
本叶只补齐停止绘制的终态；**死亡场景渐隐、死亡音效及异种逐槽生产格式仍未实现/验收**，
不把 headless 绘制计划断言当作 GPU 像素或完整死亡表现证明。
现有单玩家双重复敌槽 SceneRunner 用例通过正常 Prepare→Main、真实输入和 `UpdateCompleted`
检查致死反馈仍可见、首次反馈结束仅死亡槽消失、存活槽下一轮仍可见，以及第二 Main 同 URI 初态。

## #209：统一逐角色玩家绑定（当前唯一形态）

承自 #135 与 [m6-no-legacy-compat-20261111](role-battle-visual-readiness.md)：`PalBattleVisualProfile` 的玩家侧不再有标量 `RoleId/PlayerSpriteUri/AttackEffectSpriteUri/AttackEffectIndex/PlayerPosition`，只保留 `Players : PalBattleVisualPlayer[]`（typed Resource，party 顺序），root `FormatVersion=2`。旧 FormatVersion=1 共享精灵产物显式拒绝并须重新 Prepare；没有双 loader、headRoleId+人数折叠加载或领队回退，单角色也是一项完整绑定。

公开入口统一为 `Load(uri, IReadOnlyList<int> partyRoleIds, enemyTeams, requirementTeamIds, palette)`；Story 重载按 `CheckedStoryProfile.BattleContent.Players` 传完整 party，非 Story 首战按已核 `content.Players` 逐位传入。加载器逐一认证 `Players[i].RoleId == partyRoleIds[i]` 与 `PlayerPosition == g_rgPlayerPos[party-1][i]`（battle.c:27-31 布局列：单角色 `(240,170)`，双人 `(200,176)`/`(256,152)`），数量与 party 一一对应；换位/缺员/错位精确拒绝，不以队伍位置、图号或领队身份推断第二角色。攻击效果组仍是 `rgwBattleEffectIndex[实际战斗精灵][1]×3`（fight.c:2029）的作者声明：每成员可选、必须配对、帧 `Index*3+{0,1,2}` 必须可寻址。

checked `Players`（`CheckedBattleVisualPlayer`）逐成员持有自己的 sprite/位置/效果帧；共享同一合法图仍是两个独立绑定（身份、姿势、受击、死亡不合并）。renderer 的玩家纹理键与效果键含 party 成员下标，普攻效果取行动成员自己的效果组；`PlayerOrigins` 返回各成员已校验的布局列。Prepare 的 author schema 迁移为 `visual.players[]`（严格配对/拒绝混写，见 [story package preparation](story-package-preparation.md)），staging/final 读回走同一公开加载链；9 份活跃样例已迁移。旧共享精灵 profile 不被运行时重解释，equipment 覆盖门与 #135 剩余验收不因本节放行。

## 作者 schema

- 根 `FormatVersion` 默认 **-1**，作者必须显式写1；`SourceIdentity` 必须非空，仅为声明，不是素材来源认证或人工/真实模式开关。native Visual root 为上文 #209 的 `FormatVersion=2`。
- `visual.players` 按 party 顺序逐角色声明（1..2 项）；每项 `roleId` 必须等于该位置 story 的 Core `BattleContent.Players[i].RoleId`，单角色也是一项绑定。`playerSpriteUri` 指向既有 `PalSprite`；它的 `SpriteId` 是图档索引声明，**不是 roleId**，不同角色可共享同一合法图。
- 每项 `playerPosition` 是作者显式 bottom-center 坐标，两个分量均为 SHORT，且必须等于参考布局 `g_rgPlayerPos[party-1][i]` 列（battle.c:27-31）；缺省哨兵不合法。旧 visual 级标量 player 字段拒绝，须重新准备。
- `PaletteUri` 指向显式准备的 `PalPalette`，要求 RGB768，并逐色等于 story 当前 active palette。当前无 palette-changing VM slice；不推测独立战斗调色板规则，不从 RGB 图片反推索引。
- `Teams : PalBattleVisualTeam[]` 的 `TeamId` 集合精确等于 story requirements 的 `BattleTeamIds`，无缺失、多余、重复；每个 `EnemySlots` 数组按顺序精确覆盖对应 Core enemies，每槽 `TargetId` 等于该敌位的 `Id`（战斗目标身份，**不是 enemy 定义、对象或图片 ID**）。
- 每槽显式写 SHORT `EnemyPosition`（bottom-center，作者已应用所需 Y offset）。每槽还必须显式写 `EnemySpriteUri`（既有 `PalSprite`）。每个 team 显式写 `BackgroundUri`（既有 `PalDialogueIndexedImage`，SourceIdentity 非空、320×200、完整 indices/alpha）。每槽五个帧 metadata 均必填并按上述 WORD 域校验；不在此推进时间。
- 两个 sprite 要有非空 `Source` 声明、WORD `SpriteId`、正的受限 decoded length/FrameCount、全部 nominal `0..FrameCount-1`；保留额外合法可寻址 frame aliases。每个 frame 的 WORD Index 唯一、SourceOffset 非负，size 与 indices/alpha 完整匹配。player 必须可寻址0..9，enemy 分段所需每个索引都必须存在，包括 aliases。
- `PalFrame.Texture` **不要求提供**；后续显示可以用原 indices/alpha 与已校验 active palette 着色。没有新增第二图像 DTO、JSON/blob、registry 或 runtime 准备入口。

## 依赖、隔离及所有权

根采用字符串 URI，先检查 root 的真实外部依赖图，再 IgnoreDeep 加载，避免重型 leaf 缺失在根深加载时成为引擎 ERROR。实际 leaf 图在加载之前按同一 internal `NativeContentDependencies` 检查规范路径、类型、UID/fallback、循环、纹理 source/.import、文件存在与精确 SHA-256 keys；外引 team metadata/frame 也纳入闭包。根配置自身是作者声明，**不自 hash**、不得回链 root；leaf 是正式 native Resource，而非 JSON 转装。

该 helper 是从 Story 原私有实现的最小提取；Story 的校验算法/诊断保持，额外可用的无 hash preflight 仅供 visual root 加载前验证。完成后再核声明文件 bytes；不锁定文件系统、不保证返回后或晚期并发替换。hash 不认证 `.cs/.gd`、引擎、`.godot` import output、缓存同步、GPU 像素或原始版权来源。详见 [Story 的证据边界](native-story-content.md#局部依赖与证据边界)。

返回 role/坐标/metadata 是值投影，teams/hash 是独立只读集合；持有本次 IgnoreDeep 的 sprite/palette/background 实例。**原生 Resource 仍可变**，调用方须按 prepared-resource 只读契约消费，不保证深不可变或热重载，不 clone-on-get。没有 Node 实例化，失败不留下视觉节点。

## #164：独立 sprite 的公开结构加载与 checked 加载

复用同一模块和原生 `PalSprite` / `PalFrame`，不新增 Resource、格式或运行时准备器：

- `NativeBattleVisualProfile.LoadSprite(uri)`：规范 URI 和数据依赖预检后，以 `IgnoreDeep` 加载，执行与完整 visual 共用的结构校验。保留 Source 声明、可选/null Texture、额外可寻址 alias 和原 indices/alpha；不认证原档来源。完整 visual 内部仍直接使用共用结构 core，保持原 root 的 hash 流程与诊断优先级，不因新增 standalone 包装重复改变旧错误上下文。
- `LoadSprite(uri, expected, resourceSha256)`：`expected` 必须由调用者独立 author，或从已校验现代包构造，不能先加载目标再把目标当唯一预期。开始任何目标 I/O 前，先捕获完整、纯 managed 的 scalar/frame/plane/Source/Texture descriptor，再复制 Ordinal hash map；不持有调用者可变 Resource、Variant collection、planes 或 dictionary。返回值仍是隔离但可变的原生 Resource，调用者按只读契约消费。

checked 精确比较 `SpriteId`、`DecodedByteLength`、nominal `FrameCount`、包含 alias 的实际 Index 集合，以及各 Index 的 `SourceOffset`、宽高、indices/alpha。帧集合顺序不构成身份，alias 不增加 nominal count。结构上非法的 expected 在目标预检前拒绝；不能通过提供坏 URI 掩盖 expected 的字段错误。

Source 是有类型的 JSON-like 值树：Nil/Bool/Int/有限 Float/String、String-key Dictionary、Array。严格保留 Variant 类型、key/value、数组顺序，字典枚举顺序不构成身份。expected 与 actual 各有独立 **depth64（root0）/work65536** 上限，key 也计数、深度为 parent+1。重复别名按值拷贝合法；不使用私有指针、反射或未验证的 collection identity。循环或更深/更大的有限树均有界拒绝，准确报告 `exceeds depth/work bound (possibly cyclic)`，不声称已证明循环。此限制仅适用于新 checked overload，旧 compatibility/full-profile Source 不增加限制。

Texture 可选性精确：null 只能匹配 null。非 null 的 expected 必须有规范 native `ResourcePath`，尺寸与所在 frame 一致；快照并比较 URI、`GetClass()` 原生类型及宽高。实际同 URI 异类型、同类型异 URI、缺失/多出或尺寸不符均拒绝。不会把 generic indexed sprite 改成强制有纹理；未来 FIRE Prepare 必须从其已验证包显式构造实际 PNG Texture 预期，不能用 null expectation 绕过必需纹理。不会复制缓存或在加载时自动 import。

checked hash map **包含 sprite root**（区别于完整 visual 的不自 hash 根）；复用 `NativeContentDependencies` 在加载前、字段/Source/Texture 比较后认证整个数据图，并精确比较 key 集合。包括 external frame、opaque metadata、Texture、image source 和 `.import`；缺/多/错 hash、非法类型/路径、循环或 UID/fallback 冲突明确拒绝。仍排除 scripts/engine/generated `.godot` output；源归档 SHA 仍只是声明。同步前后检查不是文件系统锁、并发快照、缓存真实性或 GPU 认证，也不保证返回后任意热修改安全。

自动化从真实 `ResourceSaver`/typed `IgnoreDeep` 和独立 literal 构造证明字段/Source/Texture、精确依赖、URI/type/dimensions、null 双向、外部 frame/opaque、UID、未准备 PNG remap 拒绝及新加载隔离；原 full-profile 回归保留。SVG 的实际 source/`.import` 正例与自有 PNG 缺 remap 负例分别标识，不宣称已交付 FIRE PNG Prepare。指定 RED、普通 fixture 错误、实际 GREEN 和正常 Main 兼容的本轮数字退出与原件见绑定候选收据；SOURCE/metadata 不代替实际 Game，也不补签 #147、完整共享门、真实资源、GPU/人工或整个 M6。

## 参考与明确留待后续的行为

只读 `reference/sdlpal/battle.c`：27 单角色位置表；880–949 按 role→F 图档、enemy object→enemy definition→ABC 图档加载，enemy 位置含 Y offset；124/199 bottom-center；735 当前 palette/night；982–987 FBP320×200。`fight.c`：2079/2120 普攻 player8/9；4987–5045 enemy idle/magic/attack 分段，attack loop `<=AttackFrames` 末索引为 idle+magic+attack−1；zero attack 取 idle−1。这里保留全原索引帧和必要分段 metadata，不猜动画帧索引、坐标或时间。

人工测试使用独立 literal 2×1 indexed frames、320×200 background 和作者坐标；SpriteId41/97 与 role0/team18/target7 特意不同。它们是可提交 synthetic 数据，不是原档复制或来源认证。`NativeBattleVisualProfileTests` 覆盖真实 ResourceSaver→严格 Load、版本/身份/精确映射、未选 numeric 数据、帧 aliases/平面、palette、hash/缺失/type/UID/cycle/外引 metadata 和隔离；旧 `NativeStoryProfileTests` 验证兼容。

**未交付**：攻击 effect 图档/索引绑定、动作/动画时间推进、renderer/GPU、Main/input/HUD、完整 package Prepare、真实 SceneRunner 往返。后续 UI 必须消费同一真实 Core 生命周期与本次校验资源，不另写规则引擎。冻结 `first-mandatory-battle-pre-trigger-v1` 与真实 owner28/@09C3 gate 不变，不能靠人工 visual 绑定绕开；#46/M4 尚未完成。

后续 [Story package production prepare](story-package-preparation.md) 已提供 typed Story/Visual 的统一离线绑定入口（checkpoint A，复用已准备依赖）；本 visual binding 自身不负责 Prepare、动作推进或 Main。完整可见人工作者数据包 B 现已交付，见下节；正常 Main 尚未接线。


## #46 checkpoint B: original authored sample

完整可提交人工作者 native 静态/双语非空bitmap字形/索引视觉资源及 Win/Loss 现代输入已提供，见 [正常准备命令、来源、搜索和数值设计](../../samples/authored-battle/README.md)。正常 numeric/story wrappers 分别准备两种场景；两个精确 Game 数据用例核双语 strict Load、真实搜索/可见平面和公开 Core Won/Lost 续接。本文上文 checkpoint A 的历史范围不改写；本B仍不接 Main/HUD/input/动作/effect，不签 SceneRunner/GPU/人工/CI、#46或M4完成。


## #46 D2-A: optional reference-presentation data, not playback

Version1 remains the base format; the root still defaults to −1 and the writer explicitly writes1. `AttackEffectSpriteUri=""` and `AttackEffectIndex=-1` mean absent. Native slot idle speed is now mandatory as described above; old team metadata resources are rejected and must be explicitly prepared again. No effect or idle speed is synthesized. Present effect fields must be paired, the group must be WORD0..65535, and all three **int** indices `group*3 + {0,1,2}` must be addressable in the checked `PalSprite`. High groups never wrap to WORD frame0. Existing nominal frame/extra-alias/index-plane validation applies unchanged. Every provided idle speed must be WORD, including legal0; no zero-to-one coercion. Root hashes include the effect's full native dependency graph using the existing exact closure.

`CheckedBattleVisualProfile.AttackEffectSprite` and read-only `AttackEffectFrames` retain the loaded native instances; `AttackEffectIndex` and slot `IdleAnimSpeed` are value projections. `RequireReferencePresentation()` is a pure selected-capability check: missing effect throws `InvalidDataException`; missing slot speed is already rejected by the native loader. It does not load a URI, advance a battle, consume randomness or change state. Resources are still mutable and must be consumed read-only. Format-valid unselected numeric teams remain outside these requirements.

The group is the resolved **column1** mapping for the actual player battle sprite (`fight.c2055`), not a role, enemy target, equipment object or effect sprite's own ID. `global.c1978` resolves role base battle sprite then nonzero equipment overrides; current no-equipment author content declares sprite41→group0 itself. This is an authored mapping, not authentication of retail tables. `PAL_SpriteGetFrame` takes **INT** (`palcommon.c803`), not WORD. No new layout copy is needed: existing SHORT bottom-center positions and frame dimensions are the inputs to `fight.c2125/2208`'s effect bottom-center `enemy(x,y-height/3+10)`, then `(−16,+16)` per frame. Applying this algorithm remains D2-B.

B now supplies three different original indexed diagonal light-stroke frames, shared active palette and alpha; normal Story Prepare publishes them through the same strict graph. D2-A does **not** connect these data to Main/renderer. Player windup/approach/effects/recoil/return, persistent per-battle idle WORD counter (including decrement0→65535), floating-number feedback and actual presentation capability selection remain D2-B. Existing C/D1 rendering continues; no GPU/manual/CI/fullgate/#46/M4 completion is claimed. Audio playback stays out of scope.


D2-B now selects this capability at normal Story startup, before map/session/RNG creation. Old v1 absence is still legal for strict **format** loading, but no longer runs normal Story silently in C/D1 fallback. Playback and live WORD counters consume these same checked resources; see [the D2-B runtime boundary](main-story-roundtrip.md#d2-b-player-effects-persistent-enemy-idle-and-impact-hp). Floating-number/reference label lifecycle remains D2-C; this is not full D/#46 completion.


## D2-C-A: optional native UI digit binding (not runtime floating feedback)

Version1 adds `NumberSpriteUri=""` (absent). A declared URI means a complete three-color UI digit sprite: Yellow19..28, Blue29..38, Cyan56..65, each ordered digit0..9 (`ui.c639–745`). The existing `PalSprite` validator still permits legal extra frames, aliases beyond nominal FrameCount, arbitrary valid frame dimensions and original alpha. Completeness is this binding's contract, not a new generic sprite rule; FrameCount need not be66. The full native graph participates in the existing exact dependency/hash checks before loading and after validation.

`CheckedBattleVisualProfile.NumberSprite` retains the loaded native object. `YellowNumberFrames`, `BlueNumberFrames`, `CyanNumberFrames` are read-only lists of those same frames; resources themselves remain mutable, consumed read-only, **not deep immutable**. `RequireNumberPresentation()` purely rejects absence without loading URIs, creating nodes, advancing time or consuming RNG. Absence is base-format valid; normal Main does **not yet select** this new capability, so D2-B startup compatibility is unchanged. A present incomplete sprite is malformed for this binding. Hash identity is not provenance.

Digits are UI sprite images, not natural-language Words or dialogue-font glyphs. Their reference drawing origin is **top-left**, not fighter bottom-center. Six pixels is layout advance, not a source-frame width requirement. The authored sample supplies original5×7 digit strokes with an offset warm-white shadow in6×8 indexed planes, using the unchanged active palette (Yellow5, Blue1, Cyan4, shadow79). This is original artificial art, not retail UI extraction, and no language/font registry or runtime preparation is added.

Public Story Prepare accepts optional `visual.numberSpriteUri`, leases its same native graph, hashes it and performs strict readback through the normal writer. Both authored inputs bind `Visual/Numbers.tres`. Fresh prepared `Assets/AuthoredBattleD2CA-20261103/{Win,Loss}/Story` reuse Static/Numbers; old outputs remain untouched. This checkpoint only delivers the complete resource vertical. Main floating-number lifecycle, queue, real-wall age, critical/evade Words lifecycle and drawing remain **D2-C-B**, not delivered here. Audio, GPU/manual/CI/fullgate and #46/M4 completion remain out of scope.
