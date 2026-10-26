# Game 测试当前入口

本页只维护当前 Game 测试入口、覆盖口径与失败判定。职责选择见[测试职责矩阵](../../../docs/testing/test-layers.md)，初始化、SceneRunner 生命周期、时序边界和完整门规则见[Game 测试分层](../../../docs/testing/game-test-layers.md)。旧命令、变异、性能对照、M2/M3 交付年表与历史失败见[历史运行记录](../../../docs/archive/m3/game-test-run-history.md)。

## 当前入口与覆盖

Game 测试继续使用现有 Godot 程序集、固定 GdUnit 包和 `tests/Pal.Game.Tests/game.runsettings`。真实版本、bootstrap 和包来源以[当前包消费说明](../../../docs/testing/gdunit-source-packages.md)及仓库配置为准。测试资源是本例自有、忽略的 native fixture；真实资源和私密证据不进入提交或上传。

共享完整门仍唯一使用 `scripts/Test-M0.ps1`，依赖 Core→CLI→Prepare→Game 的串行顺序和当前脚本生成的批次/身份。当前源码库存为 Game 242 positive + 3 deliberate controls = 245 discovered；完整门的15份历史 TRX/19主命令属于 M3 历史身份，#32 后工程治理的16/20/1441不得倒写为M3结果。静态库存不是通过证据。

## 选择运行范围

- 新增/迁移场景行为：默认选 SceneRunner，遵循真实 Main 初始化、`UpdateCompleted` 完成边界、输入释放、finally 清理和资源隔离。
- argv、READY、诊断、退出码或必要进程隔离：只选对应最小真实进程契约。
- 精确业务时间：运行 Core 显式时间测试；Game 等待仅是活性保护。
- 完整候选：只调用共享 `scripts/Test-M0.ps1`，不将局部 TRX、静态 discovery 或历史结果拼成全门。

定点运行使用[显式范围入口](../../../docs/testing/range-regression.md)，而非从历史命令墙取命令。示例只说明调用形状，不授予运行授权；两个变量须预先指向批准的 Godot console 和已验证的四文件 bundle，证据目录须不存在：

```powershell
& ./scripts/Test-Range.ps1 -Scope game -GameBatch process `
  -GodotPath $ApprovedGodotConsole -FixedPackageDirectory $VerifiedFourFileBundle `
  -OutputDirectory $NewEvidenceDirectory
```

该入口设置并恢复 `GODOT_BIN` / `PAL_TEST_GODOT`，不隐式运行未选的 Prepare；需要 Prepare 时显式加入 Scope，按固定顺序先于 Game。绕过入口直接 `dotnet` 时，必须先 bootstrap、设置这两个批准引擎路径变量并完成当前所需构建/import。实际 SDK/Godot/GdUnit/Prepare/solution 运行遵守根 AGENTS 的全局串行规则，完整参数以脚本/config 为准。

## 失败与证据

检查实际数字退出码、TRX Summary/RunInfo、Results/Definitions/Entries、发现身份和完整输出；部分 Passed、`aborted=0`、READY 或一条 PASS 日志都不表示完成。中止、崩溃、未知退出、输出不完整、缺结果、非预期 skip 或指定失败原因不符时，保存原件并停止，不自动重试、续跑、提高时限或放宽断言。按[当前异常处理规则](../../../docs/agents/test-diagnostics.md)分类；历史症状只在其明确指针触发时读取。

headless/SceneRunner、GPU、物理键盘/桌面人工和远端 CI 是互补证据，不能相互代替。M3 当前交付状态和历史限制见[M3 验收检查点](../../../docs/milestones/m3/acceptance.md#当前状态)；这不是新的运行结论。

## 维护边界

保持 `Main.tscn` 的真实入口和现有测试装配，不新增第二 Godot 项目、替代业务实现或内部测试 seam。变更时先冻结独立预期与完整身份，再同步脚本/config/本页；需要时序变异、输出捕获或完整门诊断，进入 Game 分层与当前异常规则，不在本页重写历史证据。
