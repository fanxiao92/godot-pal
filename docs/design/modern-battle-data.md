# 现代战斗数值 v1（#46 有界数值准备 seam）

状态：纯 Core `ModernBattleContent.Parse(string)` → 既有 `TriggerBattleContent`，及独立 typed native Resource 编解码 `NativeBattleContent.Write/CreateCore/Load` 已实现。Core 不读文件、不抽 RNG、不创建战斗或模拟获胜；native runtime 不重读 JSON。显式离线 `PrepareBattleData.Prepare`、Editor scene 和正常 PowerShell wrapper 也已交付（见下方准备契约）；**数值准备不是完整人工 package 或 Main 资源链**。Core 测试字符串与可提交 `tests/Fixtures/ModernBattleData/battle-data.json` 均为独立人工数值，不是完整 storypackage 或原版认证。冻结的真实 `first-mandatory-battle-pre-trigger-v1`、envelope/provenance 和 owner28 `@09C3` gate 均未改动。

当前 #167 的新增装备 WORD 仅接通 Core 源码，**不在上述既有 native/Prepare 交付范围内**；运输缺口、禁止旧 writer 使用及验收状态见下方 [#167 Core 窄契约](#167-core装备-word-保真与选中战斗拒绝)。其余带票号的检查点保留各自历史范围。

## 显式角色名字对象声明与 JSON/native 运输（#158 / #159）

`ClassicBattlePlayer.NameObjectId` 是非位置构造参数的可选 `int?` 声明，默认 null 表示旧内容未声明映射；0 与 65535 都是合法对象编号，不把0当作缺省哨兵。参考 `fight.c` 的濒死/友亡检查，角色以名字编号选择全局对象，不能用 RoleId、队伍位置或相等的脚本入口反推对象身份。

公开纯格式验证 `ModernBattleContent.Validate` 要求一份内容的全部 player 同时声明映射或全部省略；已声明值必须为 WORD。相同名字对象必须声明完全相同的两个初始脚本入口（包括0），任一槽冲突即带对应 player/字段路径拒绝；不同名字对象即使两个入口相同也保持不同身份。现有防御复制、单角色兼容投影与 record-with 保留该可空值。原有未声明映射的构造和验证不变。

JSON player 可选精确字段 `nameObjectId`：缺席才表示 legacy null，字段出现时必须为非 null 的 WORD 整数，拒绝布尔、字符串、小数、`1.0`、负数、越界、对象及数组。既有 Ordinal 未知键与递归重复键规则不变，`NameObjectId` 不是此字段的别名。JSON 与 native 运输在同一 #159 交付中接通；不能把内部 Parse 已接受、native 仍 fail-closed 的检查点当成交付。旧 v1 JSON 省略字段仍兼容，但不保证旧严格 JSON reader 接受新字段。

原生 `PalBattlePlayer` 以导出 `HasNameObjectId=false` 和 `NameObjectId=0` 表达可空值。`Write` 先执行完整纯格式验证，再写出 has/value 和两个 hook WORD；原 #158 的 unsupported-transport guard 仅在读写两端同时接通后移除。`CreateCore` 在忽略未声明值之前显式拒绝 false+nonzero；true 时的 WORD 范围、全员映射及同名字两槽种子冲突复用原 Core 验证，错误仍带选中 identity 和字段信息。`CreateCore/Load` 返回独立防御复制，不保留可变 Resource alias。原生格式仍为1；两项新导出均缺席的旧 player 资源依默认 false/0 读为 null，保留既有 hook WORD，不推断或迁移真实名字编号。

**这里仅运输被动元数据，不开放运行时 owner。** 实现与用例本身不替代 #159 实际 native/Prepare 执行、整票独立复核及本地集成收据。

本声明验证不读写 world，不建立活脚本表，不改变钩子检测/完成或保存返回入口；live owner、0090、跨战斗/新执行器共享和真正 root-return 写回仍需后续前置与 #129 单独交付。人工声明不等于原版真实数据绑定，Core Unit 也不等于 native、Prepare、Main 或完整 M6 验收。

## 根与严格读取

根对象全部必填：`formatVersion: 1`、`roles`、`players`、`growth`、`teams`。不接受 profile/sourceKind/人工 ID 来跳过规则；未来 package/native 外壳另行诚实记录来源和图像/事件绑定。

- JSON 不允许注释、尾逗号、重复键（即使值相同）、非有限数字。每层仅接受下表字段；下划线开头的注释字段可忽略，但其中的重复键仍拒绝。键与七项 hidden 名称按 Ordinal 大小写精确匹配。
- `WORD` 是整数 `0..65535`，不接受字符串、布尔、null、小数或 `1.0` 记法；signed WORD 为 `-32768..32767`。bool 只接受 JSON true/false。
- 除明确标注可选的字段/section（包括 player `nameObjectId`）外，下述字段都必填；player cover **值**允许 null，但 cover 字段本身不能缺失。`nameObjectId` 出现时不允许 null。零、空列表也须显式声明，不能因缺数据就推断没有装备、钩子或成长。
- 结构/范围错误为 `InvalidDataException`，带 `battle-data.json.$` 及对象/字段路径（例如 `teams[1].teamId`）；复用的递归重复键诊断带父字段路径及重复键名，数组内该诊断不带索引。语法错误保留原 `JsonException` 为 inner exception。

`roles`、`players`、`growth` 和每队的 `enemies` 都保留数组 shape；当前到单角色 typed content 的 binding **明确仅接受恰好一项**，空项、多项（包括重复角色/战内 target ID）都拒绝，不挑第一项、不默默丢弃。`teams` 可多项且非空，teamId 必须唯一。未来 #47 可沿数组扩展 typed binding 与能力，不需要改成另一套格式；**当前未实现多角色/多敌引擎**。

## Role：复用 BattleRoleSnapshot

| 字段 | 类型/约束 |
|---|---|
| `roleId` | 整数0..5 |
| `level` | WORD（是否<=99属于当前能力预检） |
| `hp`, `maxHp`, `mp`, `maxMp` | WORD；maxHp>0，hp<=maxHp，mp<=maxMp；hp允许0供正式开场规则处理 |
| `primaryExperience` | `{value, level, count}`，三项均WORD |
| `hiddenExperience` | 恰好七个正式键：`health`, `magic`, `attack`, `magicPower`, `defense`, `dexterity`, `flee`，每项同上XP对象；可附_注释 |
| `equipment` | 恰好6项WORD |
| `magics` | 恰好32项WORD槽位（不是唯一ID注册表） |
| `statuses` | 恰好9项WORD |
| `coveredByRoleId` | 整数0..5，保留原角色编号；0仍可指角色0，不是通用的无cover sentinel。当前单角色零cover支持域只接受存储值0；实际能否掩护还取决于队伍和战斗规则，后续由 #48 扩展 |

先完成本 reader 的严格校验，再复用 `BattleModels.Role` 转换；不调用或放松真实 frozen profile validator，不增加平行 role DTO。

## Player：复用 ClassicBattlePlayer

| 字段 | 类型/约束 |
|---|---|
| `roleId`, `hp`, `maxHp` | 与role相同且三者必须一致 |
| `attack`, `defense`, `dexterity` | WORD，基表面板；声明装备闭包时有效面板由 `BattleEquipmentState` 按正式规则另行投影（本切片不接入会话执行） |
| `equipment`, `statuses` | 分别恰好6、9项WORD，显式能力输入，不从role悄悄推断 |
| `poisons` | 显式WORD数组；既有typed模型未定义固定毒槽宽，本格式不臆造宽度 |
| `dyingScript`, `friendDeathScript` | WORD |
| `nameObjectId` | 可选非 null WORD；省略表示未声明，所有 player 必须同时声明或同时省略；同名字对象的两个 hook 种子分别相等 |
| `coveredByRoleId` | null或整数0..5；combat模型以null表示没有实际cover，不能将snapshot存储的角色编号0直接当作该null。当前能力预检只接受此字段为null |
| `attackAll` | bool |

## Growth：复用 ClassicBattleGrowthData

每项 `{roleId, levelUpExperience, levelUpMagics}`：roleId为0..5且与role/player一致；`levelUpExperience` 为完整100项按level索引的WORD数组，**格式允许0**；`levelUpMagics` 为本role完整有序 `{level: WORD, magicId: WORD}` 行数组，允许空表。零行、已知法术、重复行及顺序均保留，不是ID注册表，不排序去重。包作者须提供完整表，单份数值JSON本身不能证明来源完整性。

正阈值、primary按role等级/hidden按自身等级低于阈值、没有待新增法术等，均由正式零成长能力预检证明；不能把本阶段保守能力限制永远写成数据格式规则。

## Team / Enemy：复用 ClassicBattleEnemy

每队 `{teamId: WORD, enemies: [...]}`。enemy全部必填：

| 字段 | 类型/约束 |
|---|---|
| `id` | 非零WORD，**战内target ID**，不是原始敌定义或图片ID；无需与teamId相等 |
| `level`, `hp` | WORD，hp>0 |
| `attack`, `defense`, `dexterity` | signed WORD，原表offset，非有效面板 |
| `physicalResistance` | WORD |
| `statuses`, `poisons` | 9项WORD状态、显式WORD毒数组 |
| `turnStartScript`, `readyScript`, `battleEndScript` | WORD |
| `magicId`, `dualMove`, `attackEquivItem`, `attackEquivItemRate` | WORD |
| `experience`, `cash` | WORD |

战内ID在一场内唯一；当前一场仅一个敌人，所以任何多项都明确拒绝。不同队可使用相同战内ID；未来同原enemy97出现两次时，也不暗含两者必须共享一个target ID。原始定义身份/图片绑定由未来native包决定，此纯数值格式不臆造它们。

## 格式合法 ≠ 选中入口可执行

所有合法非零装备、status、毒、cover、hooks、attack-all、魔法/双动/等效物品、奖励，以及不能保证零成长的表，均原样进入 typed content。`TriggerScriptSession.Start` 对选中入口的 next/loss/flee 保守闭包调用既有纯预检，在任何caller/角色/请求副作用与RNG前拒绝不支持内容；未被选择入口引用的其他队可以保留。不会通过simulate获胜来验证内容，也不因来源为人工而放行。

`TriggerBattleContent.InitialRole / Player / Growth / Teams` 为public get-only投影。构造器继续防御拷贝全部嵌套集合，输出只读；record的with只生成独立copy，不能改content中的记录。native writer读取这些同一typed值，不另造业务factory或角色schema。

公开 Core 用例从reader输出真正 `Start` → active lifecycle `SelectAttack/ResolveNextAction` → 准确自有completion ack → 原事件display/end：独立字面值43KO后HP125/MP35、attack1/health2；真实Lost HP0/MP20不恢复。另覆盖严格字段/维数/类型/范围/ID、全部现有能力字段不丢失、0阈值及重复成长行保留、选中入口无RNG拒绝和嵌套只读。它们本身不证明native准备、文件安全、Main输入/渲染、GPU、人工操作或CI。


## Typed 格式验证与 native Resource v1（#46 codec 检查点）

`ModernBattleContent.Validate(TriggerBattleContent)` 是公开、纯数值格式验证，与上述 JSON 数值/维数规则一致。Parse 在严格 JSON 读取后调用；native CreateCore 也调用。它允许合法非零能力、WORD 等级、0 阈值和重复有序成长行，不调用 frozen 真实 profile gate、selected-entry 能力闭包、RNG 或模拟战斗。既有 content 构造与 `Start` 的错误/副作用时机不改。

Game 正式 API 位于 `Pal.Game.Profiles.NativeBattleContent`：

- `Write(TriggerBattleContent)` 先格式验证，再创建独立 `PalBattleContent` 资源；不写文件、不准备完整包。
- `CreateCore(PalBattleContent, string identity = "native battle content")` 严格检查根版本、数组 shape、nested null、growth role identity、重复 team ID、canonical cover 及 canonical Name has/value，再复制字段并做 Core 格式验证。输出的所有 nested 集合独立且只读，不保留可变 Godot Resource alias。
- `Load(string uri)` 仅接受规范 `res://` URI，要求存在且类型为 `PalBattleContent`，用 `ResourceLoader.CacheMode.IgnoreDeep` 后走同一个 CreateCore。错误带选中 URI。它不读 `battle-data.json`、M1、原档或 MKF，也不解释 JSON blob。

根 `[GlobalClass] PalBattleContent` 的导出字段是显式存储的 `FormatVersion=1`（资源默认值为 invalid `-1`，正式 Write 赋1；ResourceSaver 必须写出版本，缺失版本不得继承为 v1）、`Roles: PalBattleRole[]`、`Players: PalBattlePlayer[]`、`Growth: PalBattleGrowth[]`、`Teams: PalBattleTeam[]`。前三组及每队 `Enemies: PalBattleEnemy[]` 当前必须恰好一项，team 非空且 ID 唯一；保留数组形式不代表 #47 多人能力已实现。Role/primary/hidden XP 复用 #45 的原生类型与最小共享 `NativeBattleRoleAdapter`；共享 adapter 只转换/检查必要 null，不把本格式/单角色/能力限制施加到旧 frozen envelope。

新增 player/enemy 资源逐项导出 Core 的全部字段（名字对应 PascalCase；enemy 的 attack/defense/dexterity 仍为 signed offset）。`PalBattleGrowth` 显式导出 `RoleId`、100 项 `LevelUpExperience`、完整有序 `PalBattleLearnableMagic[] LevelUpMagics`（`Level/MagicId`）；不排序去重。player 的 nullable cover 用 `HasCoveredByRoleId` 加整数 `CoveredByRoleId`：false 时值必须为0，true 时0..5；true+0 是实际 role0，不等同 null。role snapshot 的 CoveredByRoleId 继续保留原编号，不当作无 cover sentinel。player 的 nullable Name 同样用 `HasNameObjectId/NameObjectId` 表示：false+0 是未声明，true+0 是真实名字对象0；false+nonzero 必须拒绝，true 的 WORD/共享种子规则见上节。

数值资源没有 profile/source/人工开关、来源认证或 storyprofile hash。本 codec 不能证明源数据完整性、图像/语言/事件闭包或资源来源。显式离线数值生产准备入口现已追加，契约见下节；Main 从不自动 prepare，也没有新增 CLI 作者命令。

本检查点的 headless native-map 用例真实执行：内存现代 JSON Parse → 正式 Write → ResourceSaver 保存 → ResourceLoader/严格 Load → 正式 trigger/lifecycle 的真实 Won/Lost 与一次性自有 receipt ack。独立字面预期 Won HP125/MP35、Lost HP0/MP20，attack1/health2；并核所有非零字段、signed/WORD 边界、0 阈值/重复成长行、未引用非零队保留、nested 隔离、URI/版本/类型/null/shape/ID/范围及选中不支持内容无 RNG/请求副作用。本次修补精确运行新 native class 43 例（含真实保存文件删除显式版本后的拒绝，以及保留 CreateCore 输出再变更 Resource 的 nested 隔离/只读核对）；旧 frozen profile class 33 例沿用未改源码的既有精确运行证据，不冒称完整 Game/Prepare/Main、GPU、人工或 CI 验收。


## 六角色装备闭包与有效面板投影（#48 unit 1）

状态：数据/typed/Prepare 链已交付；#48 unit 2 已把执行语义接入 session/Main（DualAttack 双击、cover、有效面板、持续状态保留，见[经典战斗契约](classic-battle-session.md)），装备 battleSprite override 仍在副作用前拒绝。指定 RED 于 `6cfce2a` 真实失败（现有公开 Parse 拒绝 `roleEquipment` 未知字段），Core 15 例新增后完整 Core **831**，NativeBattleContentTests **55**（44+11，含装备 roundtrip 与 10 个 typed 边界）、公开 PrepareBattleDataTests **35**（33+2）；native-map 批次 408、库存 604、game 601，同步于 GameTestBatches/GameBatches 回归。

根对象新增两个可选 section（旧零装备文件不变合法）。

`roleEquipment`：恰 6 行 × 6 槽 WORD，即参考 `rgwEquipment[6][6]`（global.h）的六角色网格；非 party 角色行同样必须声明（参考 `PAL_UpdateEquipments` 遍历全部 `MAX_PLAYER_ROLES`）。每个 party 角色行必须与 `roles[].equipment` 逐字相等，不匹配即拒。

`equipments`：数组，每项恰 `objectId`（非零 WORD，全数组唯一）与 `effects`（恰 6 行，按 role 索引）——即该对象装备脚本（0x0017/0x001A/0x002D）对每个 role 的真实效应行。每行恰 `attack`/`defense`/`dexterity`/`battleSprite`（WORD，寄存器 SET 语义）与 `statuses`（恰 9 WORD），另可选 `magicPower`/`fleeRate`/`poisonResistance`（WORD，旧 JSON 省略时解析为0；typed 可为 null，既有 native reader 将这些旧字段的所有负数读为 null，历史行为不变；#53 依 retail stat18/21/22 装备写入加入，有效面板=基面板+寄存器和，同一 WORD 回绕规则；poisonResistance 仅入面板投影，战斗毒域仍不消费）。非零 status 仅允许 bravery/protect/haste/dualAttack（index 5..8）且值 1000..65535：>999 是参考 `PAL_RemovePlayerStatus` 判定“装备持续状态，不随普通清理清除”的边界，普通 1..999 回合值不属于装备闭包。网格非零项必须在 `equipments` 声明（缺失即拒）；已声明但未被穿戴的对象合法保留（同未引用队）。仅声明其中一个 section 亦合法（零网格+对象、或未带网格的旧内容）；无 section 时非零 `roles[].equipment` 保持格式合法但 `BattleEquipmentState.Initialize` 在任何状态前拒绝（“补闭实际装备脚本”的缺省拒绝面）。

Core 正式规则（`Pal.Core.BattleEquipmentState`，纯数据）：

- 初始化按 `PAL_UpdateEquipments`（global.c:1352）：清零全部寄存器后，按 role 外层、slot 内层逐个套用已穿戴对象效应行；寄存器 SET 不叠加，重复 Initialize 幂等，新会话从零起步不叠加旧效果。
- `AttackBonus/DefenseBonus/DexterityBonus/Effective*` 按 `PAL_GetPlayerAttackStrength/Defense/Dexterity`（global.c:1736/1800/1832，含 extra 槽位 0..6 求和；本切片 extra 恒零，留待临时效果）WORD 回绕；`EffectivePlayer` 输出战斗可消费的 `ClassicBattlePlayer` 投影（本 unit 不接入 session）。
- `BattleSprite(roleId, base)` 按 `PAL_GetPlayerBattleSprite`（global.c:1978-2008）：最后一个非零装备精灵寄存器胜出；基础精灵仍由视觉链提供，本层只暴露 override 数据。
- `StatusOverlay/ApplyStatuses` 按 `PAL_SetPlayerStatus` 好状态分支（global.c:2255）：仅取更大值、不叠加；既有 lifecycle 基础清理（<=999 清零）自然保留持续装备状态。

typed 同步：`PalBattleContent` 新增 `Equipments: PalBattleEquipment[]` 与展平 `RoleEquipment: int[]`（空=未声明，否则恰 36 WORD；Godot 导出不支持嵌套数组）；`PalBattleEquipment`/`PalBattleEquipmentEffect` 逐字段导出效应行。`Write/CreateCore` 与 JSON 同一套严格校验（形状、WORD、重复、闭包、party 行一致、status 域）；`PrepareBattleData` 无需改动即携带装备闭包，Prepare 新增两例真实走 fixture→Prepare→Load→Initialize 手算断言。选中入口对非零装备仍拒绝（本 unit 边界）：装备影响真实战斗循环留待 unit 2。


## #167 Core：装备 WORD 保真与选中战斗拒绝

状态：#166 拆分为 #167 Core 与 #168 native/Prepare 两个独立叶子。以下描述当前 #167 源码契约，不是全叶交付收据：Parse 与 selected-guard 两轮及其余四个边界 Fact 已经完整 Core **1335 GREEN**；全叶交付另以独立 Standards/Spec 双审、checkpoint 与实际集成记录为准，本页不替代这些记录。Core 反馈不证明 #168、真实私有 profile、#166/#161 或完整 M6 验收，不增加 story credit。

- **v1 严格 JSON / typed**：`formatVersion` 仍为1；`equipments[].effects[]` 新增可选、Ordinal 精确键 `attackAll` / `cooperativeMagic`，都是整数 WORD `0..65535`，不是 player 的 bool `attackAll`。缺席→null，显式整数0→0，非零值原样保留；显式 JSON null、负数、越界、非整数、布尔、字符串、对象和数组均带对应字段路径拒绝。直接 typed 值的 null 是合法未声明，非 null 值须为 WORD。此规则不改旧 `magicPower/fleeRate/poisonResistance` 的 JSON omission→0 或 native all-negative→null 行为。
- **兼容与复制**：在既有 `BattleEquipmentEffect` 上增加非位置 `int? AttackAll { get; init; }` / `int? CooperativeMagic { get; init; }`；旧八位置构造与生成的八输出 `Deconstruct` 不变，旧五参数调用仍可使用原默认值。`TriggerBattleContent` 既有 record-with 防御复制保留新标量并复制 statuses，不新增模型或改变基表/已知法术槽。
- **六角色寄存器**：`BattleEquipmentState.Initialize` 按六角色×六穿戴槽保留两套 nullable WORD 寄存器（每角色七项，index6 为 extra）；每个实际穿戴对象只使用对应 role 效应行，SET 不叠加。item0、extra 和未写寄存器均为 null，显式0不丢；已声明但未穿戴的对象不应用。重复初始化从空寄存器开始，快照不保留输入可变 alias。这些 WORD 不加入基面板或数值加成，不把 field65 当作 known magic，也不把 field4 转成已支持的全体攻击能力。
- **只读查询**：公开 `int? AttackAllWord(int roleId, int registerIndex)` 与 `int? CooperativeMagicWord(int roleId, int registerIndex)` 均为 O(1)，不暴露数组；先验证 roleId `0..5`，再验证 registerIndex `0..6`，越界抛含相应参数名和值的 `ArgumentOutOfRangeException`。
- **格式合法不代表可执行**：直接 `ClassicBattleSession`、lifecycle 与 trigger 的选中入口预检复用同一 `ClassicBattleSession.ValidatePlayer`。所选角色任一寄存器的新 WORD 非零（不限于1）即在有效面板/状态投影、公开状态/战斗请求、事件前缀副作用及 RNG 前拒绝；null/0 通过此项检查，其他未参战角色的合法非零值允许保留。既有 player 数值及能力检查顺序和原诊断不变（包括 bool `p.AttackAll`、sprite override、status）；仅在这些旧检查通过后，新增错误才指出 equipment `attackAll/cooperativeMagic` 的 role/register。原 battleSprite 与 player attack-all 拒绝边界不变，不开放全体攻击、合击或其他 M7 能力。

来源例子仅为 role2/slot3/item163 的 `attackAll=1`、`battleSprite=6`，及 role3/slot5/item260 的 `cooperativeMagic=351`；不据此宣称整个测试网格或四个新增 Fact 是 retail/natural-save。其余人工哨兵和布置只作独立保真/边界核对，不能替代真实六角色来源、前置进度或私有资源闭包。

**当前 native/Prepare 缺口**：`PalBattleEquipmentEffect` 与 `NativeBattleContent.Write/CreateCore` 尚未运输这两个新字段，`PrepareBattleData` 仍经过该旧 writer；它会丢失新值，不能把当前 Core 接受当作 native 往返保真。**在 #168 实际交付前，禁止将含这两个新字段的内容经旧 native writer 作者化或 Prepare**（包括显式0）。#168 是待 Core 叶实际验收并集成后的独立后续工作；未来计划为这两个新导出使用 -1↔null、0..65535 原值往返，其他负数或越界拒绝，但当前未实现，新的保存/加载及公开 Prepare 证据也未交付。下节既有 Prepare 契约与历史计数不为新字段补签。


## 显式离线数值准备（#46 有界 Prepare 检查点）

唯一生产转换入口：`Pal.Game.Editor.PrepareBattleData.Prepare(string inputDirectory, string outputDirectory)`。参数为物理目录路径，返回规范 `res://Assets/.../BattleContent.tres`，不执行选中事件、RNG、战斗模拟或 frozen #45 gate。只负责上文完整格式合法性；合法非零装备、hooks、reward、0 阈值及有序重复成长行原样保留，**不代表未来 selected-entry 能力闭包已满足**。

输入目录必须实际存在、位于 `src/Pal.Game` 外，且恰好一个普通顶层 `battle-data.json`；额外文件或目录（即使为空）均拒绝。本入口限制原文件 **1..16MiB**，严格 UTF-8 解码，可带 UTF-8 BOM（BOM 计入原始字节及稳定性 hash），拒绝 UTF-16/非法 UTF-8；JSON 使用同一个 `ModernBattleContent.Parse`，不补齐缺字段。两侧规范化物理路径，按平台大小写/根规则判边界，双向不得重叠。输出必须为项目 `Assets` 下新或空目录，不能是文件或已有任意 entry。两侧全部 ancestor、输入树及输出树拒绝 link/reparse point（包括悬空链接）；允许输出缺失多层 ancestor。不改变 #45 对空子目录的旧契约。

全路径/格式预检后，持续持有 `OpenStableRead` 只读 lease（Windows `FileShare.Read`），保留原始字节 hash。按阶段复核路径、精确输入/输出 entry、当前文件和 lease 的内容 hash。复用 `NativeBattleContent.Write`，`ResourceSaver` 保存唯一 owned 临时 `.tres`，由正式 `NativeBattleContent.Load` 严格实际回读，再 `File.Move(..., overwrite: false)` 原子发布固定文件。输出复核只容许本次临时文件。失败只尝试清自有临时文件和本次新建空目录，不递归删除已有目录或他者新文件；清理失败不覆盖原始异常。hash 仅用于读稳定性，不是来源认证、canonical digest 或 storyprofile receipt。

正常编辑器入口 `res://Editor/PrepareBattleData.tscn` 的 `_Ready` 只接受精确顺序 `--battle-input <dir> --battle-output <dir>`，调用同一 public Prepare，成功后输出 `PAL_PREPARE_BATTLE_DATA:BattleContent.tres` 并 exit0；失败 stderr `Battle data native import failed: <原诊断>` 并 exit1，不发成功 marker。

先按[固定 GdUnit 源码包与 bootstrap](../testing/gdunit-source-packages.md)准备仓库锁定的包；下面的 wrapper 会执行项目 build，但不负责下载或升级依赖。

```powershell
# 显式离线操作；不会由正常 Main 自动执行。OutputPath 必须新建或为空。
pwsh -NoProfile -File ./scripts/Prepare-BattleData.ps1 `
  -InputPath ./tests/Fixtures/ModernBattleData `
  -OutputPath ./src/Pal.Game/Assets/MyBattleData `
  -GodotPath $ApprovedGodotConsole
```

wrapper 负责唯一参数/路径预检、固定 Godot 版本、正常项目 build、启动上述 Editor scene、实际退出/诊断和 marker 验证，不维护第二 converter。可选 `-TimeoutSeconds` 沿既有准备工具的60秒默认、1..600范围，不修改共享预算；参数错 exit2，其余失败 exit1。实际引擎运行仍全局串行，发现已有未知 Godot 仅查 PID/Name 并暂停。

本检查点的 Game direct 新类32例调用同一 public Prepare，真正 Load 产物后经正常 lifecycle 真实KO验证 HP125/MP35；格式/UTF-8/大小/目录/覆盖/双向重叠/缺失 ancestor/Windows 大小写及 reparse 路径边界分别覆盖。Prepare process 新18例负责正常 PS/Editor argv、exit、marker 和错误传播，完整 Prepare 79例实际通过。两层共读一份可提交独立人工 numeric JSON（Core 不引入文件 I/O）；无新增业务factory或测试装配接口。晚期源替换、他者插入、磁盘/权限/rename失败及 cleanup失败没有确定性故障注入运行证明，只保留生产逐阶段复核及 owned cleanup 的代码审阅边界。它不是完整人工 storypackage、Main/SceneRunner/UI、GPU、物理操作、远端 CI 或整 #46/M4 验收。
