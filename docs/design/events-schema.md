# events.json 契约

状态：**转换策略与 v1 契约已定义；Pal.Extract 的完整 chunk 4 导出/重建已实现基础离线子集。** 当前只具名核实过的 `0x0000`、`0x003C` 与 `0xFFFF`；其余 opcode 保留为 `raw`。此离线能力不表示运行时支持或全游戏入口/可达性已核实。对话使用原始消息引用，正文由独立语言包提供；语言包格式与 SLF 显示语义的集成留到 M3 事件里程碑，阶段边界见 [总体路线](development-plan.md)。M1/M2 仅处理静态场景资源与构建，不要求转换或执行事件。全量 opcode 注册表、真实数据验证与运行时支持按里程碑补齐，不将本文描述当成已存在的工具能力。

原版剧情、触发与 NPC 行为由 `SSS.MKF` 中的脚本驱动。离线工具将其转为 `data/extracted/events/` 中的可读 JSON，运行时只读取现代资源，不解析 MKF。背景见 [架构设计](architecture.md)、[决策索引](decisions.md) 和 [忠实转写决策](../adr/0001-faithful-event-transcription.md)。术语见 [术语表](../../GLOSSARY.md)。

## 当前 Pal.Extract 命令子集

```text
pal-extract export-events --input <原始资源目录> --output <新建或空目录>
pal-extract rebuild-events --input <events目录> --output <新文件>
```

`export-events` 只读 `SSS.MKF` 的 chunk 0（32-byte `EVENTOBJECT` 数组）、chunk 1（8-byte `SCENE` 数组）及 chunk 4（8-byte `SCRIPTENTRY` 数组）。它完整转写 chunk 4 到 `shared.json`，全部命令保留原地址、顺序后继及三个 WORD 操作数；当前场景和场景对象的非零 trigger/auto 根也被枚举为入口。由于尚未核实足以安全划分的完整可达性，全部命令保守放在 `shared`，不声称对象表 union 根或全局可达性已完成分类。

实现的具名映射限于 `0x0000 end(keep)`、`0x003C setDialog(upper)`、`0xFFFF showDialog`；其余 opcode—including 契约的其他基础动作—仍以 `raw` 保存。`rebuild-events` 校验版本、严格字段、重复 JSON 键、WORD 范围、地址完整性、next 与入口引用，并按具名映射重编码；未映射操作数和低字节字段高位保留。只写 chunk 4 字节，不重建整个 MKF。输入必须是完整事件集合，输出不会覆盖既有内容。重建后比较实际 chunk SHA-256 与清单记录；匹配时可证明字节相同，具名语义编辑导致不匹配时，成功输出会明确提示差异而不回退复制旧 source 字节。

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

注册表在离线工具中定义。每个具名 opcode 必须声明：动词、三个操作数各自的字段映射或保留规则、WORD/有符号转换、引用及特殊值、trigger/auto 差异、控制流后继和编码规则。没有核实完整映射时保持 `raw`。

反向编译以 `source.opcode` 和三个原始操作数为基础，使用**具名语义字段重新编码所有已映射操作数**，保留未映射的位和操作数；不能只把 `source` 原样复制就宣称具名转换正确。动词与原始 opcode 不匹配时拒绝。字段若只对应低位，例如颜色 BYTE，编码时保留原 WORD 中未映射的高位。

本版定义以下基础映射；它们是待实现的契约，不表示目前 CLI 已支持：

| opcode | `op` | 额外必填字段与映射 |
|---|---|---|
| `0x0000` | `end` | `mode: "keep"`；保留三个原操作数 |
| `0x0001` | `end` | `mode: "advance"`；保留三个原操作数，推进到 `next` |
| `0x0002` | `end` | `mode: "redirect"`、`to` 为引用映射 operand 0、`limit` 为 WORD 映射 operand 1；operand 2 保留 |
| `0x0003` | `goto` | `to` 为引用映射 operand 0、`limit` 为 WORD 映射 operand 1；operand 2 保留 |
| `0x001F` | `giveItem` | `itemId` 为 WORD 映射 operand 0、`count` 为有符号整数映射 operand 1；operand 2 保留 |
| `0x003B` | `setDialog` | `box: "center"`、`color` 为 `0..255` 映射 operand 0 低字节、`rngWord` 为 WORD 映射 operand 2；operand 1 保留 |
| `0x003C` / `0x003D` | `setDialog` | `box: "upper"` / `"lower"`、`portraitId` 为 WORD 映射 operand 0、`color` 为 `0..255` 映射 operand 1 低字节、`rngWord` 为 WORD 映射 operand 2 |
| `0x003E` | `setDialog` | `box: "centerWindow"`、`color` 为 `0..255` 映射 operand 0 低字节；operand 1、2 保留 |
| `0xFFFF` | `showDialog` | `messageId` 为 WORD 映射 operand 0、`descriptionLineWord` 为 WORD 映射 operand 1；operand 2 保留。正文通过语言包解析，不内嵌可执行的 `text` 字段 |

注意：

- `limit` 是对应空闲计数器的比较上限，**不是帧延迟**；0 的特殊语义保留。
- `0x0002` 在计数条件成立时停止/改入口，条件失败会清零计数并继续 `next`。因此不能把所有 `end` 当成无条件终止。
- `giveItem.count` 可以为负；不把原始 `65535` 错读成增加 65535 个。
- `setDialog` 与 `showDialog` 分开保留，不能把前者吞并进后一条文字命令。对话框、头像、颜色及 RNG 状态是执行上下文；`rngWord` 非零表示真，但编码仍保留完整 WORD。
- `showDialog.messageId` 是原始消息引用，不是某个语言包内部重新编号的字符串下标。同文不同 ID 仍是不同引用；反向编译使用 ID，而非按文字内容反查索引。正文及控制信息保存在语言包，保留原文、空行和转义，不 trim 或自行合并。
- `descriptionLineWord` 在 trigger 中不参与消息显示，在 WIN95 物品/法术描述调用方中用于行号推进：值 1 不增加行号，其他值增加一行。该行为见 [itemmenu.c](../../reference/sdlpal/itemmenu.c)、[magicmenu.c](../../reference/sdlpal/magicmenu.c)，不能误当成始终未使用的操作数。
- 语言包内容不参与 chunk 4 编码。编辑文字不会自动改写 `M.MSG`；脚本 chunk 的 round-trip 也不覆盖文字编码与消息偏移表的往返。
- 不在当前命令上接受无关语义字段，例如 `showDialog.box` 或旧草案的 `showDialog.text`。其他动作、条件、调用及修改入口指令须按最小切片补充注册，不能借用一个相似名字代替原版语义。

### 语言包与 SLF 来源

公共事件不按语言复制。当前数据包的 `m_chs.slf`、`m_eng.slf` 分别提供简体中文与英文，默认使用简体中文；离线工具将它们转成现代语言包，运行时不解析 SLF，也不回头读取 `M.MSG`。替换语言配置不改变原始指令地址、消息 ID、剧情分支或可变入口。

SLF 不保证一条原始文字指令对应一行翻译。参考 `text.c` 的 `PAL_ReadMessageFile`、`PAL_GetMsgNum` 与 `script.c` 的 `MESSAGE_GetSpan`，它按原始开始消息 ID 和跨度匹配消息块，块内可以有多行及 `[CLEAR MESSAGE]`。语言包须保留这些映射与各语言自己的显示控制；不能按翻译行数改写、复制或删除忠实转写的事件命令。

语言包文件格式及字段、消息块的执行位置衔接、trigger/auto 描述分支与清屏指令的具名映射在 M3 对应任务中定义并验证，不能仅靠本节就宣称运行时支持 SLF。名称、描述、字体与布局按参考来源处理；不额外定义对话中途热切换或跨语言逐字进度映射。

这一调整取代尚未实现的“在 `showDialog.text` 内嵌单一语言正文”草案。原始指令一对一保留和 chunk 4 往返范围不变；语言包的正确性需独立测试。

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
      "op": "giveItem",
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
