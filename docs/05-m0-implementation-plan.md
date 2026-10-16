# 05 · M0 实现计划：工程骨架与工具链

状态：Issue #2 的本机兼容性闸门已通过，详见 `06-godot-compatibility.md`。Issue #3 的正式核心/游戏/测试工程已建立并通过自动化及用户桌面可视验收，见 `07-game-startup.md`；独立 CLI 及 CI 尚未实现，M0 整体未完成。下文保留原始计划，实际验证状态以对应报告为准。

## 1. 环境检查结果

本机检查结果：

| 项目 | 结果 |
|---|---|
| 操作系统 | Windows x64 |
| Godot 命令 | `godot-mono` |
| Godot 版本 | `4.7.2.stable.mono.official.ed1daf0bf` |
| .NET SDK | `10.0.402` |
| .NET 运行时 | 已安装 .NET 10；当前 SDK 的列表中没有 .NET 8 |
| Godot C# 包 | 本机 Godot 安装目录包含 `Godot.NET.Sdk.4.7.2`、GodotSharp 等 NuGet 包 |
| Godot 插件宿主配置 | `GodotPlugins.runtimeconfig.json` 指定 `net8.0`，允许 `LatestMajor` roll-forward |
| NuGet 配置 | 已启用 nuget.org；尚未验证网络访问和包还原 |
| 原版数据 | `data/raw/` 只有 README，没有游戏数据 |

注意：SDK 能运行、Godot 能输出版本，不等于 Godot C# 项目已成功构建或启动。插件宿主的 roll-forward 设置也不能替代游戏项目的兼容性验证。

## 2. M0 范围

目标：建立可构建、可测试、可启动的最小骨架，不实现游戏内容。

包含：
- Godot 桌面窗口与最小启动场景。
- 独立于 Godot 的核心类库及引用验证。
- 离线提取 CLI 的帮助入口，不解析 MKF。
- 一条有实际断言的核心测试。
- 构建、测试、启动说明及基础 CI。

不包含：地图、行走、事件解释器、战斗、资源提取、存档实现、正式视频接入。

## 3. 技术方案与兼容性闸门

### Godot 与 .NET

- 优先使用已安装的 Godot .NET `4.7.2`，游戏项目的 `Godot.NET.Sdk` 与之保持一致。
- 使用 `global.json` 固定 .NET SDK `10.0.402`，允许同 feature band 的补丁更新；其他开发机需安装兼容 SDK。
- 在临时目录生成或建立最小 Godot C# 项目，确认此版本的默认目标框架及可用构建方式。
- 优先评估游戏、CLI、测试统一使用 `net10.0`；只有实际构建和 headless 启动通过后才将其写入正式工程。
- 若统一 `net10.0` 不兼容，则按该 Godot 版本实际支持的目标框架调整。核心类库目标框架必须能被游戏引用，CLI/测试可使用较新的框架。
- 不通过全局设置强制运行时 roll-forward 来掩盖兼容性问题；需要安装其他 SDK/运行时或更换引擎时，先征得用户同意。

### 离线工具与测试

- 建议 `Pal.Extract` 使用 C#/.NET CLI，和游戏统一语言，保持独立部署。
- 核心单元测试优先使用 xUnit；在 M0 还原成功后固定具体包版本。
- Godot 集成测试优先评估 GdUnit4Net，核对其与 Godot `4.7.2` 的兼容性。
- 若集成测试框架兼容性未确认，M0 先保留命令行启动 smoke test，不虚报已接入 GdUnit4Net；正式接入需在 M2 前完成或明确替代方案。

## 4. 目录结构

```text
GodotPal.sln
 global.json
src/
  Pal.Core/
    Pal.Core.csproj
  Pal.Game/
    project.godot
    Pal.Game.csproj
    Scenes/Main.tscn
    Scripts/Main.cs
tools/
  Pal.Extract/
    Pal.Extract.csproj
    Program.cs
tests/
  Pal.Core.Tests/
    Pal.Core.Tests.csproj
.github/workflows/
  ci.yml
```

上面的 `global.json` 与 solution 同在仓库根目录。

依赖方向：

```text
Pal.Game       → Pal.Core
Pal.Core.Tests → Pal.Core
Pal.Extract   → 独立 CLI（暂不引用游戏项目）
```

- `Pal.Core` 不引用 Godot 包，不出现 Node、Resource 等 Godot 类型。
- 不为了复用而让提取工具依赖游戏工程；共享数据契约在有真实需求时再提取。
- 原始数据与转换产物继续留在 `data/` 下并保持忽略，不放入 CI。
- 扩充 `.gitignore`：忽略 .NET 的 `bin/`、`obj/`、测试结果与本地 IDE 文件。

## 5. 执行顺序

1. **兼容性探针**：在临时目录验证 Godot C# 最小项目的还原、构建、headless 启动，记录实际框架和命令。失败时先定位，不直接搭大工程。
2. **工程骨架**：创建 solution、核心类库、Godot 工程、CLI 与测试项目，固定 SDK 和包版本。
3. **最小引用切片**：核心提供一个与引擎无关的启动状态值；游戏读取并显示它，测试断言同一行为。不提前建立空的四大系统或命令总线。
4. **CLI 入口**：支持 `--help` 与错误参数处理；帮助正常退出，未知参数返回非零退出码。不访问 `data/raw/`。
5. **Godot 启动场景**：显示项目标题与核心启动状态；可使用占位背景，不需要原版素材。
6. **构建与测试**：执行下方验收，检查 Git 中没有缓存、二进制或原版数据。
7. **基础 CI**：优先配置 Windows runner，安装固定版本 .NET 与 Godot .NET，执行构建、核心测试及 headless smoke test。首次需验证 Godot 下载地址与 NuGet 还原。桌面发布包与其他平台验证不属于 M0。
8. **文档回填**：记录实际命令、最终目标框架与测试框架结论，更新 README 和决策记录。

## 6. 验收标准

以下是计划命令，正式工程创建后需实际执行并修正；当前尚未执行：

```sh
dotnet restore GodotPal.sln
dotnet build GodotPal.sln --no-restore
dotnet test tests/Pal.Core.Tests/Pal.Core.Tests.csproj --no-build
dotnet run --project tools/Pal.Extract -- --help
godot-mono --headless --path src/Pal.Game --editor --import --quit
godot-mono --headless --path src/Pal.Game --quit-after 60
godot-mono --path src/Pal.Game
```

- 所有项目成功还原和构建。
- 核心测试通过，且不启动 Godot。
- CLI 帮助输出成功；未知参数返回非零退出码。
- headless 导入与游戏启动无 C# 加载错误。不能只看退出码，还要检查日志。
- 手动启动后窗口能显示启动状态且正常退出；headless 测试不能替代可视验收。
- 本地与 CI 使用相同的引擎、SDK 和目标框架约定。
- 如果 CI 尚未实际运行，明确标记为未验证，不能将 M0 整体验收标记为完成。

## 7. M0 之后

进入 M1 前需要用户自备的 98 柔情版数据：
1. 核对文件清单、版本特征与压缩方式。
2. 选定最小探索场景，并确认其地图、精灵、调色板、文字依赖。
3. 编写 M1 实现计划，再实现 MKF 读取及第一张图像的导出。

没有原版数据不会阻塞 M0，但会阻塞基于真实资源的 M1 验证。
