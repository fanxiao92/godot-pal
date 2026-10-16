# godot-pal

《仙剑奇侠传 98 柔情版》的跨平台桌面版重新实现 —— 使用 Godot .NET + C# 从头实现。

## 这是什么

把经典 RPG《仙剑奇侠传》(目标版本:**1998 柔情版**)做成一个跨平台的桌面游戏。**使用 Godot .NET 引擎和 C# 语言原生重写整个游戏**,代码模块清晰,方便日后给游戏加场景、剧情、任务、物品、技能、玩法。

- **忠实移植优先**:第一目标是把原版 98 柔情版完整、忠实地在桌面平台上跑起来。
- **参考而非 fork**:sdlpal 的 C 源码作为引擎逻辑的"规格说明书"(战斗公式、脚本语义、数据格式),我们照着它用 C# 重写。
- **个人自用**:自己游玩,不公开发布。

## 当前状态(2026-10-16)

处于 **M0 工程骨架阶段**。可行性已确认,核心架构已定,最小核心/游戏/测试链路已建立,详见 `docs/`。

设计讨论尚未完全结束 —— 表现层/外壳层细节、事件 JSON 的精确 schema、技术栈细节待定。已通过本机 Godot C# 兼容性闸门(Godot .NET 4.7.2 + SDK 10.0.402 + net10.0),建立最小核心/游戏/测试链路并通过 headless 启动。桌面可视验收、独立 CLI 与 CI 尚未完成。构建与验收步骤见 `docs/07-game-startup.md`。

## 仓库结构

- `docs/` —— 设计文档(**从这里开始读**)
  - `01-feasibility.md` 背景与可行性
  - `02-architecture.md` 架构设计
  - `03-development-plan.md` 开发计划 / 里程碑
  - `04-decisions.md` 决策记录与待定问题
  - `05-m0-implementation-plan.md` M0 环境检查、工程骨架计划与验收标准
  - `06-godot-compatibility.md` Issue #2 兼容性结论、复现方法与失败检测验证
  - `07-game-startup.md` Issue #3 最小启动链路、构建测试命令与验收状态
- `src/Pal.Core/` —— 纯 .NET 核心类库
- `src/Pal.Game/` —— Godot .NET 游戏工程
- `tests/Pal.Core.Tests/` —— 核心 xUnit 测试
- `scripts/` —— 兼容性探针与游戏启动检查
- `reference/sdlpal/` —— sdlpal 源码,作为引擎逻辑参考(见 `reference/README.md`)
- `data/raw/` —— 放原版 98 柔情版数据文件的地方(见 `data/raw/README.md`)

## 怎么继续

在本目录新开一个对话,让 AI 先读 `docs/` 全部 4 个文件和 `reference/README.md`,即可接着设计/实现。
