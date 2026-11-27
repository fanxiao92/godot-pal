# godot-pal 架构总览

本页是仓库当前态架构总图（对应 [ADR-0008](docs/adr/0008-native-harness-and-architecture-guardrails.md) 与代码地形整备规格 [#270](https://github.com/fanxiao92/godot-pal/issues/270)）。实时指令/模块支持状态见自动生成的 [`sdlpal` 对位与能力账本](docs/generated/capabilities-ledger.md)，验证档位见 [验证矩阵](docs/testing/verification-matrix.md)，引擎参考源码导航见 [reference/README.md](reference/README.md)。

## 1. 三段单向数据与运行时管道

```text
[原版 MKF / 私有数据] (永不入库)
        │
        ▼  tools/Pal.Extract (离线纯 .NET CLI)
[extracted JSON / PNG 中间包] (data/extracted/，永不入库)
        │
        ▼  src/Pal.Game/Editor/Prepare*.cs (Godot headless 离线准备)
[Godot 原生 .tres / .scn / Texture2D] (Samples / 本地 T3 缓存)
        │
        ▼  src/Pal.Game (Godot 4.7.2 运行时) ──▶ src/Pal.Core (纯 .NET 领域逻辑)
[可玩场景 / 对话 / 战斗 / 菜单 / 5槽存档]
```

**核心不变量**：
- 运行时墙后零 MKF、零离线 JSON 解析、零自动同步；仅消费 Godot 原生 `Resource` / `PackedScene` / `Texture2D`。
- `src/Pal.Core` 为纯 .NET 10 单库，零 Godot/GdUnit/文件 I/O/墙钟时间依赖，精确业务时间经显式时间参数接口推进。

## 2. 物理模块拓扑（保持 2 个核心 `.csproj`）

### `src/Pal.Core/`（纯 .NET 10 单库，9 个子域 + 根入口）

| 目录 | 命名空间 | 职责 |
| :--- | :--- | :--- |
| `/` | `Pal.Core` | 根入口 `Startup.cs` |
| `Shared/` | `Pal.Core.Shared` | 底座纯工具与共享常量（`EventJson`、`SaveSlotBounds`） |
| `State/` | `Pal.Core.State` | 底座实时状态与共享角色/经验快照（`RoleLiveState`、`CashLiveState`、`InventoryLiveState`、`PartyPoisonState`、`BattleRoleSnapshots`） |
| `Exploration/` | `Pal.Core.Exploration` | 探索输入、行走碰撞、场景元数据与静态规则 |
| `Dialogue/` | `Pal.Core.Dialogue` | 双语对话包、控制符词法与逐帧显示状态机 |
| `Battle/` | `Pal.Core.Battle` | 经典回合战斗核心、生命周期、现代数值内容与装备状态 |
| `Events/` | `Pal.Core.Events` | Trigger/Auto 脚本执行器、静态 `SupportedOpcodes` 能力表与动态世界 |
| `Menu/` | `Pal.Core.Menu` | 探索菜单与商店买卖会话 |
| `Save/` | `Pal.Core.Save` | 5 槽玩家存档契约、JSON 编解码、恢复计划与存盘门禁 |
| `Legacy/` | `Pal.Core.Legacy` | **冻结的历史切片**（M4 首战 `FirstMandatoryBattle*`），禁止正式域反向依赖 |

### `src/Pal.Game/Scripts/`（Godot 4.7.2 C# 工程，`.cs` 与 `.cs.uid` 成对管理）

| 目录 | 命名空间 | 职责 |
| :--- | :--- | :--- |
| `Scripts/` | `Pal.Game` | 顶层宿主入口与跨视图编排（`Main.cs`、`Main.Story.cs`、`Main.Dynamic.cs`、`GameStartup.cs`、`PlayerSaveStorage.cs`） |
| `Scripts/Resources/` | `Pal.Game.Resources` | 稳定 Godot `Resource` 定义与依赖辅助（保护样本与本地 T3 缓存 `.tres` 绑定，严禁随意改路径/命名空间） |
| `Scripts/Map/` | `Pal.Game.Map` | 原生地图/场景加载与校验 |
| `Scripts/Profiles/` | `Pal.Game.Profiles` | 剧情/探索/事件 Profile 加载与适配 |
| `Scripts/Dialogue/` | `Pal.Game.Dialogue` | 对话渲染与 Trigger 对话视图（`PalDialogueDisplay`、`PalDialoguePaletteSurface`、`PalTriggerDialogue`） |
| `Scripts/Battle/` | `Pal.Game.Battle` | 剧情战斗视图、伤害数字、姿势时钟与攻防演出 |
| `Scripts/UI/` | `Pal.Game.UI` | 探索菜单视图与商店视图（`PalMenuView`、`PalShopView`） |
| `Editor/` | `Pal.Game.Editor` | 离线资源准备脚本（`Prepare*.cs`），运行时禁止依赖 |

## 3. 冻结历史切片边界

- **M3 独立对话切片**（`PalNpcInteractionProfile` / `NativeNpcInteractionProfile`）与 **M4 首战切片**（`Pal.Core.Legacy` + 层 C1 封套资源 + `Main.cs` 旧入口）仅保留以维持既有测试覆盖，显式标记为冻结状态，禁止在其上扩展新业务功能或引入正式域反向依赖。

## 4. 分层机械护栏

| 护栏载体 | 触发时机 | 拦截范围 |
| :--- | :--- | :--- |
| `.editorconfig` + `BannedApiAnalyzers` (`RS0030`) | 编译期（`dotnet build`） | `Pal.Core` 与 `*UnitTests` 封禁磁盘 I/O、进程、墙钟时间、P/Invoke、`Godot`/`GdUnit4`；`Pal.Game` 全禁 `InternalsVisibleTo` |
| `tools/Pal.Analyzers` (`PAL0001`–`PAL0003`) | 编译期（`dotnet build`） | `PAL0001` 4 条方向红线（禁正式域引 `Legacy`、禁运行时引 `Editor`、禁底座反引上层、禁视图层互引）；`PAL0002` 友元白名单；`PAL0003` 禁生产代码测试开关钩子 |
| `scripts/Test-UnitBoundary.ps1` | T1 CI / 本地预检 | 根目录白名单、`.csproj` 窄形状、零 MSBuild 运行时依赖闭包 |
| `scripts/Test-RepoHygiene.ps1 -AllDocs` | T1 CI / 本地预检 | `.gdignore` 孤儿 `.uid`、相对链接与 `#anchor` 有效性、活动文档孤儿可达性、脚本字面引用 |
| `./pal.ps1 test check-ledger` | T1 CI / 本地预检 | `docs/generated/capabilities-ledger.md` 与代码真源零漂移 |

## 5. 文档与导航入口

- 完整文档导航：[docs/README.md](docs/README.md)
- 代码派生能力账本：[docs/generated/capabilities-ledger.md](docs/generated/capabilities-ledger.md)
- 验证矩阵与命令速查：[docs/testing/verification-matrix.md](docs/testing/verification-matrix.md)
- 编码规范：[CODING_STANDARDS.md](CODING_STANDARDS.md) · 领域术语：[GLOSSARY.md](GLOSSARY.md)
