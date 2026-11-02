# Unit 反馈：从这里开始

在仓库根目录使用 `dotnet CLI`，无需 Python、自定义测试执行器、Godot 或游戏资源。SDK 选择交给 `global.json` 与 dotnet；兼容 SDK 必须已安装，普通 NuGet restore 可以访问配置的包源，不是空缓存／离线证明。

## 1. 按影响选择工程

按实际变更及已知调用方选择一个或两个工程，说明理由。所选工程默认全量；只有调用者显式指定才加 filter，并限定结论范围。

| 变更／调用方 | 明确工程 |
| --- | --- |
| Core 公开业务规则、状态和显式业务时间 | `tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj` |
| CLI 内存图像、调色板、PNG、Sprite、YJ2 算法 | `tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj` |

Unit 不证明文件／进程或引擎接线；需要这些契约时再读[职责矩阵](test-layers.md)。Godot 后续路径从[Game 专题](game-test-layers.md)进入，不先读完整门编排。

纯内存例子来自现有 [`IndexedImageTests.Constructor_keeps_row_major_indices_alpha_and_source_metadata`](../../tests/Pal.Extract.UnitTests/IndexedImageTests.cs)：

```csharp
using Pal.Extract;
using Xunit;

[Fact]
public void Constructor_keeps_row_major_indices_alpha_and_source_metadata()
{
    var image = new IndexedImage(2, 1, [0, 255], [255, 0], frameIndex: 17, sourceOffset: 42);
    Assert.Equal(new byte[] { 0, 255 }, image.Pixels);
    Assert.Equal(new byte[] { 255, 0 }, image.Alpha);
}
```

输入和预期来自字面值；无需文件、进程或引擎。纯内存边界约束案例及被测依赖，不禁止测试宿主、restore 或日志使用文件／进程。

## 2. 直接运行，失败停止

记录 cwd、HEAD 和 dirty 状态，使用本轮全新结果目录。通过终端或 agent 工具保留实际命令、原始输出和数字退出；不要求另写采证脚本。先运行所选工程之一，例如：

```powershell
dotnet test tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj --configuration Debug --settings tests/unit.runsettings --logger 'trx;LogFileName=results.trx' --results-directory 'C:/evidence/unit-001/cli'
```

**先判定上一项。** 只有也选了 Core 且上一项通过，才执行：

```powershell
dotnet test tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj --configuration Debug --settings tests/unit.runsettings --logger 'trx;LogFileName=results.trx' --results-directory 'C:/evidence/unit-001/core'
```

保留普通隐式 restore/build，不默认加 `--no-build` 或 `--no-restore`。SDK 实际版本需要记录时用 `dotnet --version`，不另写版本比较器。

## 3. 判定与报告

[`tests/unit.runsettings`](../../tests/unit.runsettings) 使用 VSTest 的零执行失败和 xUnit 的 skip 失败配置。默认依据原生执行结果：命令正常结束、数字退出 0、输出报告通过、预期的本轮 TRX 文件存在。明显异常、中止、超时、未知退出或缺失报告均不能报通过；保存现场并列出未运行项目，不自动重试、换源、升级或缩范围。

报告只需源码/dirty 范围、工程选择及理由、实际命令、退出、输出/TRX 路径、结论和未运行项。时长使用终端/CI 可获得的数据并说明口径，未测量就写未知；不要求额外 Stopwatch、全仓散列或独立审计报告。仅可称“所选项目 Unit 反馈通过”，不是完整验收。

默认不检查 TRX 完整身份图、RunInfo 白名单或逐项发现集合，也不生成 `unit-run.json`。这些额外保证的取舍及显式维护工具见 [Unit 专题](unit-entry.md)；决定依据见 [ADR-0003](../adr/0003-native-unit-feedback.md)。
