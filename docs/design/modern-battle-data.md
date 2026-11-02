# 现代战斗数值 v1（#46 有界数值准备 seam）

状态：纯 Core `ModernBattleContent.Parse(string)` → 既有 `TriggerBattleContent`，及独立 typed native Resource 编解码 `NativeBattleContent.Write/CreateCore/Load` 已实现。Core 不读文件、不抽 RNG、不创建战斗或模拟获胜；native runtime 不重读 JSON。显式离线 `PrepareBattleData.Prepare`、Editor scene 和正常 PowerShell wrapper 也已交付（见下方准备契约）；**数值准备不是完整人工 package 或 Main 资源链**。Core 测试字符串与可提交 `tests/Fixtures/ModernBattleData/battle-data.json` 均为独立人工数值，不是完整 storypackage 或原版认证。冻结的真实 `first-mandatory-battle-pre-trigger-v1`、envelope/provenance 和 owner28 `@09C3` gate 均未改动。

## 根与严格读取

根对象全部必填：`formatVersion: 1`、`roles`、`players`、`growth`、`teams`。不接受 profile/sourceKind/人工 ID 来跳过规则；未来 package/native 外壳另行诚实记录来源和图像/事件绑定。

- JSON 不允许注释、尾逗号、重复键（即使值相同）、非有限数字。每层仅接受下表字段；下划线开头的注释字段可忽略，但其中的重复键仍拒绝。键与七项 hidden 名称按 Ordinal 大小写精确匹配。
- `WORD` 是整数 `0..65535`，不接受字符串、布尔、null、小数或 `1.0` 记法；signed WORD 为 `-32768..32767`。bool 只接受 JSON true/false。
- 下述所有字段都必填，只有 player cover **值**允许 null；字段本身不能缺失。零、空列表也须显式声明，不能因缺数据就推断没有装备、钩子或成长。
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
| `attack`, `defense`, `dexterity` | WORD，有效面板（当前能力仅支持零装备效果） |
| `equipment`, `statuses` | 分别恰好6、9项WORD，显式能力输入，不从role悄悄推断 |
| `poisons` | 显式WORD数组；既有typed模型未定义固定毒槽宽，本格式不臆造宽度 |
| `dyingScript`, `friendDeathScript` | WORD |
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
- `CreateCore(PalBattleContent, string identity = "native battle content")` 严格检查根版本、数组 shape、nested null、growth role identity、重复 team ID、canonical cover，再复制字段并做 Core 格式验证。输出的所有 nested 集合独立且只读，不保留可变 Godot Resource alias。
- `Load(string uri)` 仅接受规范 `res://` URI，要求存在且类型为 `PalBattleContent`，用 `ResourceLoader.CacheMode.IgnoreDeep` 后走同一个 CreateCore。错误带选中 URI。它不读 `battle-data.json`、M1、原档或 MKF，也不解释 JSON blob。

根 `[GlobalClass] PalBattleContent` 的导出字段是显式存储的 `FormatVersion=1`（资源默认值为 invalid `-1`，正式 Write 赋1；ResourceSaver 必须写出版本，缺失版本不得继承为 v1）、`Roles: PalBattleRole[]`、`Players: PalBattlePlayer[]`、`Growth: PalBattleGrowth[]`、`Teams: PalBattleTeam[]`。前三组及每队 `Enemies: PalBattleEnemy[]` 当前必须恰好一项，team 非空且 ID 唯一；保留数组形式不代表 #47 多人能力已实现。Role/primary/hidden XP 复用 #45 的原生类型与最小共享 `NativeBattleRoleAdapter`；共享 adapter 只转换/检查必要 null，不把本格式/单角色/能力限制施加到旧 frozen envelope。

新增 player/enemy 资源逐项导出 Core 的全部字段（名字对应 PascalCase；enemy 的 attack/defense/dexterity 仍为 signed offset）。`PalBattleGrowth` 显式导出 `RoleId`、100 项 `LevelUpExperience`、完整有序 `PalBattleLearnableMagic[] LevelUpMagics`（`Level/MagicId`）；不排序去重。player 的 nullable cover 用 `HasCoveredByRoleId` 加整数 `CoveredByRoleId`：false 时值必须为0，true 时0..5；true+0 是实际 role0，不等同 null。role snapshot 的 CoveredByRoleId 继续保留原编号，不当作无 cover sentinel。

数值资源没有 profile/source/人工开关、来源认证或 storyprofile hash。本 codec 不能证明源数据完整性、图像/语言/事件闭包或资源来源。显式离线数值生产准备入口现已追加，契约见下节；Main 从不自动 prepare，也没有新增 CLI 作者命令。

本检查点的 headless native-map 用例真实执行：内存现代 JSON Parse → 正式 Write → ResourceSaver 保存 → ResourceLoader/严格 Load → 正式 trigger/lifecycle 的真实 Won/Lost 与一次性自有 receipt ack。独立字面预期 Won HP125/MP35、Lost HP0/MP20，attack1/health2；并核所有非零字段、signed/WORD 边界、0 阈值/重复成长行、未引用非零队保留、nested 隔离、URI/版本/类型/null/shape/ID/范围及选中不支持内容无 RNG/请求副作用。本次修补精确运行新 native class 43 例（含真实保存文件删除显式版本后的拒绝，以及保留 CreateCore 输出再变更 Resource 的 nested 隔离/只读核对）；旧 frozen profile class 33 例沿用未改源码的既有精确运行证据，不冒称完整 Game/Prepare/Main、GPU、人工或 CI 验收。


## 显式离线数值准备（#46 有界 Prepare 检查点）

唯一生产转换入口：`Pal.Game.Editor.PrepareBattleData.Prepare(string inputDirectory, string outputDirectory)`。参数为物理目录路径，返回规范 `res://Assets/.../BattleContent.tres`，不执行选中事件、RNG、战斗模拟或 frozen #45 gate。只负责上文完整格式合法性；合法非零装备、hooks、reward、0 阈值及有序重复成长行原样保留，**不代表未来 selected-entry 能力闭包已满足**。

输入目录必须实际存在、位于 `src/Pal.Game` 外，且恰好一个普通顶层 `battle-data.json`；额外文件或目录（即使为空）均拒绝。本入口限制原文件 **1..16MiB**，严格 UTF-8 解码，可带 UTF-8 BOM（BOM 计入原始字节及稳定性 hash），拒绝 UTF-16/非法 UTF-8；JSON 使用同一个 `ModernBattleContent.Parse`，不补齐缺字段。两侧规范化物理路径，按平台大小写/根规则判边界，双向不得重叠。输出必须为项目 `Assets` 下新或空目录，不能是文件或已有任意 entry。两侧全部 ancestor、输入树及输出树拒绝 link/reparse point（包括悬空链接）；允许输出缺失多层 ancestor。不改变 #45 对空子目录的旧契约。

全路径/格式预检后，持续持有 `OpenStableRead` 只读 lease（Windows `FileShare.Read`），保留原始字节 hash。按阶段复核路径、精确输入/输出 entry、当前文件和 lease 的内容 hash。复用 `NativeBattleContent.Write`，`ResourceSaver` 保存唯一 owned 临时 `.tres`，由正式 `NativeBattleContent.Load` 严格实际回读，再 `File.Move(..., overwrite: false)` 原子发布固定文件。输出复核只容许本次临时文件。失败只尝试清自有临时文件和本次新建空目录，不递归删除已有目录或他者新文件；清理失败不覆盖原始异常。hash 仅用于读稳定性，不是来源认证、canonical digest 或 storyprofile receipt。

正常编辑器入口 `res://Editor/PrepareBattleData.tscn` 的 `_Ready` 只接受精确顺序 `--battle-input <dir> --battle-output <dir>`，调用同一 public Prepare，成功后输出 `PAL_PREPARE_BATTLE_DATA:BattleContent.tres` 并 exit0；失败 stderr `Battle data native import failed: <原诊断>` 并 exit1，不发成功 marker。

```powershell
# 显式离线操作；不会由正常 Main 自动执行。OutputPath 必须新建或为空。
pwsh -NoProfile -File ./scripts/Prepare-BattleData.ps1 `
  -InputPath ./tests/Fixtures/ModernBattleData `
  -OutputPath ./src/Pal.Game/Assets/MyBattleData `
  -GodotPath $ApprovedGodotConsole
```

wrapper 负责唯一参数/路径预检、固定 Godot 版本、正常项目 build、启动上述 Editor scene、实际退出/诊断和 marker 验证，不维护第二 converter。可选 `-TimeoutSeconds` 沿既有准备工具的60秒默认、1..600范围，不修改共享预算；参数错 exit2，其余失败 exit1。实际引擎运行仍全局串行，发现已有未知 Godot 仅查 PID/Name 并暂停。

本检查点的 Game direct 新类32例调用同一 public Prepare，真正 Load 产物后经正常 lifecycle 真实KO验证 HP125/MP35；格式/UTF-8/大小/目录/覆盖/双向重叠/缺失 ancestor/Windows 大小写及 reparse 路径边界分别覆盖。Prepare process 新18例负责正常 PS/Editor argv、exit、marker 和错误传播，完整 Prepare 79例实际通过。两层共读一份可提交独立人工 numeric JSON（Core 不引入文件 I/O）；无新增业务factory或测试装配接口。晚期源替换、他者插入、磁盘/权限/rename失败及 cleanup失败没有确定性故障注入运行证明，只保留生产逐阶段复核及 owned cleanup 的代码审阅边界。它不是完整人工 storypackage、Main/SceneRunner/UI、GPU、物理操作、远端 CI 或整 #46/M4 验收。
