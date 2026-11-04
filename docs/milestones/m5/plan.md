# M5：数据与脚本全量转换

## 权威范围与当前状态

状态：**范围已确认，尚未实施。** 忠实还原以 `reference/sdlpal/` 为权威参考（[ADR-0001](../../adr/0001-faithful-event-transcription.md)、[ADR-0004](../../adr/0004-data-and-script-layers.md)）。本计划落盘已确认决策与拆解骨架；不发布 GitHub Issues、不认领任务、不编码、不准备资源、不运行 Godot/测试、不提交或推送。

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
