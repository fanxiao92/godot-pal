# godot-pal

《仙剑奇侠传 98 柔情版》的跨平台桌面版重新实现 —— 使用 Godot .NET + C# 从头实现。

## 这是什么

把经典 RPG《仙剑奇侠传》(目标版本:**1998 柔情版**)做成一个跨平台的桌面游戏。**使用 Godot .NET 引擎和 C# 语言原生重写整个游戏**,代码模块清晰,方便日后给游戏加场景、剧情、任务、物品、技能、玩法。

- **忠实移植优先**:第一目标是把原版 98 柔情版完整、忠实地在桌面平台上跑起来。
- **参考而非 fork**:sdlpal 的 C 源码作为引擎逻辑的"规格说明书"(战斗公式、脚本语义、数据格式),我们照着它用 C# 重写。
- **个人自用**:自己游玩,不公开发布。

## 当前状态

**M0 工程骨架与工具链已验收通过，准备进入 M1。** 核心/游戏/CLI/测试链路、本地干净检出与真实 Windows CI 均通过，桌面显示和正常关闭也已由用户确认。

表现层/外壳层细节尚待设计。[事件 JSON 契约](docs/design/events-schema.md) 已确定原版忠实低层转写策略、v1 文件结构与基础命令,全量 opcode 映射及工具/运行时尚未实现。独立 CLI 当前仅提供帮助/错误入口,尚未实现资源提取。下一步需准备真实 98 柔情版数据并编写 M1 计划。

## 技术栈与开发环境

| 项目 | 当前基线 |
|---|---|
| 语言 | C# |
| 引擎 | Godot .NET `4.7.2`，Godot.NET.Sdk `4.7.2` |
| .NET SDK | `10.0.402`，按 `global.json` 允许同 feature band 的补丁更新 |
| 目标框架 | 全部项目为 `net10.0` |
| 离线工具 | 独立 C#/.NET CLI，不依赖 Godot 或游戏工程 |
| 自动化测试 | xUnit `2.9.3` + CLI 真实进程测试 + Godot 启动 smoke test |
| 本地验收环境 / CI | Windows x64、PowerShell 7 / GitHub Actions `windows-2022` |

版本约束以 `global.json`、各项目 `.csproj` 和脚本为准，README 提供开发环境概览。跨平台桌面是目标，其他操作系统与导出包尚未验证。GdUnit4Net 尚未接入，M2 前需验证兼容性或明确替代方案。

## 构建、测试与运行

从仓库根目录执行，需先安装上述 SDK 和完整的 Godot .NET 引擎：

```powershell
dotnet restore GodotPal.sln
dotnet build GodotPal.sln --no-restore
dotnet test GodotPal.sln --no-build --no-restore
dotnet run --project tools/Pal.Extract --no-build -- --help
```

启动桌面游戏（替换为本机完整 Godot .NET 安装中的 console 可执行文件）：

```powershell
& "C:/path/to/godot-mono.console.exe" --path src/Pal.Game
```

本地与 CI 共用的完整自动化检查入口：

```powershell
pwsh -NoProfile -File scripts/Test-M0.ps1 `
  -GodotPath "C:/path/to/godot-mono.console.exe"
```

该入口使用全新 NuGet 缓存，执行还原、构建、测试和 headless 导入/启动；日志路径会打印到终端。桌面显示和正常关闭仍需人工检查。

技术版本记录在此，检查由测试和 CI 执行，任务结果记录在 GitHub Issue 中；不要求每次检查另写一份报告。M0 的详细历史记录保留在 [归档](docs/archive/m0/acceptance.md)，仅在排障或追溯时查阅。

## 仓库结构

- `docs/` —— [文档导航](docs/README.md)
  - `design/` —— 项目设计背景、架构、总体路线和待定问题
  - `milestones/` —— 各里程碑实施计划，当前有 `m0/plan.md`
  - `archive/` —— 已完成阶段的历史记录，按需查阅
  - `agents/` —— Agent 的 Issue、标签与领域文档工作流
  - `adr/` —— 值得长期保留的架构取舍
- `GLOSSARY.md` —— 项目领域术语
- `src/Pal.Core/` —— 纯 .NET 核心类库
- `src/Pal.Game/` —— Godot .NET 游戏工程
- `tools/Pal.Extract/` —— 独立离线提取 CLI(当前仅帮助与参数检查)
- `tests/Pal.Core.Tests/` —— 核心 xUnit 测试
- `tests/Pal.Extract.Tests/` —— CLI 真实进程测试
- `scripts/` —— 引擎下载、兼容性探针、游戏启动检查与共享验收入口
- `reference/sdlpal/` —— sdlpal 源码,作为引擎逻辑参考(见 `reference/README.md`)
- `data/raw/` —— 放原版 98 柔情版数据文件的地方(见 `data/raw/README.md`)

## 怎么继续

在本目录新开一个对话,按 `AGENTS.md` 的工作流配置读取文档。设计背景见 `docs/design/` 和 `reference/README.md`;实现时读取对应 GitHub Issue 正文、评论及里程碑计划。仅在排障、环境升级或追溯历史结论时查阅 `docs/archive/`。
