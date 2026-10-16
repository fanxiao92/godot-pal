# 06 · Godot C# 兼容性验证（M0 / Issue #2）

对应任务：[GitHub Issue #2](https://github.com/fanxiao92/godot-pal/issues/2)。

## 结论与边界

**本机 Windows x64 的兼容性闸门已通过**：Godot .NET `4.7.2.stable.mono.official.ed1daf0bf` + `Godot.NET.Sdk/4.7.2` + .NET SDK `10.0.402` + 游戏目标 `net10.0`，能够实际还原、构建、headless 导入并执行 C#。

这不是整个 M0 的完成证明：没有建立正式游戏工程、核心类库、CLI 或测试工程；没有验证桌面窗口、导出、其他操作系统或 GitHub Actions。正式链路仍须在 #3–#5 中重新验收。

## 环境与默认框架

| 项目 | 实际观察 |
|---|---|
| 系统 | Windows 10.0.26200，x64 |
| Godot | `4.7.2.stable.mono.official.ed1daf0bf`，使用 console 可执行文件 |
| .NET SDK | `10.0.402`，MSBuild `18.9.15` |
| 已列出的 SDK | 只有 `10.0.402` |
| 已列出的 .NET 运行时 | .NET、ASP.NET Core、Windows Desktop `10.0.13` |
| Godot 生成的 SDK 引用 | `Godot.NET.Sdk/4.7.2` |
| Godot 生成的默认桌面目标 | **`net8.0`** |
| Godot 生成的 Android 条件目标 | `net9.0`（未测试，不在本任务范围） |
| 本次修改并验证的桌面候选 | **`net10.0`** |
| 实际 C# 游戏进程中的运行时 | `10.0.13` |

默认框架不是根据插件 runtimeconfig 推断的。探针在临时 .NET 控制台中，通过反射调用**本机随 Godot 安装的** `GodotTools.ProjectEditor.ProjectGenerator.GenAndSaveGameProject`，保存其生成的原始项目作为证据，再只修改临时桌面候选目标。

生成器调用需要解析其 MSBuild 依赖；最初直接调用因缺少 `Microsoft.Build` 失败，随后明确从已安装 SDK 目录解析这些依赖后成功。不需要安装额外软件。该调用使用编辑器内部接口，只用于这个固定版本的探针；升级引擎时须重新检查，不能当成稳定的公共 CLI。

没有在本任务中测试原始 `net8.0` 工程的还原或运行，因此不声称它在仅安装 .NET 10 的本机可运行。本次也没有设置全局运行时 roll-forward；脚本遇到 `DOTNET_ROLL_FORWARD` 覆盖会停止并要求先移除该覆盖。

## 可复现步骤

前提：Windows、PowerShell 7、可从 PATH 使用的 .NET SDK `10.0.402`、上述版本的完整 Godot .NET 安装目录。SDK 或引擎缺失时先由用户批准环境变更，脚本本身不安装软件。

从仓库根目录运行，替换为自己安装的 **console** 可执行文件路径（不要传入 Scoop shim）：

```powershell
pwsh -NoProfile -File scripts/Test-GodotCompatibility.ps1 `
  -GodotPath "C:/Users/fanxiao/scoop/apps/godot-mono/current/godot-mono.console.exe"
```

脚本行为：

1. 新建唯一的系统临时目录，输出路径；所有生成源码、缓存及日志均留在那里，不改动正式工程。
2. 查询引擎和 SDK 环境，在临时目录固定 SDK 基线及 `latestPatch` 策略。
3. 调用本机生成器，保存 `Probe.default.csproj`，校验默认目标与 SDK，再生成 `net10.0` 临时候选。
4. 创建最小场景和 C# `_Ready` 入口。C# 实际计算 `19 + 23`，打印 `PAL_COMPAT_OK:42` 和所用运行时版本，然后退出。
5. 执行还原、构建、headless 导入，逐步检查进程结果和错误日志。
6. 先运行四个真实进程反例，确认检测机制会拒绝失败，再运行正常启动。
7. 只有全部检查通过才输出最终 `PASS`；失败会抛出异常并返回非零状态。默认步骤超时为 120 秒，正常游戏为 30 秒，超时反例为 10 秒；超时会终止子进程树。

反例是临时 C# 场景的运行参数，只用于验证检查器，不是正式游戏功能。`timeout` 模式在执行 `_Ready` 后保持事件循环，不依靠阻塞线程来模拟。

临时目录保留以便排查，确认不再需要后可手动删除。探针使用本机现有 NuGet 配置和缓存；还原成功**不等于**证明完全无缓存的网络还原成功，后者属于 CI 与干净环境验收。

## 实际验证记录

探针先完成三个反例与正常启动验证；补充非零退出反例后，再从另一个全新临时目录完整运行通过。

最终检查前还补充了 ANSI 颜色转义清理，保留原始日志、使用清理后的文本做判定，并再次完整运行通过。

最终完整运行目录（本机辅助证据，不作为可移植依赖）：
`C:\Users\fanxiao\AppData\Local\Temp\pal-compat-57f8bced58884f398c899bd8773eae08`。

| 步骤 | 退出码 | 结果 |
|---|---:|---|
| 引擎版本、SDK 信息、SDK 列表 | 0 | 与基线匹配 |
| 生成默认工程 | 0 | 桌面 `net8.0`，SDK `4.7.2` |
| net10.0 还原 | 0 | 成功 |
| net10.0 构建 | 0 | 成功，0 警告、0 错误 |
| headless 导入 | 0 | 无错误，未超时 |
| 缺少标记反例 | 0 | 正确拒绝；仅退出码为零不足以通过 |
| 标记 + 运行错误反例 | 0 | 日志出现 `ERROR: PAL_EXPECTED_FAILURE`，正确拒绝 |
| 标记 + 非零退出反例 | 17 | 正确拒绝 |
| 标记 + 不退出反例 | -1（终止后） | 10 秒超时，正确拒绝 |
| 正常启动 | 0 | 有 C# 标记，无错误，未超时 |

正常启动的关键输出：

```text
Godot Engine v4.7.2.stable.mono.official.ed1daf0bf - https://godotengine.org
PAL_COMPAT_OK:42
PAL_RUNTIME:10.0.13
```

判定要求同时满足：未超时、零退出码、日志中没有 Godot/C# 加载或脚本错误、存在完整的 C# 成功标记。成功标记本身也不能掩盖其后发生的错误。

## 对后续任务的约定

- #3 的游戏优先采用本次已验证的 Godot .NET `4.7.2`、匹配的 SDK 包 `4.7.2` 和 `net10.0`。正式项目仍需完整构建与启动验收。
- 核心类库、CLI 和测试可统一以 `net10.0` 为基线；这在框架引用方向上与游戏一致，但它们尚未创建，其构建、包依赖、测试执行分别由 #3/#4 验证，不在此声称完成。
- 正式工程的 SDK 基线使用 `10.0.402`、`rollForward: latestPatch`、禁止预览版本。只允许同 feature band 内的更新；新的补丁也需要重跑检查，本次仅验证了 `10.0.402`。
- xUnit 与 GdUnit4Net 的包版本和兼容性尚未验证，交由后续任务评估；本任务没有安装测试框架。
- #5 需要验证实际引擎下载、干净还原及 CI，不能使用本机探针成功代替远端运行证据。
