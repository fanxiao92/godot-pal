# Unit 反馈：从这里开始

本页是 work agent 与新人的短上手入口。在仓库根目录操作，使用 `global.json` 指定的 .NET SDK 和普通 NuGet 缓存；无需 Godot、游戏资源或先读 PowerShell 编排。首次还原可能访问已配置的包源，这不是空缓存／离线证明。

## 1. 选工程，写理由

按实际变更及已知调用方选择下表中的一个或两个工程；范围不明先调查，不无条件全跑，也不静默省略调用方。所选工程默认全量；只有调用者显式指定才加 filter，并把结论限定为该过滤范围。

| 变更／调用方 | 明确工程 |
| --- | --- |
| Core 公开业务规则、状态和显式业务时间 | `tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj` |
| CLI 内存图像、调色板、PNG、Sprite、YJ2 算法 | `tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj` |

文件／进程、资源或引擎接线不由 Unit 证明；涉及这些契约时按[测试职责矩阵](test-layers.md)说明缺失覆盖并另选层，默认反馈不进入 Integration、Prepare、Godot、solution 或编排脚本。后续确需 Godot 时，从[Game 专题的现有测试示例与生命周期](game-test-layers.md)进入，再按其指针查 Game README 的实际命令；不先读完整门编排。

纯内存例子来自现有 [`IndexedImageTests.Constructor_keeps_row_major_indices_alpha_and_source_metadata`](../../tests/Pal.Extract.UnitTests/IndexedImageTests.cs)，直接调用生产公开构造和属性（[`IndexedImage`](../../tools/Pal.Extract/IndexedImage.cs)）：

```csharp
using Pal.Extract;
using Xunit;

// 放在 xUnit 测试类中；此处节选现有案例，不需新建重复测试。
[Fact]
public void Constructor_keeps_row_major_indices_alpha_and_source_metadata()
{
    var image = new IndexedImage(2, 1, [0, 255], [255, 0], frameIndex: 17, sourceOffset: 42);
    Assert.Equal(new byte[] { 0, 255 }, image.Pixels);
    Assert.Equal(new byte[] { 255, 0 }, image.Alpha);
}
```

输入和预期都是字面值，索引与 alpha 独立；无需文件、环境变量、进程或引擎。Unit 的纯内存边界约束案例及被测依赖，不禁止测试宿主、restore、日志使用文件／进程。

## 2. 直接运行所选工程，逐个留证

先读一次[Unit 协议](unit-entry.md#默认反馈协议)的证据／成功条件。建立本轮全新证据目录（例如仓库外 `C:/evidence/unit-001`），先保存 cwd、HEAD、dirty diff 及未跟踪相关源码，再开始整轮墙钟；每项目启动前开始项目墙钟。下面按选择执行，**前项失败即停止**，不要整段无条件粘贴：

```powershell
# 示例选择 CLI Unit；普通隐式 restore/build，无 filter。
# 用终端采集功能或外层采证保存该命令原始 stdout/stderr；退出后立即记录 $LASTEXITCODE。
dotnet test tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj --configuration Debug --logger 'trx;LogFileName=cli-unit.trx' --results-directory 'C:/evidence/unit-001/cli'

# 仅当影响范围也选了 Core 且前项完整通过时执行。
dotnet test tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj --configuration Debug --logger 'trx;LogFileName=core-unit.trx' --results-directory 'C:/evidence/unit-001/core'
```

默认保留 build，不用 `--no-build`；`--no-restore` 仅在已有匹配当前源码依赖、工程、配置的成功 restore 证据时使用，并引用证据。日志落盘并确认进程及输出结束后停止项目墙钟；完成最后一个项目的证据判定或停止留证后结束整轮墙钟。隐式 restore/build 未独立测量填“未知”，不能填 0；TRX duration 不是端到端墙钟。

## 3. 判定，再继续或停止

核对原始输出、数字退出和**本轮** TRX：退出 0 本身不够；会话须完整，Summary 为 Completed，RunInfo 无异常，执行数非零，计数与结果行一致，无失败或非预期 skip。具体字段见[成功判定](unit-entry.md#成功判定)。缺 TRX、损坏／不完整、零执行和未批准 skip 都不能通过。

遇到 restore、build、test 失败或证据异常，保存现场并停止，列出尚未运行工程；不自动重试、切源、安装 SDK、升级依赖、缩范围或提高时限。仅单项目 >5 分钟／整轮 >10 分钟是成本告警，不停止进程、不改功能结论、不触发重试；它们不是执行 timeout。

## 4. 交回报告

每轮使用同一形状，所有路径指向原件；未知用“未知”，未运行用“未运行”。完整字段和计时规则以 [Unit 协议](unit-entry.md)为准：

```text
结论：所选项目 Unit 反馈通过 / 失败，已停止 / 不完整，已停止
源码：cwd；HEAD；clean 或 dirty diff + 相关未跟踪文件快照/哈希路径；运行后是否变化
选择：有序工程清单 + 每项理由/已知调用方；filter=无（或调用者原始要求）
工具/成本：SDK；Debug/net10.0；普通缓存（非冷缓存证明）；实际 restore/build 参数
每项目：工程；实际命令；开始/结束；墙钟；原始 stdout/stderr；数字退出；本轮 TRX
        Summary/RunInfo；total/executed/passed/failed；skip 结果行；完整性；结论
整轮：开始/结束；实际墙钟；restore/build 分项=未知（若未单测）；成本告警/执行 timeout 分列
未运行：工程/层 + 原因；停止原因与指定诊断；后续动作等待明确批准
```

以下是**报告示意，不是运行证据**；每行仍须附上述公共字段。当前批准 Unit 没有 skip 豁免：

| 情形 | 每项目结果形状 | 整轮／未运行形状 |
| --- | --- | --- |
| 正常 | `exit=0; TRX=本轮; Completed; RunInfo=无; executed=N>0; passed=N; failed=0; skip=0; 完整` | `所选项目 Unit 反馈通过`；只指所选工程，不是候选／完整验收通过 |
| restore 失败 | `exit=实际非零; restore 诊断/日志=路径; TRX=未生成; build/test=未运行` | `失败，已停止`；其余所选工程列为未运行 |
| build 失败 | `exit=实际非零; build 诊断/日志=路径; test=未运行; TRX=未生成` | `失败，已停止`；还原成本未单测仍未知 |
| test 失败 | `exit=实际非零; TRX=路径; Summary=实际值; failed=N; 失败案例/诊断=原件` | `失败，已停止`；部分 Passed 不抵消失败 |
| 零执行 | `exit=0; TRX=路径; executed=0` | `未通过，已停止：零执行`，不以退出 0 或日志 PASS 放行 |
| 非预期 skip | `exit=0; NotExecuted 结果行=N>0（即使 notExecuted counter=0）` | `未通过，已停止：非预期 skip`；保留名称/原因 |
| 会话不完整 | `exit=实际数字/未知; TRX=缺失/损坏/非 Completed/RunInfo 异常; 部分结果=路径` | `不完整，已停止`；超时/中止/崩溃不能冒充预期失败 |
| 多项目中途失败 | `A=完整通过; B=实际失败/不完整; C=未运行` | `整轮未通过，已停止；未运行=[C]`；逐项目墙钟只填已运行项，整轮墙钟含停止留证 |

默认不额外 discovery／冻结案例身份。若任务确需逐项发现与执行集合比较（如工程迁移），再读[显式维护工具](unit-entry.md#显式维护工具test-unitps1)；直接命令没有该工具同等的集合核验保证。
