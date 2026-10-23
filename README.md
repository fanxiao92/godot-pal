# godot-pal

《仙剑奇侠传 98 柔情版》的跨平台桌面版重新实现 —— 使用 Godot .NET + C# 从头实现。

## 这是什么

把经典 RPG《仙剑奇侠传》(目标版本:**1998 柔情版**)做成一个跨平台的桌面游戏。**使用 Godot .NET 引擎和 C# 语言原生重写整个游戏**,代码模块清晰,方便日后给游戏加场景、剧情、任务、物品、技能、玩法。

- **忠实移植优先**:第一目标是把原版 98 柔情版完整、忠实地在桌面平台上跑起来。
- **参考而非 fork**:sdlpal 的 C 源码作为引擎逻辑的"规格说明书"(战斗公式、脚本语义、数据格式),我们照着它用 C# 重写。
- **个人自用**:自己游玩,不公开发布。

## 当前状态

**M0 工程骨架与工具链、M1 静态场景资源提取均已验收通过。** M0 的核心/游戏/CLI/测试链路、本地干净检出与真实 Windows CI 均通过，桌面显示和正常关闭也已由用户确认。M1 的本地构建、测试、真实资源核对及独立 Standards/Spec 审查已通过；其结果不替代 M2 验收。

**M2 Windows 单领队探索里程碑已完成（2026-10-22）。** 功能、自动/人工验收及收尾文档独立双轴终审均已接受；成果已合入并通过 SSH 推送到 `main`，已验证交付提交为 `a76f83bf08c421e83892cc9515cbdd4c032014ec`。[父规格 #11](https://github.com/fanxiao92/godot-pal/issues/11)、[最终验收 #17](https://github.com/fanxiao92/godot-pal/issues/17) 和后续跨盘测试缺陷 [#19](https://github.com/fanxiao92/godot-pal/issues/19) 均已关闭，[远端完整 Windows CI](https://github.com/fanxiao92/godot-pal/actions/runs/39738074108) 已通过。场景 2 / 地图 12 的静态对象、单领队离散行走/碰撞/遮挡、320×200 整数缩放、方向键/WASD 与失焦恢复已交付。入口与独立原生配置见 [M2 计划](docs/milestones/m2/plan.md)，分层证据与限制见 [M2 验收记录](docs/milestones/m2/acceptance.md)。其余表现层/外壳层能力按后续阶段设计。[事件 JSON 契约](docs/design/events-schema.md) 已确定原版忠实低层转写策略、v1 文件结构与基础命令,全量 opcode 映射及工具/运行时尚未实现。独立 CLI 已提供只读 MKF 检查、Windows YJ2/图像解码、地图/精灵导出及带 v1 来源清单的完整静态场景包。

**Game 集成测试已按职责分层。** 四方向×Arrow/WASD 的八项交互测试使用 SceneRunner，普通启动与测试共用真实 Main 初始化及失败传播，必要 argv/诊断/退出契约仍使用真实进程；M2 原有焦点冻结、清键和 fresh-press 行为保持。这批独立移植经 Core 20 项、Game 19 项匹配验证及三类指定错误反例验证，不包含 M3，不代表新版完整门、远端 CI、GPU 或人工验收通过，也不将此前 integration 的性能数字当作本分支实测。长期规则见[Game 测试分层与 SceneRunner](docs/testing/game-test-layers.md)，覆盖映射和运行入口见 [Game 测试说明](src/Pal.Game/Tests/README.md)。

用户数据已放入 `data/raw/sdlpal/`,初步核对符合 Windows 数据分支。[M1 计划](docs/milestones/m1/plan.md) 范围已确认：只提取地图、瓦片、精灵、调色板与场景对象初始数据。[实施任务 #6～#10](https://github.com/fanxiao92/godot-pal/issues/6) 已完成本地验收和独立双轴审查，结果与收尾说明见对应 Issue。场景 2 的两次完整导出 416 文件字节一致，原始数据散列/长度/mtime 不变；M1 当轮 Debug/Release 构建测试与 Godot headless 启动通过，当轮未重跑 clean-cache/桌面/远程 CI 验收。M2 负责 Godot 场景构建与探索；事件、对话、语言包和字库独立留到 M3，不阻塞场景构建。后续阶段见 [总体路线](docs/design/development-plan.md)。

## 技术栈与开发环境

| 项目 | 当前基线 |
|---|---|
| 语言 | C# |
| 引擎 | Godot .NET `4.7.2`，Godot.NET.Sdk `4.7.2` |
| .NET SDK | `10.0.402`，按 `global.json` 允许同 feature band 的补丁更新 |
| 目标框架 | 全部项目为 `net10.0` |
| 离线工具 | 独立 C#/.NET CLI，不依赖 Godot 或游戏工程 |
| 自动化测试 | Core/CLI/Prepare xUnit 与真实进程测试 + GdUnit4Net 场景/输入测试 + Godot import/Main 启动回归；Core xUnit `2.9.3` 基线保留 |
| 本地验收环境 / CI | Windows x64、PowerShell 7 / GitHub Actions `windows-2022` |

版本约束以 `global.json`、各项目 `.csproj` 和脚本为准，README 提供开发环境概览。跨平台桌面是目标，其他操作系统与导出包尚未验证；M2 先验收 Windows 开发运行，不包含导出包。GdUnit4Net 隔离关卡已通过并接入；#18 后续另获批准交付固定源码包，版本、来源与下载校验以[包说明](docs/testing/gdunit-source-packages.md)及锁文件为准，进一步升级仍需授权。

## 构建、测试与运行

从仓库根目录执行，需先安装上述 SDK 和完整的 Godot .NET 引擎：

直接还原/构建前先显式 bootstrap 固定 GdUnit 包（需要下载或已校验的本地四文件包，详见[包说明](docs/testing/gdunit-source-packages.md)）：

```powershell
./scripts/Get-GdUnitPackages.ps1
dotnet restore GodotPal.sln
dotnet build GodotPal.sln --no-restore
dotnet run --project tools/Pal.Extract --no-build -- --help
```

完整回归优先使用下方 `Test-M0.ps1`，它在内部 bootstrap 并设置 `GODOT_BIN` / `PAL_TEST_GODOT`。单独执行 Game 或 solution 测试前须按 [Game 测试说明](src/Pal.Game/Tests/README.md)设置这两个引擎路径变量、完成必要导入并核对 `tests/Pal.Game.Tests/game.runsettings`（headless、串行、会话超时及零测试失败）；默认测试过滤三个故意失败控制，完整门禁会分别验证它们。Godot、Prepare、Game 与 solution 检查全局串行。

### 测试职责

纯规则与精确时间使用 Core 公开接口；真实资源、输入、信号和更新连接默认使用 GdUnit4Net SceneRunner；argv、诊断、退出、READY 和必要进程隔离保留最小真实进程代表。SceneRunner 每例承载独立生产场景、入树前配置、在实际更新完成边界断言，并清理自有输入、订阅、节点及改动的全局状态；迁移替换旧入口而非长期叠加重复测试。新增或改写 Game 集成测试前先读[分层规范](docs/testing/game-test-layers.md)。

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

### M2 原生资源与普通 Main

已有经核对的真实资源和独立配置时直接复用，无需再次准备。清洁检出不含真实资源；只有在需要首次准备且获准使用本机数据时，才执行显式离线导入（输入必须在 Godot 工程外，输出为新/空 `Assets` 或其子目录）：

```powershell
./scripts/Prepare-GameScene.ps1 `
  -InputPath data/extracted/m1-scene-2-accepted `
  -OutputPath src/Pal.Game/Assets `
  -GodotPath "C:/path/to/godot-mono.console.exe"
```

该脚本校验完整 M1 包并生成 Godot 原生场景/资源及纹理，不覆盖非空目标，不自动清理或回滚失败输出，**不会创建探索 profile**。默认 Main 需要 `res://Assets/Scenes/Scene.tscn` 和独立的 `res://Assets/Profiles/Exploration.tres`；缺少任一项或校验不符均明确失败。profile 必须另行显式 author 为 `PalExplorationProfile`，包含经核对的起点、角色行走参数与来源；类型、字段和新用户步骤见 [M2 配置契约](docs/milestones/m2/plan.md#33-已交付的原生入口与配置)。运行时仅加载 Resource/PackedScene/Texture2D，不读取 M1 JSON/MKF，不自动提取、同步或补配置。

启动桌面游戏（替换为本机完整 Godot .NET 安装中的 console 可执行文件；普通 Main，默认 1280×800 窗口）：

```powershell
& "C:/path/to/godot-mono.console.exe" --path src/Pal.Game
# 可选：已存在且匹配的独立原生资源根及 profile，参数在 -- 之后
& "C:/path/to/godot-mono.console.exe" --path src/Pal.Game -- `
  --m2-resources res://Assets/MyScene --m2-profile res://Assets/Profiles/MyExploration.tres
```

更改资源根不会重定位默认 profile；`--m2-resources` 仅接受规范化 `res://Assets` 或子目录 URI，`--m2-profile` 接受规范化项目资源 URI，均只允许一次。示例中的自定义资源必须事先准备，不能直接粘贴到没有这些文件的检出中启动。

本地与 CI 共用的完整自动化检查入口：

```powershell
pwsh -NoProfile -File scripts/Test-M0.ps1 `
  -GodotPath "C:/path/to/godot-mono.console.exe"
```

该入口使用全新 NuGet 包/HTTP 缓存，执行还原、构建、Core/CLI/Prepare/Game 测试、发现/重复/指定失败控制和人工资源的 headless 导入/启动；日志路径会打印到终端，不清空既有 `.godot` 缓存。实际退出码、完整 TRX 与诊断共同决定是否通过。桌面/GPU/物理输入不由 headless 证明；当前本地 Windows 分层结果及已通过的远端人工资源 headless CI 见 [M2 验收记录](docs/milestones/m2/acceptance.md)。

技术版本记录在此，检查由测试和 CI 执行，任务结果记录在 GitHub Issue 中；不要求每次检查另写一份报告。M0 的详细历史记录保留在 [归档](docs/archive/m0/acceptance.md)，仅在排障或追溯时查阅。

## 仓库结构

- `docs/` —— [文档导航](docs/README.md)
  - `design/` —— 项目设计背景、架构、总体路线和待定问题
  - `milestones/` —— 各里程碑实施计划，当前有 M0、M1 与已交付 Windows 切片的 M2 计划/验收记录
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

> 请先读取 `AGENTS.md`，遵循其中的工作流；读取 `GLOSSARY.md`、`docs/design/` 下全部 Markdown 文件、`reference/README.md`、相关 ADR 以及 `docs/milestones/m2/plan.md`，核对当前里程碑和 GitHub 状态，再确认下一项已批准任务。
>
> M0/M1/M2 已完成；M2 已合入并推送 main，远端完整 Windows CI 已通过，#11/#17/#19 均已关闭。读取父规格、相应任务正文/评论及直接依赖核对实时状态，不重做已关闭的 #12～#19，不把离线预览当作桌面验收。M2 运行时使用显式准备的 Godot 原生资源与独立 `.tres` profile，不读取 M1 JSON/MKF，不接事件、对话、语言包或字库。先确认剩余已批准任务；只有另获批准才规划 M3 的真实事件/对话切片，不隐式开始实现。提交、推送和 Issue 关闭按明确授权执行。

`reference/README.md` 是参考源码导航，不要求通读整个 SDLPal；实现具体任务时再读取相关源码。`docs/archive/` 仅在排障、环境升级或追溯历史结论时查阅。
