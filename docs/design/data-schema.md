# 数据表 JSON 契约（data-schema）v1

状态：**契约框架。** 通用规则、文件组织与 13 张表清单已锁定；每张表的逐字段 schema 在 T0 基座任务中从 `reference/sdlpal/global.h` 的结构定义补齐，本文不预填字段表。分层依据见 [ADR-0004](../adr/0004-data-and-script-layers.md)。

## 范围与边界

- 13 张数据表，**只读提取 + 严格校验 + 语义解析**；不重编码回原始字节、不提供 rebuild 命令（ADR-0004）。
- 脚本 `SSS.MKF` chunk 4 走 [events-schema](events-schema.md)，不在本契约内。
- 物品/法术描述、角色名等文本走 SLF 语言包（M3/M4 已交付），不解析 `desc.dat`/`word.dat`。
- 运行时只读现代 JSON，不回读原始归档；Godot 原生资源由后续 Prepare 步骤生成。

## 通用规则

- `formatVersion` 为整数 `1`，`profile` 为 `"faithful"`；不识别的版本/profile 明确拒绝。
- JSON 不含注释、重复键、NaN 或无穷值；未知字段拒绝（下划线开头的字段仅作可读注释，忽略但不接受重复键）。
- `WORD` 为整数 `0..65535`；有符号字段按结构定义转为 `-32768..32767`，不把所有字段统一按有符号解释。
- 每张表记录其来源（MKF 名 + chunk 号）、记录数、字节长度与 chunk SHA-256；记录数 = chunk 字节长度 ÷ 结构大小，不依赖硬编码猜测。
- 数据表**只读**：只提供提取与校验，无反向编码器、无编辑后写回。

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

## 13 张表清单与结构引用

| 文件 | 来源 | `global.h` 结构 | 要点 |
|---|---|---|---|
| event-object.json | SSS 0 | `EVENTOBJECT` | 事件对象：脚本入口、位置、状态、消失时间等 |
| scene.json | SSS 1 | `SCENE` | 场景：进入/传送脚本入口、对象索引、地图 |
| object.json | SSS 2 | `OBJECT` union | **union**：`player`/`item`/`magic`/`enemy`/`poison` 五种视图，须按用途区分，不能同时按全部视图解释 |
| store.json | DATA 0 | `STORE` | `rgwItems[9]` 商品列表 |
| enemy.json | DATA 1 | `ENEMY` | 怪物：HP/攻击/防御/经验/现金/法术等 |
| enemy-team.json | DATA 2 | `ENEMYTEAM` | `rgwEnemy[5]` 敌队成员 |
| player-roles.json | DATA 3 | `PLAYERROLES` | 6 角色基表：初始 HP/MP/装备/法术/属性/声音等 |
| magic.json | DATA 4 | `MAGIC` | 法术：消耗/威力/效果/目标等 |
| battle-field.json | DATA 5 | `BATTLEFIELD` | 战场 |
| level-up-magic.json | DATA 6 | `LEVELUPMAGIC_ALL` | 各角色升级习得法术 |
| battle-effect-index.json | DATA 11 | `WORD[10][2]` | 战斗效果索引 |
| enemy-pos.json | DATA 13 | `ENEMYPOS` | `PALPOS[5][5]` 敌位坐标 |
| level-up-exp.json | DATA 14 | `WORD[MAX_LEVELS+1]` | 升级经验阈值表 |

容量常量（`palcommon.h`）：`MAX_SCENES=300`、`MAX_OBJECTS=600`、`MAX_LEVELS=99`、`MAX_PLAYER_ROLES=6`、`MAX_STORE_ITEM=9`、`MAX_ENEMIES_IN_TEAM=5`、`MAX_PLAYER_EQUIPMENTS=6`、`MAX_PLAYER_MAGICS=32`。

## 引用命名约定（T0 冻结）

脚本操作数中的 ID 引用数据表，字段命名统一为：

| 字段 | 引用目标 |
|---|---|
| `itemId` | `object.json` 的 item 视图 |
| `magicId` | `magic.json` 或 `object.json` 的 magic 视图 |
| `enemyId` | `enemy.json` |
| `enemyTeamId` | `enemy-team.json` |
| `sceneId` | `scene.json` |
| `objectId` | `object.json`（union，须带视图类型） |

此命名约定在 T0 基座冻结，events-schema 的具名命令字段与 data-schema 的表文件共用同一套 ID 语义。

## 验收

- 每张表字段值与 SDLPal 加载后的结构**独立核对**（对照 `global.h` 布局与 `LOAD_DATA`/`DO_BYTESWAP`）。
- 语义解析：字段值正确 + 来源散列/长度一致即可，不要求重编码回原始字节（区别于脚本的逐字节往返）。
- 严格 schema 校验：非法版本、重复键、未知字段、越界值、记录数不整除结构大小均明确失败并定位。
- 真实数据本机核对；CI 用人工构造微型归档夹具（覆盖 union 视图、byteswap、有符号边界）。
