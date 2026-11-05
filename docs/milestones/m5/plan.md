# M5：数据与脚本全量转换

## 权威范围与当前状态

状态：**已交付（#113 收官）。** 全部 38 张前置票（#78–#112、#114、#115）集成后，#113 完成机器化总验收（注册表完整性常驻核对、真实数据终验、反例覆盖机检）与文档收口；交付结果与票面勘误见[交付结果与勘误](#交付结果与勘误113-收官)。忠实还原以 `reference/sdlpal/` 为权威参考（[ADR-0001](../../adr/0001-faithful-event-transcription.md)、[ADR-0004](../../adr/0004-data-and-script-layers.md)）。

M5 是**纯离线**里程碑：交付 opcode 注册表 + 反向编译器 + 全量数据提取与验证。运行时执行全量 opcode 留后续里程碑，M5 不承诺可执行，只保证逐字节/字段验证证明转写保真。

## 已确认决策

1. **纯离线边界**：只做转写/提取/验证，运行时执行全量 opcode 由后续里程碑处理。
2. **165 个 opcode 全具名**：每个声明动词、操作数字段映射、WORD/有符号、引用与特殊值、trigger/auto 差异、控制流后继、反向编码规则。
3. **MKF 覆盖 11 个**：排除 `rng.mkf`（音频，留 M7）与 `fire.mkf`（循环动画，留 M7）。
4. **离线 JSON**：产物为可核对 JSON，Godot 原生资源由后续 Prepare 步骤生成，离线 CLI 保持纯 .NET 与引擎解耦。
5. **验证保真度**：脚本 chunk 4 逐字节往返；数据表语义解析（字段值 + 来源散列/长度），不重编码回字节。
6. **数据层/脚本层分层**：data-schema 与 events-schema 两套契约分离（ADR-0004）。
7. **验证数据边界**：真实数据本机往返/字段核对；CI 用人工构造完整地址数组 + 微型归档夹具，分层绑定身份。
8. **完整证据**：注册表完整性机器核对 + 真实 chunk 逐字节往返 + 数据字段独立核对 + 反向编码定向反例。
9. **数据清单 13 张表**：`SSS` chunk 0/1/2 + `DATA` chunk 0/1/2/3/4/5/6/11/13/14；不含 `desc.dat`/`word.dat`（文本走 SLF，M3/M4 已交付）。
10. **数据表文件组织**：每表一个文件 + `index.json` 清单（来源 chunk、记录数、字节长度、SHA-256）。
11. **数据表只读**：只提取 + 严格校验，无 rebuild 命令、无反编码器。
12. **MKF 图像完整包**：`abc/ball/f/fbp` 解码为完整图像包 + manifest，可被 M6 战斗 Prepare 复用。
13. **无依赖并行拆解**：唯一前置是基座（契约框架 + 引用命名约定），其余任务无相互依赖、可并行。
14. **粒度按 30 分钟 review 上限倒推**：opcode 每批约 10 个、数据表每表一个（大表拆 2）、MKF 每件一个。
15. **opcode 六域**：控制流 / 对话表现 / 状态物品 / 战斗 / 菜单 / 场景移动；域内精确归属以 `script.c` 机器核对为准。

## 范围

1. **数据表全量提取**：13 张表 → 只读 JSON（每表一文件 + index）。
2. **MKF 图像类覆盖**：`abc`/`ball`/`f`/`fbp` → 完整图像包 + manifest。
3. **opcode 全量具名**：165 个 → 注册表 + 反向编码 + 逐字节往返。

## 拆解（基座 + 独立任务 + 验收）

唯一前置 **T0 基座**：data-schema 契约文档 + 校验器框架 + 全部引用命名约定（`itemId`/`magicId`/`enemyId`/`enemyTeamId`/`sceneId`/`objectId` 等）；events 注册表框架确认（复用 `EventArchive` register 机制）+ 往返/反例测试骨架。基座冻结命名约定后，下述任务无相互依赖、可并行。

| 类别 | 任务 | 粒度 |
|---|---|---|
| 数据表（15） | EventObject / Scene / Object（拆 2）/ Store / Enemy / EnemyTeam / PlayerRoles（拆 2）/ Magic / BattleField / LevelUpMagic / BattleEffectIndex / EnemyPos / LevelUpExp | 每表：schema + 提取 + 严格校验 + 字段独立核对 |
| MKF（4） | abc / ball / f / fbp | 每件：解码 + 完整包 + manifest |
| opcode（约 16） | 按六域、每批约 10 个 | 每批：具名 + 反向编码 + 定向反例 |

opcode 的精确分批在 T0 之后从 `script.c` 的 `case` 清单机器核对再定；本计划只锁「每批约 10 个」的粒度规则。T-final 验收（1 个）：注册表机器核对（165 无遗漏）+ 真实 chunk 逐字节往返 + 汇总字段核对 + 反例汇总 + events-schema 注册表章节更新 + 文档收尾。

## 复用与契约

- 术语见 [GLOSSARY](../../../GLOSSARY.md)，分层见 [ADR-0004](../../adr/0004-data-and-script-layers.md)，忠实转写见 [ADR-0001](../../adr/0001-faithful-event-transcription.md)。
- 脚本契约沿用 [events-schema](../../design/events-schema.md)，M5 补全其注册表章节；数据表契约新写 `docs/design/data-schema.md`。
- 复用既有 MkfArchive 通用 chunk 读取、Yj2/RLE/帧表/调色板解码、ImageExporter；不造平行解码器。
- 复用可核身份的现存产物，新输出只写新建/空目录；真实原始/派生资源与图形证据不提交上传，远端仅用可提交夹具。

## 验收

- opcode 注册表 165 个无遗漏（从 `script.c` case 清单机器比对）。
- 真实 `SSS.MKF` chunk 4 逐字节往返；每张数据表字段值与 SDLPal 独立核对。
- 每个具名 opcode 的反向编码定向反例（编辑语义字段后真实重编码，非复制 source 字节）。
- CI 用人工构造完整地址数组 + 微型归档夹具，覆盖 0 下标、WORD 回绕、raw、全 opcode 样例、数据表正反例。
- 完整门按离线 CLI 的 unit/integration 层执行；无 Godot/GdUnit/GPU/人工验收（纯离线里程碑）。

## 交付结果与勘误（#113 收官）

M5 全部任务已交付：基座与 13 张数据表（#78–#91）、4 件 MKF 图像完整包（#92–#95）、17 批 165 个 opcode（#96–#112）、src 侧加载器字段迁移（#114/#115，M5 期间并入并已在 base 集成）。#113 终验收口：注册表完整性（165 位 vs script.c 173 个 `case` 标签去重清单双向差集为空）与反例覆盖（每 opcode ≥1）落为常驻 Unit 测试 `tests/Pal.Extract.UnitTests/OpcodeRegistryCompletenessTests.cs`；真实数据终验全绿（export-events→rebuild-events SHA `f36cf853…b39e72a` 逐字节一致、raw 命中 0；export-data 13 表 recordCount/byteLength/chunk SHA 与 object 视图分布核对；export-battle-images ×4）；CLI Unit / CLI Integration / Core Unit 三层终门全绿（Prepare/Game 不重跑，引用 #115 同源全绿留档）。证据：`C:/evidence/m5/113/receipt.md`。

票面勘误与裁定记录：

1. **ball.mkf 实为物品位图（票面命名勘误）**：本计划把 `abc/ball/f/fbp` 统称「MKF 图像类覆盖」，CLI 动词冻结为 `export-battle-images`；实施核对（#93）确认 `ball.mkf` 是物品位图单元（BALL.MKF，item 视图 `bitmap` 字段指向的段，itemmenu/uigame 原样 RLE 读取、无 YJ2/无帧表），非战斗精灵——冻结动词不改，语义按 #93 登记于 [battle-image-schema](../../design/battle-image-schema.md)。
2. **throwWeapon=magicId（#103 裁定）**：票面预留「throwWeapon 的 itemId」方向按证据裁定为 `magicId`：0x0066 被投掷物对象经 `PAL_BattleSimulateMagic` 的 wMagicObjectID 按 object.json magic 视图消费（fight.c:5301/5321），真实数据 344/360 全落 magic 视图证据并集 295..394。
3. **enemyTeamId=敌队表行号（#78 定名、#103 复核）**：0x0007 operand0 是 enemy-team.json（DATA chunk 2）行号（battle.c:1602 `lprgEnemyTeam[wEnemyTeam]`），不是 object union 下标。
4. **越界数据事实（原版数据如实保留，提取不校验表范围）**：0x0007 真实数据 1 条 @42205 的 enemyTeamId=380 越敌队表 380 行（0..379）上沿，参考运行时按越界行读取（#103）；0x0067 enemyUseMagic 的 magicId 395/396/397 越 magic 视图证据并集 295..394 上沿（另 0x0042 的 24 不落任何已冻结证据区间，同口径按消费视图命名，#104/#103）；0x009E/0x009F 的 enemy 视图 objectId（403..512/410..470）脚本侧首次全落冻结区间 398..550 内（佐证不并入提取期证据，#104）；0x0058 的 itemId 281..283 越 item 证据并集 61..278 上沿（#97，留后续视图判定票）。另 setAutoEntry/setTriggerEntry 的 entry 上探 41099/43524 与 setSceneScripts 的 onEnter 36203 越 0x7FFF，为 entry 保持无符号 WORD 的直接证据（#108/#112）。
5. **M4RealBattle 资产前置不计入**：M5 交付不含 `Assets/M4RealBattle53*` 系列的原生战斗资产前置（gitignored，属 M4 义务）；#115 同源 Prepare/Game 运行中 `RealFirstBattleFlowTests` 的唯一失败即此前置缺失（错误与 #114 留档逐字相同），按留档口径不计入。M5 的 abc/ball/f/fbp 完整包供 M6 战斗 Prepare 复用。
6. **CI 夹具覆盖映射**（「验收」节「CI 用人工构造完整地址数组 + 微型归档夹具」的落地对号）：
   - 完整 65,536 地址数组 + WORD 后继回绕 + 跨文件集合/重复地址标签/悬空引用 → `EventCliTests`（如 `Rebuild_events_supports_full_word_address_space_and_wraps_next_to_zero`，Integration 层微归档夹具）；
   - raw → `OpcodeCounterexampleTests.Unregistered_opcode_decodes_raw_and_round_trips_source_operands`（Unit，raw 样例 0x0032）+ `EventCliTests.Legacy_raw7_package_rebuilds_without_a_named_mapping_migration`（旧 M2/M3 包兼容）；
   - 全 opcode 样例 → 各批 `Opcode*Tests` 反例（165 位全覆盖，#113 起常驻机检）+ 各批 `EventCliTests` 真实命中形态往返样例；
   - 数据表正反例 → 各表 `*TableTests`（Unit，人工字节正反例）+ `ExportData*Tests`（Integration，微型归档夹具与 index 清单）。
