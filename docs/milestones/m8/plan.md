# M8 执行计划：全场景运行时（M8-A 核心世界与存档 V2 → M8-B 战斗脚本闭环）

## 1. 权威、基线与范围

- **跟踪入口**：[#125](https://github.com/fanxiao92/godot-pal/issues/125)（阶段归属）· [#279](https://github.com/fanxiao92/godot-pal/issues/279)（M8 wayfinder 路线图，研究结论见 `#280`/`#281`/`#282`，定稿决策见 `#283`）。
- **执行基线**：`main@ff77709`（M6、M7 验证基建 `#249`、原生 Harness `#270` 均已合入 `main` 且 T2 CI 全绿，M6 的 T3 GPU 验收证据已回填至 `#124`）。
- **里程碑编排（已冻结）**：
  - **M8**：全场景运行时（正常新游戏开局、全场景切换、全场景 `trigger`/`auto` VM、`PlayerSaveV2`、多场景原生资产与代表性跨场景主线验收）；
  - **M9**：音视频与演出本体（RIX/音效播放、RNG/AVI 过场、全屏 FBP 卷轴、通用调色板循环、震屏波纹与结局演出，对应 16 条 `M9-Deferred` + 3 条 `setDialog(RngWord!=0)`）；
  - **M10**：整体通关验证与签收。
- **核心架构红线（见根目录 [ARCHITECTURE.md](../../../ARCHITECTURE.md)）**：
  - 严守三段单向管道：M8 多场景事件脚本一并原生化为 `.tres`（运行时零 MKF、零离线 JSON、零自动同步），冻结的 M3/M4 单切片路径保持不动；
  - 保护 `src/Pal.Game/Scripts/Resources/` 既有 `.tres` 类路径/命名空间/`[Export]` 稳定，纯增量新增 `PalWorldManifest.tres` 等世界索引资源；
  - `src/Pal.Core` 保持纯 .NET 零 Godot 依赖，通过编译期 `RS0030` + `PAL0001`–`PAL0003` 与 `./pal.ps1 test check-ledger` 零漂移门禁；
  - 真实原版数据与版权截图永不提交、永不上传；`reference/sdlpal/` 只读参考。

## 2. 两阶段范围与完成条件

| 阶段 | 范围 | 独立完成条件 |
| :--- | :--- | :--- |
| **M8-A**（M8-1～M8-8） | `extract-world` 批导出 + `ObjectUnionTable` 补齐；`trigger`/`auto` 共享分派与全场景 Search/Touch；全局 `EVENTOBJECT`/`SCENE` 世界容器、正常新游戏工厂、两阶段切场/移动/追逐/跟随 + 5 条 `M8-ObservableStub`（标 `⚠️`）；角色/装备/法术/毒状态脚本；`PlayerSaveV2`（稀疏增量 + v1 fail-closed + 排除 `bBattleSpeed`）；`PalWorldManifest.tres` + 原生事件 `.tres` + `PrepareWorld` 3 进程批准备；多场景宿主接线与确定性卸载；T1 公开 2–3 合成互联场景与 T3 `prepare:v2` 场景 1 正常开局→跨场景切换→V2 跨进程存读档验收 | T0/T1 全绿（`dotnet`/`prepare`/`game` + `check-arch` + `check-ledger`）；T3 真实资源 GPU 回归全绿（含新增正常开局跨场景主线 YAML 场景）+ 人工 5 分钟审阅 `contact-sheet.png` |
| **M8-B**（M8-9～M8-10） | 战斗内脚本执行器闭环（28 条 `M8-Combat` 指令 + `0x0030 boostStat` 升级，打通 `ClassicBattleSession` 敌方回合/法术/道具/投掷/偷窃/收妖/毒状态/召唤分裂变身上下文）；M8 能力账本与文档最终收口、关闭 `#125` | T0/T1 全绿（战斗脚本正反例单测 + Game 集成测试 + `check-ledger` 零漂移）；必要时复跑 T3 GPU 回归无退化 |

## 3. 已锁定的 4 项关键契约

1. **Opcode 四档分级（零遗漏，共 111 未实现 + 9 `⚠️`）**：
   - `M8-Core`：62 条 + 5 条 `⚠️` 升级（`0x001B`/`0x001C`/`0x0020`/`0x0022`/`0x003E`）；
   - `M8-Combat`：28 条 + 1 条 `⚠️` 升级（`0x0030`）；
   - `M8-ObservableStub`：5 条非阻塞音视频/辅助指令（`0x0045`/`0x0047`/`0x0077`/`0x00A3`/`0x0092`），显式状态/事件透传并标 `⚠️`，不静默跳过后宣称忠实；
   - `M9-Deferred`：16 条 + 3 条 `⚠️`（`0x003B`–`0x003D` `RngWord!=0`），显式划界留给 M9。
2. **多场景原生资产与两阶段切场**：
   - Core 执行 `0x0059` 仅置 `PendingSceneChange` + `EnteringScene` 并短路当帧；宿主在下一 `UpdateCompleted` 边界先加载校验新 `ActiveSceneScope`（`CacheMode.IgnoreDeep`），再 `Free()` 旧 `mapScene` 并清空引用与订阅，回执后再跑新场景 `onEnter`。
   - 离线准备采用 `extract-world`（按 `mapId`/`spriteId`/`paletteId` 全局去重 + `--no-preview`）+ `PrepareWorld` 固定 3 进程批处理，世界事件脚本原生化为 `.tres`。
3. **`PlayerSaveV2`（`formatVersion = 2`）契约**：
   - 相对 M6-C v1（[m6c-save-matrix.md](../m6/m6c-save-matrix.md)）新增 `battleMusic`、`collectValue`、`screenWave`（`sWaveProgression` 不存，载入清零）、`chaseRange`/`chaseSpeedChangeCycles`、`nightPalette`、`followers(0..2)`、`trail[5]`、跨场景稀疏增量 `objects`（仅存相对静态基表有修改者，载入先播基表再覆写）与 `sceneScripts.mapId`；
   - 维持 Classic 战斗规则，继续显式排除 `bBattleSpeed`；
   - 对 `formatVersion != 2`（含 v1）在解析根节点首顺位 fail-closed 拒绝，无自动迁移、不静默回退新游戏。
4. **分层验证矩阵（遵循 [verification-matrix.md](../../testing/verification-matrix.md)）**：
   - **T0/T1（Linux CI）**：纯 .NET 领域单测 + `Pal.Extract` 批导出集成测试 + `PrepareWorld` 准备测试 + 公开 2–3 合成互联场景（切场/`onEnter`/链式切场/`onTeleport`/`0x0099`/跨场景状态持续性/V2 存读档与卸载）+ `check-arch`/`check-ledger`；
   - **T3（本地 Windows GPU）**：共享缓存升为 `prepare:v2`（与 `v1` 目录隔离，支持 `--scope full`）；新增代表性主线 YAML 场景：场景 1（余杭客栈）正常新游戏开局 → `onEnter` 与客栈/余杭镇真实跨场景切换 → V2 存读档跨进程重启恢复 → 跨场景事件对象状态持续性；辅以人工 5 分钟 `contact-sheet.png` 观感确认。

## 4. 实施叶票与依赖拓扑

全部 10 张实施叶票均挂在 M8 父规格（PRD）下并绑定原生 GitHub `blocked_by` 依赖：

- **M8-A**：
  1. **M8-1**：`Pal.Extract` `ObjectUnionTable` 补齐与 `extract-world` 批导出（无前置依赖）
  2. **M8-2**：`Pal.Core` 统一 `trigger`/`auto` 共享分派层与全场景 Search/Touch 调度（无前置依赖，可与 M8-1 并行）
  3. **M8-3**：`Pal.Core` 全局多场景世界容器、正常新游戏工厂与切场/移动/追逐/跟随指令 + 5 条 `M8-ObservableStub`（依赖 M8-2）
  4. **M8-4**：`Pal.Core` 角色属性、装备脚本、法术与玩家毒/状态指令（依赖 M8-1、M8-2，可与 M8-3 并行）
  5. **M8-5**：`Pal.Core` `PlayerSaveV2` 稀疏增量存档契约、v1 fail-closed 拒绝与世界恢复计划（依赖 M8-3、M8-4）
  6. **M8-6**：`Pal.Game` 多场景原生资源（`PalWorldManifest.tres` + 原生事件 `.tres`）、`PrepareWorld` 3 进程批准备与 T1 合成多场景夹具（依赖 M8-1，可与 M8-3/M8-4 并行）
  7. **M8-7**：`Pal.Game` 多场景运行时宿主接线与确定性卸载（依赖 M8-3、M8-5、M8-6）
  8. **M8-8**：M8-A T3 `prepare:v2` 共享缓存与场景 1 正常开局跨场景主线 GPU E2E 验收（依赖 M8-7）
- **M8-B**：
  9. **M8-9**：`Pal.Core` + `Pal.Game` 战斗内脚本执行器闭环（28 条 `M8-Combat` + `0x0030` 升级）（依赖 M8-4、M8-7）
  10. **M8-10**：M8 能力账本、文档收口与 `#125` 闭环（依赖 M8-8、M8-9）
