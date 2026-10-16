# 09 · M0 Windows CI 与集成验收（Issue #5）

任务：[Issue #5](https://github.com/fanxiao92/godot-pal/issues/5)。

状态：共享自动化入口已在本机通过；干净检出验证与真实 GitHub Actions 运行待完成。**在这些证据齐备前，M0 不标记完成。**

## 唯一自动化验收入口

从仓库根目录，在 Windows / PowerShell 7 中执行：

```powershell
pwsh -NoProfile -File scripts/Test-M0.ps1 `
  -GodotPath "C:/path/to/godot-mono.console.exe"
```

要求 .NET SDK `10.0.402`（同 feature band 的补丁更新按 `global.json` 的 `latestPatch` 策略选取，实际版本必须以日志为准）、Godot .NET `4.7.2.stable.mono.official.ed1daf0bf`，全部项目目标框架 `net10.0`。

脚本在系统临时目录下建立独立证据目录，也可通过 `-EvidenceDirectory` 指定一个尚不存在的目录。每次运行使用全新的 NuGet 包与 HTTP 缓存目录，清除 fallback 包目录覆盖；仓库 `NuGet.Config` 仅使用 nuget.org。退出时恢复调用者的环境变量。

本地与 CI 均执行：
1. 记录实际 SDK 信息。
2. 还原完整 solution（不使用现成 HTTP 缓存）。
3. 构建完整 solution。
4. 执行所有测试：核心 1 个、CLI 真实进程 4 个，保存 TRX 报告。
5. 调用同一个 `Test-GameStartup.ps1`，检查引擎版本、headless 导入及启动。
6. 只有全部自动化步骤通过才生成 `PASS.txt`。

启动检查联合判定退出码、错误日志、核心状态成功标记和超时。完整作业还有 20 分钟的 CI 超时。不能以单纯的零退出码或某一条成功标记代替整体检查。

证据包含 `acceptance.log`、`tests/*.trx`、`godot/*.log` 和成功时的 `PASS.txt`。NuGet 包目录也在临时证据目录下，但不上传为 artifact；若删除它后要复用当前工作树的构建产物，应重新执行 restore/build，而不是直接运行 `--no-restore`。

## 官方 Godot 下载与完整性

本地如需与 CI 一样下载引擎到新临时目录：

```powershell
$destination = Join-Path $env:TEMP ("pal-godot-" + [guid]::NewGuid().ToString("N"))
$godot = ./scripts/Get-Godot.ps1 -Destination $destination
./scripts/Test-M0.ps1 -GodotPath $godot
```

这只解压到指定目录，不安装软件或修改 PATH。脚本拒绝复用既有目录。

来源为 [Godot 官方 4.7.2-stable 发行版](https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable)，资源为 `Godot_v4.7.2-stable_mono_win64.zip`，SHA512 固定来自其 `SHA512-SUMS.txt`：

```text
79229fd112b0c9cbeab82363a4ef7be18ea70f1caf86bf912789335b136fbe7e01db0053a33461438c5da1c680c17bbb10040bd09bedc51221cd4423d0367757
```

校验通过后才解压；改变发行版必须重新核对来源及散列，不能仅替换文件名。本机已实际下载、校验和运行该发行包，不依赖 Scoop 修改后的文件名。

## CI 约定

- GitHub Actions 工作流 `M0 Windows acceptance` 使用 `windows-2022` runner，在 main push、PR 或手动触发时运行。
- checkout、setup-dotnet、upload-artifact 均固定到提交 SHA；只给 `contents: read`，不保留 Git 凭据，不使用包缓存恢复。
- SDK 从仓库 `global.json` 选择，引擎由共享下载脚本从官方发行版获取并校验。
- 共享验收脚本与本地相同，没有另一套“简化 CI 验收”。
- 无论成功或失败，都尝试上传日志和 TRX，保留 14 天；不上传原版数据、引擎包或 NuGet 包。
- 不取回 `reference/sdlpal` 的 LFS 资源，因为 M0 不编译或使用参考项目。它们不属于运行时依赖；不下载原版游戏数据。

## 当前验证记录

- 本机官方引擎发行包的下载、SHA512 和版本检查：通过。
- 共享入口反例：故意将 .NET host 作为 Godot 可执行文件，前面的构建/测试成功后，引擎版本检查仍判失败；未生成 `PASS.txt`，脚本返回非零状态。
- 共享入口正例：用已验证的官方 Godot 包、全新包缓存运行；还原成功，构建 0 警告/0 错误，核心 1/1 与 CLI 4/4 通过，headless 导入/启动通过。
- 干净检出：待记录。
- 真实 CI 运行：待记录。

## 人工证据与仍未验证的范围

用户已在 #3 确认桌面窗口标题、`Core ready` 状态显示及正常关闭全部正常，并检查了对应进程已退出，详见 `07-game-startup.md`。CI 不代替此项人工确认。

GdUnit4Net 尚未实际接入：采用 xUnit + 进程级 smoke test；M2 前重新验证候选框架或记录明确替代方案，见 `07-game-startup.md`。

本次不验证导出包、其他操作系统、地图或事件运行，也未提供原版数据。M0 完成后仍应先准备真实 98 柔情版数据、核对版本与资源依赖，再编写 M1 计划，不直接开始全量资源转换。
