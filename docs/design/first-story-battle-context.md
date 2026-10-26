# 首个必经剧情战：离线战前上下文契约

状态：**#45 的 `45-offline-contract` 与 `45-core-profile` 组件已实现；Godot 原生 Resource、Main 与战斗行为仍未实现。本页不宣称 #45 整票完成。**

本契约描述调用 scene 2 / map 12 / owner 28 的 trigger `@09C3` **之前**的独立验收前置。它是维护者显式 author 的剧情前置，不是自然游玩存档，也不是存档格式。它不能替代 `@09C3–@0A32`、战中角色钩子、动态 auto 或失败分支的真实执行。

## 1. 真实命令与产物

```text
pal-extract author-battle-context --input <author.json> --output <new-or-empty-directory>
```

参数顺序和数量固定。成功退出码为 `0`，stdout 仅输出：

```text
Battle context authored: first-mandatory-battle-pre-trigger-v1
```

数据/文件错误退出 `1`，诊断前缀为 `Battle context authoring failed:`；不支持、重复、缺值或调换参数退出 `2`。输入必须是严格 UTF-8 JSON（可带 UTF-8 BOM）；重复键、未知非注释字段、错误类型和非法范围均拒绝。下划线开头的字段只作注释。

输出只能是新建或空目录，路径中的链接/reparse point 被拒绝；任何失败都不自动覆盖、清理或回滚已有内容。成功写：

- `battle-context.json`：严格校验后规范缩进的完整 author 文档；
- `index.json`：`formatVersion`、固定 `profile`、固定文件名和该文件实际字节的 SHA-256。

命令只读取 author 文件，不读 MKF、SLF、M1 包或参考源码，不自动同步资源。运行时不得启动本工具，也不得读取原始档；后续 `45-core-profile` / `45-game-integration` 应把这份现代 JSON 显式准备成 Godot 原生 Resource，并在加载时复核 `index.json` 内容身份。真实原始与派生资源不提交。

## 2. v1 顶层与冻结身份

顶层字段必须且只能为：

| 字段 | 契约 |
|---|---|
| `formatVersion` | 整数 `1` |
| `profile` | `first-mandatory-battle-pre-trigger-v1` |
| `identity` | 固定切片身份 |
| `provenance` | 前置来源和禁止的可选行为 |
| `layout` | Windows 表布局身份 |
| `snapshot` | 逐字段运行前状态 |
| `closure` | 完整局部命令/资源闭包和后续票所有权 |
| `resources` | 外部现代资源相对引用、SHA-256 与所有权 |

`identity` 固定为：

```json
{
  "snapshotKind": "authored-precondition-not-natural-save",
  "sceneId": 2,
  "mapId": 12,
  "ownerId": 28,
  "triggerAddress": 2499
}
```

地址 `2499 == 0x09C3`。不能把内部开战 `@09FE` 当入口，不能写成 `natural-save`。`provenance.optionalGrinding` 与 `provenance.playerEquipmentChanges` 必须均为 `false`。`fieldSources` 中每项严格记录：

- `path`：被解释字段的 JSON 路径，且不得重复；
- `sourceKind`：`original-base`、`required-precondition` 或 `author-policy`；
- `sourceAddress`：`required-precondition` 必须使用非空 WORD 指令地址；`original-base` / `author-policy` 必须为 `null`；
- `appliesTo`：必须与路径一致：`roleN`、`team-before-09C3`、`world`、`sceneN` 或 `ownerN`。

每个 scalar 以及空数组容器都必须恰有一条来源；不存在的路径和下划线注释路径均拒绝。已由 #44/#45 与只读来源核定的冻结 tuple 必须精确为：

- role0/role1 的 `level`、六个 `equipment` 槽和 `coveredByRoleId` 均为 `original-base/null/roleN`；role1 的 `hp/maxHp` 与 `mp/maxMp` 同样为 `original-base/null/role1`；
- role0 `magics[0] == 296` 与 role1 的十个 `magics` 项均为 `original-base/null/roleN`；
- role0 `magics[1] == 345` 为 `required-precondition/@0905/role0`；
- owner28 的 `state == 2` 为 `required-precondition/@0936/owner28`。

`required-precondition` 只允许上述已批准 path/address tuple；不能用任意 WORD 地址伪造来源。role0 的最终 `hp/maxHp/mp/maxMp` 虽已冻结，但现有契约未核定其恢复指令地址或基表来源，所以这四项在 v1 必须使用 `author-policy/null/role0`，不得猜成 `original-base` 或新增 `required-precondition` tuple。其他尚未由本契约核定真实来源的字段仍须逐字段记录，但 v1 不猜测或额外冻结其 tuple；人工 fixture 对这些字段使用 `author-policy/null`。fixture 不得把上述基表事实标作 author policy，也不存在 fixture-only 来源种类。详细真实 author 还应为角色、队伍、经验、装备、状态、场景入口、对象可变字段、调色板、音乐和起点逐项记录来源；author 工具不会把原始表值伪装成已经执行的自然进度。

## 3. Windows 布局与逐字段快照

`layout` 固定为：

```json
{
  "equipmentRoleCount": 6,
  "equipmentSlotCount": 6,
  "levelUpMagicRoleCount": 5
}
```

装备初始化是 **六 role × 六槽**；升级法术表每行是 **五个可玩 role**。二者不得共用同一列数或补出第六个升级法术角色。

### 3.1 队伍与角色

`@09C3` 前 `snapshot.partyRoleIds` 必须为 `[0]`。role1 已有角色状态但尚未在队；原事件 `@09FC` 才形成 role0/role1，快照不得提前替代该指令。

`snapshot.roles` 必须按 role ID 0–5 各有一项。每项严格包含：

- `roleId`、`level`、`hp/maxHp`、`mp/maxMp`；
- `primaryExperience: { value, level, count }`；
- `hiddenExperience` 的 `health/magic/attack/magicPower/defense/dexterity/flee` 七项，每项同样保留 value、隐藏等级和动作计数；
- 六槽 `equipment`；最多 32 个不重复非零对象 ID 的 `magics`；
- 九个 `statuses` 槽；`coveredByRoleId`。

所有原 WORD 为 `0..65535`；`vanishTime` 等明确有符号字段为 `-32768..32767`。当前 HP/MP 不得超过上限，主/隐藏经验等级必须与本独立零动作基线的角色等级一致。

已经冻结且 author 必须保持的真实事实：

| 字段 | 基表/前置与最终解释 |
|---|---|
| role0 | level 1；HP 150/150；MP 100/100；基表装备 `[196,225,208,166,235,249]`；基表法术 `[296]`；`@0905` 再加入对象 345 |
| role1 | level 5；**HP 28/240、MP 10/240，不补满**；基表装备 `[197,0,210,170,237,248]`；法术 `[312,316,321,327,332,306,297,309,303,307]` |
| cover | role0 由 role2 cover；role1 由 role0 cover；即使 role2 不在队也不删除关系 |
| 装备语义 | 对象170在正式六角色装备重算中产生持续 DualAttack；快照不硬编码“有效面板合计”代替装备脚本 |
| 经验 | 主经验及七类隐藏经验分别保存；无可选练级基线不等于删除隐藏等级/动作计数 |

自动化中的六角色文档是**人工小 fixture**，只证明格式和严格拒绝，不是可提交的真实源快照；role2–5 的真实字段必须由本机 author 明确提供，不能从测试 fixture 反推。

### 3.2 世界、入口与位置

`world` 保存 scene/map、palette/night、探索音乐与必要战斗音乐，以及唯一 leader 坐标源。`leader` 必须有 `x/y/direction/layer/phase`；`direction` 只允许 `north/east/south/west`，`phase` 必须为与 `StaticSceneRules`/正常探索 seam 一致的 `0..3`。`searchProof` 保存 owner28、PAL_Search 的 13 点 sample index 和地图碰撞已通过的事实，不能只凭邻近坐标猜命中。

`sceneEntries` 保存受前置影响的 scene `enterEntry/teleportEntry`，`sceneId` 必须唯一且必须包含当前选中 scene 2。当前来源证据未把 scene2 的两个入口值核定为冻结常量，因此 v1 只要求两个值均为 WORD 且各自有 provenance，不猜测精确值。`eventObjects` 以 owner ID 唯一保存 SDLPal `EVENTOBJECT` 的全部后续会话可变字段：`x/y/direction/state/vanishTime/triggerMode/triggerEntry/autoEntry/autoIdleFrameCount/spriteId/spriteFrames/currentFrame/scriptIdleFrameCount/spritePtrOffset/autoSpriteFrames/layer`，包括跨场景被前置改变的对象，不修改静态原包。`x/y/direction`、入口、模式、sprite 和四个 frame/counter/offset 字段是 WORD；`state/layer/vanishTime` 是 signed WORD。leader `x/y` 服从现有 `StaticLeader` signed-short seam，`layer` 为 WORD。

owner28 的已冻结身份为：坐标 `(1280,256)`、前置 `@0936` 后 `state=2`、trigger mode 1、trigger `@09C3`、auto 0、sprite 210。author 还必须提供独立核过碰撞和 PAL_Search 命中的 leader 起点；工具记录该证明，但地图几何验证仍由真实资源准备/后续 SceneRunner 承担，不能由一个布尔值替代验收。

## 4. 完整局部 closure 与后续票所有权

`closure.entry` 固定 `@09C3`。每个 `entries` 项包含唯一 `id`、固定 `category` (`command/resource/validation`)、按下表内容结构化并逐字段精确核对的 `identity`、精确且无重复的 `ownerTickets` 集合、`status` 和精确 `resourceIds` 链。v1 必须且只能包含以下九项，缺一、多一或任意字段不符均拒绝 author。当前后续能力均未交付，所以九项 `status` 只能是 `contract-only`；不能仅凭引用或 hash 自称 `prepared`：

| id | 内容边界 | 执行所有权 |
|---|---|---|
| `trigger-09c3-0a32` | 完整战前 `@09C3–09FE` 与 Won `@09FF–0A32`，不从 @09FE 起步 | #52 能力、#53 真实整合 |
| `battle-team-18` | `@09FE (0012,A073,0000)`；队18=对象495×2→enemy97×2；战场24；第三操作数 boss/no-flee 复合语义 | #46/#47/#51，#53 |
| `role-hooks-random` | role1 dying `@A96E–A987`、role0 friend-death `@A9B5–A9D1` 全随机分支 | #49 |
| `equipment-six-role` | 六 role 全槽初始化、对象170双击及持续状态 | #48 |
| `growth-five-role` | 五 role 升级法术布局、主/隐藏经验、成长/恢复/清理 | #50 |
| `dynamic-auto-0a36-0a3b` | 两组动态改写后的移动/朝向/隐藏 auto，按更新推进 | #52 |
| `failure-reload-todo` | Lost→`@A073`→`0x004E` 重载请求边界；不假读档/重试/探索 | #51 |
| `battle-images` | enemy97/ABC97、role F0/F1、battlefield24/FBP24、布局/效果帧 | #53 |
| `dialogue-languages` | 局部事件和角色钩子的简中/英文消息、名称、头像、字形、UI 引用 | #49/#53 |

本组件只冻结身份和所有权；`contract-only` 明确表示运行能力尚未交付。Core/Game 在闭包能力齐备前必须于任何显示、对象变化或战斗请求之前拒绝 `@09C3`；不得静态加载成功后偷偷跳到 `@09FE`、换简单战斗或发完成标记。

`resources` 必须且只能包含下列固定 ID/kind/`ownerTickets`：`events/faithful-events/[52,53]`、`battle-data/battle-data/[46,47,48,49,50,51,53]`、`battle-images/battle-images/[53]`、`dialogue-zh-cn/dialogue-language/[49,53]`、`dialogue-en/dialogue-language/[49,53]`、`glyphs/glyph-atlas/[49,53]`。每项另存 `reference/sha256`；reference 必须是纯词法、跨平台的 forward-slash 相对路径，禁止绝对路径、反斜线、控制字符、空或 `.`/`..` segment、首尾或重复 slash、冒号/scheme、Windows 非法字符、设备名及 segment 尾随点/空格，SHA-256 必须为小写 64 位十六进制。九项 closure 的资源链接逐项冻结：trigger/dynamic 依赖 events；battle command 依赖 events+battle-data；角色钩子依赖 events+battle-data+双语对话+glyphs；装备/成长依赖 battle-data；失败边界依赖 events+双语对话+glyphs；图像和语言项分别链接其精确资源。`battle-team-18.identity` 明确保存 `@09FE`、操作数 `(0012,A073,0000)`、team/object/enemy/count/battlefield、第三操作数 boss/no-flee 特值和语义字段反向编码规则。资源来源身份不替代内容独立核对。

## 5. 覆盖审计与明确不扩张

父规格覆盖审计的四组部分覆盖按下列位置收口：

1. **真实完整 chunk 本机逐字节往返**：沿用 `export-events` / `rebuild-events`，由 #53 的真实内容准备留本机证据，#54 审计；只覆盖 SSS chunk 4，不扩大成 M5 全格式、全 opcode 或整 MKF 重建。当前自动化人工 chunk 往返不冒充真实数据证据。
2. **Windows 精确布局**：六 role 装备与五 role 升级法术由本契约固定；WORD/有符号/union 视图、帧表与直接图像入口、跨文件引用由实际转换票 #48/#49/#50/#53 分别验证。对象表 union 必须按调用语境分类；头像直接 RLE 与效果帧表不得走同一错误入口。
3. **严格负例**：本 author 覆盖版本/profile、重复键、未知/缺字段、WORD/有符号范围、角色布局、闭包缺项、资源引用路径、hash、真实 argv、链接及 new-or-empty/no-overwrite。事件集合自己的重复地址/标签、跨文件悬空引用继续由现有 event 严格加载负责；后续原生 loader 必须覆盖缺失/损坏/身份不符和同 URI 会话隔离。
4. **父规格 out-of-scope 继承**：不做新游戏全前置、全场景自由探索、全 VM/opcode、完整战斗菜单、主动魔法/道具/合击、商店/背包/换装、存档读写/兼容/自动重试、音频播放验收、视频、其他平台、导出包、运行时提取、语言热切换、fallback、内容编辑器、通用状态库或无关重构。

## 6. 后续消费接口

- `45-core-profile`（本组件已实现）：`FirstMandatoryBattleProfile.Load(indexBytes, suppliedFiles)` 是唯一公开 profile factory，接收外层提供的 exact bytes，以 ordinal 比较实际枚举出的唯一 `battle-context.json` key，严格核对 index 身份及原始 context 字节 SHA-256，再复核本页全部 v1 schema。成功 profile 以只读 lowercase `ContextSha256` 保留该已验证 exact-byte 身份。CLI author 通过 `InternalsVisibleTo("Pal.Extract")` 调用 Core 的 internal author-only canonical validator，不形成第二套 schema，也不产生无 index 的 runtime profile/hash。公开 profile 提供只读 identity/provenance/layout/role/world/resource metadata，closure identity 是九种封闭具名强类型 variant，不向消费者泄漏 JSON field-name 字典；`CreateSessionState` 深拷贝 party、角色与嵌套经验/装备/法术/状态，以及 leader/scene entry/event object 可变状态。Core 不做文件、实际路径或 Godot I/O。
- `45-game-integration`（未实现）：Editor Prepare 从本产物显式生成原生 Resource；输入/输出新建或空目录并保留真实诊断。Main 的独立 M4 argv、Resource URI、语言选择、READY 顺序及 owner28 副作用前拒绝由该组件接线。
- #46–#53：只扩展 closure 中分配的能力，复用同一 schema/profile 身份；不得建立第二套解码器、战斗循环或测试专用直装路径。

Core 自动化仅证明严格内存加载、typed metadata、exact-byte/index/file-set 拒绝与同一 profile 多 session 深拷贝；CLI 回归证明 author 仍保持既有文件/argv 行为。这些证据不证明原生 Prepare、真实资源、地图几何、Main M4、@09C3 执行、Core 战斗、SceneRunner、GPU 或物理输入。

本文件不宣称 #45 整票、Game lane 或后续 #46–#53 能力完成。
