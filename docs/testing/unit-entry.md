# 单元快入口（#32-C）

`Test-Unit.ps1` 只运行明确的 Core 与 CLI unit 项目。它不是范围回归、工程检查聚合器或完整验收；通过不能代替 CLI integration、Prepare、Game、真实资源、GPU/人工桌面或远端 CI 验收。完整门仍是 `scripts/Test-M0.ps1`。

## 调用

需要 PowerShell 7、仓库 `global.json` 选择的普通 .NET SDK 与现有三个 xUnit/Test SDK 包版本。沿用普通 NuGet restore/cache，不调用 GdUnit bootstrap，不提供/要求 Godot、固定 GdUnit bundle 或游戏资源。首次普通 NuGet restore 可能需要配置的 nuget.org 源；这不是无网络/空 NuGet 缓存证明，也不下载引擎或固定包。

```powershell
# 默认两个项目；根目录默认来自脚本位置，与当前 cwd 无关。
pwsh -NoProfile -File scripts/Test-Unit.ps1

# 明确命名的非空子集，不接受任意 csproj、filter、solution 或自动影响推断。
pwsh -NoProfile -File scripts/Test-Unit.ps1 -Project core
pwsh -NoProfile -File scripts/Test-Unit.ps1 -Project cli-unit

# 多选使用 PowerShell 数组，不是一个含逗号的字符串。
# 本脚本含 exit；外层需要继续运行/读取退出时，请在独立 pwsh 进程调用，不要 dot-source。
& ./scripts/Test-Unit.ps1 -RepositoryRoot 'C:/work/godot-pal 中文 空格' `
    -Project @('core', 'cli-unit') -OutputDirectory 'C:/evidence/new-unit-run'
```

公开参数：

| 参数 | 契约 |
| --- | --- |
| `RepositoryRoot: string` | 默认脚本上一级；人工项目 seam 使用同样的稳定相对路径 |
| `Project: string[]` | 默认 `@('core','cli-unit')`；只允许无重复、非空子集；未知/空名/重复全部失败 |
| `OutputDirectory: string` | 默认 `<RepositoryRoot>/TestResults/unit-<唯一runId>`；显式指定时必须不存在（即使空目录也拒绝）；不删除/覆盖旧结果 |

身份精确绑定 `tests/Pal.Core.Tests/Pal.Core.Tests.csproj` 和 `tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj`。当前受支持构建口径是仓库既有 `Debug/net10.0`，没有任意构建属性/框架/SDK注入接口。

脚本先以隔离 `pwsh` 调用[既有轻量边界命令](unit-boundary.md)，正确传递数组并保留退出/日志；不会 dot-source 该含 `exit` 的命令。只加载 `GameTestBatches.ps1` 的无副作用定义以复用 `Read-TestRunIdentity`，不调用会读取 Game/solution 的 `Get-GameBatchPlan`。

然后对每个选定项目显式 `dotnet build --configuration Debug`（普通 restore），再 `dotnet test --no-build --no-restore --list-tests --diag ...`。**所有选定项目的完整发现先冻结，才执行第一个项目**；执行同样仅指定项目、无 filter、无隐式重建。不从 solution 构建/发现后排除 integration。

## 成功、失败与证据

只有整个入口实际数字退出 **0** 且 `unit-run.json.status=completed` 才表示本次所选 unit 反馈完成。`PASS unit: N projects` 不是完整门通过。子命令非零退出原样传播；选择/边界/证据错误通常退出 1。报告写入失败不能把原非零退出改成 0。后续项目失败时，前一项目的通过不代表整条命令通过。

每轮使用自己的新输出目录，保留：

- `unit-run.json`：选择、实际源码/config SHA-256、工具源码 SHA、可获得的 Git HEAD（仅描述，不代替脏树 SHA）、SDK/OS/runtime/架构、缓存口径、命令 argv/cwd、原始数字退出（未知为 null）、退出/两路 EOF 完整性、逐命令 UTC 与 Stopwatch 墙钟、状态/失败原因、已核验项目的案例和执行 ID。
- `<identity>.discovery.diag.log` 及 SDK 生成的关联诊断：原始发现协议；`<identity>.discovery.json` 冻结完整 case 对象（真实 `TestCase.Id`、FQN、显示名、程序集、xUnit unique ID/完整 serialization）及程序集 SHA。不能只看控制台 `--list-tests` 行或去重显示名。
- `<identity>/results.trx`：本轮原始执行结果；每个命令的 `*.stdout.log` / `*.stderr.log` 原始字节增量落盘。SDK 设为英语诊断、流读取/入口正常与失败输出明确 UTF-8；测试中文原文不改写。
- 所选测试及直接生产 DLL/PDB、测试 deps 文件 SHA；发现到执行后再次核验，源码/config/执行工具源码也复核未变。

发现解析刻意绑定当前批准 SDK 的 VSTest `Version=7`、`OnDiscoveryMessageReceived` 协议与 xUnit 3.1.4 adapter 元数据；未知消息、缺 completion、aborted、遗漏/重复 case ID、错误程序集/adapter、缺完整 serialization、零发现、warning/error 均失败，不猜测缺失案例。未来 SDK 协议变化需要显式审查，不静默退化成计数比较。

TRX 核验复用 Results/Definitions/Entries 完整身份图，要求唯一且非空的 case/execution ID、完整名称和一致关联；本轮 run/execution GUID 不跨项目重用。额外要求单一完整 Summary/Times、Completed、没有 RunInfo、全部标准 counters 齐全且 total=executed=passed=发现数、其它 counters 全零、每例 Passed、与冻结发现的 **ID/FQN/显示名/程序集完整集合**逐项一致。非预期 skip、零执行、同数换例、损坏/缺结果均不得成功。同显示参数例允许存在，由 ID 和原始 serialization 区分。日志中的编译/宿主/aborted/crash 等失败诊断也阻断。

每命令的 `elapsedMs` 使用内部 Stopwatch：stdout/stderr 文件创建后、`Process.Start` 前启动，退出及两路复制任务完成（失败时包含必要清理等待）后在 finally 中停止；不包含随后流/进程 Dispose、报告写入、日志解码和错误分类。`startedUtc/finishedUtc` 是另记的采证时间，不用两者之差替代该 Stopwatch 窗口。

入口 `elapsedMs` 也是内部窗口：在初始工具哈希完成后启动、最终 `Save-Report` 前停止，包含其间边界、构建、发现、执行和其它哈希/报告操作，但排除初始工具哈希及最终报告写入。它**不是外层 pwsh 的完整端到端墙钟，也不等于 TRX case duration 合计或纯业务耗时**；需要外层成本时由调用者单独计时，未记录则保持未知。未知耗时/退出不补零；只有已完整核验的项目列执行摘要。`failed` 表示已知错误；未确认进程/输出完成或异常会话为 `incomplete`，不能当完成成功。

自有命令 root 与 stdout/stderr EOF 共用 600000ms 工作预算；失败时只对该自有进程树做至多 5000ms 的清理等待，不查询全机进程、不套用 Godot Observer。root exit/EOF 不是所有后代退出证明，也不是恶意项目沙箱。中止、崩溃、未知退出/输出不完整时保留原现场并停止；不要自动重试、换参数或扩大预算。目录/日志不可写等环境错误同样不能算通过。

## 本入口自己的回归

```powershell
# 先 Parser.ParseFile 预检所有改动/生成的 PS，再执行。
pwsh -NoProfile -File tests/UnitEntry.Tests.ps1
# 保存到新的自有证据目录（已存在目录拒绝）：
pwsh -NoProfile -File tests/UnitEntry.Tests.ps1 -EvidenceDirectory 'C:/evidence/new-unit-entry-regression'
```

回归使用同 SDK/包版本、同稳定路径的真实人工 Core/CLI 项目，从非仓库 cwd 调用公开入口；临时根含中文和空格。它是自有 SDK/xUnit/进程/磁盘回归，**不是离线静态检查或被选 unit 案例的一部分**。每次调用原输出/命令/数字退出留在 EvidenceDirectory；只删除自身唯一临时项目，不清理现有缓存或资源。

覆盖正常两个项目/明确子集、全部选择错误、缺项目/部署边界错误、旧输出目录不覆盖、零发现、全 skip 零执行、真实测试失败的原退出/中文双流日志、失败后正常恢复、同显示不同参数身份、同数替换参数被拒绝，以及真实 logger 无法生成预定 TRX 的结果路径阻塞反例。后两种故障仅在人工生产依赖里故意访问自有产物路径；不是增加真实业务测试模式，也不宣称轻量词法检查能证明任意间接 I/O。

可达的缺结果反例中 SDK 本身仍可能退出 0，因此入口必须另外检查 TRX 存在性。公开接口不接收任意 TRX/SDK 替身，本回归**没有**通过竞态改写 logger 完成后的 XML，未声称直接动态覆盖任意损坏 Summary/RunInfo/identity graph 的每一种变体或未知进程/EOF 故障；这些 fail-closed 分支还依赖共享身份回归与独立源码审查。此入口也不是防恶意替换完全自洽、同身份伪造结果的认证系统。

实际仓库 unit 证据及与迁移前集合的独立核对属于任务交接，不写死成本阈值或把一次集合缩小的反馈称为同集合性能优化。#32 整票、真实完整门及最终两轴审查由父任务分别验收。
