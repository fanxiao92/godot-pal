# 显式范围回归（#33）

`scripts/Test-Range.ps1` 是范围回归，不是完整 M0 门；结果固定标记 `kind=range-regression`，不会签发 `PASS.txt`。

## 选择

必须显式指定非空 `Scope` 子集：`cli-integration`、`prepare`、`game`。多范围使用 PowerShell 数组，不是 CSV：

```powershell
& ./scripts/Test-Range.ps1 -Scope @('cli-integration','prepare') `
  -GodotPath $ApprovedGodotConsole -FixedPackageDirectory $VerifiedFourFileBundle

& ./scripts/Test-Range.ps1 -Scope game -GameBatch process `
  -GodotPath $ApprovedGodotConsole -FixedPackageDirectory $VerifiedFourFileBundle `
  -OutputDirectory $NewEvidenceDirectory

pwsh -NoProfile -File ./scripts/Test-Range.ps1 -Scope cli-integration `
  -Filter 'FullyQualifiedName~Pal.Extract.Tests.InspectCliTests.'
```

- CLI／Game 的公共 Filter 仅接受 `FullyQualifiedName=<method>`、`FullyQualifiedName~<class-prefix>`；不是任意 VSTest Boolean 语法。Prepare 不接受 Filter。显式空 Filter、跨范围 Filter 均提前拒绝。
- `=` 对完整方法身份做 Ordinal 匹配，同一方法的参数案例全部保留；`~` 是 **Ordinal 前缀**，不是通配符或 substring。执行表达式由已发现的实际方法生成，避免 VSTest 的 contains 语义越选。
- Game 必须显式选一个或多个既有正向批次 `process`、`scene-dialogue`、`native-map`。可与其他 Scope 组合；其他 Scope 不能带 GameBatch。Game 参数案例以源方法身份选择，验证时保留完整参数案例身份。
- 空／未知／重复 scope 或 batch、缺项目、无匹配、零发现、零执行均失败；三个失败控制不属于正向范围。

## 准备与顺序

CLI 集成不触及 Godot、固定包、Game 计划或资源准备。Prepare／Game 需显式的批准 Godot console 路径和已核来源的完整四文件 bundle：

1. 调所选仓库既有 `Get-GdUnitPackages.ps1 -InputDirectory`，按其锁校验并原子准备仓库本地 NuGet source；显式输入不会退回网络下载。输入四文件长度、hash、mtime 前后核对。
2. 为本次调用设置 `GODOT_BIN`／`PAL_TEST_GODOT`，结束恢复旧值；SDK输出语言明确为英文以匹配既有 discovery reader。
3. 范围顺序固定 CLI → Prepare → Game；每个选中范围完成 build、完整 discovery、选择冻结、执行及验证，再进入下一范围。选中的 Prepare 全部完成后才进入 Game，避免与其真实构建／import 重叠。
4. Game 调用既有 headless `game.runsettings`，先完整发现并用 `Assert-GameDiscovery` 与源库存对照，再取正向批次与过滤交集；执行仅传一个合成 Filter。

只运行选中范围及必要构建／包准备，不隐式执行未选的 52 项 Prepare，也不运行原生 Main 启动或三轮 Game 重复；后两者属于完整门。入口在包准备／SDK／引擎工作前只查询全局 Godot PID／Name：有进程或无法核实库存即失败，记录结果但不终止任何进程；CLI 范围不查全局进程。这是冲突前检而不是跨入口锁，调用者仍必须维持 Godot／Prepare／GdUnit 全局串行，不能据一次空快照并发启动任务。失败中止、崩溃或未知结果后保留证据停止，不能自动重试。

## 身份、证据与退出

- 从任意 cwd 使用入口／RepositoryRoot 的明确路径。默认在 RepositoryRoot/TestResults 创建唯一目录；显式 OutputDirectory 必须不存在。成功、失败证据都保留，不覆盖旧轮。
- 保留实际 SDK、HEAD（若有 Git）、选中源码／配置／入口 helper hash；HEAD 不替代 dirty 源码身份。build 的实际程序集输出路径及相邻二进制／PDB／deps／runtimeconfig hash 在发现前记录并在执行后核对。固定 GdUnit 首次运行会从其 API 内嵌模板安装 runner 并重编译；只有该模板首次新增时允许 Game DLL/PDB 改变，两端 hash 与生成源码／API 身份分别记录，不宣称二进制逐字节相同或已测量进程实际加载字节。其它产物变化、模板不符或额外源码新增仍失败。
- CLI／Prepare 使用 Version 7 原始 discovery 的 testId、FQN、显示名、xUnit unique ID、序列化参数、source／executor，不按显示名或 FQN 去重。完整发现和实际选择分别落盘；TRX 必须匹配独立选择，不能用成功 TRX 反推预期。
- Game 复用现有源库存、批次计划、discovery 和 TRX reader，不复制另一套弱化结果解析器。TRX graph、必需计数、GUID／时间、非零正向执行、异常 RunInfo、当前命令时间绑定及跨范围 run／execution ID 唯一均需满足。
- CLI 仅允许选中集合内的两个历史平台 `NotExecuted` 案例；是否实际 skip 按结果逐行核对，不因位列白名单就预扣通过数。只允许与实际 skip 行严格对应、位于当前会话时间内且不重复的固定 xUnit `[SKIP]` 警告，其它 RunInfo 仍失败。仅 skip 的选择失败。Unit 默认依旧不允许 skip。
- 范围与 M0 复用机械抽出的 `DotnetCheckedDefinition.ps1` 真实自有 dotnet 边界，保留原 600000ms work、5000ms cleanup、防挂起、原流／数字退出及失败分类。Range 不调用 M0 的全局／仓库进程快照采集器，仅保留其自有 PID／root exit／双流 EOF／cleanup 记录。
- 原命令已知非零原值返回，例如 7；退出 0 仍须通过日志、UTF-8、discovery／TRX／身份验证。没有确认 root／EOF／cleanup 或异常会话返回 2/incomplete；选择、结果、身份、采证等错误通常为 1。报告失败不能把已知非零改成成功；`range-run.json` 的 status 与原数字退出分开。

## 回归与证据边界

```powershell
# 27 个独立字面 TRX fixture，无 SDK／引擎。
pwsh -NoProfile -File ./tests/RangeIdentity.Tests.ps1
# 实际人工 SDK/xUnit 仓库的公开入口回归，使用仓库既有批准版本及普通包缓存。
pwsh -NoProfile -File ./tests/Test-Range.Tests.ps1 -EvidenceDirectory $NewFixtureEvidence
```

公开入口 23 次回归覆盖冲突／未知库存阻断工作、空库存进入下一实际前提、CLI 不查询库存、非法选择的准确诊断、完整发现与参数选择、同显示名不同 ID、字面前缀、未选命名空间不执行、缺当前 TRX、原生退出 7／中文原始字节、旧证据不覆盖及失败后恢复。人工 SDK fixture 不是实际 Prepare／Game 接线证据；真实代表性范围、独立复审及最终集成完整门分别记录，不拼接为完整验收或远端 CI 成功。
