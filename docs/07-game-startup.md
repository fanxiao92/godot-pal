# 07 · 最小游戏启动链路（M0 / Issue #3）

任务：[Issue #3](https://github.com/fanxiao92/godot-pal/issues/3)。

状态：正式工程构建、核心测试和 headless 启动已通过；**桌面可视验收待用户确认，因此任务尚未完成**。不代表 M0 整体完成，CI 与独立 CLI 分属 #5 与 #4。

## 工程与行为

- `src/Pal.Core/`：纯 .NET 类库，不依赖 Godot。公开 `Startup.Status`，返回 `Core ready`；这只是工具链切片，不是未来完整游戏状态模型。
- `src/Pal.Game/`：Godot .NET 项目，启动场景显示 `godot-pal` 标题。C# 在 `_Ready` 中读取核心状态、赋值给 Label，随后输出 `PAL_GAME_READY:Core ready`。
- `tests/Pal.Core.Tests/`：xUnit 单元测试，直接断言核心公开行为，不启动 Godot。
- `GodotPal.sln`：目前包含上述三个项目；后续 CLI 可加入同一入口。

游戏与核心均以 `net10.0` 为目标；Godot.NET.Sdk 固定 `4.7.2`。SDK 基线通过根目录 `global.json` 指定 `10.0.402`、`latestPatch`、不接受预览版本。不同补丁版本仍需复验，不能从本次运行推断全部兼容。

核心测试实际使用并通过的直接依赖版本：
- Microsoft.NET.Test.Sdk `17.14.1`
- xUnit `2.9.3`
- xunit.runner.visualstudio `3.1.4`（仅测试工程私有依赖）

未引入覆盖率工具、命令总线、四大系统、存档或原版资源。

## 构建与自动化验收

前提：与 Issue #2 相同的 Windows x64、Godot .NET `4.7.2.stable.mono.official.ed1daf0bf`、.NET SDK `10.0.402`、PowerShell 7。以下命令从仓库根目录执行。

```powershell
dotnet restore GodotPal.sln
dotnet build GodotPal.sln --no-restore
dotnet test tests/Pal.Core.Tests/Pal.Core.Tests.csproj --no-build
pwsh -NoProfile -File scripts/Test-GameStartup.ps1 `
  -GodotPath "C:/Users/fanxiao/scoop/apps/godot-mono/current/godot-mono.console.exe"
```

Godot 路径替换为自己的完整安装目录内的 console 可执行文件，不使用普通版引擎或 Scoop shim。脚本校验引擎版本，运行 headless 导入与启动，日志写入唯一的系统临时目录。

每个进程必须在 60 秒内完成，非零退出码、Godot/C# 加载或脚本错误均判失败；启动还必须输出完整的核心状态标记。脚本保留原始日志，清理 ANSI 颜色转义后做判定；超时终止子进程树。可通过 `-TimeoutSeconds` 调整每一步的上限。

headless 启动使用 `--quit-after 60` 运行有限帧数，正式游戏本身没有测试专用退出逻辑。这个帧数不取代外层超时。标记证明核心状态已赋给场景中的 Label，**不证明实际桌面上显示正确**。

## 红→绿与错误检测记录

| 验证 | 失败阶段 | 修正后 |
|---|---|---|
| 核心启动状态 | 空状态导致 xUnit 的字符串断言失败 | 返回 `Core ready` 后，1 个测试通过 |
| 游戏引用链路 | 场景可启动但没有 `_Ready` 逻辑，脚本因缺少标记失败 | 从核心读取状态并设置 Label 后通过 |
| 有标记仍发生错误 | 临时在标记后插入 `GD.PushError`，进程退出码仍为 0，但检查因错误日志失败 | 移除故障注入后重新构建并通过 |

故障注入没有保留在正式游戏中。构建结果为 0 警告、0 错误。非零退出和超时判定由脚本实现；Issue #2 已在其隔离探针中实际验证这两种反例，本任务不把它们冒充为正式游戏上的新增验证。

这些检查使用本机现有 NuGet 配置和缓存，不等于干净机器或 CI 已通过。

## 桌面可视验收（待完成）

```powershell
& "C:/Users/fanxiao/scoop/apps/godot-mono/current/godot-mono.console.exe" --path src/Pal.Game
```

或者在 Godot .NET 编辑器中导入 `src/Pal.Game/project.godot` 并运行。

用户需要确认：
- [ ] 窗口可见，标题为 `godot-pal`。
- [ ] 内容显示 `godot-pal` 与 `Core ready`，没有明显裁切或错误遮挡。
- [ ] 点击窗口关闭按钮后正常退出，没有运行错误。

自动化不能替代这些人工确认；未确认前保持 #3 开放。

## GdUnit4Net 评估与跟进

本次读取的上游一手资料：
- [README 兼容性表（固定源码版本）](https://github.com/godot-gdunit-labs/gdUnit4Net/blob/c9a988f3b24fb81ae7ff2460c8b36a008bac30bc/README.md)
- [v5.0.0 稳定发行说明](https://github.com/godot-gdunit-labs/gdUnit4Net/releases/tag/v5.0.0)

观察：README 的 master 行将 API `5.1.0-rc6` 与 adapter `3.2.0` 列为支持 Godot `4.7.2`，并标注 .NET 10 支持；稳定 `5.0.0` 对应表格只列到 Godot `4.4`。这不足以证明稳定版不兼容，但也不能将其当作 4.7.2 的验证证据。

本次不引入候选版测试依赖，不声称完成 GdUnit4Net 实际还原或场景测试。M0 暂使用本任务已运行的进程级 smoke test，核心使用 xUnit。

**M2 前的明确跟进项**：重新检查发布版本与兼容表，实际运行最小场景测试（包括 Godot 回调异常的失败检测），然后决定接入经过验证的 GdUnit4Net 版本或记录替代方案；不能仅凭上游支持表标记完成。
