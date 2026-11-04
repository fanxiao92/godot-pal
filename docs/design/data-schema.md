# 数据表 JSON 契约（data-schema）v1

状态：**契约框架与逐字段 schema 已冻结（#78）。** 通用规则、文件组织、13 张表的逐字段 schema 与引用命名约定已锁定；各表的提取实现由 #79–#91 逐票登记。分层依据见 [ADR-0004](../adr/0004-data-and-script-layers.md)。

## 范围与边界

- 13 张数据表，**只读提取 + 严格校验 + 语义解析**；不重编码回原始字节、不提供 rebuild 命令（ADR-0004）。
- 脚本 `SSS.MKF` chunk 4 走 [events-schema](events-schema.md)，不在本契约内。
- 物品/法术描述、角色名等文本走 SLF 语言包（M3/M4 已交付），不解析 `desc.dat`/`word.dat`。
- 运行时只读现代 JSON，不回读原始归档；Godot 原生资源由后续 Prepare 步骤生成。

## 通用规则

- `formatVersion` 为整数 `1`，`profile` 为 `"faithful"`；不识别的版本/profile 明确拒绝。
- JSON 不含注释、重复键、NaN 或无穷值；未知字段拒绝（下划线开头的字段仅作可读注释，忽略但不接受重复键）。
- `WORD` 为整数 `0..65535`；`SHORT`（有符号 WORD）字段按 `-32768..32767` 解释，不把所有字段统一按有符号解释。哪些字段按有符号解释以 `global.h` 的 C 类型声明为权威，并以下文各表标注的参考实际使用为证（负值确有语义，如 `sVanishTime = -15`）。
- 字节序一律小端：`LOAD_DATA` / `DO_BYTESWAP` 在小端主机是 no-op，原始数据按小端 WORD 读出（[global.c](../../reference/sdlpal/global.c)）。
- 每张表记录其来源（MKF 名 + chunk 号）、记录数、字节长度与 chunk SHA-256；**记录数 = chunk 字节长度 ÷ 结构大小**，不整除即明确失败并定位来源，不硬编码猜测。
- 数据表**只读**：只提供提取与校验，无反向编码器、无编辑后写回。

### 字段命名规则（T0 冻结）

- JSON 字段用 camelCase，与 events 契约一致；去掉 C 成员的 `w`/`s`/`n`/`rgw` 前缀（`wTriggerScript`→`triggerScript`、`sState`→`state`、`rgwItems`→`items`），保留其余拼写。
- 指向其他表的引用字段按 [引用命名约定](#引用命名约定t0-冻结) 命名（如 `enemyId`、`magicId`、`itemId`）。
- 多维数组按 C 布局展开为嵌套 JSON 数组，外层对应第一维（最后一个下标变化最快），不转置、不展平。
- 参考成员名在每表字段表中以「C 名」标注保留可追溯性。

## 文件组织

```text
data/extracted/data/
  index.json             版本、来源清单、文件列表与各自散列
  event-object.json      SSS chunk 0
  scene.json             SSS chunk 1
  object.json            SSS chunk 2（union，见下）
  store.json             DATA chunk 0
  enemy.json             DATA chunk 1
  enemy-team.json        DATA chunk 2
  player-roles.json      DATA chunk 3
  magic.json             DATA chunk 4
  battle-field.json      DATA chunk 5
  level-up-magic.json    DATA chunk 6
  battle-effect-index.json  DATA chunk 11
  enemy-pos.json         DATA chunk 13
  level-up-exp.json      DATA chunk 14
```

每表一个文件 + `index.json` 清单；清单记录来源 chunk、记录数、字节长度、SHA-256。

### 表文件格式 v1

每表文件为单一 JSON 对象：

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` | 整数 `1` |
| `profile` | 字符串 `"faithful"` |
| `fileId` | 与文件名一致的表标识符（如 `"event-object"`） |
| `records` | 记录对象数组，按 chunk 内物理顺序 |

记录对象只含对应结构描述符声明的字段（object.json 另含视图声明，见下）；缺失、多余或越界均拒绝。`index.json` 为：

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` | 整数 `1` |
| `profile` | 字符串 `"faithful"` |
| `sources` | 来源归档数组：`{ "archive": "SSS.MKF" \| "DATA.MKF", "sha256": 小写 64 位十六进制 }`，按 SSS、DATA 顺序 |
| `tables` | 已登记表数组，字典序；每项 `{ "file", "archive", "chunk", "recordCount", "byteLength", "sha256" }`（`sha256` 为来源 chunk 字节） |

提取命令：`pal-extract export-data --input <原始资源目录> --output <新建或空目录>`（#78 交付骨架：校验 SSS.MKF 与 DATA.MKF 存在后写 `index.json`，`tables` 暂为空数组；各表由 #79–#91 登记）。

## 引用命名约定（T0 冻结）

脚本操作数与数据表字段中的 ID 引用统一命名：

| 字段 | 引用目标 |
|---|---|
| `itemId` | `object.json` 的 item 视图 |
| `magicId` | `magic.json` 或 `object.json` magic 视图中的法术编号字段 |
| `enemyId` | `enemy.json` |
| `enemyTeamId` | `enemy-team.json` |
| `sceneId` | `scene.json` |
| `objectId` | `object.json`（union，须带视图类型） |
| `eventObjectId` | `event-object.json`（SSS chunk 0） |

**`eventObjectId` 与 `objectId` 是两张不同的表**：`event-object.json`（场景事件对象，`EVENTOBJECT`，16 WORD/32 字节）与 `object.json`（物品/法术/敌人/毒脚本 union，`OBJECT`，7 WORD/14 字节）是不同的数据源。指向前者的字段命名 `eventObjectId`，指向后者的命名 `objectId`（并声明视图）。这一区分同时冻结于 [events-schema](events-schema.md) 的 opcode 注册表章节。

此命名约定由 events-schema 的具名命令字段与 data-schema 的表字段共用同一套 ID 语义。

## 13 张表逐字段 schema

容量常量（[palcommon.h](../../reference/sdlpal/palcommon.h)）：`MAX_SCENES=300`、`MAX_OBJECTS=600`、`MAX_EVENT_OBJECTS=5500`、`MAX_LEVELS=99`、`MAX_PLAYER_ROLES=6`、`MAX_PLAYABLE_PLAYER_ROLES=5`、`MAX_STORE_ITEM=9`、`MAX_ENEMIES_IN_TEAM=5`、`MAX_PLAYER_EQUIPMENTS=6`、`MAX_PLAYER_MAGICS=32`、`NUM_MAGIC_ELEMENTAL=5`。容量是参考的上限约定；实际记录数仍由 chunk 长度 ÷ 结构大小得出。

### event-object.json — SSS 0，`EVENTOBJECT`，32 字节（16 WORD）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `vanishTime` | `sVanishTime` | SHORT | 消失倒计时；可负（[play.c](../../reference/sdlpal/play.c):92 逐帧 +1/−1；[script.c](../../reference/sdlpal/script.c) 0x004B 写 −15） |
| `x` | `x` | WORD | 地图 X 坐标 |
| `y` | `y` | WORD | 地图 Y 坐标 |
| `layer` | `sLayer` | SHORT | 层；可负（script.c 0x0084 以 `(SHORT)` 写入） |
| `triggerScript` | `wTriggerScript` | WORD | trigger 入口（chunk 4 地址；对应 events 入口槽 `wTriggerScript`） |
| `autoScript` | `wAutoScript` | WORD | auto 入口（chunk 4 地址；对应入口槽 `wAutoScript`） |
| `state` | `sState` | SHORT | 对象状态（0 隐藏/1 正常/2 拦阻，`OBJECTSTATE`）；负值有语义，按有符号存储 |
| `triggerMode` | `wTriggerMode` | WORD | 触发方式（0..8，`TRIGGERMODE`） |
| `spriteNum` | `wSpriteNum` | WORD | 精灵编号（MGO.MKF） |
| `spriteFrames` | `nSpriteFrames` | WORD | trigger 侧精灵总帧数 |
| `direction` | `wDirection` | WORD | 朝向（`PALDIRECTION` 0..3） |
| `currentFrameNum` | `wCurrentFrameNum` | WORD | 当前帧号 |
| `scriptIdleFrame` | `nScriptIdleFrame` | WORD | trigger 空闲计数器 |
| `spritePtrOffset` | `wSpritePtrOffset` | WORD | 参考标注 FIXME，原样保留 |
| `spriteFramesAuto` | `nSpriteFramesAuto` | WORD | auto 侧精灵总帧数 |
| `scriptIdleFrameCountAuto` | `wScriptIdleFrameCountAuto` | WORD | auto 空闲计数器 |

真实数据核对（本机只读）：162464 ÷ 32 = 5077 条（≤ `MAX_EVENT_OBJECTS`）。

### scene.json — SSS 1，`SCENE`，8 字节（4 WORD）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `mapNum` | `wMapNum` | WORD | 地图编号（MAP.MKF） |
| `scriptOnEnter` | `wScriptOnEnter` | WORD | 进入场景脚本入口（chunk 4 地址；对应入口槽 `wScriptOnEnter`） |
| `scriptOnTeleport` | `wScriptOnTeleport` | WORD | 传送离开脚本入口（chunk 4 地址；对应入口槽 `wScriptOnTeleport`） |
| `eventObjectIndex` | `wEventObjectIndex` | WORD | 本场景事件对象起始基址（对象从 `eventObjectIndex + 1` 起，指向 event-object.json） |

真实数据核对：2360 ÷ 8 = 295 条（≤ `MAX_SCENES`）。

### object.json — SSS 2，`OBJECT` union，14 字节（7 WORD，WIN95）/ 12 字节（6 WORD，DOS）

union 表：**每条记录必须声明 `view`（`player` / `item` / `magic` / `enemy` / `poison`）之一，同一物理 WORD 组不得同时按多视图解释**；记录对象只含所声明视图的字段。视图判定依据独立证据（脚本引用、入口槽、语言包名称区间），由 object 表提取票（拆 2）核对固定；未确定视图的记录显式留待调查，不猜测、不默认。

变体判定沿用参考 `PAL_IsWINVersion`：chunk 2 字节长度对 `sizeof(OBJECT)`（14）整除且对 12 不整除 ⇒ WIN95；反向 ⇒ DOS（结合压缩签名计分）。DOS 包加载时参考把 `rgwData[6]=rgwData[5]`（flags）、`rgwData[5]=0` 转成 WIN 布局（[global.c](../../reference/sdlpal/global.c) `PAL_LoadDefaultGame`）；提取按 chunk 内原始布局忠实导出，WIN/DOS 视图字段差异见下（`scriptDesc` 为 WIN95 新增槽）。真实数据核对：7910 ÷ 14 = 565 条（÷12 余 2）⇒ WIN95。

| 视图 | JSON 字段 | C 名（WORD 序） | 类型 | 说明 |
|---|---|---|---|---|
| player | `reserved` | `wReserved[2]` | WORD[2] | 参考注明恒零，原样导出不强制 |
| player | `scriptOnFriendDeath` | `wScriptOnFriendDeath` | WORD | 队友死亡入口（chunk 4 地址；入口槽 `player.wScriptOnFriendDeath`） |
| player | `scriptOnDying` | `wScriptOnDying` | WORD | 濒死入口（入口槽 `player.wScriptOnDying`） |
| item | `bitmap` | `wBitmap` | WORD | BALL.MKF 位图编号 |
| item | `price` | `wPrice` | WORD | 价格 |
| item | `scriptOnUse` | `wScriptOnUse` | WORD | 使用入口（入口槽 `item.wScriptOnUse`） |
| item | `scriptOnEquip` | `wScriptOnEquip` | WORD | 装备入口（入口槽 `item.wScriptOnEquip`） |
| item | `scriptOnThrow` | `wScriptOnThrow` | WORD | 投掷入口（入口槽 `item.wScriptOnThrow`） |
| item | `scriptDesc` | `wScriptDesc` | WORD | 描述脚本（WIN95；auto 入口槽 `item.wScriptDesc`；DOS 无此槽） |
| item | `flags` | `wFlags` | WORD | `ITEMFLAG` 位集（usable/equipable/throwable/consuming/applyToAll/sellable/可装备角色位 6..） |
| magic | `magicId` | `wMagicNumber` | WORD | 指向 magic.json（DATA chunk 4）的法术编号 |
| magic | `reserved1` | `wReserved1` | WORD | 参考注明恒零，原样导出 |
| magic | `scriptOnSuccess` | `wScriptOnSuccess` | WORD | 成功入口（入口槽 `magic.wScriptOnSuccess`） |
| magic | `scriptOnUse` | `wScriptOnUse` | WORD | 使用入口（入口槽 `magic.wScriptOnUse`） |
| magic | `scriptDesc` | `wScriptDesc` | WORD | 描述脚本（WIN95；auto 入口槽 `magic.wScriptDesc`；DOS 无此槽） |
| magic | `reserved2` | `wReserved2` | WORD | 参考注明恒零，原样导出 |
| magic | `flags` | `wFlags` | WORD | `MAGICFLAG` 位集 |
| enemy | `enemyId` | `wEnemyID` | WORD | 指向 enemy.json（DATA chunk 1）；亦为 ABC.MKF 位图编号 |
| enemy | `resistanceToSorcery` | `wResistanceToSorcery` | WORD | 法术/毒抗性（0..10） |
| enemy | `scriptOnTurnStart` | `wScriptOnTurnStart` | WORD | 回合开始入口（入口槽 `enemy.wScriptOnTurnStart`） |
| enemy | `scriptOnBattleEnd` | `wScriptOnBattleEnd` | WORD | 战斗结束入口（入口槽 `enemy.wScriptOnBattleEnd`） |
| enemy | `scriptOnReady` | `wScriptOnReady` | WORD | 就绪入口（入口槽 `enemy.wScriptOnReady`） |
| poison | `poisonLevel` | `wPoisonLevel` | WORD | 毒等级 |
| poison | `color` | `wColor` | WORD | 头像颜色 |
| poison | `playerScript` | `wPlayerScript` | WORD | 玩家中毒每回合脚本（入口槽 `poison.wPlayerScript`） |
| poison | `reserved` | `wReserved` | WORD | 参考注明恒零，原样导出 |
| poison | `enemyScript` | `wEnemyScript` | WORD | 敌人中毒每回合脚本（入口槽 `poison.wEnemyScript`） |

player 视图物理上占 union 前段（4 WORD 有定义，后 3 WORD 保留位原样导出为 `padding` 数组——win 布局 7 WORD 中未定义槽不丢弃）。

### store.json — DATA 0，`STORE`，18 字节（WORD[9]）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `items` | `rgwItems[MAX_STORE_ITEM]` | WORD[9] | 商品列表；元素为 `itemId`（object.json item 视图），0 = 空槽 |

真实数据核对：378 ÷ 18 = 21 条。

### enemy.json — DATA 1，`ENEMY`，70 字节（35 WORD）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `idleFrames` | `wIdleFrames` | WORD | 待机总帧数 |
| `magicFrames` | `wMagicFrames` | WORD | 施法总帧数 |
| `attackFrames` | `wAttackFrames` | WORD | 普攻总帧数 |
| `idleAnimSpeed` | `wIdleAnimSpeed` | WORD | 待机动画速度 |
| `actWaitFrames` | `wActWaitFrames` | WORD | 参考标注 FIXME |
| `yPosOffset` | `wYPosOffset` | WORD | Y 偏移 |
| `attackSound` | `wAttackSound` | SHORT | 普攻音效；声明 SHORT，负值有语义（[fight.c](../../reference/sdlpal/fight.c):2928 对同类音效做 `>= 0` 判断） |
| `actionSound` | `wActionSound` | SHORT | 参考标注 FIXME；声明 SHORT 按有符号导出 |
| `magicSound` | `wMagicSound` | SHORT | 施法音效（fight.c:2928 WIN95 `>= 0` 判断） |
| `deathSound` | `wDeathSound` | SHORT | 死亡音效 |
| `callSound` | `wCallSound` | SHORT | 入场音效 |
| `health` | `wHealth` | WORD | HP |
| `exp` | `wExp` | WORD | 击败经验 |
| `cash` | `wCash` | WORD | 击败现金 |
| `level` | `wLevel` | WORD | 等级 |
| `magicId` | `wMagic` | WORD | 法术编号，指向 magic.json |
| `magicRate` | `wMagicRate` | WORD | 施法概率 |
| `attackEquivItemId` | `wAttackEquivItem` | WORD | 普攻等价物品，指向 object.json item 视图 |
| `attackEquivItemRate` | `wAttackEquivItemRate` | WORD | 等价物品概率 |
| `stealItemId` | `wStealItem` | WORD | 偷取物品，指向 object.json item 视图 |
| `stealItemCount` | `nStealItem` | WORD | 可偷取数量 |
| `attackStrength` | `wAttackStrength` | WORD | 攻击力 |
| `magicStrength` | `wMagicStrength` | WORD | 灵力 |
| `defense` | `wDefense` | WORD | 防御 |
| `dexterity` | `wDexterity` | WORD | 身法 |
| `fleeRate` | `wFleeRate` | WORD | 逃跑率 |
| `poisonResistance` | `wPoisonResistance` | WORD | 毒抗性 |
| `elementalResistance` | `wElemResistance[NUM_MAGIC_ELEMENTAL]` | WORD[5] | 五系元素抗性 |
| `physicalResistance` | `wPhysicalResistance` | WORD | 物理抗性 |
| `dualMove` | `wDualMove` | WORD | 是否双动 |
| `collectValue` | `wCollectValue` | WORD | 炼化收集值 |

真实数据核对：10780 ÷ 70 = 154 条。

### enemy-team.json — DATA 2，`ENEMYTEAM`，10 字节（WORD[5]）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `enemies` | `rgwEnemy[MAX_ENEMIES_IN_TEAM]` | WORD[5] | 敌队成员；元素为 `enemyId`（enemy.json），0 = 空位 |

真实数据核对：3800 ÷ 10 = 380 条。

### player-roles.json — DATA 3，`PLAYERROLES`，900 字节（450 WORD，单条记录）

数组维度均为 `[MAX_PLAYER_ROLES]=6`（角色维）；多维数组外层为第一维。

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `avatar` | `rgwAvatar` | WORD[6] | 状态界面头像（RGM.MKF） |
| `battleSprite` | `rgwSpriteNumInBattle` | WORD[6] | 战斗精灵（F.MKF）；`rgwBattleEffectIndex` 按此编号索引（fight.c:2055） |
| `spriteNum` | `rgwSpriteNum` | WORD[6] | 场景精灵（MGO.MKF） |
| `name` | `rgwName` | WORD[6] | 角色名（WORD.DAT 文字索引） |
| `attackAll` | `rgwAttackAll` | WORD[6] | 是否全体攻击 |
| `unknown1` | `rgwUnknown1` | WORD[6] | 参考标注 FIXME，原样导出 |
| `level` | `rgwLevel` | WORD[6] | 等级 |
| `maxHp` | `rgwMaxHP` | WORD[6] | HP 上限 |
| `maxMp` | `rgwMaxMP` | WORD[6] | MP 上限 |
| `hp` | `rgwHP` | WORD[6] | 初始 HP |
| `mp` | `rgwMP` | WORD[6] | 初始 MP |
| `equipment` | `rgwEquipment[MAX_PLAYER_EQUIPMENTS][MAX_PLAYER_ROLES]` | WORD[6][6] | 装备（部位×角色）；元素为 `itemId`，0 = 未装备 |
| `attackStrength` | `rgwAttackStrength` | WORD[6] | 攻击力基值 |
| `magicStrength` | `rgwMagicStrength` | WORD[6] | 灵力基值 |
| `defense` | `rgwDefense` | WORD[6] | 防御基值 |
| `dexterity` | `rgwDexterity` | WORD[6] | 身法基值 |
| `fleeRate` | `rgwFleeRate` | WORD[6] | 逃跑率 |
| `poisonResistance` | `rgwPoisonResistance` | WORD[6] | 毒抗性 |
| `elementalResistance` | `rgwElementalResistance[NUM_MAGIC_ELEMENTAL][MAX_PLAYER_ROLES]` | WORD[5][6] | 五系元素抗性（系×角色） |
| `unknown2` | `rgwUnknown2` | WORD[6] | 参考标注 FIXME |
| `unknown3` | `rgwUnknown3` | WORD[6] | 参考标注 FIXME |
| `unknown4` | `rgwUnknown4` | WORD[6] | 参考标注 FIXME |
| `coveredBy` | `rgwCoveredBy` | WORD[6] | 低 HP/异常时代为承受的角色 |
| `magic` | `rgwMagic[MAX_PLAYER_MAGICS][MAX_PLAYER_ROLES]` | WORD[32][6] | 法术表（槽×角色）；元素为 `magicId`，0 = 未习得 |
| `walkFrames` | `rgwWalkFrames` | WORD[6] | 行走帧（参考标注 ???） |
| `cooperativeMagic` | `rgwCooperativeMagic` | WORD[6] | 合击法术编号 |
| `unknown5` | `rgwUnknown5` | WORD[6] | 参考标注 FIXME |
| `unknown6` | `rgwUnknown6` | WORD[6] | 参考标注 FIXME |
| `deathSound` | `rgwDeathSound` | WORD[6] | 死亡音效 |
| `attackSound` | `rgwAttackSound` | WORD[6] | 攻击音效 |
| `weaponSound` | `rgwWeaponSound` | WORD[6] | 武器音效 |
| `criticalSound` | `rgwCriticalSound` | WORD[6] | 会心音效 |
| `magicSound` | `rgwMagicSound` | WORD[6] | 施法音效 |
| `coverSound` | `rgwCoverSound` | WORD[6] | 掩护音效 |
| `dyingSound` | `rgwDyingSound` | WORD[6] | 濒死音效 |

真实数据核对：900 ÷ 900 = 1 条。音效字段在 PLAYERROLES 中是 `WORD` 数组（不同于 ENEMY 的 SHORT 音效），按无符号导出。

### magic.json — DATA 4，`MAGIC`，32 字节（16 WORD）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `effect` | `wEffect` | WORD | 效果精灵 |
| `type` | `wType` | WORD | `MAGIC_TYPE`（0 普通/1 攻全体/2 攻整队/3 攻全场/4 施于单人/5 施于全队/8 变身/9 召唤） |
| `xOffset` | `wXOffset` | WORD | X 偏移 |
| `yOffset` | `wYOffset` | WORD | Y 偏移 |
| `summonEffect` | `rgSpecific.wSummonEffect` | WORD | 召唤效果精灵（`type=9` 时按此视图解释，与 `layerOffset` 二选一） |
| `layerOffset` | `rgSpecific.sLayerOffset` | SHORT | 非召唤法术层偏移（fight.c:2518 `(SHORT)` 读取；`type=9` 以外的视图） |
| `speed` | `wSpeed` | SHORT | 效果速度；声明 SHORT（fight.c 按 `(wSpeed+5)*10` 使用） |
| `keepEffect` | `wKeepEffect` | WORD | 参考标注 FIXME |
| `fireDelay` | `wFireDelay` | WORD | 发动阶段起始帧 |
| `effectTimes` | `wEffectTimes` | WORD | 效果次数 |
| `shake` | `wShake` | WORD | 震屏 |
| `wave` | `wWave` | WORD | 波动屏 |
| `unknown` | `wUnknown` | WORD | 参考标注 FIXME |
| `costMp` | `wCostMP` | WORD | MP 消耗 |
| `baseDamage` | `wBaseDamage` | WORD | 基础伤害 |
| `elemental` | `wElemental` | WORD | 元素（0 无，末位为毒） |
| `sound` | `wSound` | SHORT | 音效；声明 SHORT 按有符号导出 |

`rgSpecific` 是同一物理 WORD 的双视图：记录**必须按 `type` 恰好声明其一**（`type=9` 召唤 ⇒ `summonEffect`；其余 ⇒ `layerOffset`），不得两者并存或两者皆缺。真实数据核对：3328 ÷ 32 = 104 条。

### battle-field.json — DATA 5，`BATTLEFIELD`，12 字节（6 WORD）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `screenWave` | `wScreenWave` | WORD | 屏幕波动等级 |
| `magicEffect` | `rgsMagicEffect[NUM_MAGIC_ELEMENTAL]` | SHORT[5] | 五系法术效果倍率；声明 SHORT（fight.c:244 `sDamage *= 10 + rgsMagicEffect[...]`，可负） |

真实数据核对：696 ÷ 12 = 58 条。

### level-up-magic.json — DATA 6，`LEVELUPMAGIC_ALL`，20 字节（10 WORD/条）

每条记录：

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `roles` | `m[MAX_PLAYABLE_PLAYER_ROLES]` | 对象[5] | 每角色一项，按角色顺序 |

`roles` 元素：

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `level` | `wLevel` | WORD | 达到该等级 |
| `magicId` | `wMagic` | WORD | 习得的法术，指向 magic.json |

真实数据核对：400 ÷ 20 = 20 条。

### battle-effect-index.json — DATA 11，`WORD[10][2]`，40 字节（单条记录）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `rows` | `rgwBattleEffectIndex[10][2]` | WORD[10][2] | 行下标为战斗精灵编号（`PAL_GetPlayerBattleSprite`，fight.c:2055 用 `[sprite][1]`、fight.c:2387 用 `[sprite][0]`）；语义归属由后续使用点核对，本表不猜名 |

真实数据核对：40 ÷ 40 = 1 条。

### enemy-pos.json — DATA 13，`ENEMYPOS`，100 字节（单条记录）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `positions` | `pos[MAX_ENEMIES_IN_TEAM][MAX_ENEMIES_IN_TEAM]` | `{x,y}`[5][5] | 敌位坐标（battle.c:936 `EnemyPos.pos[i][wMaxEnemyIndex]`） |

`positions` 元素：

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `x` | `PALPOS.x` | WORD | X 坐标 |
| `y` | `PALPOS.y` | WORD | Y 坐标 |

真实数据核对：100 ÷ 100 = 1 条。

### level-up-exp.json — DATA 14，`WORD[MAX_LEVELS+1]`，200 字节（单条记录）

| JSON 字段 | C 名 | 类型 | 说明 |
|---|---|---|---|
| `levelUpExp` | `rgLevelUpExp[MAX_LEVELS+1]` | WORD[100] | 升级经验阈值表；下标为当前等级（battle.c:1106 `dwExp >= rgLevelUpExp[rgwLevel[w]]`） |

真实数据核对：200 ÷ 200 = 1 条。

## 验收

- 每张表字段值与 SDLPal 加载后的结构**独立核对**（对照 `global.h` 布局与 `LOAD_DATA`/`DO_BYTESWAP`；上文各表「真实数据核对」即以本机只读数据的 chunk 长度 ÷ 结构大小验证记录数整除性）。
- 语义解析：字段值正确 + 来源散列/长度一致即可，不要求重编码回原始字节（区别于脚本的逐字节往返）。
- 严格 schema 校验：非法版本、重复键、未知字段、越界值、记录数不整除结构大小均明确失败并定位。
- 真实数据本机核对；CI 用人工构造微型归档夹具（覆盖 union 视图、byteswap、有符号边界）。
