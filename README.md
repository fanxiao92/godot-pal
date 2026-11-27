# godot-pal

《仙剑奇侠传 98 柔情版》的跨平台桌面版重新实现 —— 使用 Godot .NET + C# 从头实现。

## 这是什么

把经典 RPG《仙剑奇侠传》（目标版本：**1998 柔情版**）重实现为跨平台桌面游戏。使用 Godot .NET 引擎与 C# 原生重写核心逻辑与表现层，保持模块清晰以便扩展。

- **忠实移植优先**：第一目标是把原版 98 柔情版完整、忠实地在桌面平台上跑起来。
- **参考而非 fork**：`reference/sdlpal/` 的 C 源码仅作为只读规格参考（战斗公式、脚本语义、数据格式），用 C# 重新实现。
- **个人自用**：本地游玩，不提交、不上传任何原版数据或版权资源。

## 当前状态

- **M0–M6 已主线交付并合入 `main`**：涵盖工程骨架（M0）、静态场景提取（M1）、单领队探索（M2）、真实 NPC 双语对话切片（M3）、首战剧情战斗上下文（M4）、全量数据表与 165 opcode 忠实转写/往返（M5），以及完整经典战斗、状态/装备/背包/商店菜单与五槽存读档/战败重载（M6）。
- **M7 验证基建已落地**：独立游戏测试工程 `tests/Pal.Game.Tests`（官方 nuget.org GdUnit4）、跨平台统一前门 `./pal.ps1`、四档验证分层（T0–T3）、`ubuntu-24.04` 3 并行 Linux CI（`dotnet` / `prepare` / `game`）与 T3 共享真实资源缓存 + GPU YAML 端到端回归均已上线。
- 动态任务进度、当前里程碑与远端状态以 [GitHub Issues](https://github.com/fanxiao92/godot-pal/issues) 与实时提交/CI 为准；各里程碑计划见 [docs/README.md](docs/README.md)。

## 技术栈与开发环境

| 项目 | 当前基线 |
| --- | --- |
| 语言 / 目标框架 | C# / `net10.0` |
| 引擎 | Godot .NET `4.7.2`（`Godot.NET.Sdk 4.7.2`） |
| .NET SDK | `10.0.x`（以 `global.json` 为准） |
| 统一前门 | 根目录 `./pal.ps1`（PowerShell 7 / `pwsh`，Windows 与 Linux 共用） |
| 离线工具 | `tools/Pal.Extract`（纯 .NET CLI，零 Godot 依赖） |
| 自动化测试 | Core/Extract xUnit + CLI/Prepare 集成 + `tests/Pal.Game.Tests`（官方 GdUnit4）+ T3 GPU YAML E2E |
| 自动 CI | GitHub Actions `ubuntu-24.04` 3 并行必需 job（`dotnet` / `prepare` / `game`） |

具体版本约束以 `global.json`、各项目 `.csproj` 与 `pal.ps1` 为准。

## 快速开始与常用命令

所有测试、引擎准备、资源准备与证据生成均通过 `./pal.ps1` 执行；选档规则与证据要求见[验证矩阵](docs/testing/verification-matrix.md)。

### 1. 构建与日常验证（T0 / T2）

```powershell
# 还原与构建
dotnet restore GodotPal.sln
dotnet build GodotPal.sln --no-restore

# T0 增量选测（自动基于 .csproj 依赖图与 tests/affected-map.psd1 选测；加 --explain 仅预览）
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test affected

# 定向运行单层
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test unit
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test integration
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test game
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test lra

# 本地全量公开套件（映射自检 + unit + integration + game）
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test full
```

### 2. 固定版 Godot 准备

```powershell
# 定位本机已安装的固定版 Godot 4.7.2-stable mono
pwsh -NoProfile -NonInteractive -File ./pal.ps1 godot locate

# 或按当前 OS 自动下载固定版本并完成 --headless --import 预热
pwsh -NoProfile -NonInteractive -File ./pal.ps1 godot install --destination .artifacts/godot
pwsh -NoProfile -NonInteractive -File ./pal.ps1 godot import
```

### 3. 离线提取与资源准备

将原版 98 柔情版数据文件放在本机私有目录（例如 `data/raw/sdlpal/`，绝不提交或上传）：

```powershell
# 只读检查原始 MKF 归档
dotnet run --project tools/Pal.Extract --no-build -- inspect --input data/raw/sdlpal

# 提取静态场景包（详见 docs/milestones/m1/extract-scene.md）
dotnet run --project tools/Pal.Extract --no-build -- extract-scene `
  --input data/raw/sdlpal --output data/extracted/m1-scene-2 `
  --scene 2 --palette 0 --lighting day

# 显式准备单类原生资源（统一经 pal.ps1 封装 scripts/Prepare-*.ps1）
pwsh -NoProfile -NonInteractive -File ./pal.ps1 assets prepare `
  --kind scene --input data/extracted/m1-scene-2 --output src/Pal.Game/Assets
```

### 4. T3 真实资源 GPU 端到端回归与证据报告

```powershell
# 设置原版数据目录（可选 PAL_CACHE_DIR 指定共享缓存根）
$env:PAL_RAW_DATA_DIR = "C:/path/to/raw-sdlpal"

# 命中或构建仓库外共享缓存并链接至工作树
pwsh -NoProfile -NonInteractive -File ./pal.ps1 assets prepare
pwsh -NoProfile -NonInteractive -File ./pal.ps1 assets status

# 运行本地 GPU YAML 端到端 5 场景并生成标准 PR 证据评论
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test e2e --gpu --out .artifacts/pal/verify-t3
pwsh -NoProfile -NonInteractive -File ./pal.ps1 report --from .artifacts/pal/verify-t3 --out .artifacts/pal/verify-t3/pr-comment.md
```

### 5. 启动桌面游戏

运行时仅消费已准备的 Godot 原生 Resource / PackedScene / Texture2D（不读取 JSON/MKF，不自动同步）：

```powershell
& "<path-to-godot-mono-console>" --path src/Pal.Game
```

## 仓库结构

- `pal.ps1` —— 跨平台统一前门 CLI（测试、CI、Godot 安装预热、资源准备、证据报告）
- `AGENTS.md` —— Agent 按需上下文指针与不可逆安全硬约束
- `CODING_STANDARDS.md` —— `/code-review` Standards 轴评审标准
- `GLOSSARY.md` —— 项目领域术语表
- `docs/` —— [文档导航](docs/README.md)
  - `testing/` —— [验证矩阵](docs/testing/verification-matrix.md)及 3 篇专题规范（Game 分层、时序边界、合并保护）
  - `adr/` —— 架构决策记录（`ADR-0001`–`ADR-0007`）
  - `design/` —— 项目设计背景、架构与数据/事件/战斗契约
  - `milestones/` —— 各里程碑实施计划（M0–M6）
  - `agents/` —— Agent 执行环境、Issue 工作流、单票/多票与异常诊断规则
  - `archive/` —— M0 历史验收归档（按需查阅）
- `src/Pal.Core/` —— 纯 .NET 核心类库（零 Godot 依赖）
- `src/Pal.Game/` —— 纯 Godot .NET 游戏生产工程（零测试依赖）
- `tools/Pal.Extract/` —— 独立离线提取与往返验证 CLI
- `tools/long-running-agent/` —— 基于 GitHub Issues 的长任务编排器
- `tests/` —— 全部自动化测试工程：
  - `Pal.Core.UnitTests/`、`Pal.Extract.UnitTests/` —— 纯内存 xUnit 单元测试
  - `Pal.Extract.IntegrationTests/`、`Pal.Prepare.Tests/` —— CLI 文件进程与 Godot Prepare 集成测试
  - `Pal.Game.Tests/` —— 独立 Godot .NET / 官方 GdUnit4 集成测试与 T3 GPU YAML E2E 场景
  - `affected-map.psd1` —— T0 增量选测映射表
- `scripts/` —— `pal.ps1` 模块、离线资源准备原语与轻量仓库护栏脚本
- `reference/sdlpal/` —— 只读 SDLPal 参考源码（见 [reference/README.md](reference/README.md)）
- `data/raw/` —— 本地原版 98 柔情版数据目录（不提交，见 [data/raw/README.md](data/raw/README.md)）

## 新会话接入提示

新开对话时直接按 [AGENTS.md](AGENTS.md) 触发对应指针：先读[执行环境契约](docs/agents/execution-environment.md)，核对仓库、分支、HEAD、已有改动与目标 Issue 正文/评论/依赖；验证与取证统一遵循[验证矩阵](docs/testing/verification-matrix.md)与 `./pal.ps1`。
