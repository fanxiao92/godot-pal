# M5 opcode 盘点（机器核对自 script.c）

状态：机器核对初稿，来源为 `reference/sdlpal/script.c` 的 `case` 清单与逐块注释。精确分批在 T0 基座任务中再按本文锁定，不替代逐 opcode 的语义核对。

## 总览

- **165 个 unique opcode**：`0x0000–0x00A7`（168 个位置，缺失 4 个）+ `0xFFFF`。
- **缺失 4 个**：`0x0032`、`0x0048`、`0x0072`、`0x009D`（原版未使用/保留）。
- **173 个 `case` 标签**：`PAL_RunTriggerScript` 与 `PAL_RunAutoScript` 两个 switch 合并；auto 复用部分 opcode 并独占 `0x00A7`。
- **auto 专属**：`0x00A7`（仅 `wScriptEntry++`，推进无副作用）。
- **FIXME 空实现**：`0x0078`（SDLPal 自身未实现，注释 `FIXME: ???`）。
- 存在 fall-through 组合：`0x000B–0x000E` 为同一「walk one step」块，方向由 `opcode - 0x000B` 决定。

## 六域分布

| 域 | 数量 | 说明 |
|---|---|---|
| 控制流 | 29 | end/goto/jump/call/条件跳转/计数/等待/失败/随机跳转/退出 |
| 对话表现 | 8 | setDialog/showDialog/重绘/备份恢复屏幕 |
| 状态物品 | 29 | item/装备/HP/MP/金钱/属性/状态/毒/法术列表/等级 |
| 战斗 | 22 | 开战/敌方行动/战斗结果/敌人技能/战斗精灵 |
| 菜单 | 2 | buy/sell 菜单 |
| 场景移动 | 75 | 见细分 |
| **合计** | **165** | |

### 场景移动细分（75）

| 子域 | 数量 |
|---|---|
| 移动/位置/朝向 | 31 |
| 对象操作（state/script/animate/sync/layer/hide） | 13 |
| 屏幕/表现（fade/palette/FBP/RNG/shake/wave/ending） | 19 |
| 音乐音效 | 5 |
| 场景/地图切换 | 5 |
| 特殊（FIXME `0x0078`、auto `0x00A7`） | 2 |

## 分批建议（每批 ~10）

| 域 | 建议批数 |
|---|---|
| 控制流（29） | 3 |
| 对话表现（8） | 1 |
| 状态物品（29） | 3 |
| 战斗（22） | 2 |
| 菜单（2） | 并入战斗批 |
| 场景移动：移动（31）/ 对象（13）/ 屏幕（19）/ 音乐+场景+特殊（12） | 3 / 2 / 2 / 1 |
| **总计** | **约 16 批** |

## 完整清单（按域）

### 控制流（29）

| opcode | 注释 |
|---|---|
| 0x0000 | Stop running |
| 0x0001 | Stop running and replace the entry with the next line |
| 0x0002 | Stop running and replace the entry with the specified one |
| 0x0003 | unconditional jump |
| 0x0004 | Call script |
| 0x0006 | Jump to the specified address by the specified rate |
| 0x0008 | Replace the entry with the next instruction |
| 0x0009 | wait for the specified number of frames |
| 0x000A | Goto the specified address if player selected no |
| 0x0041 | Mark the script as failed |
| 0x004D | wait for any key |
| 0x004E | Load the last saved game |
| 0x0058 | Jump if there is less than the specified number of the specified items |
| 0x005D | Jump if player doesn't have the specified poison |
| 0x005E | Jump if enemy doesn't have the specified poison |
| 0x0061 | Jump if player is not poisoned |
| 0x0064 | Jump if enemy's HP is more than the specified percentage |
| 0x0068 | Jump if it's enemy's turn |
| 0x0074 | Jump if not all players are full HP |
| 0x0079 | Jump if the specified player is in the party |
| 0x0081 | Jump if the player is not facing the specified event object |
| 0x0083 | Jump if event object is not in the specified zone of the current event object |
| 0x0085 | Delay for a period |
| 0x0086 | Jump if the specified item is not equipped |
| 0x0091 | Jump if the enemy is not first of same kind |
| 0x0094 | Jump if the state of event object is the specified one |
| 0x0095 | Jump if the current scene is the specified one |
| 0x00A0 | Quit game |
| 0x00A2 | Jump to one of the following instructions randomly |

### 对话表现（8）

| opcode | 注释 |
|---|---|
| 0x0005 | Redraw screen |
| 0x003B | Show dialog in the middle part of the screen |
| 0x003C | Show dialog in the upper part of the screen |
| 0x003D | Show dialog in the lower part of the screen |
| 0x003E | Show text in a window at the center of the screen |
| 0x008E | Restore the screen |
| 0x00A6 | backup screen |
| 0xFFFF | Print dialog text |

### 状态物品（29）

| opcode | 注释 |
|---|---|
| 0x0017 | set the player's extra attribute |
| 0x0018 | Equip the selected item |
| 0x0019 | Increase/decrease the player's attribute |
| 0x001A | Set player's stat |
| 0x001B | Increase/decrease player's HP |
| 0x001C | Increase/decrease player's MP |
| 0x001D | Increase/decrease player's HP and MP |
| 0x001E | Increase or decrease cash by the specified amount |
| 0x001F | Add item to inventory |
| 0x0020 | Remove item from inventory |
| 0x0022 | Revive player |
| 0x0023 | Remove equipment from the specified player |
| 0x0028 | Apply poison to enemy |
| 0x0029 | Apply poison to player |
| 0x002A | Cure poison by object ID for enemy |
| 0x002B | Cure poison by object ID for player |
| 0x002C | Cure poisons by level |
| 0x002D | Set the status for player |
| 0x002E | Set the status for enemy |
| 0x002F | Remove player's status |
| 0x0030 | Increase player's stat temporarily by percent |
| 0x0055 | Add magic to a player |
| 0x0056 | Remove magic from a player |
| 0x0057 | Set the base damage of magic according to MP value |
| 0x005A | Halve the player's HP |
| 0x005F | Kill the player immediately |
| 0x0088 | Set the base damage of magic according to amount of money |
| 0x008D | Increase player's level |
| 0x008F | Halve the cash amount |

### 战斗（22）

| opcode | 注释 |
|---|---|
| 0x0007 | Start battle |
| 0x0021 | Inflict damage to the enemy |
| 0x0031 | Change battle sprite temporarily for player |
| 0x0033 | collect the enemy for items |
| 0x0034 | Transform collected enemies into items |
| 0x0039 | Drain HP from enemy |
| 0x003A | Player flee from the battle |
| 0x0042 | Simulate a magic for player |
| 0x004A | Set the current battlefield |
| 0x005B | Halve the enemy's HP |
| 0x0060 | Immediate KO of the enemy |
| 0x0066 | Throw weapon to enemy |
| 0x0067 | Enemy use magic |
| 0x0069 | Enemy escape in battle |
| 0x006A | Steal from the enemy |
| 0x006B | Blow away enemies |
| 0x0089 | Set the battle result |
| 0x008A | Enable Auto-Battle for next battle |
| 0x0092 | Show a magic-casting animation for a player in battle |
| 0x009C | Enemy division itself |
| 0x009E | Enemy summons another monster |
| 0x009F | Enemy transforms into something else |

### 菜单（2）

| opcode | 注释 |
|---|---|
| 0x0026 | Show the buy item menu |
| 0x0027 | Show the sell item menu |

### 场景移动（75）

**移动/位置/朝向（31）**：0x000B–0x0016（walk/direction/gesture/position）、0x0038、0x003F、0x0044、0x0046、0x004C、0x0062、0x0063、0x006C、0x006E、0x0070、0x007A、0x007B、0x007C、0x007D、0x007F、0x0082、0x0097、0x0098、0x00A1。

**对象操作（13）**：0x0024、0x0025、0x0040、0x0049、0x004B、0x0052、0x005C、0x006F、0x007E、0x0084、0x0087、0x0090、0x009A。

**屏幕/表现（19）**：0x0035、0x0036、0x0037、0x004F、0x0050、0x0051、0x0053、0x0054、0x0071、0x0073、0x0076、0x0080、0x008B、0x008C、0x0093、0x0096、0x009B、0x00A4、0x00A5。

**音乐音效（5）**：0x0043、0x0045、0x0047、0x0077、0x00A3。

**场景/地图切换（5）**：0x0059、0x0065、0x006D、0x0075、0x0099。

**特殊（2）**：0x0078（FIXME）、0x00A7（auto 推进）。

## 核对边界

- 域归类以注释语义为据，个别边界 opcode（如 `0x004E` load-saved-game、`0x0035` shake）归入控制流/屏幕表现，实施时可在对应任务内再核对。
- 每个 opcode 的具名字段映射、trigger/auto 差异、反向编码规则仍需在对应任务中从 `script.c` 逐块核对，本清单只锁定「域归属与数量」。
