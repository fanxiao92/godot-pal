# godot-pal

《仙剑奇侠传 98 柔情版》的跨平台桌面版重新实现 —— 使用 Godot .NET + C# 从头实现。

## 这是什么

把经典 RPG《仙剑奇侠传》(目标版本:**1998 柔情版**)做成一个跨平台的桌面游戏。**使用 Godot .NET 引擎和 C# 语言原生重写整个游戏**,代码模块清晰,方便日后给游戏加场景、剧情、任务、物品、技能、玩法。

- **忠实移植优先**:第一目标是把原版 98 柔情版完整、忠实地在桌面平台上跑起来。
- **参考而非 fork**:sdlpal 的 C 源码作为引擎逻辑的"规格说明书"(战斗公式、脚本语义、数据格式),我们照着它用 C# 重写。
- **个人自用**:自己游玩,不公开发布。

## 当前状态

**M0 工程骨架与工具链已验收通过，准备进入 M1。** 核心/游戏/CLI/测试链路、本地干净检出与真实 Windows CI 均通过，桌面显示和正常关闭也已由用户确认。

设计讨论尚未完全结束 —— 表现层/外壳层细节、事件 JSON 的精确 schema、技术栈细节待定。已通过本机 Godot C# 兼容性闸门(Godot .NET 4.7.2 + SDK 10.0.402 + net10.0),建立最小核心/游戏/测试链路并通过 headless 启动及用户桌面可视验收。独立 CLI 当前仅提供帮助/错误入口,尚未实现资源提取。共享验收入口和 CI 证据见 `docs/09-m0-acceptance.md`;下一步需准备真实 98 柔情版数据并编写 M1 计划。

## 仓库结构

- `docs/` —— 设计文档(**从这里开始读**)
  - `01-feasibility.md` 背景与可行性
  - `02-architecture.md` 架构设计
  - `03-development-plan.md` 开发计划 / 里程碑
  - `04-decisions.md` 决策记录与待定问题
  - `05-m0-implementation-plan.md` M0 环境检查、工程骨架计划与验收标准
  - `06-godot-compatibility.md` Issue #2 兼容性结论、复现方法与失败检测验证
  - `07-game-startup.md` Issue #3 最小启动链路、构建测试命令与验收状态
  - `08-extract-cli.md` Issue #4 独立 CLI 入口、进程测试与复现命令
  - `09-m0-acceptance.md` Issue #5 共享验收入口、官方引擎下载与真实 CI 证据
- `src/Pal.Core/` —— 纯 .NET 核心类库
- `src/Pal.Game/` —— Godot .NET 游戏工程
- `tools/Pal.Extract/` —— 独立离线提取 CLI(当前仅帮助与参数检查)
- `tests/Pal.Core.Tests/` —— 核心 xUnit 测试
- `tests/Pal.Extract.Tests/` —— CLI 真实进程测试
- `scripts/` —— 兼容性探针与游戏启动检查
- `reference/sdlpal/` —— sdlpal 源码,作为引擎逻辑参考(见 `reference/README.md`)
- `data/raw/` —— 放原版 98 柔情版数据文件的地方(见 `data/raw/README.md`)

## 怎么继续

在本目录新开一个对话,按 `AGENTS.md` 的工作流配置读取文档。设计背景见 `docs/01-feasibility.md` 至 `docs/04-decisions.md` 和 `reference/README.md`;实现时还需读取对应 GitHub Issue 及里程碑计划/验证记录。
