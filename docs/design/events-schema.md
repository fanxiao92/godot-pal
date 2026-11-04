# events.json 契约

状态：**转换策略与 v1 契约已定义；Pal.Extract 的完整 chunk 4 导出/重建已实现基础离线子集。** 具名映射由 #78 冻结的表驱动 opcode 注册表承载（75 个 opcode 位、67 个动词：M5 批1 #96 基础控制流、批2 #97 条件跳转一、批3 #98 条件跳转二、批4 #99 对话表现、状态物品批1 #100，见第 4 节注册表章节）；其余 opcode 保留为 `raw`。此离线能力不表示运行时支持或全游戏入口/可达性已核实。对话使用原始消息引用，正文由独立语言包提供；语言包格式与 SLF 显示语义的集成留到 M3 事件里程碑，阶段边界见 [总体路线](development-plan.md)。M1/M2 仅处理静态场景资源与构建，不要求转换或执行事件。全量 opcode 注册表、真实数据验证与运行时支持按里程碑补齐，不将本文描述当成已存在的工具能力。

原版剧情、触发与 NPC 行为由 `SSS.MKF` 中的脚本驱动。离线工具将其转为 `data/extracted/events/` 中的可读 JSON，运行时只读取现代资源，不解析 MKF。背景见 [架构设计](architecture.md)、[决策索引](decisions.md) 和 [忠实转写决策](../adr/0001-faithful-event-transcription.md)。术语见 [术语表](../../GLOSSARY.md)。

## 当前 Pal.Extract 命令子集

```text
pal-extract export-events --input <原始资源目录> --output <新建或空目录>
pal-extract rebuild-events --input <events目录> --output <新文件>
```

`export-events` 只读 `SSS.MKF` 的 chunk 0（32-byte `EVENTOBJECT` 数组）、chunk 1（8-byte `SCENE` 数组）及 chunk 4（8-byte `SCRIPTENTRY` 数组）。它完整转写 chunk 4 到 `shared.json`，全部命令保留原地址、顺序后继及三个 WORD 操作数；当前场景和场景对象的非零 trigger/auto 根也被枚举为入口。由于尚未核实足以安全划分的完整可达性，全部命令保守放在 `shared`，不声称对象表 union 根或全局可达性已完成分类。

实现的具名映射由[opcode 注册表](#opcode-注册表表驱动78-冻结)的 75 条描述符承载（`end` 三变体（keep/advance/redirect，#96）、`goto`（#96）、`call`（#96）、`advanceEntry`（#96）、`confirmNoJump`（#96）、`itemShortageJump`（#97）、`playerLacksPoisonJump`（#97）、`enemyLacksPoisonJump`（#97）、`notPoisonedJump`（#97）、`enemyHpAboveJump`（#97）、`enemyTurnJump`（#97）、`partyHpNotFullJump`（#97）、`waitForAnyKey`（#97）、`roleInPartyJump`（#98）、`notFacingObjectJump`（#98）、`objectOutOfZoneJump`（#98）、`delay`（#98）、`itemNotEquippedJump`（#98）、`notFirstOfKindJump`（#98）、`stateMatchJump`（#98）、`sceneMatchJump`（#98）、`quitGame`（#98）、`randomBranch`（#98）、`restoreScreen`（#99）、`backupScreen`（#99）、`setExtraAttr`（#100）、`equipItem`（#100）、`incPlayerAttr`（#100）、`setPlayerStat`（#100）、`changeCash`（#100）、`addItem`（#100）、`removeItem`（#100）、`randomJump`、`startBattle`、`changeHp`（#100 复核）、`changeMp`（#100 复核）、`boostStat`、`setDialog` 四变体（#99 复核）、`showDialog`（#99 复核）、`redraw`（#99 复核修正）、`waitUpdate`、`walkStep`、`face`、`walkTo`、`setObjectPosition`、`setObjectGesture`、`setPartyFacing`、`changeHpMp`（#100 复核）、`revivePlayer`、`setAutoEntry`、`setTriggerEntry`、`setTriggerMode`、`markFailed`、`setMusic`、`setPartyPos`、`setState`、`setBattlefield`、`requestReload`、`fadeToRed`、`fadeOut`、`hide`、`setSceneScripts`、`fadeScene`、`setParty`、`noop`、`setObjectScript`、`setStateRange`）；其余 opcode—including 契约的其他基础动作—仍以 `raw` 保存，后续批次只追加描述符。`rebuild-events` 校验版本、严格字段、重复 JSON 键、WORD 范围、地址完整性、next 与入口引用，并按具名映射重编码；未映射操作数和低字节字段高位保留。只写 chunk 4 字节，不重建整个 MKF。输入必须是完整事件集合，输出不会覆盖既有内容。重建后比较实际 chunk SHA-256 与清单记录；匹配时可证明字节相同，具名语义编辑导致不匹配时，成功输出会明确提示差异而不回退复制旧 source 字节。

导出目前因无法证明所有权而将完整指令集放在 `shared`；重建器接受符合 v1 清单的多文件集合，并按全局文件/标签表校验跨文件 `next` 与入口引用。工具仍不支持通过 JSON 更改脚本长度/地址、对象表 union 根分类或运行时加载；真实原始资源不属于测试或仓库输入。CLI 人工回归覆盖完整 65,536 地址空间与 WORD 后继回绕、跨文件集合、重复地址/标签及悬空引用；这不替代真实数据往返。

## 1. 转换策略与范围

- **忠实转写**：原版一条指令对应一条命令，保留原始顺序、操作数与地址映射。使用具名动作、条件命令、标签与跳转，不尝试恢复结构化 `if`、循环或对话树。
- **结构化子集**：`sequence`、`if`、`choice`、`loop` 留给后续手写新内容。它们不出现在原版转写产物中，不要求能编译回原版字节码，也不属于本版基础命令契约。
- **增量支持**：尚未具名的 opcode 用 `raw` 无损保存。保存成功不等于可以执行；运行时遇到不支持的命令必须明确失败，不能跳过或当成成功。
- **无损范围**：未编辑的完整转写产物可以重建原始脚本 chunk 4；不承诺重建整个 MKF、消息文件或其他资源。

## 2. 已核对的参考事实

来源为仓库内固定副本，不依赖在线文档；当前数据包已完成初步结构核对，真实事件与文字语义需在 M3 对照验证。

| 事实 | 参考位置 |
|---|---|
| 使用 SSS 的 chunk 0 事件对象、1 场景、2 对象表、4 脚本 | [global.c](../../reference/sdlpal/global.c) 的 `PAL_LoadDefaultGame`、`PAL_ReadGlobalGameData` |
| chunk 3 为 `M.MSG` 消息偏移表 | [text.c](../../reference/sdlpal/text.c) 的 `PAL_InitText` |
| 指令由一个 opcode 和三个 WORD 操作数组成，合计 8 字节；读取按小端处理 | [global.h](../../reference/sdlpal/global.h) 的 `SCRIPTENTRY`、[global.c](../../reference/sdlpal/global.c) 的 `LOAD_DATA` / `DO_BYTESWAP` |
| trigger 与 auto 的停止、跳转、等待、文字处理存在差异 | [script.c](../../reference/sdlpal/script.c) 的 `PAL_RunTriggerScript`、`PAL_RunAutoScript` |
| WIN95 物品/法术描述由菜单循环调用 auto，并读取文字指令 operand 1 控制行号 | [itemmenu.c](../../reference/sdlpal/itemmenu.c)、[magicmenu.c](../../reference/sdlpal/magicmenu.c) 的描述循环 |
| 状态包含对象状态、可变入口和两类脚本各自的空闲计数器 | [global.h](../../reference/sdlpal/global.h) 的 `EVENTOBJECT` |

不预先断言文件恰有五个 chunk、固定 opcode 总数或最小切片只需多少个 opcode。未核实的操作保持 `raw`；专用条件与跳转按对应 opcode 核对，不能依据动词名猜测。

## 3. 文件集合与公共规则

完整忠实转写集合包括一个清单和若干事件文件：

```text
data/extracted/events/
  index.json       版本、原始脚本身份与事件文件列表
  scene-001.json   只归属于某个场景的指令
  shared.json      多个所有者可达、归属不确定或动态目标可能涉及的指令
  objects.json     只归属于物品、法术、敌人等对象脚本的指令
  unassigned.json 未被已知入口追踪到的指令，包括保留槽位
```

除 `index.json` 外，仅输出有入口或命令的文件。`scene-NNN` 中 NNN 是至少三位、左侧补零的场景 ID。所有文件使用 UTF-8 JSON，不含注释、重复键、NaN 或无穷值。

公共约束：

- `formatVersion` 为整数 `1`，`profile` 为 `"faithful"`；不识别的版本或 profile 明确拒绝，不猜测兼容。
- WORD 的存储值为整数 `0..65535`。有符号字段按注册表转换为 `-32768..32767`，不把所有操作数统一解释为有符号数。
- `fileId`、入口 `id` 和命令 `label` 均匹配 `[A-Za-z_][A-Za-z0-9_-]*`；分别在集合、文件入口数组和文件命令数组内唯一。
- 各对象中以下划线开头的字段仅作可读注释；运行时与反向编译器忽略它们。其他未定义字段拒绝，避免拼写错误被静默接受。
- 所有表格中的字段均为必填，除非明确标为可选。数组顺序不能替代显式引用或原始地址。

### 3.1 清单 `index.json`

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` | 整数 `1` |
| `profile` | 字符串 `"faithful"` |
| `source` | 下表定义的对象，仅供离线工具识别原始脚本 |
| `files` | 不重复的 `fileId` 字符串数组，按字典序；对应同目录 `<fileId>.json`，不得包含路径或扩展名 |

`source` 的字段：

| 字段 | 类型与约束 |
|---|---|
| `archive` | 字符串 `"SSS.MKF"`，来源名称，不是运行时文件路径 |
| `chunk` | 整数 `4` |
| `byteOrder` | 字符串 `"little"` |
| `instructionCount` | 整数 `1..65536`；超出本版可寻址范围则拒绝并报告，不截断 |
| `byteLength` | 整数，必须等于 `instructionCount * 8` |
| `sha256` | 原始 chunk 4 字节的 SHA-256，小写 64 位十六进制字符串 |

这是一份**完整集合**的清单，不用同一 profile 表示只保留可达指令的裁剪结果。M3 首次输出忠实事件集合时，即使只具名最小切片涉及的命令，其他指令也保留为 `raw`；完整覆盖不要求提前实现全量语义。M1 的静态场景包不生成本清单，也不要求提供事件文件。

### 3.2 事件文件

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` | 整数 `1` |
| `profile` | 字符串 `"faithful"` |
| `fileId` | 与清单及文件名一致的标识符 |
| `entries` | 入口对象数组，见下表 |
| `commands` | 命令对象数组，按 `source.index` 升序；不复制共享命令 |

入口对象：

| 字段 | 类型与约束 |
|---|---|
| `id` | 文件内唯一标识符 |
| `owner` | `{ "type": "scene" | "eventObject" | "object", "id": WORD }`；ID 在对应数据表中校验 |
| `slot` | 下表列出的入口字段；对象表使用带类型前缀的字段名区分 union 视图，不接受任意新增字段名 |
| `kind` | `"trigger"` 或 `"auto"`，是**本次调用入口的执行方式**，不是某段指令固有的属性 |
| `target` | 有效命令引用，或 `null` 表示原始入口值 0、禁用入口 |
| `sourceAddress` | WORD；非空 target 的原始地址必须与目标命令的 `source.index` 相等，空 target 必须为 0 |

入口字段与执行方式：

| `owner.type` | 允许的 `slot` | `kind` |
|---|---|---|
| `scene` | `wScriptOnEnter`、`wScriptOnTeleport` | `trigger` |
| `eventObject` | `wTriggerScript` / `wAutoScript` | 分别为 `trigger` / `auto` |
| `object` | `player.wScriptOnFriendDeath`、`player.wScriptOnDying`；`item.wScriptOnUse`、`item.wScriptOnEquip`、`item.wScriptOnThrow`；`magic.wScriptOnUse`、`magic.wScriptOnSuccess`；`enemy.wScriptOnTurnStart`、`enemy.wScriptOnBattleEnd`、`enemy.wScriptOnReady`；`poison.wPlayerScript`、`poison.wEnemyScript` | `trigger` |
| `object` | `item.wScriptDesc`、`magic.wScriptDesc` | `auto` |

对象表是 union，必须结合实际用途和引用确认其类型；不能把每个对象同时按全部视图解释成入口。未确定类别时保留全部指令并标注调查缺口，不伪造根。所有者 ID 不等于调用参数：调用上下文可能是 NPC、队员、敌人或描述行号，须由对应调用方定义。

同一指令可以被不同入口共享，甚至以不同执行方式调用，因此不能仅给脚本块贴一个 `kind` 后复用给所有调用者。新入口类型须先在对应数据契约中定义，不把未分类的根丢弃。

### 3.3 命令公共字段

| 字段 | 类型与约束 |
|---|---|
| `label` | 文件内唯一标识符；忠实导出默认使用 `L` + 四位大写十六进制原始地址，例如 `L001F` |
| `op` | 已注册的动词或 `"raw"` |
| `next` | 原始顺序后继的命令引用；按 WORD 算术取 `(source.index + 1) & 65535`，目标超出实际数组时为 `null`。即使命令停止或无条件跳转，也保留此信息供条件失败、入口推进等语义使用 |
| `source` | `{ "index": WORD, "opcode": WORD, "operands": [WORD, WORD, WORD] }`；必须恰好三个操作数，包括未使用、保留及非零字段 |

具名命令还携带第 4 节定义的语义字段。`raw` 没有额外执行字段，原始内容只存于 `source`，不在顶层再复制 opcode / operands。

`source` 供离线重建和诊断使用；运行时不能直接解释其中的 opcode 或操作数来绕过具名命令。加载时将标签引用解析为执行位置，不要求运行时认识 MKF 格式。

### 3.4 引用及零地址

- 本文件引用为 `"L001F"`，跨文件引用为 `"shared#L001F"`；大小写敏感，必须唯一解析。禁止文件路径、URL、多个 `#` 或空片段。
- 集合的所有指令，包括下标 0，在且仅在一个文件内保留；引用不得指向遗漏或重复的指令。
- `next` 必须对应 WORD 顺序后继，即使在另一文件。若完整数组有 65536 条，下标 65535 的后继回绕到 0；较短数组最后一条没有后继则为 `null`。实际执行需要后继而它为空时必须报错，不自行补指令。`commands` 数组的物理相邻关系不决定执行顺序。
- **入口的零值与控制流目标的零值不混为一谈**：禁用入口用 `null`；指令操作数中的目标 0 仍引用下标 0 的标签。trigger 的零地址停止规则，以及 auto 在一次调用内部跳到 0 的行为，由各自执行器遵循参考语义处理。
- 普通场景循环不会调用入口为 0 的 auto；但不得据此删除下标 0，或把所有指向它的控制流都优化成统一 `end`。

## 4. 双向注册表与基础命令

注册表在离线工具中定义（#78 起为表驱动实现，见下文章节）。每个具名 opcode 必须声明：动词、三个操作数各自的字段映射或保留规则、WORD/有符号转换、引用及特殊值、trigger/auto 差异、控制流后继和编码规则。没有核实完整映射时保持 `raw`。

反向编译以 `source.opcode` 和三个原始操作数为基础，使用**具名语义字段重新编码所有已映射操作数**，保留未映射的位和操作数；不能只把 `source` 原样复制就宣称具名转换正确。动词与原始 opcode 不匹配时拒绝。字段若只对应低位，例如颜色 BYTE，编码时保留原 WORD 中未映射的高位。

本版定义以下基础映射；它们是待实现的契约，不表示目前 CLI 已支持：

| opcode | `op` | 额外必填字段与映射 |
|---|---|---|
| `0x0000` | `end` | `mode: "keep"`；保留三个原操作数 |
| `0x0001` | `end` | `mode: "advance"`；保留三个原操作数，推进到 `next` |
| `0x0002` | `end` | `mode: "redirect"`、`to` 为引用映射 operand 0、`limit` 为 WORD 映射 operand 1；operand 2 保留 |
| `0x0003` | `goto` | `to` 为引用映射 operand 0、`limit` 为 WORD 映射 operand 1；operand 2 保留 |
| `0x0004` | `call` | `to` 为引用映射 operand 0（被调 trigger 脚本入口）、`eventObjectId` 为 WORD 映射 operand 1（0=继承调用者的对象 id）；operand 2 保留（#96） |
| `0x0008` | `advanceEntry` | 无操作数映射；三个原操作数保留（#96） |
| `0x000A` | `confirmNoJump` | `to` 为引用映射 operand 0（玩家选否时跳转）；operand 1、2 保留（#96） |
| `0x004D` | `waitForAnyKey` | 无操作数映射；三个原操作数保留；实际按键集合为 KeySearch/KeyMenu（#97） |
| `0x004E` | `requestReload` | 无操作数映射；三个原操作数保留；后继为 stop（#97 复核修正：参考直接 `return 0`） |
| `0x0058` | `itemShortageJump` | `itemId` 为 WORD 映射 operand 0、`count` 为有符号整数映射 operand 1（`(SHORT)` 比较）、`to` 为引用映射 operand 2；库存持有量 < count 时跳转（#97） |
| `0x005D` | `playerLacksPoisonJump` | `objectId` 为 WORD 映射 operand 0（object.json poison 视图）、`to` 为引用映射 operand 1；当前玩家角色未携带该毒对象时跳转（#97） |
| `0x005E` | `enemyLacksPoisonJump` | 同 `playerLacksPoisonJump`，检查敌人 rgPoisons；operand 2 保留（#97） |
| `0x0061` | `notPoisonedJump` | `to` 为引用映射 operand 0；当前玩家角色完全无毒时跳转；operand 1、2 保留（#97） |
| `0x0064` | `enemyHpAboveJump` | `percent` 为 WORD 映射 operand 0（无符号乘子）、`to` 为引用映射 operand 1；敌当前 HP×100 > 敌表最大 HP×percent 时跳转；operand 2 保留（#97） |
| `0x0068` | `enemyTurnJump` | `to` 为引用映射 operand 0（敌方行动回合时跳转）；operand 1、2 保留（#97） |
| `0x0074` | `partyHpNotFullJump` | `to` 为引用映射 operand 0（任一在队队员 HP 低于上限时跳转）；operand 1、2 保留（#97） |
| `0x0079` | `roleInPartyJump` | `roleNameWord` 为 WORD 映射 operand 0（与在队角色的 rgwName 名字 WORD 全等比较，不是角色下标）、`to` 为引用映射 operand 1；任一在队角色名字命中时跳转；operand 2 保留（#98） |
| `0x0081` | `notFacingObjectJump` | `eventObjectId` 为 WORD 映射 operand 0（1 基全局 event object 下标，先验当前场景对象区间）、`zoneUnits` 为 WORD 映射 operand 1（面向判定 |dx|+2|dy| < units×32+16，且对象 sState>0；命中且 zoneUnits>0 还把该对象触发模式置 kTriggerTouchNormal+zoneUnits）、`to` 为引用映射 operand 2；不在场景/不在面向区域/状态非正即跳转并置脚本失败标记；无保留操作数（#98） |
| `0x0083` | `objectOutOfZoneJump` | `eventObjectId` 为 WORD 映射 operand 0（同 0x0081 场景区间先验）、`zoneUnits` 为 WORD 映射 operand 1（出界判定 |dx|+2|dy| ≥ units×32+16，与 0x0081 方向相反）、`to` 为引用映射 operand 2；不在场景或出界即跳转并置脚本失败标记；无保留操作数（#98） |
| `0x0085` | `delay` | `delayUnits` 为 WORD 映射 operand 0（阻塞 units×80 ms 墙钟，无任何游戏更新）；operand 1、2 保留（#98） |
| `0x0086` | `itemNotEquippedJump` | `itemId` 为 WORD 映射 operand 0（object.json item 视图，与 rgwEquipment 精确比对）、`count` 为 WORD 映射 operand 1（无符号；全队装备数 < count 即跳，运行时把 0 归一为 1，仅改内存条目）、`to` 为引用映射 operand 2；无保留操作数（#98） |
| `0x0091` | `notFirstOfKindJump` | `to` 为引用映射 operand 0（本战斗敌人非同类首个即跳）；operand 1、2 保留（#98） |
| `0x0094` | `stateMatchJump` | `eventObjectId` 为 WORD 映射 operand 0（共享前奏选对象：0/0xFFFF=调用者）、`state` 为有符号整数映射 operand 1（`(SHORT)` 与 sState 比较）、`to` 为引用映射 operand 2；状态相等即跳（正向条件）；无保留操作数（#98） |
| `0x0095` | `sceneMatchJump` | `sceneId` 为 WORD 映射 operand 0（与当前场景 wNumScene 全等）、`to` 为引用映射 operand 1（相等即跳）；operand 2 保留（#98） |
| `0x00A0` | `quitGame` | 无操作数映射；三操作数保留；后继为 stop（进程经 PAL_Shutdown 退出，#98） |
| `0x00A2` | `randomBranch` | `count` 为 WORD 映射 operand 0（分支数：实际执行到后续 count 条之一，无率无目标操作数）；operand 1、2 保留；后继声明为 randomBranch（#98） |
| `0x0007` | `startBattle` | `enemyTeamId` 为 WORD 映射 operand 0；`lossTarget` 为可空引用映射 operand 1；`isBoss` 为布尔值，`fleeTarget` 为可空引用，二者共同映射 operand 2，规则见下文 |
| `0x001F` | `addItem` | `itemId` 为 WORD 映射 operand 0、`count` 为有符号整数映射 operand 1（负值即从库存移除）；operand 2 保留。v1 契约占位名 `giveItem`，#100 起定名 `addItem`（script.c:974 `(SHORT)` 转型） |
| `0x0006` | `randomJump` | `rate` 为 WORD 映射 operand 0、`to` 为引用映射 operand 1；operand 2 保留。执行时抽取 `RandomLong(1,100)`，**抽取值 ≥ rate 时跳转**，否则顺序后继；预检必须同时覆盖两个真实分支（#49） |
| `0x001B` | `changeHp` | `scope` 为 WORD 映射 operand 0（0=调用者角色，非 0=全队）、`amount` 为有符号整数映射 operand 1；operand 2 保留。仅战中角色钩子执行语义已交付：PAL_IncreaseHPMP 只改活人并封顶（#49；#100 复核确认 scope 为整 WORD 非零即全队的布尔旗标，非位掩码/角色下标/名字 WORD） |
| `0x001C` | `changeMp` | 同 `changeHp`，作用于 MP；仅战中角色钩子执行语义已交付（#49；#100 复核同上） |
| `0x0030` | `boostStat` | `field` 为 WORD 映射 operand 0（PLAYERROLES WORD 块索引：17 攻击、18 灵力、19 防御、20 身法、21 逃跑）、`percent` 为有符号整数映射 operand 1、`roleOperand` 为 WORD 映射 operand 2（0=调用者）。执行按 `kBodyPartExtra` 寄存器写入 `基面板×percent/100`（WORD 截断、覆盖写），战后随寄存器清理（#49） |
| `0x003B` | `setDialog` | `box: "center"`、`color` 为 `0..255` 映射 operand 0 低字节、`rngWord` 为 WORD 映射 operand 2；operand 1 保留（#99 复核） |
| `0x003C` / `0x003D` | `setDialog` | `box: "upper"` / `"lower"`、`portraitId` 为 WORD 映射 operand 0、`color` 为 `0..255` 映射 operand 1 低字节、`rngWord` 为 WORD 映射 operand 2（#99 复核） |
| `0x003E` | `setDialog` | `box: "centerWindow"`、`color` 为 `0..255` 映射 operand 0 低字节；operand 1、2 保留（#99 复核） |
| `0x008E` | `restoreScreen` | 无操作数映射；三操作数保留；trigger switch only（script.c:3430），auto 到达即 TerminateOnError（#99） |
| `0x00A6` | `backupScreen` | 无操作数映射；三操作数保留；shared（InterpretInstruction script.c:3071）（#99） |
| `0xFFFF` | `showDialog` | `messageId` 为 WORD 映射 operand 0、`descriptionLineWord` 为 WORD 映射 operand 1；operand 2 保留。正文通过语言包解析，不内嵌可执行的 `text` 字段（#99 复核） |

### opcode 注册表（表驱动，#78 冻结）

Pal.Extract 的具名映射由 `OpcodeRegistry` 的**描述符表**驱动：每个已核对的 opcode 一条描述符，decode / validate / encode 全部走通用路径；后续 17 批 opcode 票只需追加描述符与对应测试，不改动通用路径。未登记 opcode 一律 `raw`。

每条描述符声明：

- `opcode` 与 `op` 动词；
- 三个操作数的**字段绑定**（未列出的操作数原样保留源 WORD）：

| 绑定种类 | 语义 |
|---|---|
| `word` | 无符号 0..65535，由字段值重编码 |
| `signedWord` | -32768..32767，补码编码回 WORD |
| `lowByte` | 0..255 映射操作数低字节；源 WORD 未映射的高位保留 |
| `reference` | 必填标签引用，解析为 chunk 4 地址 |
| `battleTarget` | 可空标签引用：null 编码 0；引用下标 0 被拒绝（须用 null） |
| `bossFlag` | 与同一操作数上 battleTarget 的空值严格等价的布尔（`isBoss` ⟺ `fleeTarget == null`） |
| `opcodeOffset` | 值 = opcode − base（如 `walkStep.direction`），编码时校验与 opcode 一致 |
| 常量字段 | 固定 JSON 值必须与描述符一致（`setDialog.box`、`end.mode`） |

- **执行方式声明**（trigger/auto 差异，对应 `script.c` 的 switch 拓扑：`PAL_RunTriggerScript` 与 `PAL_RunAutoScript` 各自持有少量控制流 case，其余统一委托 `PAL_InterpretInstruction`）：`shared`（两执行器都经 InterpretInstruction）／`triggerSwitchOnly`（仅 trigger 循环直接处理，auto 执行到该 opcode 会 TerminateOnError）／`bothSwitches`（两个执行器各自处理、语义一致）／`bothSwitchesDiffer`（两个执行器各自处理、行为不同，如各自空闲计数器、WIN95 描述文字）；
- **控制流后继声明**：`sequential`／`stop`／`conditionalJump`（引用目标或 next 二选一）／`battleResult`（Won→next、Lost→lossTarget、Fled→fleeTarget）／`messageSpan`（随消息跨度推进）／`randomBranch`（随机多路：后续 count 条之一；#98 为 0x00A2 新增）。

#### 字段命名约定（T0 冻结，与 data-schema 共用）

| 字段 | 引用目标 |
|---|---|
| `itemId` | object.json 的 item 视图 |
| `magicId` | magic.json（或 object.json magic 视图） |
| `enemyId` | enemy.json |
| `enemyTeamId` | enemy-team.json |
| `sceneId` | scene.json |
| `objectId` | object.json union（须带视图类型） |
| `eventObjectId` | event-object.json（SSS chunk 0） |

**`eventObjectId` 与 `objectId` 是两张不同的表**：`event-object.json`（场景事件对象，EVENTOBJECT，16 WORD）与 `object.json`（物品/法术/敌人/毒脚本 union，7 WORD）是不同数据源。指向前者的操作数命名 `eventObjectId`，指向后者的命名 `objectId`（并声明视图）。#78 依此重命名：`startBattle.teamId`→`enemyTeamId`；`setAutoEntry`/`setTriggerEntry`/`setTriggerMode`/`setState`/`setObjectPosition` 的 `objectId`→`eventObjectId`；`setStateRange` 的 `fromObjectId`/`toObjectId`→`fromEventObjectId`/`toEventObjectId`。既有 `src/Pal.Core` 严格加载器与 Pal.Game prepare 仍按旧字段名解析，迁移由后续票处理（#78 写入范围不含 src/）。

#### script.c 逐 opcode 核对结论（#78）

| opcode | 参考实际寻址（[script.c](../../reference/sdlpal/script.c)） | 结论 |
|---|---|---|
| 0x0024 setAutoEntry / 0x0025 setTriggerEntry / 0x0040 setTriggerMode / 0x0049 setState / 0x0013 setObjectPosition | `PAL_InterpretInstruction` 前奏按 operand 0 选 `pCurrent`：0 或 0xFFFF=调用者，否则 `lprgEventObject[operand0−1]`（1 基）；case 体写 `pCurrent->wAutoScript`/`wTriggerScript`/`wTriggerMode`/`sState`/`x,y`（0x0024/0x0025/0x0040/0x0049 另有 operand0≠0 守卫） | **event object 下标** → `eventObjectId` |
| 0x0090 setObjectScript | case 0x0090：`gpGlobals->g.rgObject[operand0].rgwData[2 + operand2] = operand1` | **object union 下标，不是 event object** → 保留 `objectId`（配合 `slotIndex`；语义槽随 union 视图而定，与第 3.2 节对象表入口槽对应） |
| 0x009A setStateRange | case 0x009A：`for (i = operand0; i <= operand1; i++) lprgEventObject[i−1].sState = operand2` | **event object 闭区间** → `fromEventObjectId`/`toEventObjectId` |
| 0x0007 startBattle | `PAL_StartBattle(wEnemyTeam, ...)`；敌队表为 DATA chunk 2（enemy-team.json） | → `enemyTeamId` |
| 0x006D setSceneScripts | case 0x006D：`rgScene[operand0−1].wScriptOnEnter/wScriptOnTeleport = operand1/2` | scene 下标 → `sceneId`（1 基） |
| 0x004A setBattlefield | case 0x004A：`gpGlobals->wNumBattleField = operand0` | 战场编号 → `battlefieldId` |

#### 当前注册表（75 个 opcode 位，67 个动词）

| opcode | op | 字段（绑定） | 执行方式 | 后继 |
|---|---|---|---|---|
| 0x0000 | `end` | `mode`="keep"（常量） | bothSwitches | stop |
| 0x0001 | `end` | `mode`="advance"（常量） | bothSwitches | stop |
| 0x0002 | `end` | `mode`="redirect"（常量）、`to`(reference,0)、`limit`(word,1) | bothSwitches | conditionalJump |
| 0x0003 | `goto` | `to`(reference,0)、`limit`(word,1) | bothSwitches | conditionalJump |
| 0x0004 | `call` | `to`(reference,0)、`eventObjectId`(word,1) | bothSwitches | sequential |
| 0x0006 | `randomJump` | `rate`(word,0)、`to`(reference,1) | bothSwitches | conditionalJump |
| 0x0007 | `startBattle` | `enemyTeamId`(word,0)、`lossTarget`(battleTarget,1)、`isBoss`(bossFlag,2⟺fleeTarget)、`fleeTarget`(battleTarget,2) | triggerSwitchOnly | battleResult |
| 0x0008 | `advanceEntry` | — | triggerSwitchOnly | sequential |
| 0x0009 | `waitUpdate` | `frames`(word,0)、`runTriggers`(word,1)、`updateGestures`(word,2) | bothSwitchesDiffer | sequential |
| 0x000A | `confirmNoJump` | `to`(reference,0) | triggerSwitchOnly | conditionalJump |
| 0x000B–0x000E | `walkStep` | `direction`(opcodeOffset, base 0x000B) | shared | sequential |
| 0x000F | `face` | `direction`(word,0)、`frame`(word,1) | shared | sequential |
| 0x0010 | `walkTo` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0013 | `setObjectPosition` | `eventObjectId`(word,0)、`x`(word,1)、`y`(word,2) | shared | sequential |
| 0x0014 | `setObjectGesture` | `frame`(word,0) | shared | sequential |
| 0x0015 | `setPartyFacing` | `direction`(word,0)、`gesture`(word,1)、`memberIndex`(word,2) | shared | sequential |
| 0x0017 | `setExtraAttr` | `equipPartWord`(word,0)、`field`(word,1)、`value`(signedWord,2) | shared | sequential |
| 0x0018 | `equipItem` | `equipPartWord`(word,0)、`itemId`(word,1) | shared | sequential |
| 0x0019 | `incPlayerAttr` | `field`(word,0)、`delta`(signedWord,1)、`roleOperand`(word,2) | shared | sequential |
| 0x001A | `setPlayerStat` | `field`(word,0)、`value`(signedWord,1)、`roleOperand`(word,2) | shared | sequential |
| 0x001B | `changeHp` | `scope`(word,0)、`amount`(signedWord,1) | shared | sequential（#100 复核） |
| 0x001C | `changeMp` | `scope`(word,0)、`amount`(signedWord,1) | shared | sequential（#100 复核） |
| 0x001D | `changeHpMp` | `scope`(word,0)、`amount`(signedWord,1) | shared | sequential（#100 复核） |
| 0x001E | `changeCash` | `amount`(signedWord,0)、`to`(reference,1) | shared | conditionalJump |
| 0x001F | `addItem` | `itemId`(word,0)、`count`(signedWord,1) | shared | sequential |
| 0x0020 | `removeItem` | `itemId`(word,0)、`count`(word,1)、`shortageTarget`(battleTarget,2) | shared | conditionalJump |
| 0x0022 | `revivePlayer` | `scope`(word,0)、`healthTenths`(word,1) | shared | sequential |
| 0x0024 | `setAutoEntry` | `eventObjectId`(word,0)、`entry`(word,1) | shared | sequential |
| 0x0025 | `setTriggerEntry` | `eventObjectId`(word,0)、`entry`(word,1) | shared | sequential |
| 0x0030 | `boostStat` | `field`(word,0)、`percent`(signedWord,1)、`roleOperand`(word,2) | shared | sequential |
| 0x003B | `setDialog` | `box`="center"、`color`(lowByte,0)、`rngWord`(word,2) | triggerSwitchOnly | sequential |
| 0x003C | `setDialog` | `box`="upper"、`portraitId`(word,0)、`color`(lowByte,1)、`rngWord`(word,2) | triggerSwitchOnly | sequential |
| 0x003D | `setDialog` | `box`="lower"、`portraitId`(word,0)、`color`(lowByte,1)、`rngWord`(word,2) | triggerSwitchOnly | sequential |
| 0x003E | `setDialog` | `box`="centerWindow"、`color`(lowByte,0) | triggerSwitchOnly | sequential |
| 0x0040 | `setTriggerMode` | `eventObjectId`(word,0)、`mode`(word,1) | shared | sequential |
| 0x0041 | `markFailed` | — | shared | sequential |
| 0x0043 | `setMusic` | `musicId`(word,0)、`mode`(word,1) | shared | sequential |
| 0x0046 | `setPartyPos` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0049 | `setState` | `eventObjectId`(word,0)、`state`(signedWord,1) | shared | sequential |
| 0x004A | `setBattlefield` | `battlefieldId`(word,0) | shared | sequential |
| 0x004D | `waitForAnyKey` | — | shared | sequential |
| 0x004E | `requestReload` | — | shared | stop（#97 复核修正） |
| 0x004F | `fadeToRed` | — | shared | sequential |
| 0x0050 | `fadeOut` | `frames`(word,0) | shared | sequential |
| 0x0052 | `hide` | `frames`(word,0) | shared | sequential |
| 0x0058 | `itemShortageJump` | `itemId`(word,0)、`count`(signedWord,1)、`to`(reference,2) | shared | conditionalJump |
| 0x005D | `playerLacksPoisonJump` | `objectId`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x005E | `enemyLacksPoisonJump` | `objectId`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x0061 | `notPoisonedJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0064 | `enemyHpAboveJump` | `percent`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x0068 | `enemyTurnJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0005 | `redraw` | `delayUnits`(word,1)、`updateGestures`(word,2) | triggerSwitchOnly | sequential（#99 复核修正） |
| 0x006D | `setSceneScripts` | `sceneId`(word,0)、`onEnter`(word,1)、`onTeleport`(word,2) | shared | sequential |
| 0x0073 | `fadeScene` | `mode`(word,0) | shared | sequential |
| 0x0074 | `partyHpNotFullJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0075 | `setParty` | `roleId0`(word,0)、`roleId1`(word,1)、`roleId2`(word,2) | shared | sequential |
| 0x0078 | `noop` | — | shared | sequential |
| 0x0079 | `roleInPartyJump` | `roleNameWord`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x0081 | `notFacingObjectJump` | `eventObjectId`(word,0)、`zoneUnits`(word,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0083 | `objectOutOfZoneJump` | `eventObjectId`(word,0)、`zoneUnits`(word,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0085 | `delay` | `delayUnits`(word,0) | shared | sequential |
| 0x0086 | `itemNotEquippedJump` | `itemId`(word,0)、`count`(word,1)、`to`(reference,2) | shared | conditionalJump |
| 0x008E | `restoreScreen` | — | triggerSwitchOnly | sequential |
| 0x0091 | `notFirstOfKindJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0094 | `stateMatchJump` | `eventObjectId`(word,0)、`state`(signedWord,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0095 | `sceneMatchJump` | `sceneId`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x00A0 | `quitGame` | — | shared | stop |
| 0x00A2 | `randomBranch` | `count`(word,0) | shared | randomBranch |
| 0x00A6 | `backupScreen` | — | shared | sequential |
| 0x0090 | `setObjectScript` | `objectId`(word,0)、`value`(word,1)、`slotIndex`(word,2) | shared | sequential |
| 0x009A | `setStateRange` | `fromEventObjectId`(word,0)、`toEventObjectId`(word,1)、`state`(signedWord,2) | shared | sequential |
| 0xFFFF | `showDialog` | `messageId`(word,0)、`descriptionLineWord`(word,1) | bothSwitchesDiffer | messageSpan |

#### M5 批1 条目：基础控制流（#96，后续批次条目格式样板）

每批新增的具名 opcode 在此登记逐条语义。条目固定包含：动词、字段映射（含操作数寻址语义）、执行方式（对照 `script.c` switch 拓扑与行号）、后继声明、反向编码规则、真实数据命中计数（SSS.MKF chunk 4，43538 条指令）。本批核对结论：**全部操作数为立即 WORD**（`pScript->rgwOperand[n]` 直读，[script.c](../../reference/sdlpal/script.c) 无 wOperaxx 变量寻址），故无 variable 类字段；两个 switch 的控制流 case 逐行核对，行号如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0000 | `end` keep | 无操作数映射 | bothSwitches（trigger 3206：`fEnded=TRUE`，保存入口不变；auto 3522：返回原地址） | stop | 无映射字段，三操作数保留源 WORD | 3882 |
| 0x0001 | `end` advance | 无操作数映射 | bothSwitches（trigger 3213：`fEnded`+保存入口=next；auto 3528：`wScriptEntry++`） | stop | 同上 | 1188 |
| 0x0002 | `end` redirect | `to`=operand0（立即 WORD 地址→标签）、`limit`=operand1（空闲阈值 WORD） | bothSwitches（trigger 3221：空闲计数守卫通过则 `fEnded`+保存入口=`to`，否则 reset 计数走 next；auto 3535：同守卫，返回 `to`；两执行器各用 `nScriptIdleFrame`/`wScriptIdleFrameCountAuto` 计数，逻辑形状一致） | conditionalJump（`to` 或 next） | `to` 解析标签重编码、`limit` 由字段值重编码；operand2 保留 | 513（目标全部在范围内） |
| 0x0003 | `goto` | `to`=operand0、`limit`=operand1 | bothSwitches（trigger 3241：`wScriptEntry=to` 后循环内立即执行；auto 3551：同赋值后 `goto begin` 同帧执行；空闲守卫同 0x0002，失败时 reset 计数走 next） | conditionalJump（`to` 或 next） | 同 0x0002 | 493（目标全部非零且在范围内） |
| 0x0004 | `call` | `to`=operand0（被调 trigger 脚本入口地址）、`eventObjectId`=operand1（1 基 event object 表下标，0=继承调用者） | bothSwitches（trigger 3260 / auto 3568：均递归调 `PAL_RunTriggerScript(operand0, operand1?operand1:wEventObjectID)`——**被调脚本一律按 trigger 语义执行**；返回后 `wScriptEntry++`） | sequential | `to` 解析重编码、`eventObjectId` 重编码；operand2 保留 | 327（315 个 `eventObjectId`=0） |
| 0x0006 | `randomJump`（#78 已具名，本批复核） | `rate`=operand0（1..100，`RandomLong(1,100) >= rate` 时跳转）、`to`=operand1 | bothSwitches（trigger 3301：判定通过即 `wScriptEntry=to; continue`；auto 3577：判定通过且 **operand1≠0** 才跳，目标 0 在 auto 是「不跳」、在 trigger 是「跳到 0=停止」——零目标边缘差异记录在案，不改变声明口径） | conditionalJump（`to` 或 next） | `rate`/`to` 均重编码（#78 已定） | 235（96 个目标为 0，合法引用 L0000） |
| 0x0008 | `advanceEntry` | 无操作数映射 | triggerSwitchOnly（trigger 3337：`wScriptEntry++` 且 `wNextScriptEntry=新值`——推进保存入口但**不停止**本次运行；auto 无 case，default→`PAL_InterpretInstruction`→`TerminateOnError`(3078)） | sequential | 无映射字段，三操作数保留源 WORD | 36 |
| 0x0009 | `waitUpdate`（#78 已具名，本批复核） | `frames`=operand0、`runTriggers`=operand1、`updateGestures`=operand2 | bothSwitchesDiffer（trigger 3345：同步忙等 `frames`（0→1）帧，每帧 `PAL_GameUpdate(runTriggers?TRUE:FALSE)`、`updateGestures` 时 `PAL_UpdatePartyGestures`；auto 3595：每帧一次 `++wScriptIdleFrameCountAuto >= frames` 才推进，**仅 operand0 有效**） | sequential | 三字段均重编码（#78 已定） | 1999 |
| 0x000A | `confirmNoJump` | `to`=operand0（选否跳转目标） | triggerSwitchOnly（trigger 3375：`PAL_ConfirmMenu()` 选否→`wScriptEntry=to`，否则 next；auto 无 case→TerminateOnError） | conditionalJump（`to` 或 next） | `to` 解析重编码；operand1、2 保留 | 26（目标全部在范围内） |

真实数据核对：五个跳转类 opcode（0x0002/0x0003/0x0004/0x0006/0x000A）的全部目标操作数要么为 0（引用 L0000，合法）要么落在 chunk 4 内，无越界；export→rebuild 字节一致回归见 #96 回执。目标引用的悬空拒绝发生在重建的标签解析处（与 `randomJump` 同口径）；`startBattle` 式的导出期范围预检（`DecodeValidatesReferenceRange`）本批不启用。

#### M5 批2 条目：条件跳转一（#97）

本批核对结论：**全部操作数为立即 WORD**（同批1，`pScript->rgwOperand[n]` 直读，无变量寻址）；10 个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（script.c:587 起，switch 于 650，default→3078 `TerminateOnError`），executor 一律 `shared`，两执行器行为一致；条件跳转统一经 `wScriptEntry = 目标操作数 − 1` 赋值 + 函数尾统一 `return wScriptEntry + 1`（script.c:3085）生效——目标 WORD 0 合法（引用 L0000，到达即停）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0041 | `markFailed`（#78 已具名，复核） | 无操作数映射 | shared（1623：`g_fScriptSuccess = FALSE`；该标记由 `PAL_RunTriggerScript` 每次调用开头置 TRUE（3189），供调用方检查脚本成败） | sequential | 无映射字段，三操作数保留源 WORD | 4 |
| 0x004D | `waitForAnyKey` | 无操作数映射 | shared（1753：`PAL_WaitForKey(0)` 无超时阻塞；参考实现实际只接受 KeySearch/KeyMenu（[play.c](../../reference/sdlpal/play.c):641 `fAllowAnyKey=FALSE`），真正任意键的 `PAL_WaitForAnyKey` 未被本 case 调用——动词沿用 script.c 注释，实际按键集合记录在案） | sequential | 三操作数保留源 WORD | 0 |
| 0x004E | `requestReload`（#78 已具名，复核修正） | 无操作数映射 | shared（1760：`PAL_FadeOut(1)`+`PAL_ReloadInNextTick(bCurrentSaveSlot)`（1764-1765）后直接 `return 0`（1766）——不回到 `wScriptEntry+1`，trigger 循环因入口 0 退出且保存入口不变，auto 返回 0） | **stop**（#78 原声明 sequential 有误，本批修正） | 三操作数保留源 WORD | 1 |
| 0x0058 | `itemShortageJump` | `itemId`=operand0（object.json item 视图下标，`PAL_GetItemAmount` 按 rgInventory.wItem 精确比对）、`count`=operand1（**有符号**：1864 `(SHORT)(rgwOperand[1])` 比较；库存持有量 0..65535 恒非负，负阈值永不触发）、`to`=operand2 | shared（1859：持有量 < count 时 `wScriptEntry = to − 1`（1866）） | conditionalJump（to 或 next） | 三字段均重编码（itemId/count 由值、to 解析标签）；无保留操作数 | 9（itemId 81..283；count 8 个 1、1 个 36；目标全在范围内） |
| 0x005D | `playerLacksPoisonJump` | `objectId`=operand0（**object.json poison 视图**下标：与运行时 rgPoisonStatus[].wPoisonID 精确相等比较，该状态值即对象下标）、`to`=operand1 | shared（1914：`PAL_IsPlayerPoisonedByKind(wEventObjectID, objectId)`（1918）为假时跳（1920）——wEventObjectID 此处按玩家角色解释） | conditionalJump | 两字段重编码；operand2 保留 | 12（objectId 全部落在 555..560，即 data-schema 毒对象区间 551..564 内——该区间冻结的「脚本引用」证据源） |
| 0x005E | `enemyLacksPoisonJump` | `objectId`=operand0（同上，比较对象为 `g_Battle.rgEnemy[wEventObjectID].rgPoisons[].wPoisonID`）、`to`=operand1 | shared（1924：扫描 MAX_POISONS 槽无该毒对象即跳（1938）） | conditionalJump | 同 0x005D | 6（objectId 555..560 各 1） |
| 0x0061 | `notPoisonedJump` | `to`=operand0 | shared（1957：`PAL_IsPlayerPoisonedByLevel(wEventObjectID, 0)`（1961）为假时跳（1963）——任意非空、等级<99 的毒即算中毒） | conditionalJump | `to` 重编码；operand1、2 保留 | 2（目标均为 39505） |
| 0x0064 | `enemyHpAboveJump` | `percent`=operand0（**无符号** WORD：1987-1989 `当前 HP×100 > 敌表最大 HP×operand0`，乘子无 SHORT 转换，语义为百分比阈值）、`to`=operand1 | shared（1983：以 `rgEnemy[wEventObjectID].wObjectID` 查对象表 wEnemyID 再查敌表最大 HP） | conditionalJump | 两字段重编码；operand2 保留 | 2（percent 为 5/25；目标在范围内） |
| 0x0068 | `enemyTurnJump` | `to`=operand0 | shared（2025：`g_Battle.fEnemyMoving`（2029）为真时跳（2031）） | conditionalJump | `to` 重编码；operand1、2 保留 | 9（目标全在范围内） |
| 0x0074 | `partyHpNotFullJump` | `to`=operand0 | shared（2149：遍历在队队员，任一 `HP < MaxHP`（2156）即跳（2158）） | conditionalJump | `to` 重编码；operand1、2 保留 | 3（目标 2008×2、3320） |

真实数据核对：七个跳转类 opcode（0x0058/0x005D/0x005E/0x0061/0x0064/0x0068/0x0074）的全部目标操作数非零且落在 chunk 4（43538 条）内；export→rebuild 字节一致回归见 #97 回执。0x005D/0x005E 的 `objectId` 全部落在 555..560，正是 [data-schema](data-schema.md) poison 视图冻结的「脚本引用」证据首源，但把该证据接入 export-data 视图判定属后续票，本批不改变 object.json 导出；0x0058 的 `itemId` 含 281..283，超出 #81 item 证据并集 61..278 的上沿，同属后续视图判定票的输入。悬空拒绝仍在重建标签解析处（与批1同口径），本批不启用导出期范围预检。

#### M5 批3 条目：条件跳转二（#98）

本批核对结论：**全部操作数为立即 WORD**（同批1/批2，`pScript->rgwOperand[n]` 直读，无变量寻址）；10 个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger/auto 开关均无对应 case，executor 一律 `shared`，两执行器行为一致）；条件跳转统一经 `wScriptEntry = 目标操作数 − 1` + 函数尾 `return wScriptEntry + 1`（script.c:3085）生效，目标 WORD 0 合法（引用 L0000）。两处例外形状：0x0081/0x0083 跳转时同时置 `g_fScriptSuccess = FALSE`（跳转即标记脚本失败）；0x00A0 直接 `PAL_Shutdown(0)` 进程退出不返回；0x00A2 相对当前条目随机推进。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0079 | `roleInPartyJump` | `roleNameWord`=operand0（比较对象是 `PlayerRoles.rgwName[在队角色]` 的名字 WORD（WORD.DAT 名字索引，**不是角色下标 0..5**；真实数据操作数为 37/38/39/41）、`to`=operand1 | shared（2230：2234-2242 遍历在队成员，任一 rgwName 全等命中即 2239 `wScriptEntry = to − 1`） | conditionalJump（to 或 next） | 两字段重编码；operand2 保留 | 8（name 37×4/38/39×2/41；目标 6349..41473 全在范围内） |
| 0x0081 | `notFacingObjectJump` | `eventObjectId`=operand0（1 基全局 event object 下标；2395-2396 先验落在当前场景对象区间，否则直接跳）、`zoneUnits`=operand1（无符号 WORD 乘子，2419 `|dx|+2|dy| < units×32+16` 且对象 `sState>0` 才算面向；命中且 zoneUnits>0 时 2426 把该对象 `wTriggerMode = kTriggerTouchNormal + zoneUnits`，供下一帧触发）、`to`=operand2 | shared（2390：不在场景（2401）、不在面向区域或状态非正（2431）即跳并 2402/2432 置 `g_fScriptSuccess = FALSE`） | conditionalJump | 三字段重编码 | 26（对象 35..4725；zoneUnits 0×5/1×20/2×1；目标 38780..39824） |
| 0x0083 | `objectOutOfZoneJump` | `eventObjectId`=operand0（同 0x0081 的场景区间先验）、`zoneUnits`=operand1（出界判定 2466 `|dx|+2|dy| ≥ units×32+16`，与 0x0081 方向相反；2463-2464 的距离是调用者对象 `pEvtObj` 与 operand0 选定对象 `pCurrent` 之差）、`to`=operand2 | shared（2448：不在场景（2458）或出界（2468）即跳并 2459/2469 置脚本失败标记） | conditionalJump | 三字段重编码 | 2（zoneUnits 均 0；目标 10423/31286） |
| 0x0085 | `delay` | `delayUnits`=operand0 | shared（2511：2515 `UTIL_Delay(units×80 ms)` 纯墙钟阻塞，仅泵事件循环，**不跑 `PAL_GameUpdate`/场景重绘/手势更新**——与 0x0009 waitUpdate 的逐帧游戏更新语义相对，两者不可互换） | sequential | delayUnits 重编码；operand1、2 保留 | 10（units 4..30） |
| 0x0086 | `itemNotEquippedJump` | `itemId`=operand0（object.json item 视图：2530 与 `rgwEquipment[x][role]` 精确比对）、`count`=operand1（**无符号**：2536 `y < rgwOperand[1]` 无 SHORT 转换，y 为 int；全队六装备槽累计装备数 < count 即跳；2522-2523 运行时把 count 0 **原地改写为 1**（仅改内存中的脚本条目，不落盘，编解码仍按 0 编码））、`to`=operand2 | shared（2518：2525-2535 统计全队装备数） | conditionalJump | 三字段重编码（count 0 仍按 0 编码，运行时归一） | 1（itemId 274、count 0（运行时按 1）、目标 11991） |
| 0x0091 | `notFirstOfKindJump` | `to`=operand0 | shared（2615：2622-2633 仅在 `fInBattle` 时按 `wEventObjectID` 为战斗敌位扫描同 `wObjectID` 敌人统计自身位次；2634 `self_pos > 1` 即 2635 跳——**非同类首个敌人才跳**，非战斗时 self_pos=0 永不跳） | conditionalJump | `to` 重编码；operand1、2 保留 | 5（目标全为 WORD 0 → L0000，合法） |
| 0x0094 | `stateMatchJump` | `eventObjectId`=operand0（共享前奏选 `pCurrent`：0/0xFFFF=调用者，否则 1 基下标，含 Dream 2.11 的 `>0x9000` 回绕 hack，与 setState 族同口径）、`state`=operand1（**有符号**：2679 `(SHORT)` 与 `sState` 比较）、`to`=operand2 | shared（2675：状态相等即 2681 跳——正向条件，与负向跳转族相反） | conditionalJump | 三字段重编码 | 26（state 0×8/2×18；目标 0×16、16475、24840×7、27506、27510） |
| 0x0095 | `sceneMatchJump` | `sceneId`=operand0（与 `wNumScene` 全等，WORD 比较）、`to`=operand1 | shared（2685：2689 相等即 2691 跳） | conditionalJump | 两字段重编码；operand2 保留 | 1（sceneId 179、目标 39827） |
| 0x00A0 | `quitGame` | 无操作数映射 | shared（2990：2994-2995 WIN95 配置先 `PAL_EndingScreen()`；2996 `PAL_AdditionalCredits()`；2997 `PAL_Shutdown(0)` 进程退出——脚本永不返回，后继为 stop） | stop | 三操作数保留源 WORD | 1 |
| 0x00A2 | `randomBranch` | `count`=operand0 | shared（3018：3022 `wScriptEntry += RandomLong(0, count−1)`，经函数尾 return+1 实际执行到 **current+1..current+count 之一**；后续去向由这些条目（通常是 goto）自身决定。票面预想的「地址数组起始偏移」不成立：操作数是**分支数**，目标不在操作数内，逐个读自 wScriptEntry 相对位置；与 0x0006 randomJump 的差异：randomJump 是「率 + 单一目标」，本 opcode 无率无目标操作数，多路性来自后续条目序列。count 0 或 1 时 `RandomLong(0,−1/0)` 返回 0（util.c:237 `to<=from` 直接返回 from），退化为顺序后继） | **randomBranch**（本批新增后继声明：后续 count 条之一） | count 重编码；operand1、2 保留 | 2（count 4 与 14；span 42913..42916、42954..42967 均在 43538 条数组内） |

真实数据核对：六个显式目标跳转类 opcode（0x0079/0x0081/0x0083/0x0086/0x0094/0x0095）的全部目标操作数要么为 0（引用 L0000，合法）要么落在 chunk 4（43538 条）内；0x0091 全部 5 个目标恰为 0；0x00A2 的两个分支 span 均在数组内。raw 命中从 #97 的 7142 降至 7060（−82 = 本批 10 个新具名 opcode 命中之和）。export→rebuild 字节一致回归见 #98 回执。框架最小扩展：`OpcodeSuccessor` 新增 `randomBranch` 成员（纯声明，不参与 decode/validate/encode 路径）；字段模型无需扩展，word/signedWord/reference 足以表达本批全部语义。引用字段命名遵守冻结约定：`eventObjectId`（event-object.json，0x0081/0x0083/0x0094）、`itemId`（object.json item 视图，0x0086）、`sceneId`（scene.json，0x0095）；`roleNameWord` 为本批新定名——操作数比较的是 rgwName 名字 WORD（player-roles.json 的 name 列值，WORD.DAT 索引），不是角色下标，真实数据操作数 37/38/39/41 亦非 0..5；`zoneUnits` 亦为新定名（32 像素为 1 单位的区域半径）。

#### M5 批4 条目：对话表现（#99）

本批核对结论：全部操作数仍为立即 WORD（同批1–3，`pScript->rgwOperand[n]` 直读，无变量寻址）；8 个 opcode 中 6 个为既有具名复核、2 个新具名（0x008E restoreScreen、0x00A6 backupScreen）。拓扑：0x0005/0x003B–0x003E/0x008E 位于 `PAL_RunTriggerScript` 的 trigger switch（executor `triggerSwitchOnly`；auto 执行到这些 opcode 经 default 委托 `PAL_InterpretInstruction`，两者均无对应 case → 3078 `TerminateOnError`）；0x00A6 位于共享 `PAL_InterpretInstruction`（executor `shared`）；0xFFFF 两 switch 各有 case 且行为不同（executor `bothSwitchesDiffer` 维持）。**复核修正一处：0x0005 redraw 的延迟操作数是 operand 1 而非 #78 原绑定的 operand 0**——script.c:3295 `UTIL_Delay((operand1 == 0) ? 60 : (operand1 * 60))`，operand 2 是重绘前更新队伍手势的开关（3287），operand 0 整个 case 未使用；修正后 `delayUnits` 重绑 operand 1、新增 `updateGestures`(word,2)，operand 0 保留源 WORD（真实数据 operand 0 全 0）。`rngWord` 语义核实：参考对 operand 2 做整 WORD 非零测试（3397/3407/3417 `? TRUE : FALSE`），非零且头像非零时 `PAL_StartDialog` 先 `VIDEO_BackupScreen` 再播放 RGM.MKF 动态头像（[text.c](../../reference/sdlpal/text.c):1271-1274），屏幕由 redraw 的 `PAL_DialogIsPlayingRNG` 分支（3275-3277）或 0x008E 恢复——编码保留完整 WORD 的既有决定维持不变。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0005 | `redraw`（#78 已具名，**复核修正**） | `delayUnits`=operand1（×60 ms 墙钟延迟；0 运行时按 60 ms 计，仅表达式内归一，编码仍按 0）、`updateGestures`=operand2（非零才重绘前 `PAL_UpdatePartyGestures(FALSE)`，真值 WORD 同 waitUpdate 的 0/0xFFFF 形状）；operand0 参考 case 全程未用，保留源 WORD | triggerSwitchOnly（3269：3273 先 `PAL_ClearDialog(TRUE)`；3275 `PAL_DialogIsPlayingRNG()` 为真则 3277 `VIDEO_RestoreScreen`（RNG 对话启动时备份的屏幕由此恢复）；3279 战斗中 `PAL_BattleMakeScene`+整屏拷贝；否则 3287-3289 手势更新、3292-3294 `PAL_MakeScene`+`VIDEO_UpdateScreen`、3295 延迟；3298 `wScriptEntry++`） | sequential | delayUnits/updateGestures 由字段值重编码；operand0 保留 | 2673（2657 条全零；2×5、3×6、5×1；updateGestures=0xFFFF×4；operand0 全 0 佐证其未用） |
| 0x003B | `setDialog`（#78 已具名，复核） | `color`=operand0 低字节（`(BYTE)` 转型，高位保留）、`rngWord`=operand2；operand1 保留（`PAL_StartDialog` 头像参数硬传 0） | triggerSwitchOnly（3391：3395 清对话、3396-3397 `PAL_StartDialog(kDialogCenter, (BYTE)operand0, 0, operand2 ? TRUE : FALSE)`；3398 推进） | sequential | color 低字节重编码保高位、rngWord 重编码；operand1 保留 | 112（rngWord 全 0、颜色高位全 0） |
| 0x003C | `setDialog`（复核） | `portraitId`=operand0（RGM.MKF 头像 chunk 下标）、`color`=operand1 低字节、`rngWord`=operand2 | triggerSwitchOnly（3401：3406-3407 `PAL_StartDialog(kDialogUpper, (BYTE)operand1, operand0, operand2 ? TRUE : FALSE)`；3408 推进） | sequential | 三字段重编码；无保留操作数 | 1307（portraitId 非零 985；rngWord 非零 4） |
| 0x003D | `setDialog`（复核） | 同 0x003C 布局，`kDialogLower` | triggerSwitchOnly（3411：3416-3417；3418 推进） | sequential | 同 0x003C | 1544（portraitId 非零 1246；rngWord 非零 3） |
| 0x003E | `setDialog`（复核） | `color`=operand0 低字节；头像硬 0、`fPlayingRNG` 硬 FALSE，operand1、2 保留 | triggerSwitchOnly（3421：3426 `PAL_StartDialog(kDialogCenterWindow, (BYTE)operand0, 0, FALSE)`；3427 推进） | sequential | color 低字节重编码保高位；operand1、2 保留 | 452（三操作数全 0） |
| 0x008E | `restoreScreen`（新） | 无操作数映射 | triggerSwitchOnly（3430：3434 `PAL_ClearDialog(TRUE)`、3435 `VIDEO_RestoreScreen(gpScreen)`、3436 `VIDEO_UpdateScreen(NULL)`、3437 推进；auto 无 case→InterpretInstruction 无 case→TerminateOnError。另注：`MESSAGE_GetSpan` 扫描消息块时把块内 0x008E 条目一并跨过（3125 循环条件含 `wOperation == 0x008E`）——restoreScreen 常嵌在消息块内，由跨度推进隐式消费而非逐条执行） | sequential | 三操作数保留源 WORD | 351（三操作数全 0） |
| 0x00A6 | `backupScreen`（新） | 无操作数映射 | shared（3071：3075 `VIDEO_BackupScreen(gpScreen)`、3077 break 后函数尾 3085 `return wScriptEntry + 1`；两执行器均经 default 委托到达） | sequential | 三操作数保留源 WORD | 0（真实数据零命中；备份由 text.c:1273 RNG 对话启动、战斗等其他非脚本调用点触发，不影响描述符声明） |
| 0xFFFF | `showDialog`（#78 已具名，复核） | `messageId`=operand0（原始消息引用；消息 0 在显示循环中触发清屏恢复 3455-3457）、`descriptionLineWord`=operand1（trigger 显示不用；WIN95 [itemmenu.c](../../reference/sdlpal/itemmenu.c):275 / [magicmenu.c](../../reference/sdlpal/magicmenu.c):197 描述循环按 `operand1 != 1 ? 1 : 0` 推进行号；本批真实数据全 0） | bothSwitchesDiffer（trigger 3440：`pszMsgFile` 配置下 3446 `MESSAGE_GetSpan` 跨度推进 + 3448-3461 `PAL_GetMsgNum` 逐条 `PAL_ShowDialogText`；无消息文件时 3464 单条显示 + 3466 推进。auto 3609：WIN95 下 3612-3614 按 `wEventObjectID`（`iDescLine = wEventObjectID & ~PAL_ITEM_DESC_BOTTOM`）算描述行位、3618 `MESSAGE_GetSpan` + 3620-3627 `PAL_DrawText` 直绘描述行；非 `pszMsgFile` 时 3631 单条直绘 + 3632 推进；非 WIN95 3637 仅推进） | **messageSpan**（bothSwitchesDiffer 依据复核：trigger 是玩家确认式对话框、auto 是 WIN95 描述行直绘，显示语义不同；但两者的 `pszMsgFile` 路径都用 `MESSAGE_GetSpan` 跨度推进，后继声明按跨度口径维持） | messageId/descriptionLineWord 重编码；operand2 保留 | 13514（messageId=0 ×1（清屏恢复哨兵）、operand1/operand2 全 0） |

真实数据核对：本批命中 redraw 2673 + setDialog 四变体 3415（112+1307+1544+452，与 #78 的 3415 总数吻合）+ restoreScreen 351 + backupScreen 0 + showDialog 13514，共 19953。raw 命中从 #98 的 7060 降至 6709（−351 = 0x008E 新具名命中；0x00A6 零命中；六个复核项本已具名，无 raw 变化）。export→rebuild SHA `f36cf853…b39e72a` 字节一致（0x0005 字段重绑不破坏往返：映射操作数 1/2 由字段重编码回原 WORD、operand0 保留源值）。本批无引用字段，无悬空拒绝面变化；`DecodeValidatesReferenceRange` 导出期预检不启用。

#### M5 状态物品批1 条目（#100）

本批核对结论：全部操作数仍为立即 WORD（同批1–4，`pScript->rgwOperand[n]` 直读，无变量寻址）；10 个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger/auto 开关均无对应 case，executor 一律 `shared`），后继除 changeCash/removeItem 两跳转类为 conditionalJump 外一律 sequential（break 后函数尾 3085 `return wScriptEntry + 1`）。**「player/role」类操作数语义逐条核实**：0x0017/0x0018 的目标角色是 `wEventObjectID` 本身（装备脚本由 [uigame.c](../../reference/sdlpal/uigame.c):2048-2050 以 `PAL_RunTriggerScript(wScriptOnEquip, wPlayerRole)` 进入，事件对象参数即 0 基玩家角色下标，763/778 直接用作行内偏移、无 −1）；0x0019/0x001A 的 operand 2 是与 0x0030 boostStat 相同的 roleOperand 形状（822-826/855-859：0=调用者角色、非 0=operand−1 的 1 基角色下标，真实数据仅 0/2）；0x001B/0x001C/0x001D 的 operand 0 是整 WORD 非零即全队的布尔旗标（874/903/932 `if (pScript->rgwOperand[0])`，真实数据仅 0/1），**不是位掩码、角色 ID，也不是 #98 发现的 rgwName 名字 WORD**。属性编号语义：0x0017 operand 1 与 0x0019/0x001A operand 0 是 PLAYERROLES 展平 WORD 数组的**行号**（每行 MAX_PLAYER_ROLES=6 个 WORD，763/829/862 按 `field×6 + role` 寻址），字段名沿用 0x0030 的 `field` 表达索引语义、不硬编码枚举名；行号映射：0=avatar、1=battleSprite、2=sprite、3=name、4=attackAll、6=level、7=maxHP、8=maxMP、9=HP、10=MP、11–16=rgwEquipment 各装备部位行、17=attackStrength、18=magicStrength、19=defense、20=dexterity、21=fleeRate、22=poisonResistance、23–27=elementalResistance[0..4]、64=walkFrames、65=cooperativeMagic（真实数据：0x0017 用 17–27、0x0019 用 {6,7,8,17–21}、0x001A 用 {0,1,2,4,64,65}）。0x0017/0x0018 的 operand 0 是 `0x0B + 装备部位`（759/772 减 0x0B 后索引 rgEquipmentEffect[7]/rgwEquipment[6]，0x0B..0x11 覆盖 head/body/shoulder/hand/feet/wear/extra），定名 `equipPartWord` 表达原始 WORD 编码而非部位枚举值。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0017 | `setExtraAttr` | `equipPartWord`=operand0（0x0B+装备部位：0x0B head…0x10 wear、0x11 extra；759 减 0x0B 选 `rgEquipmentEffect[part]`，extra 寄存器即 0x0030 boostStat 的写入目标）、`field`=operand1（PLAYERROLES 行号，763 `field×6 + wEventObjectID` 寻址——角色即调用者事件对象参数）、`value`=operand2（**有符号**：763-764 `(SHORT)` 赋值） | shared（752：纯赋值写入装备效果寄存器，break；3085 return+1） | sequential | 三字段重编码；无保留操作数 | 213（equipPartWord 全部 0x0B..0x11；field 17–27 即攻击/灵力/防御/身法/逃跑/抗毒/五行抗性行；value −20..200，如 65526=−10） |
| 0x0018 | `equipItem` | `equipPartWord`=operand0（772 减 0x0B 选 `rgwEquipment[part]` 并 773 记 `g_iCurEquipPart` 供后续 0x001A）、`itemId`=operand1（object.json item 视图：777/781 与 rgwEquipment 及库存比对，含新旧各 1 件时直接换装不走库存尾插入）；目标角色=wEventObjectID（778，玩家角色下标） | shared（768：778 先 `PAL_RemoveEquipmentEffect` 清旧效果，781-810 换装并调库存与 `wLastUnequippedItem`；break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 106（equipPartWord 0x0B..0x10；itemId 163..274 逐条互异；operand2 全 0） |
| 0x0019 | `incPlayerAttr` | `field`=operand0（PLAYERROLES 行号，829 `field×6 + iPlayerRole`）、`delta`=operand1（**有符号**：829 `(SHORT)` 加）、`roleOperand`=operand2（822-826：0=调用者角色=wEventObjectID、非 0=operand−1 的 1 基角色下标；命名沿用 0x0030 同形状字段） | shared（813：829 `p[...] +=` 直接改基面板；break） | sequential | 三字段重编码；无保留操作数 | 17（field {6,7,8,17–21}=level/maxHP/maxMP/攻击/灵力/防御/身法/逃跑；delta 2..190；roleOperand 0×9、2×8） |
| 0x001A | `setPlayerStat` | `field`=operand0（同 0x0019 行号语义）、`value`=operand1（**有符号**：862 `(SHORT)` 赋值）、`roleOperand`=operand2（855-859 同形状） | shared（834：844-846 **装备进行中**（`g_iCurEquipPart != -1`，由 0x0018 置位）时写入 `rgEquipmentEffect[part]` 寄存器而非 PlayerRoles 基面板——装备脚本先 0x0018 后 0x001A 的组合即装备效果来源；862 赋值 break） | sequential | 三字段重编码；无保留操作数 | 30（field {0,1,2,4,64,65}=avatar/battleSprite/sprite/attackAll/walkFrames/cooperativeMagic；value 1..512；roleOperand 0/2） |
| 0x001B | `changeHp`（#78 已具名，复核） | `scope`=operand0（**布尔旗标**：非 0=全队 874-882、0=单人=wEventObjectID 玩家角色 889；非位掩码/角色下标/名字 WORD，真实数据仅 0/1）、`amount`=operand1（**有符号**：880/889 `(SHORT)`） | shared（867：`PAL_IncreaseHPMP(role, amount, 0)` 只改活人并按上限封顶；scope≠0 时任一队员实际变化才置脚本成功标记，不影响后继） | sequential | 两字段重编码；operand2 保留（映射复核无误） | 40（scope 0×38、1×2） |
| 0x001C | `changeMp`（#78 已具名，复核） | 同 0x001B，作用于 MP（908/916） | shared（896） | sequential | 同 0x001B | 10（scope 0×9、1×1） |
| 0x001D | `changeHpMp`（#78 已具名，复核） | 同 0x001B，operand1 同值同时施于 HP 与 MP（935-937/944-945） | shared（923） | sequential | 同 0x001B | 25（scope 0×9、1×16） |
| 0x001E | `changeCash` | `amount`=operand0（**有符号**：957/966 `(SHORT)`，负值即扣钱）、`to`=operand1（现金不足时的跳转目标：962 `wScriptEntry = to − 1` 标准跳转惯用法 + 3085 return+1；目标 WORD 0 合法引用 L0000，与批2/批3 跳转族同口径） | shared（952：957 负数且 `dwCash < |amount|`（无符号比较）即 962 跳，否则 966 `dwCash += (SHORT)amount`；break） | conditionalJump（现金不足→to，否则 next） | 两字段重编码；operand2 保留（参考 case 未用） | 59（amount 负 19/非负 40；目标 0×42、非零 17 个（3742..43064）全在 43538 条内） |
| 0x001F | `addItem`（v1 契约占位名 `giveItem`） | `itemId`=operand0（object.json item 视图）、`count`=operand1（**有符号**：974 `(SHORT)`，负值即从库存移除） | shared（970：974 `PAL_AddItemToInventory(itemId, count)`；break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 425（itemId 61..344 共 233 种；count 0/2/3/5/7/9；operand2 全 0） |
| 0x0020 | `removeItem` | `itemId`=operand0（object.json item 视图）、`count`=operand1（**无符号**：981 WORD 无 `(SHORT)` 转型；983-984 运行时把 0 归一为 1 仅改内存条目，编解码仍按 0——与 0x0086 同口径）、`shortageTarget`=operand2（**可空引用**复用 battleTarget 字段类型：null↔WORD 0；986 条件 `count <= 库存数 || operand2 == 0`——WORD 0 表示「不足也照删」，非 0 时库存+装备合计不足才 1023 `wScriptEntry = target − 1` 跳转；引用地址 0 拒绝、必须用 null） | shared（977：988 先删库存（部分删除时 y<0 余量回填 x），1003-1016 库存不够再卸全队装备槽凑数；break） | conditionalJump（不足且目标非空→shortageTarget，否则 next） | 三字段重编码（count 0 仍按 0 编码、shortageTarget null 编码 WORD 0） | 46（itemId 76..292；count 0/1/9/36；shortageTarget null×33、非空 13 个（6417..43078）全在范围内） |

真实数据核对：本批命中合计 971（新具名 896 = 0x0017 213 + 0x0018 106 + 0x0019 17 + 0x001A 30 + 0x001E 59 + 0x001F 425 + 0x0020 46；复核 75 = 0x001B 40 + 0x001C 10 + 0x001D 25 本已具名）。raw 命中从 #99 的 6709 降至 5813（−896）。0x001E 的 17 个非零跳转目标与 0x0020 的 13 个非空 shortageTarget 全部落在 chunk 4（43538 条）内；两者的 WORD 0 目标编码语义不同——0x001E 按普通引用 L0000（跳到条目 0，与 randomJump 零目标同口径），0x0020 按 null（不跳转、照删），互不混用。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`equipPartWord`（0x0B+部位原始 WORD）与 `roleOperand` 沿用既有 word 类型；`shortageTarget` 是 battleTarget 可空引用绑定在跳转类 opcode 的首次复用（WORD 0=非跳转语义而非「跳到 0」，null↔0 编码与 startBattle 的 loss/flee target 同机制，字段类型描述本就是通用可空引用）；`DecodeValidatesReferenceRange` 导出期预检不启用（与批1–4 同口径，悬空拒绝仍在重建标签解析处）。

#### raw 保底与反向编码规则

- 未登记 opcode 一律 `raw`，仅携带公共字段；已登记 opcode 不得用 `raw`（唯一例外：兼容旧 M2/M3 包的 raw opcode 7 仍可无损重建，其操作数不按引用解释，见 startBattle 章节）。
- 反向编码以 `source.opcode` 与三个源操作数为底，**全部映射字段重新编码**；未映射操作数原样保留；`lowByte` 字段保留源 WORD 高位；动词与源 opcode 不匹配时拒绝。
- 单元层的反例测试骨架（编辑语义字段 → 产物字节必须不同于源字节且等于手工重算值）作为后续每批 opcode 票的强制样板。

### `startBattle` 的完整双向映射（M4 codec 检查点）

依据固定 [script.c](../../reference/sdlpal/script.c) 的 `PAL_RunTriggerScript` / `case 0x0007` 与 [battle.c](../../reference/sdlpal/battle.c) 的 `PAL_StartBattle(WORD wEnemyTeam, BOOL fIsBoss)`：

- operand 0 为敌队编号，完整映射 `enemyTeamId: 0..65535`，本层不假设敌队资源存在（#78 起由 `teamId` 更名，指向 enemy-team.json）。
- operand 1 为 0 时 `lossTarget: null`，Lost 也走 `next`；非零时为 Lost 的标签目标。
- operand 2 为 0 时 `isBoss: true`、`fleeTarget: null`，传入 boss 参数，正常逃跑不允许成功；非零时 `isBoss: false`、`fleeTarget` 为该地址，Fled（参考枚举拼写 `kBattleResultFleed`）跳到该目标。`isBoss` 必须恰好等价于 `fleeTarget` 为空，不允许互相矛盾的两个独立设置。
- Won 或其他未命中上述条件的返回结果均走 `next`；参考同时清除 `fAutoBattle`，本指令不改写返回保存入口。`PAL_StartBattle` 自身处理真实战斗、Won 结算及公共清理后返回结果，不能由 codec 模拟这些副作用。
- 非空 `lossTarget` / `fleeTarget` 必须按本文件或 `file#label` 解析到集合内**非零**地址。引用下标 0 会与原始零值特殊含义冲突，故拒绝，必须用 `null`。`0xFFFF` 在本指令没有 sentinel 含义：enemyTeamId 可为 65535，结果目标 65535 仅在完整 65536 条集合中引用实际命令。
- 三个 WORD 全部由语义字段重新编码：`enemyTeamId`、已解析的 loss 地址或 0、已解析的 flee 地址或 0。`source.operands` 完整保留作来源诊断，但不能覆盖编辑后的字段；`next` 仍严格遵守完整数组的 WORD 顺序后继，包括回绕和真末端 `null`。即使真末端也校验所有结果引用。
- 新导出总是使用具名 `startBattle`。为兼容此前合法 M2/M3 完整包，旧 `raw` opcode 7 仍可严格仅携带公共字段，离线无损重建及 Core 集合加载；其原始操作数不按引用解析。此例外不放松 `0x0000` / `0x003C` / `0xFFFF` 必须具名的既有规则，也不为运行时提供 raw 执行通道，不要求迁移旧资源或改变 v1/profile。
- Core 已追加显式 `TriggerBattleContent` + `IBattleRandom` 的 trigger 战斗 binding 检查点：具名 `startBattle` 暂停于准确 active `ClassicBattleLifecycleSession`，真实结算完成且匹配回执后 Won 走 next、Lost 走 lossTarget（null 则 next），不改原 ReturnEntry。详见[正式接口及支持域](classic-battle-session.md#trigger-事件binding46-有界检查点)。原有无战斗配置的构造器仍在副作用前拒绝 `startBattle`，所有构造器都拒绝执行 `raw`。
- 能力预检覆盖选中入口的 next、lossTarget、编码的 fleeTarget 完整保守闭包（包括跨文件与循环），至 end(keep)/零地址停止；不因当前只会产生 Won/Lost 而忽略 fleeTarget。预检使用纯数据验证与独立语言 preview，不创建预演战斗或消耗生产随机值。auto 没有获得战斗执行语义；逃跑动作/结果仍属 #51，Main、native 准备与表现尚未接通。

注意：

- `limit` 是对应空闲计数器的比较上限，**不是帧延迟**；0 的特殊语义保留。
- `0x0002` 在计数条件成立时停止/改入口，条件失败会清零计数并继续 `next`。因此不能把所有 `end` 当成无条件终止。
- `addItem.count`（v1 契约占位名 `giveItem`，#100 定名）可以为负；不把原始 `65535` 错读成增加 65535 个。`removeItem.count` 与之相反是无符号 WORD，且 0 在运行时按 1 处理（编码仍按 0）。
- `setDialog` 与 `showDialog` 分开保留，不能把前者吞并进后一条文字命令。对话框、头像、颜色及 RNG 状态是执行上下文；`rngWord` 非零表示真，但编码仍保留完整 WORD。`rngWord` 非零且头像非零时 `PAL_StartDialog` 先备份屏幕再播放 RGM.MKF 动态头像（text.c:1271-1274），该屏幕由 redraw 的 RNG 分支（script.c:3275-3277）或 0x008E 恢复（#99 核）。
- `showDialog.messageId` 是原始消息引用，不是某个语言包内部重新编号的字符串下标。同文不同 ID 仍是不同引用；反向编译使用 ID，而非按文字内容反查索引。正文及控制信息保存在语言包，保留原文、空行和转义，不 trim 或自行合并。
- `descriptionLineWord` 在 trigger 中不参与消息显示，在 WIN95 物品/法术描述调用方中用于行号推进：值 1 不增加行号，其他值增加一行。该行为见 [itemmenu.c](../../reference/sdlpal/itemmenu.c)、[magicmenu.c](../../reference/sdlpal/magicmenu.c)，不能误当成始终未使用的操作数。
- 语言包内容不参与 chunk 4 编码。编辑文字不会自动改写 `M.MSG`；脚本 chunk 的 round-trip 也不覆盖文字编码与消息偏移表的往返。
- 不在当前命令上接受无关语义字段，例如 `showDialog.box` 或旧草案的 `showDialog.text`。其他动作、条件、调用及修改入口指令须按最小切片补充注册，不能借用一个相似名字代替原版语义。

### 语言包与 SLF 来源

公共事件不按语言复制。当前数据包的 `m_chs.slf`、`m_eng.slf` 分别提供简体中文与英文，默认使用简体中文；离线工具将它们转成现代语言包，运行时不解析 SLF，也不回头读取 `M.MSG`。替换语言配置不改变原始指令地址、消息 ID、剧情分支或可变入口。

SLF 不保证一条原始文字指令对应一行翻译。参考 `text.c` 的 `PAL_ReadMessageFile`、`PAL_GetMsgNum` 与 `script.c` 的 `MESSAGE_GetSpan`，它按原始开始消息 ID 和跨度匹配消息块，块内可以有多行及 `[CLEAR MESSAGE]`。语言包须保留这些映射与各语言自己的显示控制；不能按翻译行数改写、复制或删除忠实转写的事件命令。

语言包文件格式及字段、消息块的执行位置衔接、trigger/auto 描述分支与清屏指令的具名映射在 M3 对应任务中定义并验证，不能仅靠本节就宣称运行时支持 SLF。名称、描述、字体与布局按参考来源处理；不额外定义对话中途热切换或跨语言逐字进度映射。

这一调整取代尚未实现的“在 `showDialog.text` 内嵌单一语言正文”草案。原始指令一对一保留和 chunk 4 往返范围不变；语言包的正确性需独立测试。

### 语言包现代格式 v1

`pal-extract extract-dialogue --input <原始资源目录> --reference <reference/sdlpal> --output <新建或空目录>` 是显式离线转换入口。它读取 `m_chs.slf`、`m_eng.slf`、`RGM.MKF` 的 chunk 59、`DATA.MKF` 的 chunk 12，以及固定参考中的 `ascii.h` / `fontglyph.h`；运行时不解析这些输入。输出包含简体中文、英文语言包、来源散列、索引图像资源与实际使用的 ISO/Unifont 字形位图。图像保留调色板索引和独立 alpha，由运行时的当前场景调色板着色；头像的 RGM chunk 是直接 RLE，提示图标的 DATA chunk 是带帧表的 RLE，两者使用各自正确的解码入口。

`index.json` 的 v1 字段为 `formatVersion: 1`、`defaultLanguage: "zh-Hans"`、排序后的 `languages: ["en", "zh-Hans"]` 与 `messageReferences: "original-start-message-id-and-span"`。每个语言文件 (`zh-Hans.json`、`en.json`) 含 `formatVersion: 1`、语言 ID、来源文件名及 SHA-256，以及按来源顺序排列的 `messages`。每个消息块记录原始 `startMessageId`、`endMessageId` 和按序 `entries`；`text` 项保留完整 UTF-8 字符串，包括空串、空白和 SDLPal 文本显示控制符，`clear` 项代表 `[CLEAR MESSAGE]`，不能折叠成空文本。块的跨度为 `endMessageId - startMessageId`，不是内容行数。不同语言的行数可不同，事件指令与 ID 不变。

游戏配置须显式选择 `zh-Hans` 或 `en`；默认简中。语言包按所选语言及原始 `(startMessageId, span)` 严格查找，缺失/重复/格式非法内容报错，不回退另一语言、不按文字内容寻找相同文本，也不在对话中热切换。人工数据应覆盖不同译文长度、块内 ID 入口、空行、CLEAR 及缺失资源。转换输出和真实来源不进入仓库或 CI；自动化只用人工消息内容和微型归档夹具。

现代语言包由 Godot 原生资源显式准备/导入，不应在运行时回读 SLF/M.MSG、M1 JSON 或执行 pal-extract。当前命令转换消息文本、目标头像/提示图及消息实际使用的 ISO/Unifont 字形；它不声称支持全游戏显示控制。不能以系统字体替代。新增显示行为仍按固定 SDLPal 参考独立核验。

### 完整事件文件示例

以下为人工构造的格式示例，不是已验证的游戏数据；清单及其他文件仍须补齐全局地址覆盖。

```json
{
  "formatVersion": 1,
  "profile": "faithful",
  "fileId": "scene-001",
  "entries": [
    {
      "id": "npc-1-trigger",
      "owner": { "type": "eventObject", "id": 1 },
      "slot": "wTriggerScript",
      "kind": "trigger",
      "target": "L0001",
      "sourceAddress": 1
    }
  ],
  "commands": [
    {
      "label": "L0001",
      "op": "setDialog",
      "box": "lower",
      "portraitId": 0,
      "color": 79,
      "rngWord": 0,
      "next": "L0002",
      "source": { "index": 1, "opcode": 61, "operands": [0, 79, 0] }
    },
    {
      "label": "L0002",
      "op": "showDialog",
      "messageId": 7,
      "descriptionLineWord": 13,
      "next": "L0003",
      "source": { "index": 2, "opcode": 65535, "operands": [7, 13, 29] }
    },
    {
      "label": "L0003",
      "op": "addItem",
      "itemId": 123,
      "count": -1,
      "_item": "示例物品名",
      "next": "L0004",
      "source": { "index": 3, "opcode": 31, "operands": [123, 65535, 9] }
    },
    {
      "label": "L0004",
      "op": "end",
      "mode": "keep",
      "next": null,
      "source": { "index": 4, "opcode": 0, "operands": [0, 0, 0] }
    }
  ]
}
```

跨文件跳转与原始兜底示例（独立命令片段，目标定义在其他示例之外）：

```json
[
  {
    "label": "L000A",
    "op": "goto",
    "to": "shared#L0014",
    "limit": 0,
    "next": "L000B",
    "source": { "index": 10, "opcode": 3, "operands": [20, 0, 42] }
  },
  {
    "label": "L000B",
    "op": "raw",
    "next": "shared#L000C",
    "source": { "index": 11, "opcode": 134, "operands": [0, 65535, 8] }
  }
]
```

## 5. 文件归属与可达性

入口从场景、事件对象及对象表中枚举，包含物品、法术、敌人、毒等相关脚本字段。可达性须覆盖顺序后继、显式跳转、条件分支、脚本调用、入口改写及两种执行方式下的继续位置；不能只沿 `goto` 切分。

- 只被单一场景关联的命令放入该场景文件。
- 只被非场景对象入口关联的命令放入 `objects`。
- 跨场景、跨所有者类别共享的命令，或无法可靠划分的命令，保守放入 `shared`。
- 未被追踪到的指令放入 `unassigned`，不视作可删除的死代码。
- 同一条指令只定义一次；入口引用与跳转可以跨文件。场景文件的入口数组负责定位 NPC，不靠重复复制命令形成“脚本块”。

遇到 `raw`、动态入口或尚不明白的控制流，不能声称可达性分析完整。可以先把无法确定归属的指令保守保留在 `shared`，后续再改善文件组织；**归属变化不改变原始地址、顺序后继和引用语义**。

最小切片运行时仍须加载所有被引用的文件，并在加载时验证引用完整性。不得把一个场景文件单独加载成功等同于完整资源集合可执行。

## 6. 运行时执行与状态

事件系统采用可暂停的步进方式保留 trigger 的阻塞语义，而不是把所有脚本都按“一 tick 一条”统一处理。

| 执行方式 | 约束 |
|---|---|
| trigger | 在一次调用内持续推进，遇到等待玩家、战斗、动画等命令暂停，完成后继续。当前执行位置与调用返回后保存的入口是两个不同状态；按具体 opcode 决定是否推进或改写入口 |
| auto | 通常每次场景更新推进一条命令；部分跳转在同一次调用内继续，等待指令可停留原位。调用 trigger、文字描述等操作保留参考分支，不做统一简化 |

需保留或显式建模：对象 `sState`、trigger/auto 可变入口、各自空闲计数器、调用者上下文、脚本成功标记、对话上下文以及等待/调用的继续位置。`0xFFFF` 在 WIN95 auto 中用于描述文字，并非总是玩家确认式对话；具名相同也须按执行方式区分行为。

剧情条件还依赖背包、队伍、金钱等游戏状态。不要把“没有独立的通用剧情标志数组”误写为“全部剧情状态只有对象状态和入口两项”；负数对象状态、消失时间及其他参考字段不能丢弃。当前忠实转写不额外发明通用 flags 来替代这些状态。

运行时可用每次推进的指令预算发现无限跳转；预算耗尽应报告错误，不能靠额外 yield 悄悄改变 auto 的更新节奏。具体调度接口、存档中的暂停状态及预算值在运行时规格中确定。

## 7. 可读名字

数字 ID、消息 ID 和引用是正式数据；`_item`、`_scene` 等仅为注释。名字来自已选定的文字来源：原始文字集合使用 `WORD.DAT`，当前 SLF 语言包使用其名称映射；有明确 ID 对应时才生成注释，不能假定每个场景或 NPC 都有名称。运行时显示名称从语言包解析，不依赖导出时生成的注释。

可选 `symbols.json` 使用分类、十进制 ID 字符串到名称的映射，例如：

```json
{ "scene": { "17": "客栈" }, "eventObject": { "42": "客栈伙计" } }
```

人工符号优先用于对应 ID 的展示注释，未命中时使用可核对的游戏名称或省略。重命名或删除注释不能改变引用、执行与反向编译结果。

## 8. 重建、错误处理与验收

### 离线重建规则

1. 校验版本、集合文件列表、字段类型和注册表映射。
2. 汇总所有命令，检查 `source.index` 恰好覆盖 `0..instructionCount-1`，不重复、不缺失；核对顺序后继、入口和引用。
3. 按地址排序。具名命令通过注册表反向编码；`raw` 使用保留的 opcode 与三个操作数。按四个小端 WORD 写回，不裁掉零值或未使用操作数。
4. 未编辑产物与原 chunk 4 比较长度、SHA-256 并**逐字节比对**。插入、删除或重编号不是忠实往返中的隐式操作，需另行定义重定位工具；本版拒绝用补零或重新排序掩盖缺失。
5. 语义字段被编辑后，编码采用新字段值，原 `source` 仅保留未映射信息与来源。此时可能不再等于原散列，应报告差异，而不是把原始字节复制回来掩盖编辑。

### 错误边界

- 离线转换对长度非 8 的倍数、重复地址、悬空引用、非法范围、缺失消息或无法可靠解码的文字明确失败并定位来源，不静默补零或替换乱码。
- 不知道含义的 opcode 可以原样保存为 `raw`，但不能猜测其操作数中哪些是地址。
- 运行时在执行前完成版本、字段、引用与命令能力检查；被选定入口可能执行的 `raw` 或未支持命令必须报告“不支持”，不得跳过。控制流无法证明安全时按保守闭包检查，不因它当前不在首条路径就判为可执行。
- 未被选定入口使用的归档命令可以保留而不执行；这不代表全量命令语义已实现。错误包含文件、标签及原始地址，便于追溯。

### 实施时必须补充的测试

- 包含下标 0、不可达指令、同文不同消息 ID、未使用非零操作数、有符号负值与共享片段的完整往返。
- 对具名字段、原始基线字段分别做修改，确认映射字段确实重编码、保留字段不丢失；仅改 `_` 注释或切换语言包，脚本 chunk 编码输出不变。
- 跨文件显式跳转、隐式顺序后继、WORD 顺序地址回绕、不同入口共享同一指令，以及零地址的上下文差异。
- 三种停止模式、`0x0002` 计数失败后继续、`0x0003` 的 limit、auto 跳转与等待、WIN95 描述文字。
- 中英文共享原始消息引用、不同文字行数和清屏位置、SLF 消息区间、从区间内部入口执行时的匹配、文字控制符及名称映射；缺失引用明确失败，不静默混用其他语言。
- 非法版本、重复键、未知字段、越界值、缺地址、重复地址、悬空引用与运行时不支持的命令。

**逐字节往返证明表示与转换保真，不证明事件执行语义忠实。** trigger/auto 的状态迁移、等待、分支和表现命令仍需独立测试并对照参考行为验证。文字/控制符、消息解码与渲染也有各自测试，不能由 chunk 4 比对代替。

## 9. 后续范围

- **M1/M2**：只提取静态场景资源并在 Godot 构建探索场景；原始脚本入口数值保留为来源数据，不要求事件转写、语言包或执行支持。
- **M3**：针对一个真实交互核对具名操作、消息映射和语言包契约，接入切片所需的事件执行、状态、等待回执及对话；明确不支持命令的失败路径，不预设 opcode 数量。
- **M4**：随战斗切片补充相关事件语义，不要求 M3 先完成所有战斗指令。
- **M5**：补齐全量转写、注册表和反向编译器，完成真实全局脚本数组的逐字节验证。
- **第二阶段**：为手写新内容定义结构化 profile、条件表达式、嵌套字段及执行语义。可以共享事件系统能力，但不宣称这些命令已被 v1 `faithful` 接受。
