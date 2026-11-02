# 现代战斗数值 JSON v1（#46 有界 Core seam）

状态：纯 Core `ModernBattleContent.Parse(string)` → 既有 `TriggerBattleContent` 已实现。它不读文件、不抽 RNG、不创建战斗或模拟获胜。供下一显式 native Resource 准备读取同一 typed 内容；**不是已交付的人工 package/native/Editor/CLI/Main 资源链**。测试中的字符串是纯内存人工数值，不是人工资源交付或原版认证。冻结的真实 `first-mandatory-battle-pre-trigger-v1`、envelope/provenance 和 owner28 `@09C3` gate 均未改动。

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
| `coveredByRoleId` | 整数0..5；沿既有snapshot约定0为无cover |

先完成本 reader 的严格校验，再复用 `BattleModels.Role` 转换；不调用或放松真实 frozen profile validator，不增加平行 role DTO。

## Player：复用 ClassicBattlePlayer

| 字段 | 类型/约束 |
|---|---|
| `roleId`, `hp`, `maxHp` | 与role相同且三者必须一致 |
| `attack`, `defense`, `dexterity` | WORD，有效面板（当前能力仅支持零装备效果） |
| `equipment`, `statuses` | 分别恰好6、9项WORD，显式能力输入，不从role悄悄推断 |
| `poisons` | 显式WORD数组；既有typed模型未定义固定毒槽宽，本格式不臆造宽度 |
| `dyingScript`, `friendDeathScript` | WORD |
| `coveredByRoleId` | null或整数0..5；沿combat模型约定null为无cover，不能与snapshot的0约定混同 |
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

`TriggerBattleContent.InitialRole / Player / Growth / Teams` 为public get-only投影。构造器继续防御拷贝全部嵌套集合，输出只读；record的with只生成独立copy，不能改content中的记录。下一native writer应读取这些同一typed值，不另造业务factory或角色schema。

公开 Core 用例从reader输出真正 `Start` → active lifecycle `SelectAttack/ResolveNextAction` → 准确自有completion ack → 原事件display/end：独立字面值43KO后HP125/MP35、attack1/health2；真实Lost HP0/MP20不恢复。另覆盖严格字段/维数/类型/范围/ID、全部现有能力字段不丢失、0阈值及重复成长行保留、选中入口无RNG拒绝和嵌套只读。它们不证明native准备、文件安全、Main输入/渲染、GPU、人工操作或CI。
