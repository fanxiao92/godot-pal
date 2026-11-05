# events.json 契约

状态：**转换策略与 v1 契约已定义；Pal.Extract 的完整 chunk 4 导出/重建已实现基础离线子集。** 具名映射由 #78 冻结的表驱动 opcode 注册表承载（165 个 opcode 位、156 个动词：M5 批1 #96 基础控制流、批2 #97 条件跳转一、批3 #98 条件跳转二、批4 #99 对话表现、状态物品批1 #100、状态物品批2 #101、状态物品批3 #102（状态物品域 29 个 opcode 收官）、战斗批1 #103、战斗批2+菜单 #104（战斗域 22 个 opcode 收官、菜单域 2 个 opcode 收官）、场景移动批1 #105、场景移动批2 #106、场景移动批3 #107（移动子域 31 个 opcode 收官）、场景移动对象批1 #108、场景移动对象批2 #109（对象子域 13 个 opcode 收官）、场景移动屏幕批1 #110、场景移动屏幕批2 #111（屏幕子域 19 个 opcode 收官）、场景移动音乐场景特殊 #112（场景移动域 75 个 opcode 收官、165 个 opcode 全量收官），见第 4 节注册表章节）；#112 机器核对注册表位集合与 script.c case 清单双向差集为空，无 opcode 保留为 `raw`（`raw` 仅作为遗留/未知 opcode 的保底编码形态保留）。此离线能力不表示运行时支持或全游戏入口/可达性已核实。对话使用原始消息引用，正文由独立语言包提供；语言包格式与 SLF 显示语义的集成留到 M3 事件里程碑，阶段边界见 [总体路线](development-plan.md)。M1/M2 仅处理静态场景资源与构建，不要求转换或执行事件。全量 opcode 注册表、真实数据验证与运行时支持按里程碑补齐，不将本文描述当成已存在的工具能力。

原版剧情、触发与 NPC 行为由 `SSS.MKF` 中的脚本驱动。离线工具将其转为 `data/extracted/events/` 中的可读 JSON，运行时只读取现代资源，不解析 MKF。背景见 [架构设计](architecture.md)、[决策索引](decisions.md) 和 [忠实转写决策](../adr/0001-faithful-event-transcription.md)。术语见 [术语表](../../GLOSSARY.md)。

## 当前 Pal.Extract 命令子集

```text
pal-extract export-events --input <原始资源目录> --output <新建或空目录>
pal-extract rebuild-events --input <events目录> --output <新文件>
```

`export-events` 只读 `SSS.MKF` 的 chunk 0（32-byte `EVENTOBJECT` 数组）、chunk 1（8-byte `SCENE` 数组）及 chunk 4（8-byte `SCRIPTENTRY` 数组）。它完整转写 chunk 4 到 `shared.json`，全部命令保留原地址、顺序后继及三个 WORD 操作数；当前场景和场景对象的非零 trigger/auto 根也被枚举为入口。由于尚未核实足以安全划分的完整可达性，全部命令保守放在 `shared`，不声称对象表 union 根或全局可达性已完成分类。

实现的具名映射由[opcode 注册表](#opcode-注册表表驱动78-冻结)的 165 条描述符承载（`end` 三变体（keep/advance/redirect，#96）、`goto`（#96）、`call`（#96）、`advanceEntry`（#96）、`confirmNoJump`（#96）、`itemShortageJump`（#97）、`playerLacksPoisonJump`（#97）、`enemyLacksPoisonJump`（#97）、`notPoisonedJump`（#97）、`enemyHpAboveJump`（#97）、`enemyTurnJump`（#97）、`partyHpNotFullJump`（#97）、`waitForAnyKey`（#97）、`roleInPartyJump`（#98）、`notFacingObjectJump`（#98）、`objectOutOfZoneJump`（#98）、`delay`（#98）、`itemNotEquippedJump`（#98）、`notFirstOfKindJump`（#98）、`stateMatchJump`（#98）、`sceneMatchJump`（#98）、`quitGame`（#98）、`randomBranch`（#98）、`restoreScreen`（#99）、`backupScreen`（#99）、`setExtraAttr`（#100）、`equipItem`（#100）、`incPlayerAttr`（#100）、`setPlayerStat`（#100）、`changeCash`（#100）、`addItem`（#100）、`removeItem`（#100）、`removeEquipment`（#101）、`applyPoisonToEnemy`（#101）、`applyPoisonToPlayer`（#101）、`curePoisonByObjectIdForEnemy`（#101）、`curePoisonByObjectIdForPlayer`（#101）、`curePoisonsByLevel`（#101）、`setStatusPlayer`（#101）、`setStatusEnemy`（#101）、`clearPlayerStatus`（#101）、`addMagic`（#102）、`removeMagic`（#102）、`setMagicBaseDamageByMp`（#102）、`halveHp`（#102）、`killPlayer`（#102）、`setMagicBaseDamageByCash`（#102）、`increaseLevel`（#102）、`halveCash`（#102）、`inflictDamageToEnemy`（#103）、`changeBattleSprite`（#103）、`collectEnemyForItems`（#103）、`transformCollectedEnemies`（#103）、`teleportParty`（#106）、`drainHpFromEnemy`（#103）、`playerFlee`（#103）、`simulateMagic`（#103）、`halveEnemyHp`（#103）、`killEnemy`（#103）、`pauseEnemyChasing`（#106）、`speedUpEnemyChasing`（#106）、`throwWeapon`（#103）、`enemyUseMagic`（#104）、`enemyEscape`（#104）、`stealFromEnemy`（#104）、`blowAwayEnemies`（#104）、`walkNpcStep`（#106）、`setBattleResult`（#104）、`enableAutoBattle`（#104）、`showMagicCastAnimation`（#104）、`enemyDivide`（#104）、`enemySummon`（#104）、`enemyTransform`（#104）、`showBuyMenu`（#104）、`showSellMenu`（#104）、`randomJump`、`startBattle`（#103 复核）、`changeHp`（#100 复核）、`changeMp`（#100 复核）、`boostStat`（#102 复核）、`setDialog` 四变体（#99 复核）、`showDialog`（#99 复核）、`redraw`（#99 复核修正）、`waitUpdate`、`walkStep`、`face`、`walkTo`、`walkToSlow`（#105）、`setObjectPosition`、`setObjectPositionRelativeToParty`（#105）、`setObjectGesture`、`setPartyFacing`、`setObjectFacing`（#106）、`changeHpMp`（#100 复核）、`revivePlayer`（#101 复核）、`setAutoEntry`（#108 复核）、`setTriggerEntry`（#108 复核）、`rideEventObjectSlow`（#106）、`setTriggerMode`（#108 复核）、`markFailed`、`setMusic`（#112 复核）、`rideEventObject`（#106）、`setPartyPos`、`setState`（#108 复核）、`setBattlefield`（#103 复核）、`chasePlayer`（#106）、`requestReload`、`fadeToRed`（#110 复核维持）、`fadeOut`（#110 复核修正：frames→delay）、`hide`（#108 复核）、`setSceneScripts`（#112 复核）、`fadeScene`（#110 复核修正：mode→speed）、`setParty`（#112 复核）、`noop`（#112 复核）、`setObjectScript`（#109 复核）、`setStateRange`（#109 复核）、`moveParty`、`partyWalkToSlow`、`partyWalkTo`、`partyWalkToFast`、`walkToFast`、`moveObject`、`moveViewport`、`walkToFastest`、`rideEventObjectFast`、`setFollower`、`gatherParty`（#107，移动子域收官）、`nullifyObject`、`setBattleHidingTime`（#108，对象批1）、`syncState`、`setObjectLayer`、`placeUsedItem`、`animateObject`（#109，对象批2，对象子域收官）、`shakeScreen`、`setPlayingRng`、`playRng`、`fadeIn`、`useDayPalette`、`useNightPalette`、`waveScreen`（#110，屏幕批1）、`showFbp`、`toggleDayNightPalette`、`setPalette`、`colorFade`、`sceneFade`、`showEndingAnimation`、`fadeToScene`、`scrollFbp`、`showFbpWithEffectSprite`（#111，屏幕批2，屏幕子域收官）、`setBattleMusic`、`playSound`、`stopMusic`、`playCdMusic`、`changeScene`、`setPlayerSprite`、`changeSceneMap`、`advanceEntry` auto 位（#112，音乐场景特殊，场景移动域 75 个收官 + 165 个全量收官））；script.c 全部 165 个 opcode 位至此全部具名（#112 机器核对：注册表位集合与 script.c case 清单双向差集为空），其余 opcode—including 契约的其他基础动作—不再存在，`raw` 仅保留为遗留编码形态的保底。`rebuild-events` 校验版本、严格字段、重复 JSON 键、WORD 范围、地址完整性、next 与入口引用，并按具名映射重编码；未映射操作数和低字节字段高位保留。只写 chunk 4 字节，不重建整个 MKF。输入必须是完整事件集合，输出不会覆盖既有内容。重建后比较实际 chunk SHA-256 与清单记录；匹配时可证明字节相同，具名语义编辑导致不匹配时，成功输出会明确提示差异而不回退复制旧 source 字节。

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
| `0x0007` | `startBattle` | `enemyTeamId` 为 WORD 映射 operand 0（敌队表行号，battle.c:1602 `lprgEnemyTeam[wEnemyTeam]`，非 object union 下标）；`lossTarget` 为可空引用映射 operand 1；`isBoss` 为布尔值，`fleeTarget` 为可空引用，二者共同映射 operand 2，规则见下文（#103 复核维持） |
| `0x001F` | `addItem` | `itemId` 为 WORD 映射 operand 0、`count` 为有符号整数映射 operand 1（负值即从库存移除）；operand 2 保留。v1 契约占位名 `giveItem`，#100 起定名 `addItem`（script.c:974 `(SHORT)` 转型） |
| `0x0006` | `randomJump` | `rate` 为 WORD 映射 operand 0、`to` 为引用映射 operand 1；operand 2 保留。执行时抽取 `RandomLong(1,100)`，**抽取值 ≥ rate 时跳转**，否则顺序后继；预检必须同时覆盖两个真实分支（#49） |
| `0x001B` | `changeHp` | `scope` 为 WORD 映射 operand 0（0=调用者角色，非 0=全队）、`amount` 为有符号整数映射 operand 1；operand 2 保留。仅战中角色钩子执行语义已交付：PAL_IncreaseHPMP 只改活人并封顶（#49；#100 复核确认 scope 为整 WORD 非零即全队的布尔旗标，非位掩码/角色下标/名字 WORD） |
| `0x001C` | `changeMp` | 同 `changeHp`，作用于 MP；仅战中角色钩子执行语义已交付（#49；#100 复核同上） |
| `0x0030` | `boostStat` | `field` 为 WORD 映射 operand 0（PLAYERROLES WORD 块索引：17 攻击、18 灵力、19 防御、20 身法、21 逃跑）、`percent` 为有符号整数映射 operand 1（1425 `(SHORT)` 转型，本域唯一有符号字段）、`roleOperand` 为 WORD 映射 operand 2（0=调用者、非 0=operand−1 的 1 基角色下标）。执行按 `kBodyPartExtra` 寄存器写入 `基面板×percent/100`（WORD 截断、覆盖写），战后随寄存器清理（#49；#102 复核维持） |
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
| `storeId` | store.json（DATA chunk 0 行号，#104 新增：0x0026 showBuyMenu） |
| `sceneId` | scene.json |
| `objectId` | object.json union（须带视图类型） |
| `eventObjectId` | event-object.json（SSS chunk 0） |

**`eventObjectId` 与 `objectId` 是两张不同的表**：`event-object.json`（场景事件对象，EVENTOBJECT，16 WORD）与 `object.json`（物品/法术/敌人/毒脚本 union，7 WORD）是不同数据源。指向前者的操作数命名 `eventObjectId`，指向后者的命名 `objectId`（并声明视图）。#78 依此重命名：`startBattle.teamId`→`enemyTeamId`；`setAutoEntry`/`setTriggerEntry`/`setTriggerMode`/`setState`/`setObjectPosition` 的 `objectId`→`eventObjectId`；`setStateRange` 的 `fromObjectId`/`toObjectId`→`fromEventObjectId`/`toEventObjectId`。既有 `src/Pal.Core` 严格加载器与 Pal.Game prepare 仍按旧字段名解析，迁移由后续票处理（#78 写入范围不含 src/）。

#### script.c 逐 opcode 核对结论（#78）

| opcode | 参考实际寻址（[script.c](../../reference/sdlpal/script.c)） | 结论 |
|---|---|---|
| 0x0024 setAutoEntry / 0x0025 setTriggerEntry / 0x0040 setTriggerMode / 0x0049 setState / 0x0013 setObjectPosition / 0x0012 setObjectPositionRelativeToParty（#105 新增）/ 0x0016 setObjectFacing（#106 新增）/ 0x006C walkNpcStep（#106 新增）/ 0x007D moveObject（#107 新增）/ 0x006F syncState、0x007E setObjectLayer、0x0084 placeUsedItem、0x0087 animateObject（#109 新增） | `PAL_InterpretInstruction` 前奏按 operand 0 选 `pCurrent`：0 或 0xFFFF=调用者，否则 `lprgEventObject[operand0−1]`（1 基）；case 体写 `pCurrent->wAutoScript`/`wTriggerScript`/`wTriggerMode`/`sState`/`x,y`/`wDirection`+`wCurrentFrameNum`（0x0016）/`x,y +=`（0x006C/0x007D）/`sLayer`（0x007E）/条件同步 sState 后写调用者（0x006F）/`x,y,sState`（0x0084，另作场景窗口先验）/`wCurEventObjectID` 帧动画（0x0087）（0x0024/0x0025/0x0040/0x0049/0x0016 另有 operand0≠0 守卫，0x0016 的 0 是整体跳过的 no-op 形态；0x0012/0x0013/0x006C/0x007D/0x006F/0x007E/0x0087 无守卫直接用，0x0084 的窗口判定自然排除 0） | **event object 下标** → `eventObjectId` |
| 0x0090 setObjectScript | case 0x0090：`gpGlobals->g.rgObject[operand0].rgwData[2 + operand2] = operand1`（#109 复核维持：operand0 直下标无 −1，rgwData[2+slot] 命中各视图脚本槽 2..4） | **object union 下标，不是 event object** → 保留 `objectId`（配合 `slotIndex`；语义槽随 union 视图而定，与第 3.2 节对象表入口槽对应） |
| 0x009A setStateRange | case 0x009A：`for (i = operand0; i <= operand1; i++) lprgEventObject[i−1].sState = operand2`（#109 复核维持：operand2 无 (SHORT) 转型但写入 SHORT 存储域，符号语义同 setState.state） | **event object 闭区间** → `fromEventObjectId`/`toEventObjectId` |
| 0x0007 startBattle | `PAL_StartBattle(wEnemyTeam, ...)`；敌队表为 DATA chunk 2（enemy-team.json） | → `enemyTeamId` |
| 0x006D setSceneScripts | case 0x006D：`rgScene[operand0−1].wScriptOnEnter/wScriptOnTeleport = operand1/2` | scene 下标 → `sceneId`（1 基） |
| 0x004A setBattlefield | case 0x004A：`gpGlobals->wNumBattleField = operand0` | 战场编号 → `battlefieldId` |

#### 当前注册表（165 个 opcode 位，156 个动词）

| opcode | op | 字段（绑定） | 执行方式 | 后继 |
|---|---|---|---|---|
| 0x0000 | `end` | `mode`="keep"（常量） | bothSwitches | stop |
| 0x0001 | `end` | `mode`="advance"（常量） | bothSwitches | stop |
| 0x0002 | `end` | `mode`="redirect"（常量）、`to`(reference,0)、`limit`(word,1) | bothSwitches | conditionalJump |
| 0x0003 | `goto` | `to`(reference,0)、`limit`(word,1) | bothSwitches | conditionalJump |
| 0x0004 | `call` | `to`(reference,0)、`eventObjectId`(word,1) | bothSwitches | sequential |
| 0x0006 | `randomJump` | `rate`(word,0)、`to`(reference,1) | bothSwitches | conditionalJump |
| 0x0007 | `startBattle` | `enemyTeamId`(word,0)、`lossTarget`(battleTarget,1)、`isBoss`(bossFlag,2⟺fleeTarget)、`fleeTarget`(battleTarget,2) | triggerSwitchOnly | battleResult（#103 复核） |
| 0x0008 | `advanceEntry` | — | triggerSwitchOnly | sequential |
| 0x0009 | `waitUpdate` | `frames`(word,0)、`runTriggers`(word,1)、`updateGestures`(word,2) | bothSwitchesDiffer | sequential |
| 0x000A | `confirmNoJump` | `to`(reference,0) | triggerSwitchOnly | conditionalJump |
| 0x000B–0x000E | `walkStep` | `direction`(opcodeOffset, base 0x000B) | shared | sequential |
| 0x000F | `face` | `direction`(word,0)、`frame`(word,1) | shared | sequential |
| 0x0010 | `walkTo` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0011 | `walkToSlow` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0012 | `setObjectPositionRelativeToParty` | `eventObjectId`(word,0)、`xOffset`(word,1)、`yOffset`(word,2) | shared | sequential |
| 0x0013 | `setObjectPosition` | `eventObjectId`(word,0)、`x`(word,1)、`y`(word,2) | shared | sequential |
| 0x0014 | `setObjectGesture` | `frame`(word,0) | shared | sequential |
| 0x0015 | `setPartyFacing` | `direction`(word,0)、`gesture`(word,1)、`memberIndex`(word,2) | shared | sequential（#106 复核） |
| 0x0016 | `setObjectFacing` | `eventObjectId`(word,0)、`direction`(word,1)、`frame`(word,2) | shared | sequential |
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
| 0x0021 | `inflictDamageToEnemy` | `scope`(word,0)、`damage`(word,1) | shared | sequential |
| 0x0022 | `revivePlayer` | `scope`(word,0)、`healthTenths`(word,1) | shared | sequential（#101 复核） |
| 0x0023 | `removeEquipment` | `playerRole`(word,0)、`partWord`(word,1) | shared | sequential |
| 0x0024 | `setAutoEntry` | `eventObjectId`(word,0)、`entry`(word,1) | shared | sequential（#108 复核） |
| 0x0025 | `setTriggerEntry` | `eventObjectId`(word,0)、`entry`(word,1) | shared | sequential（#108 复核） |
| 0x0026 | `showBuyMenu` | `storeId`(word,0) | shared | sequential |
| 0x0027 | `showSellMenu` | — | shared | sequential |
| 0x0028 | `applyPoisonToEnemy` | `scope`(word,0)、`objectId`(word,1) | shared | sequential |
| 0x0029 | `applyPoisonToPlayer` | `scope`(word,0)、`objectId`(word,1) | shared | sequential |
| 0x002A | `curePoisonByObjectIdForEnemy` | `scope`(word,0)、`objectId`(word,1) | shared | sequential |
| 0x002B | `curePoisonByObjectIdForPlayer` | `scope`(word,0)、`objectId`(word,1) | shared | sequential |
| 0x002C | `curePoisonsByLevel` | `scope`(word,0)、`maxLevel`(word,1) | shared | sequential |
| 0x002D | `setStatusPlayer` | `status`(word,0)、`rounds`(word,1) | shared | sequential |
| 0x002E | `setStatusEnemy` | `status`(word,0)、`rounds`(word,1)、`to`(reference,2) | shared | conditionalJump |
| 0x002F | `clearPlayerStatus` | `status`(word,0) | shared | sequential |
| 0x0030 | `boostStat` | `field`(word,0)、`percent`(signedWord,1)、`roleOperand`(word,2) | shared | sequential（#102 复核） |
| 0x0031 | `changeBattleSprite` | `battleSprite`(word,0) | shared | sequential |
| 0x0033 | `collectEnemyForItems` | `to`(reference,0) | shared | conditionalJump |
| 0x0034 | `transformCollectedEnemies` | `to`(reference,0) | shared | conditionalJump |
| 0x0035 | `shakeScreen` | `times`(word,0)、`level`(word,1) | shared | sequential |
| 0x0036 | `setPlayingRng` | `rngId`(word,0) | shared | sequential |
| 0x0037 | `playRng` | `startFrame`(word,0)、`endFrame`(word,1)、`speed`(word,2) | shared | sequential |
| 0x0038 | `teleportParty` | `to`(reference,0) | shared | conditionalJump |
| 0x0039 | `drainHpFromEnemy` | `amount`(word,0) | shared | sequential |
| 0x003A | `playerFlee` | `to`(reference,0) | shared | conditionalJump |
| 0x003B | `setDialog` | `box`="center"、`color`(lowByte,0)、`rngWord`(word,2) | triggerSwitchOnly | sequential |
| 0x003C | `setDialog` | `box`="upper"、`portraitId`(word,0)、`color`(lowByte,1)、`rngWord`(word,2) | triggerSwitchOnly | sequential |
| 0x003D | `setDialog` | `box`="lower"、`portraitId`(word,0)、`color`(lowByte,1)、`rngWord`(word,2) | triggerSwitchOnly | sequential |
| 0x003E | `setDialog` | `box`="centerWindow"、`color`(lowByte,0) | triggerSwitchOnly | sequential |
| 0x003F | `rideEventObjectSlow` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0040 | `setTriggerMode` | `eventObjectId`(word,0)、`mode`(word,1) | shared | sequential（#108 复核） |
| 0x0041 | `markFailed` | — | shared | sequential |
| 0x0042 | `simulateMagic` | `magicId`(word,0)、`baseDamage`(word,1)、`enemyOperand`(signedWord,2) | shared | sequential |
| 0x0043 | `setMusic` | `musicId`(word,0)、`mode`(word,1) | shared | sequential（#112 复核维持） |
| 0x0044 | `rideEventObject` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0045 | `setBattleMusic` | `battleMusicId`(word,0) | shared | sequential |
| 0x0046 | `setPartyPos` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential（#106 复核） |
| 0x0047 | `playSound` | `soundId`(word,0) | shared | sequential |
| 0x0049 | `setState` | `eventObjectId`(word,0)、`state`(signedWord,1) | shared | sequential（#108 复核） |
| 0x004A | `setBattlefield` | `battlefieldId`(word,0) | shared | sequential（#103 复核） |
| 0x004B | `nullifyObject` | — | shared | sequential |
| 0x004C | `chasePlayer` | `chaseRange`(word,0)、`speed`(word,1)、`floating`(word,2) | shared | sequential |
| 0x004D | `waitForAnyKey` | — | shared | sequential |
| 0x004E | `requestReload` | — | shared | stop（#97 复核修正） |
| 0x004F | `fadeToRed` | — | shared | sequential（#110 复核维持） |
| 0x0050 | `fadeOut` | `delay`(word,0) | shared | sequential（#110 复核修正） |
| 0x0051 | `fadeIn` | `delay`(word,0) | shared | sequential |
| 0x0052 | `hide` | `frames`(word,0) | shared | sequential（#108 复核） |
| 0x0053 | `useDayPalette` | — | shared | sequential |
| 0x0054 | `useNightPalette` | — | shared | sequential |
| 0x0055 | `addMagic` | `magicId`(word,0)、`roleOperand`(word,1) | shared | sequential |
| 0x0056 | `removeMagic` | `magicId`(word,0)、`roleOperand`(word,1) | shared | sequential |
| 0x0057 | `setMagicBaseDamageByMp` | `magicId`(word,0)、`mpMultiplier`(word,1) | shared | sequential |
| 0x0058 | `itemShortageJump` | `itemId`(word,0)、`count`(signedWord,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0059 | `changeScene` | `sceneId`(word,0) | shared | sequential |
| 0x005A | `halveHp` | — | shared | sequential |
| 0x005B | `halveEnemyHp` | `maxDamage`(word,0) | shared | sequential |
| 0x005C | `setBattleHidingTime` | `units`(word,0) | shared | sequential |
| 0x005D | `playerLacksPoisonJump` | `objectId`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x005E | `enemyLacksPoisonJump` | `objectId`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x005F | `killPlayer` | — | shared | sequential |
| 0x0060 | `killEnemy` | — | shared | sequential |
| 0x0061 | `notPoisonedJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0062 | `pauseEnemyChasing` | `cycles`(word,0) | shared | sequential |
| 0x0063 | `speedUpEnemyChasing` | `cycles`(word,0) | shared | sequential |
| 0x0064 | `enemyHpAboveJump` | `percent`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x0065 | `setPlayerSprite` | `playerRole`(word,0)、`spriteNum`(word,1)、`reload`(word,2) | shared | sequential |
| 0x0066 | `throwWeapon` | `magicId`(word,0)、`damageUnits`(word,1) | shared | sequential |
| 0x0067 | `enemyUseMagic` | `magicId`(word,0)、`magicRate`(word,1) | shared | sequential |
| 0x0068 | `enemyTurnJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0069 | `enemyEscape` | — | shared | sequential |
| 0x006A | `stealFromEnemy` | `stealRate`(word,0) | shared | sequential |
| 0x006B | `blowAwayEnemies` | `strength`(signedWord,0) | shared | sequential |
| 0x006C | `walkNpcStep` | `eventObjectId`(word,0)、`xOffset`(signedWord,1)、`yOffset`(signedWord,2) | shared | sequential |
| 0x0005 | `redraw` | `delayUnits`(word,1)、`updateGestures`(word,2) | triggerSwitchOnly | sequential（#99 复核修正） |
| 0x006D | `setSceneScripts` | `sceneId`(word,0)、`onEnter`(word,1)、`onTeleport`(word,2) | shared | sequential（#112 复核维持） |
| 0x006E | `moveParty` | `xOffset`(signedWord,0)、`yOffset`(signedWord,1)、`layerUnits`(word,2) | shared | sequential |
| 0x006F | `syncState` | `eventObjectId`(word,0)、`state`(signedWord,1) | shared | sequential |
| 0x0070 | `partyWalkToSlow` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0071 | `waveScreen` | `level`(word,0)、`progression`(signedWord,1) | shared | sequential |
| 0x0073 | `fadeScene` | `speed`(word,0) | shared | sequential（#110 复核修正） |
| 0x0074 | `partyHpNotFullJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0075 | `setParty` | `roleId0`(word,0)、`roleId1`(word,1)、`roleId2`(word,2) | shared | sequential（#112 复核维持） |
| 0x0076 | `showFbp` | `fbpId`(word,0)、`fade`(word,1) | shared | sequential |
| 0x0077 | `stopMusic` | `fadeUnits`(word,0) | shared | sequential |
| 0x0078 | `noop` | — | shared | sequential（#112 复核维持） |
| 0x0079 | `roleInPartyJump` | `roleNameWord`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x007A | `partyWalkTo` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x007B | `partyWalkToFast` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x007C | `walkToFast` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x007D | `moveObject` | `eventObjectId`(word,0)、`xOffset`(signedWord,1)、`yOffset`(signedWord,2) | shared | sequential |
| 0x007E | `setObjectLayer` | `eventObjectId`(word,0)、`layer`(signedWord,1) | shared | sequential |
| 0x007F | `moveViewport` | `xOffset`(signedWord,0)、`yOffset`(signedWord,1)、`frames`(word,2) | shared | sequential |
| 0x0080 | `toggleDayNightPalette` | `paletteOnly`(word,0) | shared | sequential |
| 0x0081 | `notFacingObjectJump` | `eventObjectId`(word,0)、`zoneUnits`(word,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0082 | `walkToFastest` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0083 | `objectOutOfZoneJump` | `eventObjectId`(word,0)、`zoneUnits`(word,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0084 | `placeUsedItem` | `eventObjectId`(word,0)、`state`(signedWord,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0085 | `delay` | `delayUnits`(word,0) | shared | sequential |
| 0x0086 | `itemNotEquippedJump` | `itemId`(word,0)、`count`(word,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0087 | `animateObject` | `eventObjectId`(word,0) | shared | sequential |
| 0x0088 | `setMagicBaseDamageByCash` | `magicId`(word,0) | shared | sequential |
| 0x0089 | `setBattleResult` | `result`(word,0) | shared | sequential |
| 0x008A | `enableAutoBattle` | — | shared | sequential |
| 0x008B | `setPalette` | `paletteId`(word,0) | shared | sequential |
| 0x008C | `colorFade` | `color`(lowByte,0)、`delay`(word,1)、`fromColor`(word,2) | shared | sequential |
| 0x008D | `increaseLevel` | `levels`(word,0) | shared | sequential |
| 0x008E | `restoreScreen` | — | triggerSwitchOnly | sequential |
| 0x008F | `halveCash` | — | shared | sequential |
| 0x0091 | `notFirstOfKindJump` | `to`(reference,0) | shared | conditionalJump |
| 0x0092 | `showMagicCastAnimation` | `playerOperand`(word,0) | shared | sequential |
| 0x0093 | `sceneFade` | `step`(signedWord,0) | shared | sequential |
| 0x0094 | `stateMatchJump` | `eventObjectId`(word,0)、`state`(signedWord,1)、`to`(reference,2) | shared | conditionalJump |
| 0x0095 | `sceneMatchJump` | `sceneId`(word,0)、`to`(reference,1) | shared | conditionalJump |
| 0x0096 | `showEndingAnimation` | — | shared | sequential |
| 0x0097 | `rideEventObjectFast` | `x`(word,0)、`y`(word,1)、`h`(word,2) | shared | sequential |
| 0x0098 | `setFollower` | `spriteNum0`(word,0)、`spriteNum1`(word,1) | shared | sequential |
| 0x0099 | `changeSceneMap` | `sceneId`(word,0)、`mapNum`(word,1) | shared | sequential |
| 0x009B | `fadeToScene` | — | shared | sequential |
| 0x009C | `enemyDivide` | `copies`(word,0)、`failTarget`(battleTarget,1) | shared | conditionalJump |
| 0x009E | `enemySummon` | `objectId`(word,0)、`count`(signedWord,1)、`failTarget`(battleTarget,2) | shared | conditionalJump |
| 0x009F | `enemyTransform` | `objectId`(word,0) | shared | sequential |
| 0x00A0 | `quitGame` | — | shared | stop |
| 0x00A1 | `gatherParty` | — | shared | sequential |
| 0x00A2 | `randomBranch` | `count`(word,0) | shared | randomBranch |
| 0x00A3 | `playCdMusic` | `track`(signedWord,0)、`musicId`(word,1) | shared | sequential |
| 0x00A4 | `scrollFbp` | `fbpId`(word,0)、`speed`(word,2) | shared | sequential |
| 0x00A5 | `showFbpWithEffectSprite` | `fbpId`(word,0)、`effectSprite`(word,1)、`fade`(word,2) | shared | sequential |
| 0x00A6 | `backupScreen` | — | shared | sequential |
| 0x00A7 | `advanceEntry` | — | autoSwitchOnly | sequential |
| 0x0090 | `setObjectScript` | `objectId`(word,0)、`value`(word,1)、`slotIndex`(word,2) | shared | sequential（#109 复核维持） |
| 0x009A | `setStateRange` | `fromEventObjectId`(word,0)、`toEventObjectId`(word,1)、`state`(signedWord,2) | shared | sequential（#109 复核维持） |
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

#### M5 状态物品批2 条目（#101）

本批核对结论：全部操作数仍为立即 WORD（同批1–批4/状态物品批1，`pScript->rgwOperand[n]` 直读，无变量寻址），且**十个 case 全无 `(SHORT)` 转型，无一个有符号字段**；10 个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger/auto 开关均无对应 case，executor 一律 `shared`），后继除 0x002E 抗性跳转为 conditionalJump 外一律 sequential（break 后函数尾 3085 `return wScriptEntry + 1`）。**角色寻址逐条对号**：0x0022/0x0029/0x002B/0x002C 的 operand 0 是 #100 核实的全员布尔旗标形状（`if (rgwOperand[0])`：0=单人=wEventObjectID 玩家角色、非 0=全队）；0x0028/0x002A 用同一旗标形状扫战斗敌人（0=`g_Battle.rgEnemy[wEventObjectID]`、非 0=全部非空敌槽）；0x002D/0x002F 的目标角色是 wEventObjectID 本身（无操作数，同 0x0017/0x0018 形状）；0x002E 目标是 `g_Battle.rgEnemy[wEventObjectID]`；**0x0023 operand 0 是第四种形状：直接 0 基 PLAYERROLES 角色下标操作数**（1108 `iPlayerRole = rgwOperand[0]` 直用无 −1、无调用者回退，与 #100 三形状及 0x0075 setParty 的 operand−1 1 基形状均不同），定名 `playerRole`；其 operand 1 为 0=卸全部部位 / 1..MAX_PLAYER_EQUIPMENTS=1 基部位号（1129/1132 按 operand1−1 索引），定名 `partWord` 表达原始 WORD 编码（区别于 0x0017/0x0018 的 0x0B+部位 `equipPartWord`）。毒对象操作数=object.json poison 视图下标（`rgObject[operand1].poison` 与 rgPoisons[].wPoisonID 全等比较，#82/#97 先例，定名 `objectId`）；status 编号是 rgPlayerStatus/rgwStatus 数组行号（按参考数组索引表达，不硬编码枚举名，定名 `status`；参考行号：0=Confused、1=Paralyzed（非 CLASSIC 为 Slow）、2=Sleep、3=Silence、4=Puppet、5=Bravery、6=Protect、7=Haste、8=DualAttack，kStatusAll=9）；回合数/等级按实际算术均为无符号 WORD（PAL_SetPlayerStatus 的 wNumRound、PAL_CurePoisonByLevel 的 wMaxLevel 均 WORD 形参，无符号比较）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0022 | `revivePlayer`（#78 已具名，复核） | `scope`=operand0（**全员布尔旗标**：非 0=全队 1057-1080、0=单人=wEventObjectID 1082-1097；仅对 HP=0 者生效）、`healthTenths`=operand1（**无符号**：1068-1069/1088-1089 `rgwHP = rgwMaxHP × operand1 / 10`，即最大 HP 十分率；复活同时 1071/1091 `PAL_CurePoisonByLevel(w, 3)` 清等级 ≤3 的毒、1072-1074/1092-1094 循环清全部状态；全队路径任一复活才置脚本成功标记） | shared（1052：break） | sequential | 两字段重编码；operand2 保留（映射复核无误） | 13（scope 0×4、1×9；healthTenths 10×9、5×2、1、3；operand2 全 0） |
| 0x0023 | `removeEquipment` | `playerRole`=operand0（**直接 0 基 PLAYERROLES 角色下标**，第四种角色寻址形状：1108 直用无 −1）、`partWord`=operand1（0=卸全部部位：1115-1123 逐部位回库存并 1122 清效果；1..6=1 基部位号：1129-1132 按 operand1−1 索引 rgwEquipment 单部位） | shared（1104：1109 分支；break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 3（playerRole 1×2、2×1；partWord 6×2、0×1；operand2 全 0） |
| 0x0028 | `applyPoisonToEnemy` | `scope`=operand0（0=`rgEnemy[wEventObjectID]`、非 0=全部非空敌槽，1179/1184）、`objectId`=operand1（object.json poison 视图：1196-1198 `RandomLong(0,9) ≥ wResistanceToSorcery` 抗性判定通过后 1203-1207 查重、1211-1213 写入空毒槽并立即以 `rgObject[operand1].poison.wEnemyScript` 递归执行敌人毒触发脚本；单人路径 1220-1253 同形） | shared（1175：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 17（scope 0×13、1×4；objectId 551..562 全在 poison 视图区间；operand2 全 0） |
| 0x0029 | `applyPoisonToPlayer` | `scope`=operand0（全员布尔旗标，1261）、`objectId`=operand1（poison 视图：1269-1271/1280-1282 `RandomLong(1,100) > PAL_GetPlayerPoisonResistance` 判定通过后 `PAL_AddPoisonForPlayer`，其内部以 `rgObject[wPoisonID].poison.wPlayerScript` 立即执行玩家毒触发脚本，[global.c](../../reference/sdlpal/global.c):1459） | shared（1257：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 16（scope 0×13、1×3；objectId 551..560、563、564；operand2 全 0） |
| 0x002A | `curePoisonByObjectIdForEnemy` | `scope`=operand0（同 0x0028 敌人旗标形状，1291）、`objectId`=operand1（poison 视图：1300-1305/1319-1324 扫 MAX_POISONS 槽，wPoisonID 全等命中即把该槽毒对象与毒脚本归零） | shared（1287：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 3（scope 全 0；objectId {555,561,562}；operand2 全 0） |
| 0x002B | `curePoisonByObjectIdForPlayer` | `scope`=operand0（全员布尔旗标，1335）、`objectId`=operand1（poison 视图：1340/1345 `PAL_CurePoisonByKind` 按 wPoisonID 全等清除，global.c:1520） | shared（1331：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 19（scope 0×18、1×1；objectId 551..560；operand2 全 0） |
| 0x002C | `curePoisonsByLevel` | `scope`=operand0（全员布尔旗标，1353）、`maxLevel`=operand1（**无符号**：1358/1363 `PAL_CurePoisonByLevel`，global.c:1608 按 `poison.wPoisonLevel <= maxLevel` WORD 无符号比较清除；仅玩家/全队，无敌人变体） | shared（1349：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 4（scope 全 0×4；maxLevel 2×3、3×1；operand2 全 0） |
| 0x002D | `setStatusPlayer` | `status`=operand0（rgPlayerStatus 行号）、`rounds`=operand1（**无符号**：PAL_SetPlayerStatus 的 wNumRound，global.c:2173——好/坏状态与 Puppet 的置位规则不同但均 WORD 存储按回合递减，[fight.c](../../reference/sdlpal/fight.c):1636/4482；拒绝时仅 1373 清脚本成功标记不跳转）；目标角色=wEventObjectID 本身（1371，无操作数） | shared（1367：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 19（status 0..8 全行号；rounds 32760×5、9×3、5×4、3×3、4×2、7×2；operand2 全 0） |
| 0x002E | `setStatusEnemy` | `status`=operand0（rgwStatus 行号；PAL_CLASSIC（本参考默认构建，common.h:26）下 1384 恒 `i=9`，非 CLASSIC 构建对 kStatusSlow 行用 14（1386）——仅抗性骰面不同，不影响字段绑定）、`rounds`=operand1（**无符号**：1391 `rgwStatus[operand0] = operand1` 原样赋值，fight.c:1657-1659 按回合递减消费）、`to`=operand2（抗性跳转目标：1389 `RandomLong(0,i) > wResistanceToSorcery` 判定失败即 1395 `wScriptEntry = to − 1` 标准跳转惯用法 + 3085 return+1；目标 WORD 0 合法引用 L0000，与批2/批3 跳转族同口径）；目标=`g_Battle.rgEnemy[wEventObjectID]`（1381，无操作数） | shared（1377：break） | conditionalJump（抗住→to，否则应用后 next） | 三字段重编码；无保留操作数 | 10（status 0..3；rounds 4×4、5×3、3×2、0×1；目标 38783×9、43072×1 全在 43538 条内且无零目标） |
| 0x002F | `clearPlayerStatus` | `status`=operand0（rgPlayerStatus 行号：1403 `PAL_RemovePlayerStatus`，global.c:2304 对 >999 的装备赋予值拒清）；目标角色=wEventObjectID 本身（无操作数） | shared（1399：break） | sequential | status 重编码；operand1、2 保留（参考 case 未用） | 11（status 0..3；operand1/2 全 0） |

真实数据核对：本批命中合计 115（新具名 102 = 0x0023 3 + 0x0028 17 + 0x0029 16 + 0x002A 3 + 0x002B 19 + 0x002C 4 + 0x002D 19 + 0x002E 10 + 0x002F 11；复核 13 = 0x0022 本已具名）。raw 命中从 #100 的 5813 降至 5711（−102）。四个毒 opcode 的全部 objectId 落在 551..564，正是 [data-schema](data-schema.md) 毒对象冻结区间，与 #97 的 0x005D/0x005E 证据同源；0x002E 的 10 个目标全为非零且落在 chunk 4（43538 条）内；0x002D 的 rounds 含 32760×5（大于多数常见值、仅 WORD 无符号读法无歧义，无 `(SHORT)` 转型佐证）。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`playerRole`/`partWord`/`scope`/`objectId`/`maxLevel`/`status`/`rounds` 全部沿用既有 word 类型，`to` 沿用普通 reference（WORD 0=L0000，非可空绑定）；`DecodeValidatesReferenceRange` 导出期预检不启用（与批1–批4 及状态物品批1 同口径，悬空拒绝仍在重建标签解析处）。

#### M5 状态物品批3 条目（#102，状态物品域收官）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；9 个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger/auto 开关逐一核对无对应 case，executor 一律 `shared`），后继全部 sequential（break 后函数尾 3085 `return wScriptEntry + 1`，本批无跳转类）。signed/unsigned：**仅复核项 0x0030 boostStat 有 `(SHORT)` 转型（1425 percent，本域唯一有符号字段），八个新 case 全无转型，全部无符号 WORD**。**「player/role」操作数对号**：0x0055/0x0056 的 operand 1 是与 0x0030/0x0019/0x001A 相同的 roleOperand 形状（1820-1828/1836-1844：0=调用者角色=wEventObjectID、非 0=operand−1 的 1 基角色下标）；0x0057/0x005A/0x005F/0x008D 的目标角色是 wEventObjectID 本身（无操作数，同 0x0017/0x0018/0x002D 形状；0x005A/0x005F 的 case 注释明说 wEventObjectID here should indicate the player role）；**0x0088 完全不涉及角色**（只写全局现金与法术表）。magicId 操作数=object.json magic 视图对象下标（#82/#87/#90 引用方向先例：PAL_AddMagic/PAL_RemoveMagic 把原样操作数存入 rgwMagic 槽并按全等比较，global.c:2084/2139；运行时引用不是提取期视图证据，data-schema 对象表已注记此边界）。**0x0057 与 0x0088 的 byMp/byCash 差异**：两者都经 `rgObject[magicId].magic.wMagicNumber` 定位法术表行并覆写其 wBaseDamage，但 byMp 用调用者角色 rgwMP×乘子（operand 1，WORD 0 运行时归一 8 仅改内存、编码仍 0，1852）写入后把该 MP 清零（1855-1856），目标角色=wEventObjectID 无操作数；byCash 无角色无乘子操作数，取 min(dwCash,5000)×2÷5 写入并把同一 min 值从 DWORD 现金中扣除（2553-2556）。killPlayer 的 scope 形状：无操作数，`rgwHP[wEventObjectID]=0`（1947）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0030 | `boostStat`（#78 已具名，复核） | `field`=operand0（PLAYERROLES 展平行号，1423-1424 `field×MAX_PLAYER_ROLES + iPlayerRole` 寻址；写入目标是 rgEquipmentEffect[kBodyPartExtra] 展平 WORD 寄存器即 0x0017 的 extra 部位）、`percent`=operand1（**有符号**：1425 `(SHORT)` 转型，`基面板×percent/100` WORD 截断覆盖写）、`roleOperand`=operand2（1414-1421：0=调用者角色=wEventObjectID、非 0=operand−1 的 1 基角色下标） | shared（1406-1427：break；3085 return+1） | sequential | 三字段重编码；无保留操作数（**映射复核维持**） | 14（field 17×5、18×4、20×3、21×2；percent 5..100 全非负；roleOperand 全 0） |
| 0x0055 | `addMagic` | `magicId`=operand0（object.json magic 视图对象下标：1829 `PAL_AddMagic(i, operand0)`，global.c:2084 全等查重后写入首个空 rgwMagic 槽，重复拒绝）、`roleOperand`=operand1（1820-1828：0=调用者、非 0=operand−1 的 1 基角色下标，与 0x0030 同形状） | shared（1816：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 15（magicId 295..393 共 14 种全部落在 data-schema magic 视图证据并集 295..394 内；roleOperand 1×9、2×3、5×3（1 基，真实数据无 0）；operand2 全 0） |
| 0x0056 | `removeMagic` | 同 0x0055 形状：`magicId`=operand0、`roleOperand`=operand1（1836-1844）；1845 `PAL_RemoveMagic(i, operand0)` 按 wMagic 全等命中即把该 rgwMagic 槽清零（global.c:2139，未持有则静默无操作） | shared（1832：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 1（magicId 370；roleOperand 1；operand2 0） |
| 0x0057 | `setMagicBaseDamageByMp` | `magicId`=operand0（1853 `rgObject[operand0].magic.wMagicNumber` 定位法术表行）、`mpMultiplier`=operand1（**无符号**：1852 `(operand1 == 0) ? 8 : operand1` 运行时归一仅改内存、编码仍 0，与 0x0005/0x0086 归一口径同）；写入：1854-1855 `wBaseDamage = rgwMP[wEventObjectID] × 乘子`、1856 该 MP 清零；目标角色=wEventObjectID（无操作数） | shared（1848：break） | sequential | 两字段重编码；operand2 保留（参考 case 未用） | 1（magicId 370；mpMultiplier 0——零售数据恰用运行时归一 8 的编码；operand2 0） |
| 0x005A | `halveHp` | 无操作数映射：1892 `rgwHP[wEventObjectID] /= 2`（WORD 无符号除法；case 注释明说 wEventObjectID should indicate the player role） | shared（1887：break） | sequential | 三操作数保留源 WORD | 1（三操作数全 0） |
| 0x005F | `killPlayer` | 无操作数映射：1947 `rgwHP[wEventObjectID] = 0`（同上 player role 注释；无全员变体、无状态/毒清理） | shared（1942：break） | sequential | 三操作数保留源 WORD | 8（三操作数全 0） |
| 0x0088 | `setMagicBaseDamageByCash` | `magicId`=operand0（2555 同 0x0057 定位法术表行）；写入：2553 `i = min(dwCash, 5000)`、2554 `dwCash -= i`、2556 `wBaseDamage = i × 2 ÷ 5`；**无角色无乘子操作数**，与 0x0057 的差异见本节前言 | shared（2549：break） | sequential | magicId 重编码；operand1、2 保留（参考 case 未用） | 1（magicId 394（证据并集上沿）；operand1/2 全 0） |
| 0x008D | `increaseLevel` | `levels`=operand0（**无符号**：2597 `PAL_PlayerLevelUp(wEventObjectID, operand0)`，global.c:2347 的 wNumLevel WORD 形参——逐级随机涨属性封顶 MAX_LEVELS/999 并清经验）；目标角色=wEventObjectID（无操作数） | shared（2593：break） | sequential | levels 重编码；operand1、2 保留（参考 case 未用） | 1（levels 1；operand1/2 全 0） |
| 0x008F | `halveCash` | 无操作数映射：2604 `dwCash /= 2`（DWORD 现金整除减半，无操作数参与） | shared（2600：break） | sequential | 三操作数保留源 WORD | 1（三操作数全 0） |

真实数据核对：本批命中合计 43（新具名 29 = 0x0055 15 + 0x0056 1 + 0x0057 1 + 0x005A 1 + 0x005F 8 + 0x0088 1 + 0x008D 1 + 0x008F 1；复核 14 = 0x0030 本已具名）。raw 命中从 #101 的 5711 降至 5682（−29）。全部 magicId（0x0055 的 295..393、0x0056/0x0057 的 370、0x0088 的 394）落在 [data-schema](data-schema.md) magic 视图证据并集 295..394 内，与 #82 的法术视图区间同源佐证；0x0057 的真实 mpMultiplier=0 恰是运行时归一 8 的编码形态（编码按 0 往返）；0x0055 roleOperand 仅 1/2/5（1 基、无 0 调用者形态），0x0030 roleOperand 全 0。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`magicId`/`roleOperand`/`mpMultiplier`/`levels` 全部沿用既有 word 类型；本批无引用字段，无悬空拒绝面变化；`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径）。**状态物品域（#77 六域之一，共 29 个 opcode：0x0017–0x0020、0x0022–0x0023、0x0028–0x0030、0x0055–0x0057、0x005A、0x005F、0x0088、0x008D、0x008F）至此全部具名。**

#### M5 战斗批1 条目（#103）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；除 0x0007 位于 `PAL_RunTriggerScript` 的 trigger switch（executor `triggerSwitchOnly`，复核维持）外，其余 11 个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger/auto 开关逐一核对无对应 case，executor 一律 `shared`）；后继除 0x0033/0x0034/0x003A 三跳转类为 conditionalJump（`wScriptEntry = to − 1` + 函数尾 3085 `return wScriptEntry + 1`，目标 WORD 0 合法引用 L0000）外一律 sequential（break 后函数尾 return+1）。**战斗中的「玩家/敌人」寻址逐条对号**：0x0021 operand 0 是 #101 的全员布尔旗标形状扫战斗敌人（0=`g_Battle.rgEnemy[wEventObjectID]`、非 0=全部非空敌槽）；0x0033/0x003A/0x005B/0x0060 的目标敌槽=`rgEnemy[wEventObjectID]`（无操作数）；0x0039 的受益玩家=`rgParty[g_Battle.wMovingPlayerIndex].wPlayerRole`（当前行动玩家，无操作数）、被抽敌槽=`rgEnemy[wEventObjectID]`；0x0031 的目标玩家角色=wEventObjectID（写入 rgEquipmentEffect[kBodyPartExtra] 展平寄存器的 rgwSpriteNumInBattle 行，与 0x0030 boostStat 同寄存器、战后随寄存器清理）；0x0042 operand 2 是把 roleOperand 形状应用于敌槽的第四种变体：`(SHORT)` 读入后取负判定（1634-1636：0 或负值→调用者敌槽 wEventObjectID、正值→operand−1 的 1 基敌槽下标），定名 `enemyOperand` 并声明 signedWord（本批唯一 `(SHORT)` 转型字段）；0x0066 目标恒为调用者敌槽（2013 `(SHORT)wEventObjectID` 直传，无操作数）。**魔法操作数=object.json magic 视图对象下标 `magicId`**：0x0042/0x0066 均经 `PAL_BattleSimulateMagic(target, operand0, damage)`（fight.c:5301）把 operand 0 作为 wMagicObjectID 按 `rgObject[id].magic` 消费（fight.c:5321 按 wFlags 的 apply-to-all 位覆盖目标为全体）；0x0066 真实数据 operand0 全为 344/360，恰落 data-schema magic 视图证据并集 295..394 内——票面预留的「throwWeapon 的 itemId」方向按证据裁定为 `magicId`（被投掷物对象经其 magic 视图消费；0x0042 的真实 magicId 含 24，不在任何已冻结视图证据区间，按消费视图命名、运行时引用不并入提取期视图证据，与 #102 同口径）。signed/unsigned：除 0x0042 enemyOperand 外本批全部数值字段无 `(SHORT)` 转型、全为无符号 WORD（0x0021 damage/0x0039 amount/0x005B maxDamage 的减法/封顶均为 WORD 无符号算术）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0007 | `startBattle`（#78 已具名，复核维持） | `enemyTeamId`=operand0（敌队表行号：battle.c:1531 `PAL_StartBattle(wEnemyTeam, fIsBoss)` 形参、1602 `lprgEnemyTeam[wEnemyTeam].rgwEnemy[j]` 逐槽取敌对象——指向 enemy-team.json（DATA chunk 2）行，**不是 object union 下标**，既有映射维持）、`lossTarget`=operand1（败战且非 0 跳：3322-3325）、`isBoss`⟺`fleeTarget`=operand2（3326-3329 逃战且非 0 跳；3320 `fIsBoss = !operand2`，boss 战不可逃且无逃逸目标） | triggerSwitchOnly（3316：3334 另置 `fAutoBattle = FALSE`） | battleResult | 四字段重编码（含 boss 耦合校验）；无保留操作数（**映射复核维持**） | 434（enemyTeamId 0..380；lossTarget null×8、41075×424、6186/13783 各 1；fleeTarget null×56（boss）、41073×378——全部目标在 43538 条内；1 条 @42205 的 enemyTeamId=380 超出敌队表 380 行（0..379）上沿，参考运行时按越界行读取，提取按 WORD 原样保留不做表范围校验） |
| 0x0021 | `inflictDamageToEnemy` | `scope`=operand0（#101 全员布尔旗标形状扫敌：1030 `if (rgwOperand[0])`、0=`rgEnemy[wEventObjectID]`、非 0=1035-1042 扫全部非空敌槽）、`damage`=operand1（**无符号**：1039/1049 `e.wHealth -= operand1` WORD 减法无 `(SHORT)` 转型，回绕语义）；operand2 参考 case 未用 | shared（1026：break） | sequential | 两字段重编码；operand2 保留 | 43（scope 0×41、1×2；damage 1..400；operand2 全 0） |
| 0x0031 | `changeBattleSprite` | `battleSprite`=operand0（1433-1434 写 rgEquipmentEffect[kBodyPartExtra].rgwSpriteNumInBattle[wEventObjectID]——调用者玩家角色的临时战斗形象覆盖写，同 0x0030 boostStat 的 extra 部位寄存器、战后清理；PLAYERROLES 行号映射中行 1 同名）；operand1、2 参考 case 未用 | shared（1429：break） | sequential | battleSprite 重编码；operand1、2 保留 | 1（battleSprite 5；operand1/2 全 0） |
| 0x0033 | `collectEnemyForItems` | `to`=operand0（1441 敌槽 `rgEnemy[wEventObjectID].e.wCollectValue != 0` 判定、为零时 1448 `wScriptEntry = to − 1` 标准跳转惯用法；目标 WORD 0 合法引用 L0000）；成功路径 1442-1444 把该敌 wCollectValue 累入全局 gpGlobals->wCollectValue；目标敌槽=rgEnemy[wEventObjectID]（无操作数）；operand1、2 参考 case 未用 | shared（1437：break） | conditionalJump（无收集值→to，否则累入后 next） | to 解析标签重编码；operand1、2 保留 | 1（to 43072 在范围内；operand1/2 全 0） |
| 0x0034 | `transformCollectedEnemies` | `to`=operand0（1517 全局 wCollectValue == 0 时 1517 `wScriptEntry = to − 1`；同上零目标口径）；成功路径：PAL_CLASSIC（本参考默认构建）下 1461-1465 `i = RandomLong(1, wCollectValue)` 封顶 9、非 CLASSIC 1467-1471 `RandomLong(1,9)` 封顶 wCollectValue，1474 扣减、1475 `i--`、1477 `PAL_AddItemToInventory(lprgStore[0].rgwItems[i], 1)` 发商店表 0 的 rgwItems[0..8] 随机一件，1479-1513 弹中央窗展示获得物（物品位图+名字+等待确认）；operand1、2 参考 case 未用 | shared（1452：break） | conditionalJump（计数器空→to，否则发物+展示后 next） | to 解析标签重编码；operand1、2 保留 | 1（to 38780 在范围内；operand1/2 全 0） |
| 0x0039 | `drainHpFromEnemy` | `amount`=operand0（**无符号**：1579 `rgEnemy[wEventObjectID].e.wHealth -= operand0`、1580 `rgwHP[w] += operand0` 全 WORD 算术）；受益玩家=当前行动玩家 `rgParty[g_Battle.wMovingPlayerIndex].wPlayerRole`（1577，无操作数），1582-1585 封顶 MaxHP；被抽敌槽=rgEnemy[wEventObjectID]（无操作数）；operand1、2 参考 case 未用 | shared（1573：break） | sequential | amount 重编码；operand1、2 保留 | 1（amount 180；operand1/2 全 0） |
| 0x003A | `playerFlee` | `to`=operand0（1592 `g_Battle.fIsBoss` 为真（boss 战不可逃）时 1597 `wScriptEntry = to − 1`；否则 1601 `PAL_BattlePlayerEscape()`；目标 WORD 0 合法引用 L0000）；无操作数选逃跑者（全队逃跑语义由 PAL_BattlePlayerEscape 实现）；operand1、2 参考 case 未用 | shared（1588：break） | conditionalJump（boss→to，否则逃跑后 next） | to 解析标签重编码；operand1、2 保留 | 1（to 43072 在范围内；operand1/2 全 0） |
| 0x0042 | `simulateMagic` | `magicId`=operand0（object.json magic 视图对象下标：1639 `PAL_BattleSimulateMagic(i, operand0, operand1)` 的 wMagicObjectID 形参，fight.c:5301/5321 经 `rgObject[id].magic` 消费）、`baseDamage`=operand1（**无符号** WORD 直传 wBaseDamage 形参）、`enemyOperand`=operand2（**有符号**：1634 `i = (SHORT)(operand2) − 1` 取负判定——0 或负值→调用者敌槽 wEventObjectID、正值→operand−1 的 1 基敌槽下标，1635-1638；apply-to-all 法术在 fight.c:5321-5324 按 wFlags 覆盖目标为全体） | shared（1630：break） | sequential | 三字段重编码；无保留操作数 | 40（magicId 24×26、323/329/337/349/353/364/365/366/367/368/372/373/374/381 各 1——除 24 外全部落在 magic 视图证据并集 295..394 内，24 不在任何已冻结证据区间（对象表该下标记 undetermined），按消费视图定名；baseDamage 全 0；enemyOperand 全 0（调用者敌槽形态）） |
| 0x004A | `setBattlefield`（#78 已具名，复核维持） | `battlefieldId`=operand0（1722 `gpGlobals->wNumBattleField = operand0` 战场编号直写）；operand1、2 参考 case 未用 | shared（1719：break） | sequential | battlefieldId 重编码；operand1、2 保留（**映射复核维持**） | 84（battlefieldId 6..57；operand1/2 全 0） |
| 0x005B | `halveEnemyHp` | `maxDamage`=operand0（**无符号**：1899-1902 `w = wHealth / 2 + 1; if (w > operand0) w = operand0` WORD 无符号比较/封顶、1904 `wHealth -= w`——operand 0 是减半伤害封顶，非血量阈值）；目标敌槽=rgEnemy[wEventObjectID]（无操作数）；operand1、2 参考 case 未用 | shared（1895：break） | sequential | maxDamage 重编码；operand1、2 保留 | 1（maxDamage 1000；operand1/2 全 0） |
| 0x0060 | `killEnemy` | 无操作数映射：1953 `rgEnemy[wEventObjectID].e.wHealth = 0`（立即击倒，与 0x005F killPlayer 对偶的敌侧无操作数形状）；目标敌槽=rgEnemy[wEventObjectID]（无操作数） | shared（1950：break） | sequential | 三操作数保留源 WORD | 9（三操作数全 0） |
| 0x0066 | `throwWeapon` | `magicId`=operand0（被投掷物对象的 magic 视图下标：2013 `PAL_BattleSimulateMagic((SHORT)wEventObjectID, operand0, w)`，真实数据 344/360 全落 magic 视图证据并集内——按证据裁定定名 `magicId` 而非 `itemId`）、`damageUnits`=operand1（**无符号**：2011-2012 `w = operand1 × 5 + rgwAttackStrength[行动玩家] × RandomLong(0,3)`——operand 1 是 5 HP/单位的固定伤害份额，攻击随机部分无操作数）；目标恒为调用者敌槽（2013 直传 wEventObjectID，无操作数）；operand2 参考 case 未用 | shared（2007：break） | sequential | 两字段重编码；operand2 保留 | 32（magicId 344×29、360×3；damageUnits 0×1、10..800；operand2 全 0） |

真实数据核对：本批命中合计 648（新具名 130 = 0x0021 43 + 0x0031 1 + 0x0033 1 + 0x0034 1 + 0x0039 1 + 0x003A 1 + 0x0042 40 + 0x005B 1 + 0x0060 9 + 0x0066 32；复核 518 = 0x0007 434 + 0x004A 84 本已具名）。raw 命中从 #102 的 5682 降至 5552（−130）。0x0033/0x0034/0x003A 的三个目标（43072/38780/43072）均非零且落在 chunk 4（43538 条）内；0x0007 的 806 个非零 loss/flee 目标全部在范围内（41075×424+6186+13783 / 41073×378）；0x0066 的 magicId（344/360）落在 data-schema magic 视图证据并集 295..394 内，0x0042 的 magicId 除 24 外（323..381）同落该并集，24 如实按消费视图命名并记录（运行时引用不并入提取期视图证据）。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`scope`/`damage`/`battleSprite`/`amount`/`magicId`/`baseDamage`/`maxDamage`/`damageUnits` 全部沿用既有 word 类型、`enemyOperand` 沿用既有 signedWord 类型、`to` 沿用普通 reference（WORD 0=L0000，非可空绑定）；`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径，悬空拒绝仍在重建标签解析处）。**simulateMagic（0x0042，玩家侧）与 #104 预留的敌方 useMagic（0x0067）不同**：后者写 `rgEnemy[wEventObjectID].e.wMagic/wMagicRate`（2020-2022，operand1 WORD 0 运行时归一 10，无 PAL_BattleSimulateMagic 调用），字段形状与执行路径均不同，由 #104 另行核对（已核对，见下节）。

#### M5 战斗批2 条目（#104，战斗域收官）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；十个 case 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger/auto 开关逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`）；后继除 0x009C/0x009E 两跳转类为 conditionalJump（`if (operand != 0) wScriptEntry = operand − 1` + 函数尾 3085 `return wScriptEntry + 1`）外一律 sequential（break 后函数尾 return+1）。**敌侧寻址**：0x0067/0x006A/0x009E/0x009F 的目标敌槽=`g_Battle.rgEnemy[wEventObjectID]`（无操作数）；0x0069 的被调函数扫全部非空敌槽（无操作数选逃跑者）。**0x0092 operand 0 是第五种玩家寻址形状：1 基战斗玩家槽下标 `g_Battle.rgPlayer[operand0−1]`，WORD 0=不指定玩家**（2645-2648；区别于 roleOperand 的 0=调用者、playerRole 的 0 基直用、0x0015 memberIndex 的 0 基 rgParty 下标），定名 `playerOperand`。**magicId（0x0067）**：写入 `e.wMagic` 后由敌方行动选择消费（fight.c:4580），经 `rgObject[wMagic].magic.wMagicNumber` 按 magic 视图读取（fight.c:4671；0xFFFF=双手法术哨兵 fight.c:4663、0=无法术 fight.c:4656）——真实数据 303..397 中除 395/396/397 外均落 data-schema magic 视图证据并集 295..394 内，越上沿的 395/396/397 与 #103 的 24 同口径：按消费视图命名，运行时引用不并入提取期视图证据。**enemy 视图 objectId（0x009E/0x009F）**：两者 operand 0 均经 `rgObject[w].enemy.wEnemyID` 重载敌表行（script.c:2919/2968）；0x009E 另有 0/0xFFFF=召唤者自身对象回退（2889-2891）——真实数据 0x009E 非哨兵值 403..512、0x009F 全部 410..470，均落 data-schema enemy 视图冻结区间 398..550 内（脚本侧首次落区佐证，仍按 data-schema 边界不并入提取期证据）。signed/unsigned：本批两处 `(SHORT)` 转型——0x006B `strength`（2053 写 int 型 `g_Battle.iBlow`，真实数据 −3/−2 佐证有符号读法）与 0x009E `count`（2885，≤0 运行时归一 1 仅改内存、编码原样）；其余全部数值字段（magicId/magicRate/stealRate/result/playerOperand/copies/两个 objectId）无转型、全为无符号 WORD；0x0089 `result` 保留原始 BATTLERESULT 数值不硬编码枚举名（参考行号：0=Terminated、1=Lost、3=Won、0xFFFF=Fleed、1000=OnGoing、1001=PreBattle、1002=Pause，battle.h:33-40；0xFFFF 仅无符号可表）。**可空失败目标**：0x009C operand1/0x009E operand2 的 `!= 0` 守卫（2798-2800/2905-2907）便 WORD 0=不跳转，与 removeItem.shortageTarget 同形，定名 `failTarget` 复用可空引用绑定（null 编码 0、地址 0 引用拒绝）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0067 | `enemyUseMagic` | `magicId`=operand0（object.json magic 视图对象下标：2020 写 `rgEnemy[wEventObjectID].e.wMagic`，fight.c:4580 行动选择读取、4671 `rgObject[wMagic].magic` 消费；0=无法术、0xFFFF=双手法术哨兵）、`magicRate`=operand1（2021-2022 `(operand1 == 0) ? 10 : operand1` 运行时归一仅改内存、编码仍 0，与 0x0057/0x0086 归一口径同；fight.c:4657 `RandomLong(0,9) < wMagicRate` 骰面）；目标敌槽=rgEnemy[wEventObjectID]（无操作数）；operand2 参考 case 未用；与 0x0042 simulateMagic 的差异（无 PAL_BattleSimulateMagic 调用、写敌方字段非即时模拟）见 #103 预留与上节 | shared（2016：break） | sequential | 两字段重编码；operand2 保留 | 251（magicId 0×37、0xFFFF×61（哨兵）、303..397×153（除 395/396/397 外均落 magic 并集 295..394）；magicRate 0×241、3/4/5/7/10 计 10；operand2 全 0） |
| 0x0069 | `enemyEscape` | 无操作数映射：2039 `PAL_BattleEnemyEscape()`（battle.c:1376-1435）播放音效 45 后逐帧把**全部非空敌槽**向左移出屏幕，尾随 500ms 延迟并把 `BattleResult` 置 0（Terminated，battle.c:1434）——无操作数选逃跑者 | shared（2035：break） | sequential | 三操作数保留源 WORD | 4（operand0 0×3、1×1——非零仅参与共享前奏的对象选择，case 体不消费，如实保留；operand1/2 全 0） |
| 0x006A | `stealFromEnemy` | `stealRate`=operand0（**无符号** WORD：fight.c:5254 `(RandomLong(0,10) <= wStealRate || wStealRate == 0)`——0 恒成功、骰面 ≤ 率即成功）；2046 `PAL_BattleStealFromEnemy(wEventObjectID, operand0)`（fight.c:5193）：行动玩家（wMovingPlayerIndex）走向并偷取目标敌槽，赃物来自敌表 `wStealItem/wStealItemMoney`；目标敌槽=rgEnemy[wEventObjectID]（无操作数）；operand1、2 参考 case 未用 | shared（2042：break） | sequential | stealRate 重编码；operand1、2 保留 | 1（stealRate 6；operand1/2 全 0） |
| 0x006B | `blowAwayEnemies` | `strength`=operand0（**有符号**：2053 `g_Battle.iBlow = (SHORT)(operand0)` 写入 int 型 iBlow；fight.c:2681/2901 施法动画期间每帧给全体非空敌加随机风偏移——正=向右下 `RandomLong(0,iBlow)`、负=向左上 `RandomLong(iBlow,0)`，fight.c:3608/4576 战斗边界清零；真实数据 −3/−2 佐证）；无目标操作数（全体非空敌槽）；operand1、2 参考 case 未用 | shared（2049：break） | sequential | strength 重编码；operand1、2 保留 | 2（strength 0xFFFD(−3)、0xFFFE(−2)；operand1/2 全 0） |
| 0x0089 | `setBattleResult` | `result`=operand0（2563 `g_Battle.BattleResult = operand0` 原样赋值；保留原始 BATTLERESULT 数值不硬编码枚举名（status 行号同口径），参考行号 battle.h:33-40：0=Terminated、1=Lost、3=Won、0xFFFF=Fleed、1000=OnGoing、1001=PreBattle、1002=Pause；0xFFFF 仅无符号 WORD 可表）；operand1、2 参考 case 未用 | shared（2559：break） | sequential | result 重编码；operand1、2 保留 | 4（result 全 0（Terminated）；operand1/2 全 0） |
| 0x008A | `enableAutoBattle` | 无操作数映射：2570 `gpGlobals->fAutoBattle = TRUE`（下次战斗自动战斗；0x0007 startBattle 3334 战后清除，不改写返回保存入口） | shared（2566：break） | sequential | 三操作数保留源 WORD | 1（三操作数全 0） |
| 0x0092 | `showMagicCastAnimation` | `playerOperand`=operand0（第五种玩家寻址形状：**1 基战斗玩家槽下标**，2645-2648 `rgPlayer[operand0−1]` 播施法前动画并定帧 6；WORD 0=不指定玩家，2651-2658 的全队变色循环仍执行——0=无，非 roleOperand 的 0=调用者）；无 magic 操作数（动画纯表现）；非战斗时（2643 `fInBattle` 守卫）整 case 无操作；operand1、2 参考 case 未用 | shared（2639：break） | sequential | playerOperand 重编码；operand1、2 保留 | 1（playerOperand 2；operand1/2 全 0） |
| 0x009C | `enemyDivide` | `copies`=operand0（2805-2811：**新增副本数**，WORD 0 运行时归一 1 仅改内存、编码仍 0；总份数=copies+1，每份 HP=(原 HP+copies)/(copies+1)，副本源=调用者敌槽 `rgEnemy[wEventObjectID]` 的对象/敌表行/三入口脚本，被分裂槽=前奏选择的 `rgEnemy[wCurEventObjectID]`（operand0=0/0xFFFF 时两者同为调用者））、`failTarget`=operand1（可空：2792 剩余非空敌槽数 ≠1 或被分裂槽 HP≤1 时 2798-2800 `wScriptEntry = operand1 − 1`；WORD 0=不跳转，null 编码 0、地址 0 引用拒绝，removeItem.shortageTarget 同形）；operand2 参考 case 未用；成功路径后续重算 wMaxEnemyIndex、重载战斗精灵并播分裂位移动画 | shared（2778：break） | conditionalJump（不能分裂→failTarget，否则分裂后 next） | 两字段重编码；operand2 保留 | 2（copies 全 0（运行时归一 1 的编码形态）；failTarget 全 null；operand2 全 0） |
| 0x009E | `enemySummon` | `objectId`=operand0（object.json **enemy 视图**对象下标：2884 直读、2887-2890 0/0xFFFF=召唤者自身对象回退、2918 写入空槽 `wObjectID`、2919 `rgObject[w].enemy.wEnemyID` 重载敌表行与三入口脚本；真实数据非哨兵值全落 enemy 视图冻结区间 398..550）、`count`=operand1（**有符号**：2885 `((SHORT)(operand1) <= 0) ? 1 : (SHORT)operand1`——≤0 运行时归一 1 仅改内存、编码原样）、`failTarget`=operand2（可空：2900 空槽数不足、战斗隐藏中或召唤者 Sleep/Paralyzed/Confused 时 2905-2907 跳；WORD 0=不跳转）；召唤者=rgEnemy[wEventObjectID]（2876 起播施法帧动画，无操作数）；成功路径逐一填空槽、播音效 212 与渐变 | shared（2872：break） | conditionalJump（不能召唤→failTarget，否则召唤后 next） | 三字段重编码；无保留操作数 | 32（objectId 0×2（自身回退）、403/407/419/433×6/441×2/442/448×3/452/453×2/490×2/492×2/493/503/511/512×6 全落 398..550；count 0×18（归一 1 编码形态）、2×12、4×2；failTarget null×26、42526/42548/42557/42968/42988/42998 各 1 全在 43538 条内） |
| 0x009F | `enemyTransform` | `objectId`=operand0（object.json **enemy 视图**对象下标：2967 覆写 `rgEnemy[wEventObjectID].wObjectID`、2968-2969 经 `rgObject[operand0].enemy.wEnemyID` 重载敌表行、2971 **保留当前 HP**、2972 定帧 0，随后播渐变动画与音效 47；2960-2964 战斗隐藏中或 Sleep/Paralyzed/Confused 时静默无操作——无跳转操作数）；目标敌槽=rgEnemy[wEventObjectID]（无操作数）；operand1、2 参考 case 未用 | shared（2956：break） | sequential | objectId 重编码；operand1、2 保留 | 4（objectId 410/434/461/470 全落 398..550；operand1/2 全 0） |

真实数据核对：本批命中合计 302（全部新具名：0x0067 251 + 0x0069 4 + 0x006A 1 + 0x006B 2 + 0x0089 4 + 0x008A 1 + 0x0092 1 + 0x009C 2 + 0x009E 32 + 0x009F 4）。raw 命中从 #103 的 5552 降至 5222（−302，含菜单域 28，见下节）。0x0067 的 magicId 中 303..394 段落 data-schema magic 视图证据并集、395/396/397 越上沿如实记录（按消费视图命名，运行时引用不并入提取期证据）；0x009E/0x009F 的 enemy 视图 objectId 全部落在冻结区间 398..550 内（脚本侧首次佐证，不并入提取期证据）；0x009E 的 6 个非零 failTarget 全部在 chunk 4（43538 条）内、0x009C 的 2 个与 0x009E 其余 26 个为 WORD 0（null 编码）；0x006B 的 −3/−2 佐证 `(SHORT)` 有符号读法；0x0067 的 magicRate 0×241 为运行时归一 10 的编码形态。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`storeId`（菜单域）/`magicId`/`magicRate`/`stealRate`/`result`/`playerOperand`/`copies`/`objectId` 沿用既有 word 类型、`strength`/`count` 沿用既有 signedWord 类型、`failTarget` 沿用既有可空 battleTarget 绑定；`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径，悬空拒绝仍在重建标签解析处）。**战斗域（#77 六域之一，共 22 个 opcode：0x0007/0x0021/0x0031/0x0033/0x0034/0x0039/0x003A/0x0042/0x004A/0x005B/0x0060/0x0066（#78 具名或 #103）＋0x0067/0x0069/0x006A/0x006B/0x0089/0x008A/0x0092/0x009C/0x009E/0x009F（#104））至此全部具名。**

#### M5 菜单域条目（#104，菜单域收官）

本批核对结论：两 case 均位于共享 `PAL_InterpretInstruction`（trigger/auto 开关无对应 case，executor `shared`），后继均 sequential（break 后 3085 return+1）；两 case 均先 1161/1170 `PAL_MakeScene()` + 1162/1171 `VIDEO_UpdateScreen(NULL)` 重绘场景再进菜单，菜单本身阻塞至玩家关闭（uigame.c:1672/1755 的 `PAL_ReadMenu`/`PAL_ItemSelectMenu` 循环）。**0x0026 operand 0 = 商店表行号**（#83 提取的 store.json=DATA chunk 0，本机 21 行；`PAL_BuyMenu(wStoreNum)` 逐槽读 `lprgStore[wStoreNum].rgwItems[0..8]` 构造商品列表并按 `rgObject[item].item.wPrice` 标价，uigame.c:1629/1648/1679），定名 `storeId` 并入字段命名约定表（与 enemyTeamId 同为表行号 Id 后缀形状；#103 的 0x0034 发商店表 0 随机一件是读侧先例）；**0x0027 无操作数**（出售列表来自玩家库存 `PAL_ItemSelectMenu(kItemFlagSellable)`，uigame.c:1777，非商店表行）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0026 | `showBuyMenu` | `storeId`=operand0（商店表行号：1163 `PAL_BuyMenu(operand0)`，uigame.c:1629 形参 wStoreNum、1648 `lprgStore[wStoreNum].rgwItems[i]` 逐槽取商品（元素为 object.json item 视图 itemId，0=空槽止）、1679 `rgObject[w].item.wPrice` 标价与现金比较；购买经确认菜单扣现入库）；operand1、2 参考 case 未用 | shared（1157：1161 重绘场景；break） | sequential | storeId 重编码；operand1、2 保留 | 23（storeId 1×2、2×2、3×2、4..20 各 1——全部在 21 行表（0..20）内；operand1/2 全 0） |
| 0x0027 | `showSellMenu` | 无操作数映射：1172 `PAL_SellMenu()`（uigame.c:1755-1790）按玩家库存 `PAL_ItemSelectMenu(kItemFlagSellable)` 列可售物并按半价出售——无商店表行参与；三操作数保留源 WORD | shared（1166：1170 重绘场景；break） | sequential | 三操作数保留源 WORD | 5（三操作数全 0） |

真实数据核对：菜单域命中合计 28（0x0026 23 + 0x0027 5，全部新具名）；storeId 1..20 全部在 store.json 21 行（0..20）内。export→rebuild SHA `f36cf853…b39e72a` 字节一致。**菜单域（#77 六域之一，共 2 个 opcode：0x0026、0x0027）至此全部具名。**

#### M5 场景移动批1 条目（#105，场景移动域-移动批1）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；十个 opcode 位全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3641 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继一律 sequential（break 后函数尾 3085 `return wScriptEntry + 1`；0x0010/0x0011 未到达目的地的 `wScriptEntry--`（684/697/702）是同一条目的运行时重试停等，非跳转，与 sequential 声明不冲突）。**0x000B-0x000E 是单一 fall-through「walk one step」块**：四个 case 落入同一注释块（652-661），方向由 `wDirection = opcode − 0x000B` 差值决定（659，0..3 = kDirSouth/kDirWest/kDirNorth/kDirEast，palcommon.h:92-95），调用者对象 `pEvtObj`/`wEventObjectID` 走一步（660 `PAL_NPCWalkOneStep(wEventObjectID, 2)`），case 体不消费任何操作数（真实数据三操作数全 0 佐证）——既有 `direction`(opcodeOffset, base 0x000B) 绑定复核维持。**对象寻址分两形状**：0x000B-0x000F/0x0010/0x0011/0x0014 作用于调用者对象（pEvtObj/wEventObjectID，无对象操作数）；0x0013/0x0012 写共享前奏的 `pCurrent->x/y`（626-638：operand 0 为 0/0xFFFF=调用者、否则 operand0−1 的 1 基 event-object 表下标，含 Dream 2.11 的 >0x9000 回绕 hack，与 setState 族同口径）→ 0x0013 复核维持 `eventObjectId`(word,0)，0x0012 新增同形字段。**0x0012 锚点语义**：`pCurrent->x = operand1 + PAL_X(viewport) + PAL_X(partyoffset)`（710-713），即相对队伍当前地图位置（viewport+partyoffset）的偏移；写入目标是 WORD 型 x/y、无 `(SHORT)` 转型（真实数据唯一命中的 0xFF80 是原版作者把 −128 相对偏移按无符号 WORD 编码的形态，提取按无符号原样保留，同 #103 越表 380 的「原版数据事实如实保留」口径），定名 `xOffset`/`yOffset` 区别于 0x0013 的绝对 `x`/`y`。signed/unsigned：本批十个 case 全无 `(SHORT)` 转型，全部数值字段为无符号 WORD（0x000F 的 0xFFFF 不变哨兵按普通 WORD 保留、0 是显式值非哨兵；0x0014 的 kDirSouth 是硬编码常量非操作数）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x000B-0x000E | `walkStep`（#78 已具名，四 opcode 位复核） | `direction`=opcode−0x000B（opcodeOffset，659，单一 fall-through 块 652-661；0..3=south/west/north/east）；case 体不读操作数，三操作数保留源 WORD | shared（652：660 `PAL_NPCWalkOneStep(wEventObjectID, 2)`——调用者对象走一步；661 break） | sequential | direction 编码时校验 opcode 一致；三操作数保留源 WORD | 385（0x000B×94/0x000C×95/0x000D×91/0x000E×105，即方向 0..3 各自命中；三操作数全 0 佐证 case 体不读） |
| 0x000F | `face`（#78 已具名，复核） | `direction`=operand0（667-670：非 0xFFFF 才写 `pEvtObj->wDirection`，0 为显式朝向非哨兵）、`frame`=operand1（671-674：非 0xFFFF 才写 `pEvtObj->wCurrentFrameNum`）；两者均无符号 WORD、哨兵按原值编码；作用于调用者对象（无对象操作数）；operand2 参考 case 未用 | shared（663：break） | sequential | 两字段重编码；operand2 保留 | 493（direction 0×157/1×84/2×110/3×137/0xFFFF×5（不变哨兵）；frame 0×437、1..23 共 56（无 0xFFFF）；operand2 全 0） |
| 0x0010 | `walkTo`（#78 已具名，复核） | `x`=operand0、`y`=operand1、`h`=operand2（681-682 `PAL_NPCWalkTo(wEventObjectID, x, y, h, 3)` 直走至绝对地图坐标，速度 3 为常量非操作数；未到达时 684 `wScriptEntry--` 同条目重试）；行走者=调用者对象（非前奏 pCurrent，无对象操作数） | shared（677：break） | sequential | 三字段重编码；无保留操作数 | 387（x 5..55、y 7..116、h 0×212/1×175） |
| 0x0011 | `walkToSlow`（新） | `x`=operand0、`y`=operand1、`h`=operand2（692 帧奇偶门控 `(wEventObjectID&1)^(dwFrameNum&1)` 隔帧行走 + 694 `PAL_NPCWalkTo(wEventObjectID, x, y, h, 2)` 速度 2，两常量均非操作数；未到达时 697/702 同条目重试）；行走者=调用者对象（无对象操作数）；与 0x0010 仅速度与隔帧差异，操作数形状全同 | shared（688：break） | sequential | 三字段重编码；无保留操作数 | 421（x 1..57、y 7..115、h 0×221/1×200——与 0x0010 同形的坐标/层分布） |
| 0x0012 | `setObjectPositionRelativeToParty`（新） | `eventObjectId`=operand0（共享前奏选 pCurrent：0/0xFFFF=调用者、否则 operand0−1 的 1 基下标，626-638；无 ≠0 守卫）、`xOffset`=operand1、`yOffset`=operand2（710-713 `pCurrent->x = operand1 + PAL_X(viewport) + PAL_X(partyoffset)`、y 同理——相对队伍当前地图位置（viewport+partyoffset）的偏移写入 WORD 型 x/y；无 `(SHORT)` 转型，真实 0xFF80 为作者以无符号 WORD 编码的 −128）；case 体不写层（sLayer） | shared（706：break） | sequential | 三字段重编码；无保留操作数 | 1（eventObjectId 84、xOffset 0xFF80(65408)、yOffset 56） |
| 0x0013 | `setObjectPosition`（#78 已具名，复核维持） | `eventObjectId`=operand0（同 0x0012 前奏选 pCurrent，#78 重命名先例，复核成立：case 写 `pCurrent->x/y` 非调用者）、`x`=operand1、`y`=operand2（720-721 绝对地图坐标直写）；无层写入 | shared（716：break） | sequential | 三字段重编码；无保留操作数 | 48（eventObjectId 9..1783 全部非零非哨兵（无 0/0xFFFF，明确指定对象）；x 85..1616、y 0..1712 多为 16 的倍数） |
| 0x0014 | `setObjectGesture`（#78 已具名，复核维持） | `frame`=operand0（728 `pEvtObj->wCurrentFrameNum = operand0`；729 方向硬编码 `kDirSouth`（palcommon.h:92，值 0）**非操作数**——无 direction 字段）；作用于调用者对象 pEvtObj（无对象操作数）；operand1、2 参考 case 未用 | shared（724：break） | sequential | frame 重编码；operand1、2 保留 | 475（frame 0..35；operand1/2 全 0） |

真实数据核对：本批命中合计 2210（新具名 422 = 0x0011 421 + 0x0012 1；复核 1788 = walkStep 385 + face 493 + walkTo 387 + setObjectPosition 48 + setObjectGesture 475 本已具名）。raw 命中从 #104 的 5222 降至 4800（−422）。本批无引用字段、无跳转目标，无悬空拒绝面变化；0x0012 唯一命中的 0xFF80 按无符号 WORD 原样往返（有符号解读无参考转型佐证，不设 signedWord）。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`eventObjectId`/`xOffset`/`yOffset`/`x`/`y`/`h`/`direction`/`frame` 全部沿用既有 word 类型、walkStep 的 `direction` 沿用既有 opcodeOffset 绑定；`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径，悬空拒绝仍在重建标签解析处）。场景移动域（#77 六域之一，共 75 个 opcode 拆 8 批）本批为移动批1，剩余移动批2/3（#106/#107）、对象批1/2（#108/#109）、屏幕批1/2（#110/#111）、音乐场景特殊（#112）待后续批次。

#### M5 场景移动批2 条目（#106，场景移动域-移动批2）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；十个 opcode 位全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3641 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继除 0x0038 teleportParty 的失败分支（1568-1569 置脚本失败标记 + `wScriptEntry = to − 1`，playerFlee/changeCash 同形普通引用跳转，WORD 0=L0000）为 conditionalJump 外一律 sequential（break 后函数尾 3085 `return wScriptEntry + 1`；ride 族的同步到达循环在被调函数内部（script.c:203 起），非脚本条目重试停等）。**朝向/手势双形状复核**：0x0015 setPartyFacing 写队伍全局——`wPartyDirection = operand0`（736）且 `rgParty[operand2].wFrame = direction×3 + operand1`（737-738）：direction 是队伍朝向、gesture 是该朝向 3 帧行内偏移（非绝对帧）、memberIndex 是 0 基 rgParty 槽（无 −1，数组上限 MAX_PLAYABLE_PLAYER_ROLES=5，palcommon.h:48，真实数据仅 0..2）——三字段映射复核维持；0x0016 是对象侧变体但 operand2 是**绝对帧**（748 `pCurrent->wCurrentFrameNum = operand2`）非 gesture 行偏移，定名 frame 区别，且 operand0 兼任门控（745 `!= 0` 守卫：0 是整体跳过的 no-op 形态，0xFFFF 仍为调用者且生效，与前奏 0=调用者口径在 0 值上分岔）。0x0046 复核：viewport = (x×32+h×16, y×16+h×8) − partyoffset（1679-1685）、队员自 partyoffset 起按 wPartyDirection 相关 16/8 像素步长排布并同步 rgTrail（1690-1699）——x/y/h 与 walkTo/ride 族同一 tile 坐标系，三字段映射维持。0x004C 三操作数：chaseRange（敏感半径 tile 单位，script.c:393 `|dx|+2|dy| < range×32×gpGlobals->wChaseRange`；WORD 0 运行时归一 8 仅改内存条目 1740-1742）、speed（0→4 同口径，1745-1747）、floating 真值 WORD（非零无视障碍，script.c:437-443）。0x0062/0x0063 仅 operand0=持续周期数（wChasespeedChangeCycles，play.c:235-237 每次游戏更新递减、归零时全局范围复位 1），全局 wChaseRange 硬编码 0/3（1972/1980）非操作数——0=追捕块被 341 的 `!= 0` 守卫整体跳过（暂停）、3=敏感半径×3（加速），两 case 仅此常量差异故保持两动词。signed/unsigned：**仅 0x006C 两处 `(SHORT)` 转型（2060-2061 xOffset/yOffset，本批唯二有符号字段）**，其余全部无符号 WORD。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0015 | `setPartyFacing`（#78 已具名，复核维持） | `direction`=operand0（736 写 `gpGlobals->wPartyDirection`）、`gesture`=operand1（737-738 `rgParty[memberIndex].wFrame = direction×3 + gesture`，朝向 3 帧行内偏移非绝对帧）、`memberIndex`=operand2（737 0 基 rgParty 槽直用无 −1） | shared（732：break） | sequential | 三字段重编码；无保留操作数 | 915（direction 0×461/1×125/2×156/3×173；gesture 0×681、1..26 共 234——最大帧 3×3+26=35，与 setObjectGesture 的 frame 0..35 同一帧空间佐证；memberIndex 0×820/1×88/2×7） |
| 0x0016 | `setObjectFacing`（新） | `eventObjectId`=operand0（前奏选 pCurrent：0/0xFFFF=调用者、否则 operand0−1 1 基，626-638；**另有 ≠0 门控**：0 整体跳过 745）、`direction`=operand1（747 写 `pCurrent->wDirection`）、`frame`=operand2（748 写 `pCurrent->wCurrentFrameNum`，绝对帧） | shared（741：break） | sequential | 三字段重编码；无保留操作数 | 730（eventObjectId 0×3（no-op 门控形态）/0xFFFF×3（调用者）/1 基 11..4810×724；direction 0×377/1×105/2×105/3×142/4×1（4=kDirUnknown，palcommon.h:96，唯一一例）；frame 0×542、1..27 共 188） |
| 0x0038 | `teleportParty`（新） | `to`=operand0（失败跳转目标：1558-1561 非战斗且当前场景 `rgScene[wNumScene−1].wScriptOnTeleport`≠0 时嵌套 `PAL_RunTriggerScript(teleport, 0xFFFF)` 后顺序；战斗中或无传送脚本时 1568 置脚本失败标记并 1569 `wScriptEntry = to − 1` 跳转；普通引用 WORD 0=L0000，非可空 failTarget 绑定） | shared（1554：break） | conditionalJump（失败→to，成功→next） | `to` 解析标签重编码；operand1、2 保留 | 1（to=38780=L977C，在 43538 条内且非零） |
| 0x003F | `rideEventObjectSlow`（新） | `x`=operand0、`y`=operand1、`h`=operand2（1609-1610 `PAL_PartyRideEventObject(wEventObjectID, x, y, h, 2)`——队伍骑调用者对象至绝对 tile 坐标；速度 2 为常量非操作数；被调函数内部同步循环至到达，非脚本条目重试停等） | shared（1605：break） | sequential | 三字段重编码；无保留操作数 | 2（x 30/47、y 66/77、h 全 0） |
| 0x0044 | `rideEventObject`（新） | 同 0x003F 操作数形状，速度常量 4（1654）；被调按 x×32+h×16 / y×16+h×8 换算像素（script.c:241/304），与 0x0046 同一 tile 坐标系 | shared（1650：break） | sequential | 三字段重编码；无保留操作数 | 112（x 8..56、y 19..102、h 0×49/1×63） |
| 0x0046 | `setPartyPos`（#78 已具名，复核维持） | `x`=operand0、`y`=operand1、`h`=operand2（1679-1685 viewport=(x×32+h×16, y×16+h×8)−partyoffset；1690-1699 队员自 partyoffset 起按 wPartyDirection 相关 16/8 像素步长（1672-1677：West/South x +16、North/East −16；West/North y +8、South/East −8）排布并同步 rgTrail——三字段映射维持） | shared（1665：break） | sequential | 三字段重编码；无保留操作数 | 1065（x 0..57、y 0..120（各含 1 个 0）、h 0×582/1×483） |
| 0x004C | `chasePlayer`（新） | `chaseRange`=operand0（敏感半径 tile 单位：script.c:393 `\|dx\|+2\|dy\| < range×32×gpGlobals->wChaseRange` 才追；WORD 0 运行时归一 8 仅改内存条目 1740-1742）、`speed`=operand1（0→4 同口径，1745-1747）、`floating`=operand2（真值 WORD：非零在被调内无视障碍，script.c:436-444）；追捕者=调用者对象 wEventObjectID（1750） | shared（1733：break） | sequential | 三字段重编码；无保留操作数 | 21（chaseRange 0×2/6..128；speed 0×10/2/4/6/8；floating 0×17/1×2/0xFFFF×2——真值 WORD 0/1/0xFFFF 形状同 waitUpdate.updateGestures） |
| 0x0062 | `pauseEnemyChasing`（新） | `cycles`=operand0（1971 `wChasespeedChangeCycles = operand0`；全局 `wChaseRange = 0`（1972）硬编码非操作数——追捕块被 script.c:341 的 `!= 0` 守卫整体跳过即暂停；play.c:235-237 每次游戏更新递减、归零时 wChaseRange 复位 1） | shared（1967：break） | sequential | cycles 重编码；operand1、2 保留 | 1（cycles 600） |
| 0x0063 | `speedUpEnemyChasing`（新） | 同 0x0062 周期形状，全局 `wChaseRange = 3`（1980）——每只怪物的敏感半径×3（script.c:393），仅常量差异故保持两动词 | shared（1975：break） | sequential | cycles 重编码；operand1、2 保留 | 1（cycles 600） |
| 0x006C | `walkNpcStep`（新） | `eventObjectId`=operand0（前奏选 pCurrent/wCurEventObjectID：0/0xFFFF=调用者、否则 1 基，626-638；无守卫）、`xOffset`=operand1、`yOffset`=operand2（2060-2061 `pCurrent->x += (SHORT)operand1`、y 同理——**本批唯二 (SHORT) 转型**，像素偏移加到 WORD 型 x/y 上；2062 `PAL_NPCWalkOneStep(wCurEventObjectID, 0)` 走一步动画） | shared（2056：break） | sequential | 三字段重编码；无保留操作数 | 295（eventObjectId 0×1/0xFFFF×226（调用者）/1 基 9..3348×68；xOffset 有符号 −32..16（0×46，65504=−32）、yOffset −12..16（0×16，65524=−12）） |

真实数据核对：本批命中合计 3143（新具名 1163 = 0x0016 730 + 0x0038 1 + 0x003F 2 + 0x0044 112 + 0x004C 21 + 0x0062 1 + 0x0063 1 + 0x006C 295；复核 1980 = setPartyFacing 915 + setPartyPos 1065 本已具名）。raw 命中从 #105 的 4800 降至 3637（−1163）。0x0038 唯一命中目标 38780 非零且落在 chunk 4（43538 条）内，本批唯一引用字段；无悬空拒绝面变化，`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径）。0x006C 的有符号偏移按补码双向往返（−16 编码 0xFFF0、0xFFE0 解码 −32），与本批其余无符号 WORD 字段互不混用；0x0016 的 eventObjectId 0（no-op 门控）与 0xFFFF（调用者）两形态均有真实命中且都按 WORD 原样往返。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`chaseRange`/`speed`/`floating`/`cycles`/`eventObjectId`/`direction`/`frame`/`x`/`y`/`h`/`to` 沿用既有 word/reference 绑定，`xOffset`/`yOffset` 复用 signedWord（0x004C 的字数归一与 0x0057/0x0086 同口径：编码仍 0）。场景移动域（#77 六域之一，共 75 个 opcode 拆 8 批）本批为移动批2，剩余移动批3（#107）、对象批1/2（#108/#109）、屏幕批1/2（#110/#111）、音乐场景特殊（#112）待后续批次。

#### M5 场景移动批3 条目（#107，场景移动域-移动批3，移动子域收官）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；十一个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3641 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继全部 sequential（break 后函数尾 3085 `return wScriptEntry + 1`）——partyWalkTo 三速与 ride 三速的到达循环在被调函数内部同步完成（script.c:133-197 / 203-310），0x007C/0x0082 未到达目的地的 `wScriptEntry--`（2268/2273/2444）与 0x0010/0x0011 同条目重试停等同口径，0x007F 的滚动 do-while（2331-2377）在被解释条目内联阻塞完成，均非跳转。**速度阶梯命名**：0x007C/0x0082（walkToFast/walkToFastest，速度 4/8，0x007C 另有 0x0011 同形帧奇偶门控 2263）补齐 NPC 侧四速阶梯（既有 0x0010/0x0011 = 3/2）；0x0070/0x007A/0x007B（partyWalkToSlow/partyWalkTo/partyWalkToFast，2/4/8）为队伍侧三速；0x0097（rideEventObjectFast，8）补齐骑乘侧三速（0x003F/0x0044 = 2/4）——速度全部为常量非操作数，仅常量差异按 #105/#106 先例保持独立动词。**0x006E 移动的是队伍视口+层**：`viewport += (SHORT)xOffset/(SHORT)yOffset` 像素偏移（2103-2105）、`wLayer = layerUnits × 8`（2107，层全局在 scene.c:225 参与队员绘制 y 偏移与遮挡序），并同步 rgTrail（2095-2101）——operand2 是层单位（真实 0..10），非 walkTo 族的 tile 半行 h（本 case 无 x×32/y×16 换算）。**0x007D 是无动画的对象相对位移**：与 0x006C walkNpcStep 完全同形的前奏 pCurrent + (SHORT) 像素偏移（2281-2282），但不调 PAL_NPCWalkOneStep。**0x007F 视口三形态**（2296-2378）：偏移 0/0 = 复位常态（partyoffset 回 160,112，frames≠0xFFFF 时重绘，2296-2318）；否则按 (SHORT) 像素偏移逐帧滚动 frames 个单位（WORD 0 仍执行 do-while 单趟，2331-2377）；frames=0xFFFF 切换同一 WORD 为无符号 tile 坐标绝对摆放视口（x×32−160, y×16−112，2333-2339）——字段按有符号滚动形态命名，哨兵形态按 WORD 原样往返。**0x0098 follower 是 MGO 精灵号非角色**：operand0/1 选 follower 槽 1/2（>0 形态）无 −1 直写 `rgParty[...].wPlayerRole`（2716-2735），但 follower 的 wPlayerRole 在 res.c:337 被直接作 MGO.MKF 精灵号消费（真实 82/83 远超 MAX_PLAYER_ROLES=6，角色读法越界），按消费视图定名 `spriteNum0/spriteNum1`（data-schema `spriteNum` 同口径，区别 0x0075 setParty 的 1 基 roleId）；WORD 0 跳过该槽、全 0 清除全部 follower（2736-2739 nFollower=0）。**0x00A1 无操作数**：全体队员与 rgTrail 收拢到领队位置（队员 y−1 叠放，3004-3015）。signed/unsigned：**六处 `(SHORT)` 转型（0x006E/0x007D/0x007F 各一对 xOffset/yOffset，2104-2105/2281-2282/2326-2327）**，其余全部无符号 WORD（0x007F frames 的 0xFFFF 哨兵仅无符号可表）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x006E | `moveParty`（新） | `xOffset`=operand0、`yOffset`=operand1（**有符号**：2104-2105 `viewport += (SHORT)operand` 像素偏移，2095-2101 先同步 rgTrail）、`layerUnits`=operand2（2107 `wLayer = operand2 × 8`；scene.c:225 队员绘制 y+layer+10；真实 0..10，非 tile 半行 h——本 case 无 x×32/y×16 换算）；2109-2112 偏移非零才 PAL_UpdatePartyGestures(TRUE) | shared（2091：break 2113） | sequential | 三字段重编码；无保留操作数 | 313（xOffset −32..16（65504=−32×2、65520=−16×53）；yOffset −16..24；layerUnits 0×294、1..10×19） |
| 0x0070 | `partyWalkToSlow`（新） | `x`=operand0、`y`=operand1、`h`=operand2（2129 `PAL_PartyWalkTo(x, y, h, 2)`——全队走至绝对 tile 坐标，速度 2 常量非操作数；被调 133-197 内部阻塞至到达） | shared（2125：break 2130） | sequential | 三字段重编码；无保留操作数 | 33（x 8..50、y 16..111、h 0×20/1×13） |
| 0x007A | `partyWalkTo`（新） | 同 0x0070 操作数形状，速度常量 4（2249） | shared（2245：break 2250） | sequential | 三字段重编码；无保留操作数 | 381（x 5..56、y 8..105、h 0×187/1×194） |
| 0x007B | `partyWalkToFast`（新） | 同 0x0070 操作数形状，速度常量 8（2256） | shared（2252：break 2257） | sequential | 三字段重编码；无保留操作数 | 56（x 10..49、y 15..96、h 0×27/1×29） |
| 0x007C | `walkToFast`（新） | `x`=operand0、`y`=operand1、`h`=operand2（2263 帧奇偶门控 `(wEventObjectID&1)^(dwFrameNum&1)` 隔帧行走 + 2265-2266 `PAL_NPCWalkTo(wEventObjectID, x, y, h, 4)` 速度 4，两常量均非操作数；未到达时 2268/2273 同条目重试）；行走者=调用者对象（无对象操作数，与 0x0010/0x0011 同形） | shared（2259：break 2275） | sequential | 三字段重编码；无保留操作数 | 32（x 6..51、y 20..115、h 0×21/1×11） |
| 0x007D | `moveObject`（新） | `eventObjectId`=operand0（前奏选 pCurrent：0/0xFFFF=调用者、否则 operand0−1 1 基，626-638；无守卫）、`xOffset`=operand1、`yOffset`=operand2（**有符号**：2281-2282 `pCurrent->x += (SHORT)operand1`、y 同理——与 0x006C walkNpcStep 同形但不调 PAL_NPCWalkOneStep，无动画纯位移） | shared（2277：break 2283） | sequential | 三字段重编码；无保留操作数 | 277（eventObjectId 0xFFFF×210（调用者）/1 基 57..3114×67；xOffset −128..48（65280=−128×2、65504=−32×7）；yOffset −80..40） |
| 0x007F | `moveViewport`（新） | `xOffset`=operand0、`yOffset`=operand1（**有符号**：2326-2327 `(SHORT)` 读入逐帧像素滚动量）、`frames`=operand2（2377 `(SHORT)` 上界，WORD 0 仍执行 do-while 单趟；**0xFFFF=绝对摆放哨兵**：2333-2339 同两 WORD 改按无符号 tile 坐标 `viewport=(x×32−160, y×16−112)`）；偏移 0/0=复位常态形态（2296-2318：partyoffset 回 160,112、frames≠0xFFFF 时重绘）；三形态共用字段按滚动形态命名 | shared（2292：break 2379） | sequential | 三字段重编码；无保留操作数 | 317（复位 0/0×128、滚动 180、绝对 9；xOffset −16..192、yOffset −96..96；frames 0×141、1..150、0xFFFF×10） |
| 0x0082 | `walkToFastest`（新） | `x`=operand0、`y`=operand1、`h`=operand2（2441-2442 `PAL_NPCWalkTo(wEventObjectID, x, y, h, 8)` 速度 8 无门控；未到达时 2444 同条目重试）；行走者=调用者对象（无对象操作数） | shared（2437：break 2446） | sequential | 三字段重编码；无保留操作数 | 97（x 8..61、y 8..112、h 0×64/1×33） |
| 0x0097 | `rideEventObjectFast`（新） | 同 0x003F/0x0044 操作数形状，速度常量 8（2707-2708）；被调内部同步循环至到达（script.c:203 起） | shared（2703：break 2709） | sequential | 三字段重编码；无保留操作数 | 1（x 45、y 120、h 0） |
| 0x0098 | `setFollower`（新） | `spriteNum0`=operand0、`spriteNum1`=operand1（2716-2735 循环 i=0..1：>0 时 2721 `rgParty[wMaxPartyMemberIndex+i+1].wPlayerRole = operand[i]` **无 −1** 并重载精灵、自 rgTrail[3+i] 入场；follower 的 wPlayerRole 在 res.c:337 直接作 MGO.MKF 精灵号消费——真实 82/83 远超 MAX_PLAYER_ROLES=6，角色读法越界，按消费视图定名 spriteNum（data-schema `spriteNum` 同口径，区别 0x0075 setParty 的 1 基 roleId）；WORD 0 跳过该槽、全 0 清除全部 follower（2736-2739 nFollower=0））；operand2 参考 case 未用 | shared（2711：break 2740） | sequential | 两字段重编码；operand2 保留 | 3（(0,0)×1 清除形态、(82,0)×1、(83,0)×1 槽 1 精灵；operand2 全 0） |
| 0x00A1 | `gatherParty`（新） | 无操作数映射：3004-3011 全部 rgTrail 槽收拢到领队位置、3012-3014 全体队员 `x=领队.x, y=领队.y−1` 叠放、3015 PAL_UpdatePartyGestures(FALSE)；三操作数保留源 WORD | shared（3000：break 3016） | sequential | 三操作数保留源 WORD | 50（三操作数全 0） |

真实数据核对：本批命中合计 1560（全部新具名：0x006E 313 + 0x0070 33 + 0x007A 381 + 0x007B 56 + 0x007C 32 + 0x007D 277 + 0x007F 317 + 0x0082 97 + 0x0097 1 + 0x0098 3 + 0x00A1 50）。raw 命中从 #106 的 3637 降至 2077（−1560）。本批无引用字段、无跳转目标，无悬空拒绝面变化；0x007F 的三种形态（0/0 复位×128（其中 1 条配 0xFFFF 不重绘）、滚动×180、0xFFFF 绝对×9）全部按 WORD 原样往返；0x006E/0x007D 的有符号偏移按补码双向往返（−16↔0xFFF0、−128↔0xFF80），与本批其余无符号 WORD 字段互不混用；0x0098 的 82/83 按 MGO 精灵号消费视图命名（res.c:337，角色读法越界无参考佐证）。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`x`/`y`/`h`/`layerUnits`/`frames`/`eventObjectId`/`spriteNum0`/`spriteNum1` 沿用既有 word 绑定，`xOffset`/`yOffset` 复用 signedWord；本批无新绑定种类，`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径）。**场景移动域移动子域（31 个 opcode：批1 #105 十位（0x000B–0x000E、0x000F–0x0014）、批2 #106 十位（0x0015/0x0016/0x0038/0x003F/0x0044/0x0046/0x004C/0x0062/0x0063/0x006C）、本批十一位）至此全部具名**；场景移动域（#77 六域之一，共 75 个 opcode 拆 8 批）剩余对象批1/2（#108/#109）、屏幕批1/2（#110/#111）、音乐场景特殊（#112）待后续批次。

#### M5 场景移动对象批1 条目（#108，场景移动域-对象批1）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；七个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3641 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继全部 sequential（七 case 均为普通 break，函数尾 3085 `return wScriptEntry + 1`；本批无停等、无跳转、无引用字段）。**setState 族四 opcode（0x0024/0x0025/0x0040/0x0049）复核成立**：共享前奏按 operand 0 选 `pCurrent`（626-638：0/0xFFFF=调用者、否则 operand0−1 的 1 基下标，含 Dream 2.11 的 >0x9000 回绕 hack），case 体各有 `operand0 != 0` 守卫（1141/1151/1617/1715）——WORD 0 是整体跳过的 no-op 形态（真实数据 0x0024×5/0x0025×1/0x0040×2/0x0049×20 均有命中），调用者形态只剩 0xFFFF；写入目标 `pCurrent->wAutoScript`/`wTriggerScript`/`wTriggerMode`/`sState` = operand1（global.h:102/101/104/103）——`eventObjectId`（#78 重命名先例）+`entry`/`mode`/`state` 四组字段映射全部维持。**entry 是跨结构入口 WORD 非本文件标签**：真实数据 0x0024 entry 上探 41099（0xA08B）、0x0025 上探 43524（0xAA04，逼近 chunk 4 的 43538 条上限），各有 145/10 条越过 0x7FFF——entry 保持无符号 WORD（同 0x006D setSceneScripts 的 onEnter/onTeleport 口径，不设 reference 绑定）；entry 0 = 清除该脚本（play.c:181 `wScriptEntry != 0` 才执行，0x0024 entry 0×50、0x0025 entry 0×3 有真实命中）。**0x0040 mode 域恰为 kTrigger\* 0..8**（global.h:84-92：0=None、1..3=SearchNear/Normal/Far、4..8=TouchNear..TouchFarthest；真实命中与枚举域完全吻合）。**0x0049 state 维持 signedWord**：sState 是 SHORT 字段（global.h:103），0x0094 stateMatchJump 的 `(SHORT)` 比较与 scene.c:247 `sState < 0` 隐藏符号语义佐证；真实 0..3（3 超出 kObjState 枚举 0..2，global.h:77-79，属原版数据事实如实保留）。**0x004B 定名 `nullifyObject`（新）**：case 注释「Nullify the event object for a short while」——`pEvtObj->sVanishTime = -15` 固定常量无操作数（1730），作用于调用者对象（非前奏 pCurrent）；sVanishTime≠0 期间该对象的触发区检测与 auto 脚本整体跳过（play.c:87-94 continue、play.c:178 `sVanishTime == 0` 守卫），负值逐游戏更新 +1 回零而精灵保持绘制（scene.c:247 仅隐藏 sVanishTime > 0）——与 0x0052 hide（正数倒计时 + sState 取负隐藏精灵）形态互补；票面猜测的「对象循环动画/帧同步类」不成立。**0x0052 hide 复核成立**：作用于调用者 pEvtObj（无对象操作数），sState ×= −1 翻转隐藏符号（1798，scene.c:247 仅绘制 sState > 0）+ sVanishTime = frames（1799，WORD 0 运行时归一 800 仅改内存条目，编码仍 0——真实 0×1/100/150 三形态）→ 既有 `frames`(word,0) 映射维持。**0x005C 定名 `setBattleHidingTime`（新）**：case 注释「Hide for a while」——`g_Battle.iHidingTime = −(INT)operand0`（1911，battle.h:202 INT 战斗全局）非票面猜测的「对象图层/遮挡类」；负值是待生效形态，PAL_BattleCheckHidingEffect（fight.c:3529-3544）在物品/法术使用后取负生效（非 PAL_CLASSIC 再 ×20 与战斗速度缩放），倒计时为正期间战场不绘制我方精灵（battle.c:207）、敌方行动整体跳过（fight.c:1716/1159-1213）——编码保持正数 unit（取负是运行时行为非编码语义），WORD 0 = 清除隐藏态（−0 = 0）。signed/unsigned：**仅 0x0049 state 有 signedWord 语义（SHORT 存储域），其余全部无符号 WORD**（0x0024/0x0025 entry 的 0x7FFF 以上真实命中为无符号直接证据）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0024 | `setAutoEntry`（#78 已具名，复核维持） | `eventObjectId`=operand0（前奏选 pCurrent：0/0xFFFF=调用者、否则 operand0−1 的 1 基，626-638；**另有 ≠0 门控**：0 整体跳过 1141）、`entry`=operand1（1143 写 `pCurrent->wAutoScript`，global.h:102；entry 0=清除 auto 脚本）；operand2 参考 case 未用 | shared（1137：break 1145） | sequential | 两字段重编码；operand2 保留 | 420（eventObjectId 0×5（no-op 门控）/0xFFFF×46（调用者）/1 基 26..4810；entry 0×50、373..41099（145 条越 0x7FFF——无符号直接证据）；operand2 全 0） |
| 0x0025 | `setTriggerEntry`（#78 已具名，复核维持） | 同 0x0024 前奏+门控形状（1151），1153 写 `pCurrent->wTriggerScript`（global.h:101）；entry 0=清除 trigger 脚本；operand2 参考 case 未用 | shared（1147：break 1155） | sequential | 两字段重编码；operand2 保留 | 225（eventObjectId 0×1/0xFFFF×2/1 基 4..4725；entry 0×3、59..43524（0xAA04，逼近 43538 条上限）；operand2 全 0） |
| 0x0040 | `setTriggerMode`（#78 已具名，复核维持） | 同 0x0024 前奏+门控形状（1617），1619 写 `pCurrent->wTriggerMode`（global.h:104）——kTrigger\* 常量 0..8（global.h:84-92）；operand2 参考 case 未用 | shared（1613：break 1621） | sequential | 两字段重编码；operand2 保留 | 204（eventObjectId 0×2/0xFFFF×163（调用者多数）/1 基 10..4674；mode 0×98/2×57/6×19/3×11/7×8/8×5/4×2/5×3/1×1——恰为 0..8 枚举域；operand2 全 0） |
| 0x0049 | `setState`（#78 已具名，复核维持） | 同 0x0024 前奏+门控形状（1715），1716 写 `pCurrent->sState`——**SHORT 存储域**（global.h:103；kObjStateHidden=0/Normal=1/Blocker=2，global.h:77-79），signedWord 维持（0x0094 (SHORT) 比较与 scene.c:247 隐藏符号佐证）；operand2 参考 case 未用 | shared（1711：break 1717） | sequential | 两字段重编码；operand2 保留 | 1374（eventObjectId 0×20/0xFFFF×326（调用者多数）/1 基 4..4809；state 0×838/1×281/2×250/3×5；operand2 全 0） |
| 0x004B | `nullifyObject`（新） | 无操作数映射：1730 `pEvtObj->sVanishTime = -15` 固定常量——作用于调用者对象（非前奏 pCurrent）；sVanishTime≠0 期间触发区检测与 auto 脚本整体跳过（play.c:87-94/178）、负值逐更新 +1 回零而精灵保持绘制（scene.c:247 仅隐藏 >0，与 0x0052 hide 互补）；三操作数保留源 WORD | shared（1726：break 1731） | sequential | 三操作数保留源 WORD | 1（三操作数全 0，佐证 case 体不读） |
| 0x0052 | `hide`（#78 已具名，复核维持） | `frames`=operand0（1798 `pEvtObj->sState *= -1` 翻转隐藏符号（scene.c:247 仅绘制 sState > 0）、1799 `sVanishTime = frames`，WORD 0 运行时归一 800 仅改内存条目，编码仍 0）；作用于调用者 pEvtObj（无对象操作数）；operand1、2 参考 case 未用 | shared（1794：break 1800） | sequential | frames 重编码；operand1、2 保留 | 3（frames 0×1（默认 800 形态）/100/150；operand1/2 全 0） |
| 0x005C | `setBattleHidingTime`（新） | `units`=operand0（1911 `g_Battle.iHidingTime = −(INT)operand0`，battle.h:202 INT 战斗全局；负值为待生效形态，PAL_BattleCheckHidingEffect（fight.c:3529-3544）取负生效（非 PAL_CLASSIC ×20 与战斗速度缩放）；为正期间战场不绘制我方精灵（battle.c:207）、敌方行动跳过（fight.c:1716）；WORD 0=清除隐藏态）——取负是运行时行为，编码保持正数 unit；operand1、2 参考 case 未用 | shared（1907：break 1912） | sequential | units 重编码；operand1、2 保留 | 1（units 3；operand1/2 全 0） |

真实数据核对：本批命中合计 2228（新具名 2 = 0x004B 1 + 0x005C 1；复核 2226 = setAutoEntry 420 + setTriggerEntry 225 + setTriggerMode 204 + setState 1374 + hide 3 本已具名）。raw 命中从 #107 的 2077 降至 2075（−2）。本批无引用字段、无跳转目标，无悬空拒绝面变化；entry 的 0x7FFF 以上真实值与 mode/state 的枚举域值全部按 WORD/SHORT 原样往返（setAutoEntry entry 41099、setTriggerEntry entry 43524 为无符号直接证据；setState 全真实值 0..3 不触符号界）；0x004B/0x005C 的保留操作数与 hide 的默认 800 形态按源 WORD 原样保留。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`eventObjectId`/`entry`/`mode`/`frames`/`units` 沿用既有 word 绑定，`state` 复用 signedWord；本批无新绑定种类，`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径）。场景移动域（#77 六域之一，共 75 个 opcode 拆 8 批）本批为对象批1（对象子域 13 个 opcode 拆 2 批的首批 7 个），剩余对象批2（#109）、屏幕批1/2（#110/#111）、音乐场景特殊（#112）待后续批次。

#### M5 场景移动对象批2 条目（#109，场景移动域-对象批2，对象子域收官）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；六个 opcode 全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3641 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继除 0x0084 placeUsedItem（两条失败分支 2478/2495 `wScriptEntry = operand2 − 1` + 置脚本失败标记的标准跳转惯用法）为 conditionalJump 外全部 sequential（普通 break 进函数尾 3085 `return wScriptEntry + 1`；本批无停等）。**0x006F 定名 `syncState`（新）**：case 注释「Sync the state of current event object with another event object」（2115-2122）——若前奏 pCurrent（626-638：0/0xFFFF=调用者、否则 1 基）选中的对象 sState == `(SHORT)operand1`，则调用者 `pEvtObj->sState = (SHORT)operand1`（同值条件同步）；与 0x0024 setState 族不同**无 operand0≠0 门控**（WORD 0 选调用者自身退化为自比较，非 no-op 跳过）；sState 是 SHORT 字段（global.h:103）→ `state` 为 signedWord；operand2 参考 case 未用。**0x007E 定名 `setObjectLayer`（新）**：case 注释「Set the layer of event object」（2285-2290）——`pCurrent->sLayer = (SHORT)operand1`；sLayer 是 SHORT 字段（global.h:100），按半 tile 单位作垂直绘制偏移与绘制序（scene.c:302 `y += sLayer×8 + 9`、316 `PAL_AddSpriteToDraw(..., sLayer×8 + 2)`）——写入时无 ×8 换算故定名 `layer`（区别 moveParty 的 `layerUnits`：该处 `wLayer = operand×8`）；真实命中含 −10（0xFFF6，signedWord 直接证据）。**0x0084 定名 `placeUsedItem`（新）**：case 注释「Place the item which player used as an event object to the scene」（2473-2509）——operand0 是 1 基全局事件对象号，须落在当前场景事件对象窗口内（2475-2481 `rgScene[wNumScene−1].wEventObjectIndex < operand0 <= rgScene[wNumScene].wEventObjectIndex`，窗口判定自然排除 0）；被放在队伍面前一步（视口+队伍偏移 + 朝向相关 16/-16 px x 与 8/-8 px y，2483-2491），`PAL_CheckObstacle` 阻塞（2493-2500）或不在窗口则跳 operand2——普通 reference 跳转形状同 0x0038/0x0083；成功时 `pCurrent->sState = (SHORT)operand1`（sState 同为 SHORT 存储域 → signedWord）。**0x0087 定名 `animateObject`（新）**：case 注释「Animate the event object」（2542-2546）——`PAL_NPCWalkOneStep(wCurEventObjectID, 0)`：速度 0 位移为零（scene.c:887-888 位移乘速度），仅推进选中对象的姿态帧 `wCurrentFrameNum++`（按 nSpriteFrames 回绕，3 帧精灵按 4 回绕，scene.c:891-900）；被调函数对 id 0 或越界 id 提前返回（scene.c:873-876）；真实 189 命中三操作数全 0（仅调用者形态）——票面猜测的 zone 类不成立（无 zoneUnits，非 #98 先例形状）。**0x0090 setObjectScript 复核成立**（#78 结论维持）：2609-2611 `gpGlobals->g.rgObject[operand0].rgwData[2 + operand2] = operand1`——operand0 直下标无 −1，是 object union（object.json，7 WORD）的 0 基下标，**非事件对象**（`objectId` 维持）；operand2 选 union 脚本槽 `rgwData[2+slot]`（slot 0..2 命中各视图的脚本 WORD 2..4：item onUse/onEquip/onThrow、magic onSuccess/onUse、enemy turnStart/battleEnd/ready、poison playerScript/reserved/enemyScript、player friendDeath/dying）；operand1 是写入 WORD——跨结构 chunk 4 入口同 setAutoEntry.entry 口径，WORD 0 清槽（真实 3 命中全部 value 0 清槽）；`value`/`slotIndex` 命名维持。**0x009A setStateRange 复核成立**（#78 结论维持）：2758-2766 `for (i = operand0; i <= operand1; i++) lprgEventObject[i−1].sState = operand2`——1 基闭区间（`fromEventObjectId`/`toEventObjectId` 维持）；operand2 无 `(SHORT)` 转型但写入 SHORT 存储域（global.h:103），符号语义与 setState.state 同 → signedWord 维持；from > to 空区间不写（真实 @3065 from=1051 to=0 一命中）。signed/unsigned：**0x006F/0x0084 的 state、0x007E 的 layer、0x009A 的 state 为 signedWord（SHORT 存储域，layer 的 −10 与 state 补码双向均有验证），其余全部无符号 WORD**。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x006F | `syncState`（新） | `eventObjectId`=operand0（前奏选 pCurrent：0/0xFFFF=调用者、否则 operand0−1 1 基，626-638；**无 ≠0 门控**，WORD 0 自比较非 no-op）、`state`=operand1（**有符号**：2118 `pCurrent->sState == (SHORT)operand1` 比较、2121 `pEvtObj->sState = (SHORT)operand1` 同值条件同步写入，SHORT 存储域 global.h:103）；operand2 参考 case 未用 | shared（2115：break 2122） | sequential | 两字段重编码；operand2 保留 | 11（eventObjectId 63..139 全 1 基；state 0×10/1×1；operand2 0×10/1×1） |
| 0x007E | `setObjectLayer`（新） | `eventObjectId`=operand0（前奏选 pCurrent，626-638；无门控）、`layer`=operand1（**有符号**：2289 `pCurrent->sLayer = (SHORT)operand1`，SHORT 字段 global.h:100；消费为半 tile 单位绘制偏移/绘制序 scene.c:302/316，写入无 ×8）；operand2 参考 case 未用 | shared（2285：break 2290） | sequential | 两字段重编码；operand2 保留 | 10（eventObjectId 0xFFFF×1（调用者）/1 基 59..4168；layer −10（0xFFF6，@7DB4，signedWord 直接证据）/0×3/3/7/10/13/33×2；operand2 全 0） |
| 0x0084 | `placeUsedItem`（新） | `eventObjectId`=operand0（**1 基全局事件对象号**，兼前奏选 pCurrent 与场景窗口先验：2475-2481 须 `rgScene[wNumScene−1].wEventObjectIndex < operand0 <= rgScene[wNumScene].wEventObjectIndex`，不在窗口即跳）、`state`=operand1（**有符号**：2505 `pCurrent->sState = (SHORT)operand1`）、`to`=operand2（普通 reference：不在窗口 2478 或面前一步被 `PAL_CheckObstacle` 阻塞 2495 均跳并置失败标记；位置 = 视口+队伍偏移 + 朝向相关 16/-16 px x、8/-8 px y，2483-2491） | shared（2473：break 2509） | conditionalJump（失败→to，成功→next） | 三字段重编码；无保留操作数 | 2（eventObjectId 798/3607；state 2×2；目标 39656×2（两条同目标，均在 43538 条内）） |
| 0x0087 | `animateObject`（新） | `eventObjectId`=operand0（前奏选 `wCurEventObjectID`：0/0xFFFF=调用者 id、否则 operand0 1 基，626-638）；2545 `PAL_NPCWalkOneStep(wCurEventObjectID, 0)` 速度 0 无位移（scene.c:887-888），仅 `wCurrentFrameNum++` 姿态帧推进（nSpriteFrames 回绕，3 帧按 4，scene.c:891-900；id 0/越界提前返回 scene.c:873-876）；operand1、2 参考 case 未用 | shared（2542：break 2546） | sequential | eventObjectId 重编码；operand1、2 保留 | 189（三操作数全 0 ×189——仅调用者形态；无 zone 类操作数，票面 zone 猜测不成立） |
| 0x0090 | `setObjectScript`（#78 已具名，复核维持） | `objectId`=operand0（2609 `gpGlobals->g.rgObject[operand0]` 直下标无 −1——**object union（object.json）0 基下标，非事件对象**）、`value`=operand1（2610 写入 WORD：跨结构 chunk 4 入口同 setAutoEntry.entry 口径，0=清槽）、`slotIndex`=operand2（2610 `rgwData[2 + operand2]`：slot 0..2 → union 脚本槽 2..4，语义槽随视图而定） | shared（2607：break 2613） | sequential | 三字段重编码；无保留操作数 | 3（objectId 435/454/485；value 0×3（全部清槽）；slotIndex 0×3） |
| 0x009A | `setStateRange`（#78 已具名，复核维持） | `fromEventObjectId`=operand0、`toEventObjectId`=operand1（2762-2766 `lprgEventObject[i−1]` 1 基闭区间）、`state`=operand2（2764 无 `(SHORT)` 转型但写入 SHORT 存储域 global.h:103，signedWord 维持同 setState.state；from > to 空区间不写） | shared（2758：break 2766） | sequential | 三字段重编码；无保留操作数 | 81（from 26..4747；to 27..4749 及 0×1（@3065 from=1051 空区间）；state 0×48/1×6/2×27） |

真实数据核对：本批命中合计 296（新具名 212 = 0x006F 11 + 0x007E 10 + 0x0084 2 + 0x0087 189；复核 84 = setObjectScript 3 + setStateRange 81 本已具名）。raw 命中从 #108 的 2075 降至 1863（−212）。本批唯一引用字段是 placeUsedItem.to：两条真实目标均 39656 非零且落在 chunk 4（43538 条）内，无悬空拒绝面变化，`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径）；setObjectLayer 的 −10（0xFFF6）与三处 state 的补码双向（−123↔0xFF85）按 signedWord 原样往返；setObjectScript/setStateRange 全部真实值在无符号/SHORT 域内不触符号界。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`eventObjectId`/`objectId`/`value`/`slotIndex`/`fromEventObjectId`/`toEventObjectId` 沿用既有 word 绑定，`state`/`layer` 复用 signedWord，`to` 复用普通 reference；本批无新绑定种类。**场景移动域对象子域（13 个 opcode：批1 #108 七位 + 本批六位）至此全部具名**；场景移动域（#77 六域之一，共 75 个 opcode 拆 8 批）剩余屏幕批1/2（#110/#111）、音乐场景特殊（#112）待后续批次。

#### M5 场景移动屏幕批1 条目（#110，场景移动域-屏幕批1）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；十个 opcode 位全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3641 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继一律 sequential（十 case 均为普通 break 进函数尾 3085 `return wScriptEntry + 1`；PAL_RNGPlay/PAL_FadeOut/PAL_FadeIn/VIDEO_FadeScreen 是在被调函数内部同步完成的阻塞调用，同 #107 partyWalkTo 先例，无停等无跳转）。**三个复核项两项修正一项维持**：**0x004F fadeToRed 复核维持**（`PAL_FadeToRed()` 无参，palette.c:595-667，case 不读操作数，NoFields 维持）；**0x0050 fadeOut 复核修正 `frames`→`delay`**：operand0 是 PAL_FadeOut 的 iDelay（script.c:1778，palette.c:123-190）——调色板淡黑的步长单元（总时长 = delay×600ms、固定 10ms 步），非帧数；WORD 0 运行时归一 1 仅改内存、编码仍 0（真实 908 命中 0×874 为多数形态），case 另置 `fNeedToFadeIn = TRUE`；**0x0073 fadeScene 复核修正 `mode`→`speed`**：operand0 是 VIDEO_FadeScreen 的 wSpeed（script.c:2144，video.c:1129-1290）——备份屏→重建场景的交叉淡入步长（「值越大越慢」，12 趟 × (speed+1)×10ms），非模式选择器；前置 VIDEO_BackupScreen+PAL_MakeScene（2142-2143）重建场景。**0x0035 定名 `shakeScreen`（新）**：case 注释「Shake the screen」——`VIDEO_ShakeScreen(times, level)`（video.c:1029-1053）写 g_wShakeTime/g_wShakeLevel：times 是每次 VIDEO_UpdateScreen 递减一次的抖动倒数（video.c:571-615，奇偶交替上下位移），level 是抖动幅度的扫描线像素数；level WORD 0 运行时归一 4（1523-1526），times WORD 0 跳过倒计时并立即重绘一帧干净画面（1531-1533）——两归一仅改内存、编码仍 0。**0x0036 定名 `setPlayingRng`（新）**：case 注释「Set the current playing RNG animation」——`gpGlobals->iCurPlayingRNG = operand0`（global.h:509）：待播 RNG 动画的 **rng.mkf 段号**（消费视图：PAL_RNGPlay 的 iNumRNG 直读 rng.mkf chunk iNumRng，rngplay.c:372-449；命名同 musicId 的 MKF 段号先例），选择不播放，播放由 0x0037/结局驱动。**0x0037 定名 `playRng`（新）**：case 注释「Play RNG animation」——`PAL_RNGPlay(当前选中段, startFrame, endFrame>0?endFrame:−1, speed>0?speed:16)`：播放对象来自 setPlayingRng 的全局选择**非操作数**；endFrame WORD 0 = 播到结尾（−1 哨兵）、speed WORD 0 = 运行时 16，两归一仅改内存、编码仍 0（1546-1550）；被调函数阻塞至末帧（rngplay.c:409-434）。**0x0051 定名 `fadeIn`（新）**：case 注释「screen fade in」——`PAL_FadeIn(wNumPalette, fNightPalette, delay)` 用当前全局调色板（palette.c:193-260），delay 门控 `((SHORT)operand0 > 0) ? operand0 : 1`（1787-1789）：WORD 0 与 0xFFFF（(SHORT)−1）两形态都回退默认 1，编码保留原 WORD 无符号原样（真实恰只 0/0xFFFF 两形态，正域 1..0x7FFF 无命中）；case 另清 `fNeedToFadeIn`。**0x0053/0x0054 定名 `useDayPalette`/`useNightPalette`（新）**：case 注释「use the day/night palette」——`gpGlobals->fNightPalette = FALSE/TRUE`（global.h:533）：PAL_GetPalette 按 `fNight ? 256×3 : 0` 偏移读每个 pat.mkf 调色板段的前/后半（palette.c:79-81），两 case 均无操作数。**0x0071 定名 `waveScreen`（新）**：case 注释「Wave the screen」——`gpGlobals->wScreenWave = operand0`（global.h:379「level of screen waving」：波幅按 level/256 逐列缩放 scene.c:415，达到 0 或 ≥256 即停并双双清零 scene.c:389-397，真实命中上沿 255 恰在截断点下方）+ `gpGlobals->sWaveProgression = (SHORT)operand1`（global.h:539，每次场景更新 level += progression，scene.c:389；**本批唯一 (SHORT) 转型**，真实命中 −4（0xFFFC @7ECA）为 signedWord 直接证据）。signed/unsigned：**仅 0x0071 progression 为 signedWord（SHORT 存储域+转型+真实负值三重证据），其余全部无符号 WORD**（fadeIn 的 0xFFFF 形态按无符号原样往返，同 0x007F frames 0xFFFF 哨兵先例）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0035 | `shakeScreen`（新） | `times`=operand0（1527 `VIDEO_ShakeScreen(operand0, i)`：g_wShakeTime 每次 VIDEO_UpdateScreen 递减一次 video.c:615，奇偶交替上下位移；**WORD 0 跳过倒计时并立即重绘一帧** 1531-1533）、`level`=operand1（1523-1526 `i==0 → 4` 运行时归一仅改内存、编码仍 0；抖动幅度扫描线像素数，g_wShakeLevel 参与 srcrect 高度裁剪 video.c:579/584）；operand2 参考 case 未用 | shared（1521：break 1534） | sequential | 两字段重编码；operand2 保留 | 23（times 0×5/10/14/20/24/28/30/72/90×2/99×7/999×2；level 0×13（运行时 4 形态）/1×4/2×2/3/4×2/5；operand2 全 0） |
| 0x0036 | `setPlayingRng`（新） | `rngId`=operand0（1541 `gpGlobals->iCurPlayingRNG = operand0`，global.h:509）：待播 RNG 动画的 **rng.mkf 段号**（PAL_RNGPlay iNumRNG 直读 rng.mkf chunk，rngplay.c:372-449；musicId 的 MKF 段号先例），选择不播放；operand1、2 参考 case 未用 | shared（1537：break 1541） | sequential | rngId 重编码；operand1、2 保留 | 9（rngId 0/1/2/3/4/5/7/8/9 各一次，无 6；operand1/2 全 0） |
| 0x0037 | `playRng`（新） | `startFrame`=operand0、`endFrame`=operand1（1548-1549 `>0 ? 值 : −1`：**WORD 0 = 播到结尾**，归一仅改内存、编码仍 0；被调内部末帧 +1 防丢帧 rngplay.c:397）、`speed`=operand2（1550 `>0 ? 值 : 16`：WORD 0 = 运行时 16，编码仍 0；播放延迟 = 时基频率/speed）；播放对象 = setPlayingRng 的全局选择**非操作数**；PAL_RNGPlay 阻塞至末帧（rngplay.c:409-434） | shared（1544：break 1553） | sequential | 三字段重编码；无保留操作数 | 20（startFrame 0×9/1/17/22/55/60/75/113/163/187/196/267；endFrame 0×8/1/16/21/54/59/74/109/112/162/186/195/266；speed 0/6/7/8×9/9/14/16×6） |
| 0x004F | `fadeToRed`（#78 已具名，复核维持） | 无操作数映射：1771 `PAL_FadeToRed()` 无参（palette.c:595-667：当前调色板向红色主导形态最多 32 步渐变，跳过颜色 0x4F 保文字可读，0x4F 像素 HACKHACK 改 0x4E）；三操作数保留源 WORD | shared（1768：break 1773） | sequential | 三操作数保留源 WORD | 1（三操作数全 0，佐证 case 体不读） |
| 0x0050 | `fadeOut`（#78 已具名，**#110 复核修正 frames→delay**） | `delay`=operand0（1778 `PAL_FadeOut(operand0 ? operand0 : 1)`：**iDelay 是调色板淡黑步长单元**（palette.c:123-190，总时长 = delay×600ms、固定 10ms 步）非帧数——#78 的 frames 误读按证据修正；WORD 0 运行时归一 1 仅改内存、编码仍 0）；1777 先 `VIDEO_UpdateScreen(NULL)`，1781 置 `fNeedToFadeIn = TRUE`（下一场景绘制淡回）；operand1、2 参考 case 未用 | shared（1775：break 1782） | sequential | delay 重编码；operand1、2 保留 | 908（delay 0×874（运行时 1 形态多数）/2×19/3×10/4×3/5/6；operand1/2 全 0） |
| 0x0051 | `fadeIn`（新） | `delay`=operand0（1787-1789 `((SHORT)operand0 > 0) ? operand0 : 1` 门控：**WORD 0 与 0xFFFF（(SHORT)−1）两形态都回退默认 1**，编码保留原 WORD 无符号原样（真实恰只 0/0xFFFF，正域 1..0x7FFF 无命中——同 0x007F frames 0xFFFF 哨兵的无符号先例））：PAL_FadeIn(wNumPalette, fNightPalette, delay) 用当前全局调色板（palette.c:193-260，时长 = delay×600ms）；1786 先 `VIDEO_UpdateScreen(NULL)`，1791 清 `fNeedToFadeIn`；operand1、2 参考 case 未用 | shared（1784：break 1792） | sequential | delay 重编码；operand1、2 保留 | 4（delay 0×2（默认 1 形态）/0xFFFF×2（(SHORT)−1 形态 @0B56/@0BEC）；operand1/2 全 0） |
| 0x0053 | `useDayPalette`（新） | 无操作数映射：1805 `gpGlobals->fNightPalette = FALSE`（global.h:533）；PAL_GetPalette 按 `fNight ? 256×3 : 0` 偏移读每个 pat.mkf 调色板段的**前半**（palette.c:79-81）；三操作数保留源 WORD | shared（1802：break 1806） | sequential | 三操作数保留源 WORD | 10（三操作数全 0） |
| 0x0054 | `useNightPalette`（新） | 无操作数映射：1812 `gpGlobals->fNightPalette = TRUE`：PAL_GetPalette 读每个 pat.mkf 调色板段的**后半**（暗色夜间色，palette.c:79-81）；三操作数保留源 WORD | shared（1809：break 1813） | sequential | 三操作数保留源 WORD | 7（三操作数全 0） |
| 0x0071 | `waveScreen`（新） | `level`=operand0（2136 `gpGlobals->wScreenWave = operand0`，global.h:379「level of screen waving」：波幅按 level/256 逐列缩放 scene.c:415，达到 0 或 ≥256 即停并双双清零 scene.c:389-397）、`progression`=operand1（2137 `gpGlobals->sWaveProgression = (SHORT)operand1`，global.h:539：**SHORT 存储域+转型+真实负值三重证据**；每次场景更新 level += progression scene.c:389）；operand2 参考 case 未用 | shared（2132：break 2138） | sequential | 两字段重编码；operand2 保留 | 11（level 0×3（停止形态）/1/2×6/255（上沿恰在 256 截断下方，@7ECA）；progression −4（0xFFFC，@7ECA，signedWord 直接证据）/0×9/2（@8870）；operand2 全 0） |
| 0x0073 | `fadeScene`（#78 已具名，**#110 复核修正 mode→speed**） | `speed`=operand0（2144 `VIDEO_FadeScreen(operand0)`：**wSpeed 是交叉淡入步长**（video.c:1129-1290「值越大越慢」，12 趟 × (speed+1)×10ms）非模式选择器——#78 的 mode 误读按证据修正；2142-2143 前置 `VIDEO_BackupScreen(gpScreen)` + `PAL_MakeScene()` 重建场景后从备份屏交叉淡入）；operand1、2 参考 case 未用 | shared（2140：break 2146） | sequential | speed 重编码；operand1、2 保留 | 69（speed 0×28/2×21/3×6/4×7/5×3/6×3/7×1；operand1 0×60/54/60×2/70/72×4/80（保留）；operand2 全 0） |

真实数据核对：本批命中合计 1062（新具名 84 = 0x0035 23 + 0x0036 9 + 0x0037 20 + 0x0051 4 + 0x0053 10 + 0x0054 7 + 0x0071 11；复核 978 = fadeToRed 1 + fadeOut 908 + fadeScene 69 本已具名）。raw 命中从 #109 的 1863 降至 1779（−84）。本批无引用字段、无跳转目标，无悬空拒绝面变化；0x0071 progression 的 −4（0xFFFC）补码双向（−4↔0xFFFC）按 signedWord 原样往返，fadeIn 的 0xFFFF 形态、shakeScreen level/times 的 WORD 0 形态、playRng endFrame/speed 的 WORD 0 形态全部按无符号 WORD 原样往返（运行时归一仅改内存，同 hide 默认 800/0x0057 归一 8 先例）。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`times`/`level`/`rngId`/`startFrame`/`endFrame`/`speed`/`delay` 沿用既有 word 绑定（`delay` 按参考参数名 iDelay 定名，与动词 `delay`（0x0085）的 `delayUnits` 同为延迟步长概念），`progression` 复用 signedWord；本批无新绑定种类，`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径）。场景移动域（#77 六域之一，共 75 个 opcode 拆 8 批）本批为屏幕批1（屏幕子域 19 个 opcode 拆 2 批的首批 10 个），剩余屏幕批2（#111）、音乐场景特殊（#112）待后续批次。

#### M5 场景移动屏幕批2 条目（#111，场景移动域-屏幕批2，屏幕子域收官）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；九个 opcode 位全部位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3641 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继一律 sequential（九 case 均为普通 break 进函数尾 3085 `return wScriptEntry + 1`；PAL_ShowFBP/PAL_ScrollFBP/PAL_EndingAnimation/PAL_PaletteFade/PAL_ColorFade/PAL_SceneFade/VIDEO_FadeScreen 是在被调函数内部同步完成的阻塞调用，同 #107 partyWalkTo、#110 PAL_RNGPlay 先例，无停等无跳转；PAL_EndingAnimation 播完全部结局演出后正常返回，非进程退出）。**四个 FBP/结局类 opcode（0x0076/0x0096/0x00A4/0x00A5）的 case 体带 `!gConfig.fIsWIN95` 门控**（WIN95 构建下 0x0076 仅清屏、其余三者为空转），操作数语义按读操作数的非 WIN95 分支声明；**三个 opcode 真实 chunk 4 零命中**（0x0096/0x00A4/0x00A5——结局自身的 FBP/RNG 演出硬编码在 ending.c 内，不经脚本 opcode，如实登记为 0）。signed/unsigned：**仅 0x0093 step 为 signedWord**（(SHORT) 转型 2671/2672 + 负向消费（PAL_SceneFade iStep 负值淡出）+ 真实命中 −4/−2/−1 三重证据）；**0x008C color 是本批首见的 lowByte 绑定**（2588 `(BYTE)(operand0)` 显式转型，同 setDialog color 的低字节口径，高位保留）；其余全部无符号 WORD（0x008C fromColor 真实 0×3/0xFFFF×3 真值 WORD 形态同 waitUpdate 0/0xFFFF 口径，0x00A5 effectSprite 的 0xFFFF 是「保留当前精灵」哨兵非精灵号）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0076 | `showFbp`（新） | `fbpId`=operand0、`fade`=operand1（2211 `PAL_ShowFBP(operand0, operand1)`，ending.c:49-150：**fbp.mkf 段号**（参数注释「number of chunk in fbp.mkf file」，#95 fbp 段号契约；musicId/rngId 的 MKF 段号 Id 后缀先例）+ 图片展示渐变步长：fade 非零 → 16 趟×6 面调色板混合、每趟 (fade+1)×10ms（ending.c:74-76/134），WORD 0 → 直接 blit）；前置 2210 `PAL_EndingSetEffectSprite(0)` 清除待用特效精灵（与 0x00A5 的设置互为对偶）；WIN95 分支（2203-2207）仅清屏不读操作数；operand2 参考 case 未用 | shared（2199：break 2213） | sequential | 两字段重编码；operand2 保留 | 4（fbpId 全 0xFFFF（解压失败→缓冲清零黑屏，即淡黑过渡形态，@0B55/@0BEB/@1279/@6DBF）；fade 全 0；operand2 全 0） |
| 0x0080 | `toggleDayNightPalette`（新） | `paletteOnly`=operand0（2386-2387 `PAL_PaletteFade(wNumPalette, fNightPalette, !(operand0))`，palette.c:381-446：当前视频调色板→目标 32 趟混合；**真值 WORD 反相传入 fUpdateScene**：WORD 0 → 每趟重渲染场景（FRAME_TIME=100ms 步长，game.h:27-28 FPS=10），非零 → 仅调色板（FRAME_TIME/4=25ms））；2385 先翻转 `fNightPalette`（0x0053/0x0054 对偶对的同一全局，非操作数）；operand1、2 参考 case 未用 | shared（2381：break 2388） | sequential | paletteOnly 重编码；operand1、2 保留 | 4（paletteOnly 0×3（场景更新形态 @1277/@229F/@7031）/1×1（仅调色板形态 @3613）；operand1/2 全 0） |
| 0x008B | `setPalette`（新） | `paletteId`=operand0（2577 `gpGlobals->wNumPalette = operand0`：**pat.mkf 调色板段号**（消费视图：PAL_GetPalette 直读 pat.mkf chunk iPaletteNum palette.c:40-60，日/夜半区 0/256×3 偏移 79-81；所有 fade 被调共用）；2578-2581 仅在无待淡入（`!fNeedToFadeIn`）时立即 `PAL_SetPalette(wNumPalette, FALSE)` 应用（palette.c:93-103，昼半区固定 FALSE），有待淡入则延迟到该次淡入）；operand1、2 参考 case 未用 | shared（2573：break 2582） | sequential | paletteId 重编码；operand1、2 保留 | 14（paletteId 0×7/2×1/5×5/6×1；operand1/2 全 0） |
| 0x008C | `colorFade`（新） | `color`=operand0（2588 `(BYTE)(operand0)` 显式低字节：纯色淡入/淡出的调色板颜色索引 0..255，**lowByte 绑定本批首用**，高位保留）、`delay`=operand1（PAL_ColorFade iDelay：每步延迟 = delay×10ms，WORD 0 运行时归一 10ms 仅改内存、编码仍 0，palette.c:479-484）、`fromColor`=operand2（BOOL fFrom 真值 WORD：非零 = 从纯色淡回场景（64 步双向逼近 palette.c:462-570），0 = 淡向纯色；真实 0×3/0xFFFF×3 同 waitUpdate 真值口径）；2590 另清 `fNeedToFadeIn` | shared（2584：break 2591） | sequential | 三字段重编码（color 低字节、高位保留）；无保留操作数 | 6（color 26×2/79×2/127×2；delay 0×1/2×3/4×2；fromColor 0×3/0xFFFF×3） |
| 0x0093 | `sceneFade`（新） | `step`=operand0（2670-2671 `PAL_SceneFade(wNumPalette, fNightPalette, (SHORT)operand0)`，palette.c:262-375：**正数淡入、负数淡出**（调色板缩放 0↔63 按步长递变，每趟重渲染场景 + 100ms；步长绝对值越大趟数越少越快），WORD 0 运行时归一 1 仅改内存、编码仍 0；2672 `fNeedToFadeIn = ((SHORT)operand0) < 0`：负向淡出后下一场景须淡回；**本批唯一 (SHORT) 转型，真实命中含 −4/−2/−1 为 signedWord 三重证据之一**）；operand1、2 参考 case 未用 | shared（2666：break 2673） | sequential | step 重编码（补码双向）；operand1、2 保留 | 16（step −4（0xFFFC）×3/@29E9/@4197/@6105、−2（0xFFFE）×4、−1（0xFFFF）×2、1×3、2×2、4×1、8×1/@6143；operand1/2 全 0） |
| 0x0096 | `showEndingAnimation`（新） | 无操作数映射：2699-2700 `if (!WIN95) PAL_EndingAnimation()`（ending.c:282-553：完整结局演出——RNG 电影、FBP 静帧、字幕、AVI/CD 回退，播完正常返回）；case 不读操作数；三操作数保留源 WORD | shared（2695：break 2701） | sequential | 三操作数保留源 WORD | 0（真实 chunk 4 无命中：结局演出硬编码在 ending.c，不经脚本 opcode；全零形态为编码层声明） |
| 0x009B | `fadeToScene`（新） | 无操作数映射：2773-2775 `VIDEO_BackupScreen(gpScreen)` + `PAL_MakeScene()` + `VIDEO_FadeScreen(2)`——0x0073 fadeScene 形状在**固定速度 2**（video.c:1129-1290）下的无参变体（上游注释 FIXME「obviously wrong」如实保留）；case 不读操作数，真实两命中的 2/0xFFFF/0 是被忽略的源 WORD，保留原样 | shared（2768：break 2776） | sequential | 三操作数保留源 WORD | 2（@7C72/@7C8B 三操作数均 2/0xFFFF/0，佐证 case 体不读） |
| 0x00A4 | `scrollFbp`（新） | `fbpId`=operand0（3053 `PAL_ScrollFBP(operand0, operand2, TRUE)`，ending.c:153-278：**fbp.mkf 段号**，220 行逐行向下滚动入屏（fScrollDown 固定 TRUE 非操作数））、`speed`=operand2（wScrollSpeed 滚动步长，WORD 0 运行时归一 1 仅改内存、编码仍 0，ending.c:211-214）；前置 3046-3052 `fbpId==68` 时先 `PAL_ShowFBP(69, 0)`（上游 HACKHACK 修正结局画面，段号语义不变）；operand1 参考 case 未用 | shared（3040：break 3055） | sequential | 两字段重编码；operand1 保留 | 0（真实 chunk 4 无命中：ending.c 以硬编码速度 0xF 自用滚动，不经脚本 opcode；68/5/0xF 形态为编码层声明） |
| 0x00A5 | `showFbpWithEffectSprite`（新） | `fbpId`=operand0、`fade`=operand2（3067 `PAL_ShowFBP(operand0, operand2)` 同 0x0076）、`effectSprite`=operand1（3063-3066 `operand1 != 0xFFFF` 时 `PAL_EndingSetEffectSprite(operand1)`（ending.c:27-44 写 g_wCurEffectSprite）：**MGO.MKF 精灵号**，PAL_ShowFBP 按 ticks/150 换帧叠加在图片上（ending.c:79-129）；**0xFFFF 是保留当前精灵哨兵**非精灵号，按无符号 WORD 原样往返，同 0x007F frames 0xFFFF 先例） | shared（3057：break 3069） | sequential | 三字段重编码；无保留操作数 | 0（真实 chunk 4 无命中：ending.c 硬编码 0x27B 精灵叠于段 76，不经脚本 opcode；76/0x27B/7 形态为编码层声明） |

真实数据核对：本批命中合计 46（全部新具名：0x0076 4 + 0x0080 4 + 0x008B 14 + 0x008C 6 + 0x0093 16 + 0x0096 0 + 0x009B 2 + 0x00A4 0 + 0x00A5 0；无复核项）。raw 命中从 #110 的 1779 降至 1733（−46）。本批无引用字段、无跳转目标，无悬空拒绝面变化；0x0093 step 的 −4/−2/−1（0xFFFC/0xFFFE/0xFFFF）补码双向按 signedWord 原样往返，0x008C color 的低字节编辑保留高位（(BYTE) 转型口径），fromColor 0xFFFF、effectSprite 0xFFFF、showFbp fbpId 0xFFFF 各哨兵/真值形态按无符号 WORD 原样往返。export→rebuild SHA `f36cf853…b39e72a` 字节一致。框架零扩展：`fbpId`/`fade`/`paletteOnly`/`paletteId`/`delay`/`fromColor`/`speed`/`effectSprite` 沿用既有 word 绑定，`step` 复用 signedWord，`color` 复用 lowByte（本批首用）；本批无新绑定种类，`DecodeValidatesReferenceRange` 导出期预检不启用（与前批同口径）。**场景移动域屏幕子域（19 个 opcode：批1 #110 十位 + 本批九位）至此全部具名**；场景移动域（#77 六域之一，共 75 个 opcode 拆 8 批）剩余音乐场景特殊（#112）待后续批次。

#### M5 场景移动音乐场景特殊条目（#112，场景移动域-音乐场景特殊，场景移动域 75 个 opcode 收官 + 165 个 opcode 全量收官）

本批核对结论：全部操作数仍为立即 WORD（同前批，`pScript->rgwOperand[n]` 直读，无变量寻址）；十二个 opcode 位中十一个位于两执行器共享的 `PAL_InterpretInstruction`（trigger 开关 3206-3440 / auto 开关 3522-3640 逐一核对无对应 case，每个 case 在 script.c 仅出现一次，executor 一律 `shared`），后继一律 sequential（普通 break 进函数尾 3085 `return wScriptEntry + 1`；AUDIO_PlayMusic/AUDIO_PlaySound/AUDIO_PlayCDTrack/PAL_LoadResources 是在被调函数内部同步完成的阻塞/加载调用，同 #107 partyWalkTo、#110 PAL_RNGPlay 先例，无停等无跳转）。**例外是 0x00A7：本批唯一不位于共享 InterpretInstruction 的位，其 case（3641-3643 裸 `wScriptEntry++`，无上游注释、不读操作数）是 PAL_RunAutoScript 自有开关的最后一个 case**——trigger 开关与 InterpretInstruction 均无对应 case，trigger 执行到该位将落入共享 default 的 3077 `TerminateOnError`（进程终止），故 executor 是 auto 开关专属：**`OpcodeExecutor` 新增 `autoSwitchOnly` 成员（纯声明，不参与 decode/validate/encode 路径，同 #98 `randomBranch` 后继声明的最小扩展先例）**；后继仍是 sequential（auto 每帧解释一条指令，裸 `wScriptEntry++` 即标准推进，与 0x0008 advanceEntry 的 trigger 镜像同体，动词复用）。四个复核项全部维持（0x0043/0x006D/0x0075/0x0078 无字段更名）。signed/unsigned：**仅 0x00A3 track 为 signedWord**（3032 显式 `(SHORT)` 转型 + −1 映射 −2「do NOTHING」负域哨兵消费 + 真实 4 命中全正轨号的事实如实登记；同 0x0042 enemyOperand/0x009E count 的 (SHORT) 先例）；其余全部无符号 WORD（0x0099 sceneId 的 0xFFFF 是当前场景哨兵、0x0065 reload 的 0xFFFF 是真值形态，均按无符号原样往返）。行号与逐条语义如下。

| opcode | 动词 | 字段映射与寻址 | 执行方式（script.c 行号） | 后继 | 反向编码 | 真实数据 |
|---|---|---|---|---|---|---|
| 0x0043 | `setMusic`（#78 已具名，#112 复核维持） | `musicId`=operand0（1646 `wNumMusic = operand0` + 1647 `AUDIO_PlayMusic(operand0, …)`：**RIX music.mkf 段号**（被调形参 iNumRIX，audio.c:547，musicId 的 MKF 段号命名先例））、`mode`=operand1（1647 播放模式选择器：`operand1 != 1` 作 fLoop——**1 = 单次播放、其余 = 循环**；`operand1 == 3 && musicId != 9` 另以 3.0 秒淡入——**3 = 淡入形态（音乐 9 例外）**；两分支都是运行时行为，编码保留原 WORD） | shared（1642：break 1648） | sequential | 两字段重编码；无保留操作数 | 155（musicId 0..87（含 0×7 静音形态）；mode 0×148/1×2/3×5） |
| 0x0045 | `setBattleMusic`（新） | `battleMusicId`=operand0（1662 `wNumBattleMusic = operand0`（global.h:535）：**下一场战斗循环播放的 RIX music.mkf 段号**（消费视图 battle.c:728 `AUDIO_PlayMusic(wNumBattleMusic, TRUE, 0)`，循环固定 TRUE 非操作数）；case 不读其他操作数 | shared（1658：break 1663） | sequential | battleMusicId 重编码；operand1、2 保留 | 68（battleMusicId 14..86，37×16/38×10/39×13 多数带；operand1/2 全 0） |
| 0x0047 | `playSound`（新） | `soundId`=operand0（1708 `AUDIO_PlaySound(operand0)`，audio.c:518：**voc.mkf / sounds.mkf 段号**（参数注释「number of the sound; the absolute value is used」——运行时取绝对值仅改内存，case 无 `(SHORT)` 转型，保持无符号 WORD））；case 不读其他操作数 | shared（1704：break 1709） | sequential | soundId 重编码；operand1、2 保留 | 313（soundId 2..275，78×104 多数形态；operand1/2 全 0） |
| 0x0077 | `stopMusic`（新） | `fadeUnits`=operand0（2219-2220 `AUDIO_PlayMusic(0, FALSE, (operand0 == 0) ? 2.0f : operand0 * 3)`：**淡出时长选择器**——WORD 0 = 2 秒、非零 n = n×3 秒，随后 2221 `wNumMusic = 0`；段号 0 与不循环均为 case 内硬编码非操作数）；operand1、2 参考 case 未用（真实一条携带保留 operand1 = 1） | shared（2215：break 2222） | sequential | fadeUnits 重编码；operand1、2 保留 | 44（fadeUnits 0×20（2 秒默认形态）/1×24（3 秒形态）；operand1 0×43/1×1；operand2 全 0） |
| 0x00A3 | `playCdMusic`（新） | `track`=operand0（3029 `wNumMusic = operand1` 先记全局；3032 `int numTrack = (SHORT)operand0` + 3033 `AUDIO_PlayCDTrack(numTrack == -1 ? -2 : numTrack)`：**CD 轨号，(SHORT) 转型显式**——−1（0xFFFF）映射 −2 即 audio.c:576「do NOTHING」（保持 CD 现状不触碰，仅 RIX 回退生效），其余值即轨号；本批唯一 signedWord（0x0042/0x009E 的 (SHORT) 转型+负域哨兵先例；真实 4 命中全正轨号如实登记））、`musicId`=operand1（回退段：CD 不可用（3037）或播放失败（3034）时 `AUDIO_PlayMusic(operand1, TRUE, 0)` **循环 RIX music.mkf 段号**，与 setMusic 同口径）；operand2 参考 case 未用 | shared（3025：break 3038） | sequential | 两字段重编码（track 补码双向）；operand2 保留 | 4（track 2/4/8/9 + musicId 14/22/27/67：@234F/@5DA4/@6D1F/@7D26 四对一一对应；operand2 全 0） |
| 0x0059 | `changeScene`（新） | `sceneId`=operand0（1874-1884 门控 `operand0 > 0 && operand0 <= MAX_SCENES（palcommon.h:69 = 300）&& wNumScene != operand0` 内：`wNumScene = operand0` + `PAL_SetLoadFlags(kLoadScene)` + `fEnteringScene = TRUE` + `wLayer = 0`——**下一帧加载场景的切换指令**；**1 基 scene.json 表行号**（rgScene[operand0−1]，#80/#82 sceneId 引用方向先例）；越界或同场景 WORD 是运行时 no-op，编码保留原值） | shared（1870：break 1885） | sequential | sceneId 重编码；operand1、2 保留 | 883（sceneId 2..294（16 帧中 226×32/227×30 为主）；operand1/2 全 0） |
| 0x0065 | `setPlayerSprite`（新） | `playerRole`=operand0（1999 `g.PlayerRoles.rgwSpriteNum[operand0] = operand1`：**直接 0 基 PLAYERROLES 角色下标**，无 −1、与 0x0023 removeEquipment 的 playerRole 形状同（真实 0×119/1/2×3/3））、`spriteNum`=operand1（rgwSpriteNum，global.h:303「sprite displayed in normal scene (in MGO.MKF)」：**MGO.MKF 段号**（消费视图 res.c:325 直接作 MGO chunk 解压；setFollower spriteNum0/1 命名先例））、`reload`=operand2（2000-2004 `!fInBattle && operand2` 真值门：非零时 `kLoadPlayerSprite` 立即重载精灵；真值 WORD 口径同 0x004C floating） | shared（1995：break 2005） | sequential | 三字段重编码；无保留操作数 | 124（playerRole 0×119/1/2×3/3；spriteNum 2×48/193×17/232×25/632 上沿等；reload 0×41/0xFFFF×83） |
| 0x006D | `setSceneScripts`（#78 已具名，#112 复核维持） | `sceneId`=operand0（2069 `if (operand0)` 整体门控：WORD 0 跳过整个 case；非零即 1 基 rgScene[operand0−1] 行号）、`onEnter`=operand1（2071-2075 非零写入 `wScriptOnEnter`、2077-2081 非零写入 `wScriptOnTeleport`；**跨结构 chunk-4 入口 WORD**，同 setAutoEntry.entry 口径——真实上探 36203 越 0x7FFF，无符号直接证据）、`onTeleport`=operand2（2083-2087 **双零即同时清零两入口**，单零保持对应脚本） | shared（2065：break 2089） | sequential | 三字段重编码；无保留操作数 | 62（sceneId 2..294；onEnter 903..36203；onTeleport 0×61/11870×1） |
| 0x0075 | `setParty`（#78 已具名，#112 复核维持） | `roleId0`=operand0、`roleId1`=operand1、`roleId2`=operand2（2168-2182 逐槽：非零 i 填入下一 rgParty 槽且 `wPlayerRole = operand − 1`——**1 基角色 id、WORD 0 跳槽**；全零时 Dream 2.11 hack 保底单角色 0（2187-2192）；随后 2192-2196 重载精灵/清 rgPoisonStatus/更新装备，均非操作数） | shared（2164：break 2197） | sequential | 三字段重编码；无保留操作数 | 119（roleId0 1×80/2×9/3×17/4×2/5×11；roleId1 0×55/1..6；roleId2 0×106/3×6/5×7） |
| 0x0099 | `changeSceneMap`（新） | `sceneId`=operand0（2746 `== 0xFFFF` 哨兵分支：改当前场景 `rgScene[wNumScene−1].wMapNum` 并立即 `kLoadScene + PAL_LoadResources()`；否则 2754 `rgScene[operand0−1]` **1 基 scene.json 行号**；哨兵按无符号 WORD 原样往返，同 0x007F frames 0xFFFF 先例）、`mapNum`=operand1（写入 wMapNum（global.h:117）：**地图号**，消费视图 PAL_LoadMap/res.c:252 按此加载地图）；operand2 参考 case 未用 | shared（2742：break 2756） | sequential | 两字段重编码；operand2 保留 | 2（均哨兵形态：@7C71 mapNum 164、@7C8A mapNum 165；operand2 全 0） |
| 0x0078 | `noop`（#78 已具名，#112 复核维持） | 无操作数映射：2225-2227 上游「FIXME: ???」——**空 case 体直落 break**，经共享函数尾 3085 正常推进，真正的无操作；参考无任何更强语义可声明（如实保持 #78 具名） | shared（2224：break 2228） | sequential | 三操作数保留源 WORD | 35（三操作数全 0，佐证 case 体不读） |
| 0x00A7 | `advanceEntry`（新，动词复用 0x0008） | 无操作数映射：3641-3643 裸 `wScriptEntry++` + break，无上游注释、不读操作数——**auto 开关专属位**（PAL_RunAutoScript 自有开关的末尾 case；trigger 开关与 InterpretInstruction 均无 case，trigger 执行将落 3077 TerminateOnError）——即「每帧一条的标准推进」，等价无操作；`OpcodeExecutor` 新增 `autoSwitchOnly` 声明（纯声明，decode/validate/encode 路径零改动）；后继 sequential；动词与 0x0008（trigger 开关专属、同体推进）复用 advanceEntry | **autoSwitchOnly**（3641：3642 `wScriptEntry++`、3643 break） | sequential | 三操作数保留源 WORD | 295（三操作数全 0；地址集中于 @9BB5 起的密集带） |

真实数据核对：本批命中合计 2104（新具名 1733 = 0x0045 68 + 0x0047 313 + 0x0077 44 + 0x00A3 4 + 0x0059 883 + 0x0065 124 + 0x0099 2 + 0x00A7 295；复核 371 = setMusic 155 + setSceneScripts 62 + setParty 119 + noop 35 本已具名）。**raw 命中从 #111 的 1733 降至 0（−1733，恰为本批新具名命中数——真实 chunk 4 的全部 43538 条指令至此无一 `raw`）**。本批无引用字段、无跳转目标，无悬空拒绝面变化；0x00A3 track 的 (SHORT) 补码双向按 signedWord 往返，0x0099 sceneId 哨兵、0x0065 reload 真值、0x006D onEnter 36203 越符号界、0x0043 mode 三形态、0x0059 越界/同场景 no-op 形态均按无符号 WORD 原样往返。export→rebuild SHA `f36cf853…b39e72a` 字节一致（348304 = 43538×8）。框架最小扩展：**`OpcodeExecutor` 新增 `autoSwitchOnly` 成员**（纯声明）；`battleMusicId`/`soundId`/`fadeUnits`/`musicId`/`sceneId`/`playerRole`/`spriteNum`/`reload`/`onEnter`/`onTeleport`/`roleId0/1/2`/`mapNum` 全部沿用既有 word 绑定，`track` 复用 signedWord；无新字段绑定种类。**场景移动域（75 个 opcode：移动 31（#105-#107）+ 对象 13（#108-#109）+ 屏幕 19（#110-#111）+ 本批音乐场景特殊 12）至此全部具名；同时 script.c 全部 165 个 opcode 位全部具名（机器核对：注册表位集合 165 与 script.c case 清单 165 双向差集为空），M5 opcode 具名阶段收官。**

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
