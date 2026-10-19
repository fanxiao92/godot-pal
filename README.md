# godot-pal

《仙剑奇侠传 98 柔情版》的跨平台桌面版重新实现 —— 使用 Godot .NET + C# 从头实现。

## 这是什么

把经典 RPG《仙剑奇侠传》(目标版本:**1998 柔情版**)做成一个跨平台的桌面游戏。**使用 Godot .NET 引擎和 C# 语言原生重写整个游戏**,代码模块清晰,方便日后给游戏加场景、剧情、任务、物品、技能、玩法。

- **忠实移植优先**:第一目标是把原版 98 柔情版完整、忠实地在桌面平台上跑起来。
- **参考而非 fork**:sdlpal 的 C 源码作为引擎逻辑的"规格说明书"(战斗公式、脚本语义、数据格式),我们照着它用 C# 重写。
- **个人自用**:自己游玩,不公开发布。

## 当前状态

**M0 工程骨架与工具链、M1 静态场景资源提取均已验收通过。** M0 的核心/游戏/CLI/测试链路、本地干净检出与真实 Windows CI 均通过，桌面显示和正常关闭也已由用户确认。M1 的本地构建、测试、真实资源核对及独立 Standards/Spec 审查已通过；未推送或运行本轮远端 CI，不代表 M2 游戏场景已实现。

表现层/外壳层细节尚待设计。[事件 JSON 契约](docs/design/events-schema.md) 已确定原版忠实低层转写策略、v1 文件结构与基础命令,全量 opcode 映射及工具/运行时尚未实现。独立 CLI 已提供只读 MKF 检查、Windows YJ2/图像解码、地图/精灵导出及带 v1 来源清单的完整静态场景包。

用户数据已放入 `data/raw/sdlpal/`,初步核对符合 Windows 数据分支。[M1 计划](docs/milestones/m1/plan.md) 范围已确认：只提取地图、瓦片、精灵、调色板与场景对象初始数据。[实施任务 #6～#10](https://github.com/fanxiao92/godot-pal/issues/6) 已完成本地验收和独立双轴审查，结果与收尾说明见对应 Issue。场景 2 的两次完整导出 416 文件字节一致，原始数据散列/长度/mtime 不变；本轮 Debug/Release 构建测试与 Godot headless 启动通过，未重跑 clean-cache/桌面/远程 CI 验收。M2 负责 Godot 场景构建与探索；事件、对话、语言包和字库独立留到 M3，不阻塞场景构建。后续阶段见 [总体路线](docs/design/development-plan.md)。

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

只读检查原始场景归档（不解压、不写文件）：

```powershell
dotnet run --project tools/Pal.Extract --no-build -- inspect --input data/raw/sdlpal
```

输入目录需包含 `DATA.MKF`、`GOP.MKF`、`MAP.MKF`、`MGO.MKF`、`PAT.MKF`、`SSS.MKF`，文件名大小写不敏感；其他文件忽略。成功向 stdout 输出 JSON，包含归档散列、chunk 偏移与压缩线索；线索不等于已验证解压。退出码：成功 `0`、输入或归档错误 `1`、参数错误 `2`。报告字段与验证结果见 [Issue #7](https://github.com/fanxiao92/godot-pal/issues/7)。

完整静态场景转换（地图从真实场景记录读取；输出必须新建或为空，禁止输入内输出及链接路径）：

```powershell
dotnet run --project tools/Pal.Extract --no-build -- extract-scene `
  --input data/raw/sdlpal --output data/extracted/m1-scene-2 `
  --scene 2 --palette 0 --lighting day
```

同样只需上述六个归档，不需要文字、SLF、字库、事件产物；脚本入口只保留数值，不执行。输出一个根目录下的 PNG/JSON、`manifest.json` v1（参数、完整消费归档 SHA-256、根相对资源引用、非自递归产物散列）。调色板与昼夜必须显式选定，不按场景猜测。`preview.png` 是静态地图瓦片预览，不包含脚本初始化或人物合成。详见 [完整包契约与已执行验证](docs/milestones/m1/extract-scene.md)；单独地图命令见 [export-map](docs/milestones/m1/export-map.md)，`export-scene` 使用同样五个选项导出场景/人物切片。

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
  - `milestones/` —— 各里程碑实施计划，当前有 M0 与 M1 计划
  - `archive/` —— 已完成阶段的历史记录，按需查阅
  - `agents/` —— Agent 的 Issue、标签与领域文档工作流
  - `adr/` —— 值得长期保留的架构取舍
- `GLOSSARY.md` —— 项目领域术语
- `src/Pal.Core/` —— 纯 .NET 核心类库
- `src/Pal.Game/` —— Godot .NET 游戏工程
- `tools/Pal.Extract/` —— 独立离线提取 CLI（只读检查、地图/场景导出、完整静态场景包）
- `tests/Pal.Core.Tests/` —— 核心 xUnit 测试
- `tests/Pal.Extract.Tests/` —— CLI 真实进程测试
- `scripts/` —— 引擎下载、兼容性探针、游戏启动检查与共享验收入口
- `reference/sdlpal/` —— sdlpal 源码,作为引擎逻辑参考(见 `reference/README.md`)
- `data/raw/` —— 放原版 98 柔情版数据文件的地方(见 `data/raw/README.md`)

## 怎么继续

在本目录新开一个对话，可直接给 AI 以下提示词：

> 请先读取 `AGENTS.md`，遵循其中的工作流；读取 `GLOSSARY.md`、`docs/design/` 下全部 Markdown 文件、`reference/README.md`、相关 ADR 以及 `docs/milestones/m1/plan.md`，核对当前里程碑和 GitHub 状态，再确认下一项已批准任务。
>
> M1 只做场景资源提取，输入在 `data/raw/sdlpal/`；不包含事件、对话、语言包或字库。先核对 GitHub 中已有的 M1 实施/审查结果，不重复发布任务，不把离线预览当作 M2 验收。实现前读取对应 Issue 正文与评论，选择没有未完成阻塞项的未完成任务，按依赖顺序继续推进。

`reference/README.md` 是参考源码导航，不要求通读整个 SDLPal；实现具体任务时再读取相关源码。`docs/archive/` 仅在排障、环境升级或追溯历史结论时查阅。
