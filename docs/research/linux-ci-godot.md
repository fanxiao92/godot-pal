# 研究：Linux CI 运行 Godot 4.7 mono headless 与 GdUnit

> 研究票 [#242](https://github.com/fanxiao92/godot-pal/issues/242)，隶属地图 [#240](https://github.com/fanxiao92/godot-pal/issues/240)（约束见 Notes 第 2、3、13 条）。
> 调研时间 2026-11-26；基线 `origin/main` = `7f0567b`。只做阅读/检索与只读 `gh api` 查询，**未**运行 Godot、`dotnet test`、Prepare 或任何 workflow。
> 每条结论后的方括号是来源编号，见文末「来源」。凡标「推断」的是基于来源的推理而非来源原话，需在原型中实测确认。

## 结论速览

- 官方 `godot-builds` 已提供 `Godot_v4.7.2-stable_mono_linux_x86_64.zip`，且 SHA512 在同一发布的 `SHA512-SUMS.txt` 中，可沿用现有 `Get-Godot.ps1` 的「固定官方散列」做法，只加 Linux 分支 [S1][L2]。
- 上游 gdUnit4Net 自身 CI 就在 GitHub 托管 Ubuntu 上用官方 Linux mono 二进制跑 C# 测试；非渲染测试纯 `--headless`，需要渲染的 E2E 才套 `xvfb-run` + `--display-driver x11 --rendering-driver opengl3` [S4][S6]。这和本仓库 `game.runsettings` 已有的 `--headless` 形态一致 [L3]。
- Linux headless 的已知坑都已在 5.1.0 线修复：GD-466（ubuntu-24.04 + Godot 4.7.2 mono 下全绿后 Godot 以 134/139 信号退出导致步骤失败）、GD-493（冷启动慢时 C# 支持检查误判）、GD-526（stdout 捕获尾部丢失）[S8][S9][S10]。官方 `gdUnit4.test.adapter 3.2.0`（2026-10-25）、`gdUnit4.api 5.1.0-rc7`（2026-11-07）的发布时间都晚于这些修复的合入时间 [S13]。是否真的包含要靠原型验证（按日期推断）。
- 估算：单 job 串行全量（Unit + CLI 集成 + Prepare 集成 + Game 单轮）约 **12.5–15 分钟**，贴着 15 分钟预算的上限；拆成 3 个并行 job 后墙钟约 **10–11 分钟**（推断，依据为现有 Windows 托管 runner 实测分段，见「墙钟估算」）。
- **当前 main 已有红灯**：`7f0567b` 的 main push full-check 在 `scene-dialogue` 批次跑满 300 s 会话超时后失败（只记录到 66 个结果，该批计划 135 个）[R4][L7]。这与 Linux 无关，但迁移前必须单独诊断，否则 Linux CI 首跑无法区分平台问题和既有问题。
- 必须留在本地 Windows 的只有：`SendInput` 物理桌面验收、真实 GPU 截图、Windows 专属分支（junction、大小写不敏感比较）的本地覆盖，以及 `Win32_Process` 进程快照类脚本（这些按地图第 7/8 条本来就要退役）。

---

## 1. Godot 4.7.2 mono 在 Linux 上的获取、校验与预热

### 1.1 官方二进制

- 发布 `4.7.2-stable` 包含 `Godot_v4.7.2-stable_mono_linux_x86_64.zip`（107,698,034 字节），同时提供 `SHA512-SUMS.txt` [S1]。
- 散列值（从 [S1] 的 `SHA512-SUMS.txt` 抄录）：
  - `Godot_v4.7.2-stable_mono_linux_x86_64.zip` → `1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13`
  - 交叉核对：同一文件里 `Godot_v4.7.2-stable_mono_win64.zip` 的散列 `79229fd1…0367757` 与 `scripts/Get-Godot.ps1` 中固定的值完全一致 [S1][L2]，说明现有脚本的取值方法可直接用于 Linux。
- 现有 `Get-Godot.ps1` 只认 `mono_win64.zip`，并查找 `*console.exe` [L2]；Linux 包里没有 console 变体，需加 Linux 分支。上游安装 action 的 Linux 分支是：解压 → 把 `Godot_v…_mono_linux_x86_64/` 目录内容（二进制与 `GodotSharp/`）上移 → `chmod u+x` → 用 `GODOT_BIN` 指向二进制 [S5]。建议照做，并显式 `chmod +x`（上游就是显式加的）。

### 1.2 现成 action / 容器镜像（对比）

| 方案 | 现状 | 校验 | 评价 |
|---|---|---|---|
| 自写 `pwsh` 下载（扩展 `Get-Godot.ps1`） | 现有脚本已跑在 Windows CI [L1][L2] | 固定 SHA512 [L2] | **推荐**：前门 `pal.ps1` 本地与 CI 共用（地图第 7 条），无第三方依赖 |
| `godot-gdunit-labs/gdUnit4Net` 的 `godot-install` 复合 action | 仓库内部 action，按版本缓存，`curl` 下载 [S5] | 无散列校验 [S5] | 适合参考，不适合直接引用（仓库内部路径） |
| `godot-gdunit-labs/gdUnit4-action` | 活跃（2026-11-17 有推送），支持 `godot-net`、`headless` 输入 [S19] | 未见校验步骤 [S19] | 封装了 `dotnet test`，会与统一前门冲突 |
| `chickensoft-games/setup-godot` | 活跃，支持 .NET 版、缓存、设置 `GODOT` 环境变量 [S18] | README 未提及校验 [S18] | 可用，但多一层第三方 |
| `barichello/godot-ci:mono-4.7.2` 容器 | Docker Hub 有该标签（2026-08-18）[S20] | 镜像摘要可固定 | 要整个 job 跑在容器里，并与 setup-dotnet/global.json 对齐，复杂度高 |

缓存：zip 约 108 MB [S1]。现有 Windows 托管 runner 上，「下载 + 解压 + 校验 + 导入预热」整个步骤实测 11–13 s [R1][R2][R3][R4]，所以缓存收益很小。上游 action 用 `actions/cache` 按版本键缓存 [S5]，可作为可选优化，不是必需。

### 1.3 导入预热

- `--import`：「启动编辑器，等待所有资源导入完成后退出；隐含 `--editor` 和 `--quit`」[S2]。`--headless` 等价于 `--display-driver headless --audio-driver Dummy` [S2]，在有 GPU 的平台上也需要它来避免弹窗 [S3]。
- 现有 CI 在冷 `.godot` 缓存上执行 `--headless --path src/Pal.Game --editor --import` 预热，日志只作诊断，但要求退出码为 0 [L1]；Windows 上实测耗时已含在上面的 11–13 s 里 [R1]。
- 上游做法：先 `dotnet build`，再 `$GODOT_BIN --path . --headless --build-solutions --quit-after 200`，然后 `dotnet test --no-build` [S4]；gdUnit4-action 用的是 `-e --headless --quit-after 2000` [S19]。本仓库的 C# 由 `Godot.NET.Sdk/4.7.2` 经 `dotnet build` 构建 [L4]，预热保留 `--import` 即可（推断）。

### 1.4 Runner 镜像

- `ubuntu-latest` **已指向 Ubuntu 26.04**（runner-images README 中的标签表：`ubuntu-latest` or `ubuntu-26.04`）[S14]。gdUnit4Net 的 compile job 用 `ubuntu-24.04`，测试 job 默认 `ubuntu-22.04` [S4][S7]；GD-466 的报告环境就是 `ubuntu-24.04` + Godot 4.7.2 mono [S8]。建议**固定 `ubuntu-24.04`**，不跟随 `latest` 漂移。
- Ubuntu 24.04 镜像自带 PowerShell 7.6.10、.NET SDK 10.0.404（满足 `global.json` 的 `10.0.402` + `latestPatch`）、`xvfb`、`unzip` [S15][L5]；26.04 镜像同样有 PowerShell 7.6.10 和 `xvfb` [S16]。
- 公开仓库的标准 Linux runner 与 Windows runner 规格相同：4 vCPU / 16 GB / 14 GB SSD，免费且不限量 [S17]；本仓库是 PUBLIC（`gh repo view` 查询结果）。因此 Windows 托管 runner 的实测耗时可作为 Linux 估算的基准（推断：同规格不等于同耗时，NTFS/Defender 与 ext4 的 I/O 差异未量化）。

## 2. 官方 GdUnit（5.1.0-rc7 / adapter 3.2.0）在 Linux headless 下的已知问题

| 问题 | 平台/环境 | 状态 | 对本仓库的含义 |
|---|---|---|---|
| **GD-466**：`dotnet test` 全绿后，`GodotRuntimeTestRunner` 以信号 134/139 退出，导致步骤失败（同一提交重跑一次就过）[S8] | GitHub `ubuntu-24.04`、Godot 4.7.2 mono、`GODOT_BIN` headless [S8] | PR #475 于 2026-10-13 合入：按顺序关闭连接、同步关闭管道、报告具体信号；「全部结果报告后的失败退出」仍算 error，除非打开新增的 opt-in 设置 `TolerateExitCodeAfterCompletion`（打开后降为 warning）[S8] | 正是本仓库目标环境。adapter 3.2.0（2026-10-25）晚于该修复 [S13]（按日期推断已包含）。是否打开 `TolerateExitCodeAfterCompletion` 涉及「门内不自动重试、偶发进隔离清单」（地图第 6 条），**需要用户决定** |
| **GD-493**：C# 支持检查只等 500 ms 就读退出码，Godot 首次启动慢时抛异常，所有需要运行时的测试都不执行 [S9] | `ubuntu-24.04` + `xvfb-run`，与 OS 无关，只要冷启动慢就会出现 [S9] | 2026-10-14 关闭，里程碑 5.1.0 [S9] | 冷启动的 CI 正好是高风险场景；官方包发布时间晚于修复 [S13] |
| **GD-526**：停止 stdout 捕获时丢失最后写出的输出；Windows 上读取线程不随捕获停止而结束 [S10] | Windows/Linux/macOS [S10] | 2026-10-17 关闭 [S10]（即 fork 所钉 `a5f20fb8` 的修复，见地图第 13 条） | 官方包晚于修复 [S13]；`CaptureStdOut=true` 仍在用 [L3] |
| **GD-549**：C# 场景运行器支持 headless——被测场景放进独立 viewport，模拟输入只投递到该 viewport、每个动作只投递一次，坐标改为场景坐标 [S11] | 全平台 [S11] | PR #585 于 2026-11-07 11:49 UTC 合入 [S11]，rc7 于同日 21:40 UTC 发布 [S13]（按时间推断已包含） | **行为变化**：本仓库有 16 个测试文件用到 SceneRunner/模拟输入（`rg` 统计）[L8]。退役 fork 换 rc7 时，这是比 Linux 更大的回归风险源 |
| **GD-397**：`SignalAwaiter` 竞态导致测试套件开始前随机卡死 [S12] | Windows 报告 [S12] | 已关闭，里程碑 5.1.0，标签为 `godot related issue` [S12] | 会表现为会话超时；需要原型实跑观察 |

上游 Linux CI 的参数（可直接参考）[S4][S6]：

- `GODOT_BIN=/home/runner/godot-linux/godot`；`dotnet test … --no-build --settings .runsettings-ci -- "GdUnit4.Parameters=--headless --audio-driver Dummy --verbose"`。
- `.runsettings-ci`：`MaxCpuCount=1`、`TestSessionTimeout=600000`、`TreatNoTestsAsError=true`、启用 blame（`CollectDumpOnTestSessionHang TestTimeout="10min"`）、`GodotConnectTimeout=10000`、`CaptureStdOut=true`、TRX + HTML logger。
- 需要渲染的 E2E 才用 `xvfb-run --auto-servernum dotnet test …`，参数为 `--display-driver x11 --rendering-driver opengl3 --screen 0`。

本仓库现状 [L3]：`--headless --verbose`、`MaxCpuCount=1`、`TestSessionTimeout=120000`、`CaptureStdOut=true`、`CompileProcessTimeout=30000`、`GodotConnectTimeout=10000`。目前 `TestSessionTimeout` 由 `GameTestBatches.ps1` 按批次覆盖（部分批次 300000）[L6]。如果新前门把 Game 合成一次会话，这个值必须重定。

## 3. 本仓库对 Windows 的隐含依赖（静态扫描）

扫描方式：在 `src/ tests/ tools/ scripts/` 里 `rg` 查找 `CaseSensitiveFileSystemFact|SendInput|user32|DllImport|OperatingSystem.IsWindows|.exe|Win32`、C# 字符串中的反斜杠，以及 `res://` 引用的大小写 [L8]。

### 3.1 Linux 上可以直接跑的（已有跨平台分支）

- **符号链接/junction**：`PreparePathSafetyTests`、`BattlePrepareProcessTests`、`PrepareBattleDataTests`、`PrepareStoryPackageTests`、`NativeFirstMandatoryBattleProfileTests` 在 Windows 上走 `cmd.exe mklink /J`，其它平台走 `Directory/File.CreateSymbolicLink` [L8]。Linux 会走 symlink 分支：**这个分支至今没有在任何 CI 上跑过**，首跑可能暴露问题（风险 R3）。
- **大小写**：`CaseSensitiveFileSystemFactAttribute`（`tests/Pal.Extract.IntegrationTests/InspectCliTests.cs`）在 Windows 上会设 `Skip`，用于 `InspectCliTests` 和 `ExportMapCliTests` [L8]。Windows CI 上「CLI integration 360 passed + 2 skipped」[R1] 指的就是它们。在 Linux 上它们会**首次真正执行**（风险 R3）；同时 Windows 上的「2 skipped」会消失，计数相关断言要注意。
- 产品代码 `OperatingSystem.IsWindows() ? OrdinalIgnoreCase : Ordinal`（`Pal.Extract/ExportPaths.cs`、`EventArchive.cs`、`BattleDataProjector.cs`、`BattleContextAuthor.cs`、`Pal.Game/Editor/Prepare*.cs`、`PreparationPathSafety.cs`、`M1ImportBundle.cs`）[L8]：Linux CI 覆盖 `Ordinal` 分支，Windows 分支只能靠本地覆盖。
- **`res://` 大小写**：已提交文件（`.tscn/.tres/.cs/.godot/.gd`）中共有 87 个不同的 `res://` 引用，其中 55 个与文件实际大小写完全一致，**0 个只差大小写**，32 个指向未提交的准备产物，无法静态核对 [L8]。Linux 上大小写敏感会导致加载失败，这一点目前看风险低。
- **反斜杠字面量**：命中的都是有意构造的反例（如 `"res://Tests\\Profile.tres"`、`"bad\\path.json"`、`"user://sa\\ves"`），用来断言会被拒绝 [L8]。在 Linux 上，`\` 不是路径分隔符，如果校验依赖 `Path` API 而不是显式字符检查，结果可能不同（风险 R4，需要首跑确认）。
- **EOL**：`.gitattributes` 设了 `* text=auto eol=lf` [L8]，Linux 与 Windows 检出结果一致，不存在 CRLF 差异。
- **进程**：`MainProcessTests`、`ExplorationProcessTests` 用 `OS.GetExecutablePath()` 启动子进程 [L8]，与平台无关。Prepare 测试通过 `pwsh -NoProfile -File scripts/Prepare-*.ps1` 调用，读取 `PAL_TEST_GODOT` [L8]；Ubuntu 镜像自带 pwsh 7.6.10 [S15]。

### 3.2 只能在 Windows 上跑的（必须留在本地或退役）

| 项 | 依据 | 去向 |
|---|---|---|
| `scripts/Test-M4PhysicalDesktop.ps1`：`user32.dll` `SendInput` / `GetForegroundWindow` 物理键盘注入，配合 `M4PhysicalDesktopFixture` [L8] | Win32 API | 本地 T3 / 人工桌面验收 |
| 真实 GPU 截图（`M4FirstBattleGpuFixture`、`RealDialogueFixture` 等 `GetImage`/截图路径）[L8] | 依赖真实 GPU/桌面 | 本地 T3（地图第 5 条） |
| `Get-CimInstance Win32_Process` 进程快照：`Get-TestProcesses.ps1`、`Test-M0.ps1`、`Test-Range.ps1`、`Write-StaticScene4Profile.ps1`，以及 `ProcessSnapshot.Tests.ps1`、`Test-Range.Tests.ps1`（伪造 `dotnet.exe` shim）[L8] | CIM/WMI 只在 Windows 上有 | 按地图第 7/8 条随 Test-M0/Range/审计工具退役；如果保留，需要跨平台重写 |
| `tests/Pal.Game.Tests/TestCostReport.Tests.ps1` 硬编码 `Join-Path $PSHOME 'pwsh.exe'` [L8] | Linux 上可执行文件名是 `pwsh` | 退役，或改用 `[Environment]::ProcessPath` |
| `scripts/Get-Godot.ps1` 只支持 win64 / `*console.exe` [L2] | — | 扩展 Linux 分支（见 1.1） |
| `scripts/Prepare-DialoguePackage.ps1` 回退默认路径 `C:\Users\xiaofan\scoop\…\godot-mono.console.exe` [L8] | 个人机器路径 | 改为必须显式传入，或从前门配置读取 |
| junction（`mklink /J`）与大小写不敏感分支 | 见 3.1 | 本地 T0 覆盖；在验证矩阵中注明「仅本地 Windows」 |

## 4. 软件渲染（xvfb + Mesa llvmpipe + `opengl3`）截图可行性（不进 CI 门，仅评估）

- 上游 gdUnit4Net 的 `unit-test-api-e2e` job 在 GitHub Ubuntu runner 上用 `xvfb-run` + `--display-driver x11 --rendering-driver opengl3` 运行「需要渲染输出」的测试 [S4][S6]。按 GD-549 的说明，这类测试包括截图与 viewport 纹理、shader 输出 [S11]。这说明托管 runner 上能拿到 GL 渲染结果（推断：runner 镜像清单列出了 `xvfb` [S15]，但没有单独列 Mesa 包，llvmpipe 来自哪里未在文档中核实）。
- 本仓库项目设置是 `renderer/rendering_method="gl_compatibility"`，即 Compatibility 渲染器 [L9]，与 `--rendering-driver opengl3` 是同一条渲染路径（推断：Godot 中 Compatibility = OpenGL 3 后端）。逻辑分辨率 320×200，整数缩放 [L9]，像素风有利于跨驱动一致。
- **没有找到**任何一手来源证明 llvmpipe 与真实 GPU 输出逐像素一致。将来若进 CI，金图必须按渲染环境分别建基线并设容差，不能和本地 GPU 金图互换（与地图第 5 条一致）。结论：**技术上可行，稳定性与像素一致性未经验证**，需要专门原型。

## 5. 墙钟估算

### 5.1 现有 Windows 托管 runner 实测（同规格 4 vCPU/16 GB [S17]）

数据来自 full-check 的 TRX `Times` 与 job 时间戳 [R1][R4]：

| 段 | 2026-11-05 main push（run 42028894655，成功）| 2026-11-24 main push（run 45436681973，`7f0567b`，失败）|
|---|---|---|
| job 开始 → Core 开始（checkout、SDK、Godot+预热、空缓存 restore/build、8 个 dotnet 边界预检）| 1 m 39 s | 2 m 00 s |
| Core Unit | 10 s（928）| 18 s（1598）|
| CLI Unit | 1 s（464）| 1 s（537）|
| CLI 集成 | 26 s（362）| 22 s（421）|
| Prepare 集成 | 174 s（89）| 172 s（129）|
| Game 单轮（10 个批次串行）| 4 m 57 s（622）| `process` 65 s 后，`scene-dialogue` 跑满 300 s 会话超时，失败 |
| 整个 job（三轮 Game）| 1216 s | 708 s（失败）|

- 同期 full-check job 的耗时为 1184–1409 s，都是**三轮** Game [R1][R2][R3]。
- 当前 `GameTestBatches.ps1` 计划共 1097 个 Game 用例（85/135/2/5/6/3/4/30/782/45），其中有本地专用的真实资源用例，committable 子集的数量需要实跑才知道 [L6]。
- 每个 Godot 会话有固定开销：`scene-dynamic` 只有 2 个用例，却要 9 s [R1]。按此估算 10 个批次约 80 s 固定成本（推断）。

### 5.2 Linux 估算（推断，±30%）

以 11-05 的分段为基线，按当前用例数线性外推：`native-map` 412→782 约 70 s，`scene-dialogue` 74→135 约 60 s（前提是超时问题已修），`scene-realfull` 30–120 s，Game 单轮合计约 **6–7.5 min**。

| 形态 | 构成 | 估算墙钟 |
|---|---|---|
| A. 单 job 串行 | 安装 1–1.5 m + restore/build 1.5–2 m + Unit/CLI ≈1 m + Prepare ≈3 m + Game 6–7.5 m | **12.5–15 m**（贴近预算上限，没有余量） |
| B. 3 个并行 job | `dotnet`（Unit + CLI 集成，不装 Godot）≈3–4 m；`prepare`（装 Godot + build + Prepare）≈6–6.5 m；`game`（装 Godot + build + 预热 + Game 单轮）≈9.5–11 m | **≈10–11 m**（由 `game` job 决定） |
| B′. 在 B 基础上把 Game 合并为 1–2 个会话 | 省掉约 1 m 会话固定开销 | ≈9–10 m（需要重定会话超时，并放弃批次级隔离） |

判断：15 分钟预算**单 job 勉强可达、没有余量**；**并行 job 可以稳定达标**，而且这属于分片（全量照跑），不属于地图第 3 条说的「CI 增量选测」。CI 上各 job 是独立 VM，不受本地「Godot/GdUnit 全局串行」规则约束（推断：该规则针对的是同一台主机上的进程冲突）。

## 6. 推荐的 CI 形态

```yaml
# 草图，不是最终实现；所有 run 都调统一前门 pal.ps1（地图第 7 条）
runs-on: ubuntu-24.04            # 固定版本，不用 ubuntu-latest（已是 26.04）
jobs:
  dotnet:   # Core/CLI Unit + CLI 集成（含 CaseSensitiveFileSystemFact 首次真跑）
    steps: checkout(lfs:false) → setup-dotnet(global.json) → pwsh ./pal.ps1 ci dotnet
  prepare:  # Prepare 集成
    steps: checkout → setup-dotnet → pal.ps1 godot install (SHA512 固定, chmod +x)
           → godot --headless --path src/Pal.Game --import → pal.ps1 ci prepare
  game:     # Godot headless GdUnit 单轮
    steps: checkout → setup-dotnet → godot install → --import 预热 → pal.ps1 ci game
  # 每个 job: TRX → job summary + upload-artifact(if: always())；必需检查 = 三者都通过
```

要点：

1. **镜像**：固定 `ubuntu-24.04`，与 GD-466 修复验证时的环境以及上游 CI 一致 [S7][S8][S14]。
2. **Godot 安装**：扩展 `Get-Godot.ps1`（或并入前门），按 `$IsLinux` 选择 `mono_linux_x86_64.zip`，用固定的 SHA512 校验，`chmod +x`，输出 `GODOT_BIN`/`PAL_TEST_GODOT` [S1][S5][L2]。`actions/cache` 可选。
3. **GdUnit 参数**：保持 `--headless`（隐含 Dummy 音频 [S2]），不用 xvfb。会话超时按并行后的实际耗时重定；参考上游开启 blame 的 hang dump [S6]，便于诊断超时。
4. **退出码策略**：默认不打开 `TolerateExitCodeAfterCompletion`，让 GD-466 类问题暴露出来。如果实测出现结果全绿但信号退出，由用户决定是打开该设置还是走隔离清单（见开放问题）。
5. **证据**：TRX → job summary + artifact（地图第 8 条）。不再上传 Test-M0 的身份/进程审计文件 [L1]。
6. **不进 CI**：xvfb 渲染截图、`SendInput` 桌面验收、Windows 专属分支。

## 7. 风险清单

| # | 风险 | 可能性 | 影响 | 缓解 |
|---|---|---|---|---|
| R1 | 当前 main 的 `scene-dialogue` 批次在 Windows CI 上 300 s 超时（`7f0567b`）[R4] | 已发生 | Linux 首跑无法区分平台问题与既有问题 | 迁移前开 bug 票诊断；Linux 原型先跑其它批次 |
| R2 | GD-466 信号退出（134/139）在 Linux 上偶发 [S8] | 中（修复已合入，按日期推断在 adapter 3.2.0 中）| 门禁偶发红 | 原型多次实跑观察；不自动重试；按地图第 6 条进隔离清单，或经用户批准打开 `TolerateExitCodeAfterCompletion` |
| R3 | 从未在 CI 跑过的分支首跑：symlink 分支、`Ordinal` 分支、`CaseSensitiveFileSystemFact` 用例 [L8] | 中 | 首跑出现真实失败 | 视为发现的真实缺陷修复，不跳过；在验证矩阵中注明双平台覆盖关系 |
| R4 | 反斜杠反例在 Linux 上语义不同（`\` 不是分隔符）[L8] | 低–中 | 个别负例结果反转 | 首跑定点核对；校验改为显式字符判定 |
| R5 | 换官方 rc7 带来 GD-549 场景运行器行为变化（独立 viewport、单次投递、场景坐标）[S11]，涉及 16 个测试文件 [L8] | 中 | 大量 Game 用例回归，且与 Linux 问题混在一起 | 先在 Windows 本地完成 fork→官方的切换，再上 Linux，一次只改一个变量 |
| R6 | 冷启动慢触发连接/编译超时（GD-493 类；`GodotConnectTimeout=10000`、`CompileProcessTimeout=30000`）[S9][L3] | 低–中 | 整批不执行 | 先 `--import` 预热；必要时提高超时；`TreatNoTestsAsError` 保持开启 |
| R7 | 单 job 形态贴着 15 分钟上限（§5） | 高（如选 A） | 超预算，触发「CI 增量」讨论 | 采用并行 job（B） |
| R8 | `ubuntu-latest` 漂移到 26.04 [S14] | 已发生 | 环境不可复现 | 固定 `ubuntu-24.04` |
| R9 | GD-397 类 `SignalAwaiter` 竞态卡死 [S12] | 低 | 会话超时 | 开 blame hang dump；按隔离清单处理 |
| R10 | Linux 与 Windows 托管 runner 的 I/O/启动差异未量化（§5.2 为推断） | 中 | 估算偏差 ±30% | 原型实测后再定超时和 job 拆分 |

## 8. 开放问题（需要用户决定）

1. **CI 拓扑**：接受「3 个并行 job（分片照跑全量）」作为默认形态吗？它不算地图第 3 条说的「CI 增量」。
2. **GD-466 策略**：如果 Linux 上出现结果全绿但信号退出，是打开 `TolerateExitCodeAfterCompletion`（降为 warning，但仍要求结果完整），还是严格判红并进隔离清单？
3. **Game 会话粒度**：新前门保留批次（隔离、按批超时），还是合并为 1–2 个会话（省约 1 分钟，但单个卡死会影响全部）？
4. **runner 标签**：确认固定 `ubuntu-24.04`（地图写的是 `ubuntu-latest`，它现在指向 26.04）。
5. **顺序**：确认先在 Windows 本地完成「退役 fork → 官方 rc7」，再迁移到 Linux CI（避免 R5 与平台问题叠加）。
6. **`scene-dialogue` 超时（R1）**：是否立即开 bug 票，并作为 Linux CI 原型的前置依赖？
7. **软件渲染截图原型**：既然已确定不进 CI 门，是否还需要做一次 llvmpipe 与 GPU 像素差异的原型，作为日后 T3 的参考？

## 来源

### 外部一手来源

- [S1] Godot 官方构建发布 `4.7.2-stable`（资产列表与 `SHA512-SUMS.txt`）：https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable
- [S2] Godot 文档：命令行教程（`--headless`、`--import`、`--build-solutions`、`--rendering-driver`），godot-docs@`1f8cd89`：https://github.com/godotengine/godot-docs/blob/1f8cd89c6be2089a425354749f9612025cb236f8/tutorials/editor/command_line_tutorial.rst
- [S3] Godot 文档：Exporting for dedicated servers（headless 与 Dummy 音频）：https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_dedicated_servers.html
- [S4] gdUnit4Net `unit-tests.yml`（Linux job、xvfb E2E、Windows job），@`31a1934`：https://github.com/godot-gdunit-labs/gdUnit4Net/blob/31a19348c842770fc5a2445b596b99cb8ca2d21d/.github/workflows/unit-tests.yml
- [S5] gdUnit4Net `godot-install` 复合 action：https://github.com/godot-gdunit-labs/gdUnit4Net/blob/31a19348c842770fc5a2445b596b99cb8ca2d21d/.github/actions/godot-install/action.yml
- [S6] gdUnit4Net `Api.Test/.runsettings-ci`：https://github.com/godot-gdunit-labs/gdUnit4Net/blob/31a19348c842770fc5a2445b596b99cb8ca2d21d/Api.Test/.runsettings-ci
- [S7] gdUnit4Net `ci-pr.yml`（`ubuntu-24.04`；矩阵含 Godot `4.7`、`4.7.2`）：https://github.com/godot-gdunit-labs/gdUnit4Net/blob/31a19348c842770fc5a2445b596b99cb8ca2d21d/.github/workflows/ci-pr.yml
- [S8] GD-466 issue 与修复 PR #475：https://github.com/godot-gdunit-labs/gdUnit4Net/issues/466 、https://github.com/godot-gdunit-labs/gdUnit4Net/pull/475
- [S9] GD-493：https://github.com/godot-gdunit-labs/gdUnit4Net/issues/493
- [S10] GD-526：https://github.com/godot-gdunit-labs/gdUnit4Net/issues/526
- [S11] GD-549 与 PR #585：https://github.com/godot-gdunit-labs/gdUnit4Net/issues/549 、https://github.com/godot-gdunit-labs/gdUnit4Net/pull/585
- [S12] GD-397：https://github.com/godot-gdunit-labs/gdUnit4Net/issues/397
- [S13] NuGet 发布时间（registration API `published`）：`gdUnit4.test.adapter 3.2.0` = 2026-10-25T17:06Z，`gdUnit4.api 5.1.0-rc7` = 2026-11-07T21:40Z。https://www.nuget.org/packages/gdUnit4.test.adapter/3.2.0 、https://www.nuget.org/packages/gdUnit4.api/5.1.0-rc7
- [S14] actions/runner-images README（标签表：`ubuntu-latest` = Ubuntu 26.04）：https://github.com/actions/runner-images
- [S15] Ubuntu 24.04 镜像清单（image 20261116.376.1）：https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md
- [S16] Ubuntu 26.04 镜像清单（image 20261116.202.1）：https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2604-Readme.md
- [S17] GitHub Docs：GitHub-hosted runners（公开仓库标准 runner 规格）：https://docs.github.com/en/actions/reference/runners/github-hosted-runners
- [S18] chickensoft-games/setup-godot README：https://github.com/chickensoft-games/setup-godot
- [S19] godot-gdunit-labs/gdUnit4-action `action.yml`：https://github.com/godot-gdunit-labs/gdUnit4-action/blob/master/action.yml
- [S20] Docker Hub `barichello/godot-ci` 标签 `mono-4.7.2`：https://hub.docker.com/r/barichello/godot-ci/tags

### 本仓库（`7f0567b`）

- [L1] [.github/workflows/ci.yml](../../.github/workflows/ci.yml)
- [L2] [scripts/Get-Godot.ps1](../../scripts/Get-Godot.ps1)
- [L3] [tests/Pal.Game.Tests/game.runsettings](../../tests/Pal.Game.Tests/game.runsettings)；[tests/unit.runsettings](../../tests/unit.runsettings)
- [L4] [src/Pal.Game/Pal.Game.csproj](../../src/Pal.Game/Pal.Game.csproj)（`Godot.NET.Sdk/4.7.2`，fork 版 `gdUnit4.api 5.1.0-rc6.pal.a5f20fb8.1` / `gdUnit4.test.adapter 3.2.0-pal.a5f20fb8.1`）
- [L5] [global.json](../../global.json)
- [L6] [scripts/GameTestBatches.ps1](../../scripts/GameTestBatches.ps1)、[scripts/Test-M0.ps1](../../scripts/Test-M0.ps1)
- [L7] `git log`：`a079930`（dialogue fixture `CacheMode.IgnoreDeep` 修复）早于 `7f0567b`，即 R4 那次失败已经包含该修复
- [L8] 静态扫描：`tests/Pal.Extract.IntegrationTests/InspectCliTests.cs`、`tests/Pal.Prepare.Tests/*.cs`、`src/Pal.Game/Tests/*.cs`、`src/Pal.Game/Editor/*.cs`、`tools/Pal.Extract/*.cs`、`scripts/*.ps1`、`tests/**/*.ps1`、`.gitattributes`，以及 `src/Pal.Game` 下 `res://` 引用的大小写核对（只读 `rg`/pwsh 脚本）
- [L9] [src/Pal.Game/project.godot](../../src/Pal.Game/project.godot)（`gl_compatibility`、320×200、整数缩放）

### CI 运行记录（只读 `gh run view` / `gh run download`）

- [R1] run 42028894655（2026-11-05，main push，full-check 成功，1216 s；TRX 分段见 §5）：https://github.com/fanxiao92/godot-pal/actions/runs/42028894655
- [R2] run 42020226271（workflow_dispatch，full-check 1184 s）：https://github.com/fanxiao92/godot-pal/actions/runs/42020226271
- [R3] run 42016797195（main push，full-check 1409 s）：https://github.com/fanxiao92/godot-pal/actions/runs/42016797195
- [R4] run 45436681973（2026-11-24，main push `7f0567b`，full-check 在 `scene-dialogue` 失败）：https://github.com/fanxiao92/godot-pal/actions/runs/45436681973
