# 测试异常说明：分类与证据边界

本页记录 M2 / [Issue #17](https://github.com/fanxiao92/godot-pal/issues/17) 的历史异常，防止后续把不同失败混为一谈；不是新的诊断结果。任务实时状态与后续处理以该 Issue 正文、评论及对应提交的实际证据为准。通用测试规则见根目录 [AGENTS.md](../../AGENTS.md#测试)。

**当前检查点（2026-10-22）**：M2 已完成。#18 固定源码包及本地完整自动门、Windows 分层人工检查（含 resize/letterbox 与正常关闭反馈）、收尾文档独立双轴终审均已接受；成果已合入并通过 SSH 推送 main，#11/#17/#18 及后续跨盘测试缺陷 #19 均已关闭。已验证交付提交为 `a76f83b`，[远端完整 Windows CI39738074108](https://github.com/fanxiao92/godot-pal/actions/runs/39738074108) 已通过；此前失败CI39731421949仍为失败，根因与修复见第5节。包来源与 bootstrap 见[固定包说明](../testing/gdunit-source-packages.md)，完整门/人工时线与限制见 [M2 验收记录](../milestones/m2/acceptance.md)。以下旧失败仍为失败，后续完整 PASS 不证明 hook 精确根因或概率故障根治；本轮文档同步不授权重跑。

## 当前工具链决策

当前使用已批准的仓库配置；#18 特定固定源码 API/adapter 包是后续明确批准的变更，进一步升级仍需另行授权。版本以当前 `global.json`、项目 `.csproj`、引擎与运行脚本核对结果为准。历史异常发生在 SDK 10.0.402、Godot 4.7.2、gdUnit4.api 5.1.0-rc5、adapter 3.1.1 的组合下，这些版本仅用于识别证据环境，不是另一个版本配置源。

除已交付的 #18 特定范围外，工具升级或进一步源码构建由用户另行立项；不能因本页提到上游修复就自动升级、改捕获实现或调整 runner、管道和超时。保留基线也不意味着容忍测试失败：未通过的门禁仍然阻断验收。

## 1. Windows 输出捕获异常：会话失败，不是业务断言失败

**历史事实**：受测提交 `9fa74e8` 的 Game repeat 出现进程退出码 `-532462766`、120000ms 会话中止，只保留71条通过结果；TRX Summary 为 `Failed`。原异常链包含：

```text
ArgumentNullException
Monitor.ExitIfLockTaken
TextWriter.SyncTextWriter.Write
Console.Write
GdUnit4.Core.Hooks.WindowsStdOutHook.ProcessReadData
ReadPipeOutput
```

**相关但未证实的原因**：上游 [#526](https://github.com/godot-gdunit-labs/gdUnit4Net/issues/526) / [#527](https://github.com/godot-gdunit-labs/gdUnit4Net/pull/527) 涉及 Windows 捕获线程未及时终止、后续输出被抢读和尾部输出丢失等问题。相关性不等于已证明本项目这次 `ArgumentNullException` 的精确触发原因，当时也没有本地应用该补丁后的验证结论。后续 #18 使用含 #527 的固定源码包并通过一次完整门禁；这不反向证明该历史异常的精确根因。支持矩阵差异是风险，不是根因证据。

**判定要求**：检查原退出码、完整 TRX Summary/RunInfo 和异常栈；即使部分用例通过、计数中的 aborted/timeout 为0，也不能认定会话完整通过。保留原件后停止，不自动重试或把崩溃归作预期失败控制。后续一次未触发此异常不能证明它已修复。

## 2. 焦点恢复首步断言：85ms观测阈值与实际处理边界不同

**历史事实**：受测提交 `98b138e` 的 Game repeat 完成170条结果，169通过、1失败，Summary 为 `Failed`。失败用例为：

```text
WindowFocusSignalsFreezeUntilFreshPressThenResumeImmediatelyWithoutCatchup:1 (north-primary)
```

- 新的非echo Up 输入日志：测试局部帧计数75、ticks=1252。
- 首次观测到正确首步状态：局部帧计数77、ticks=1349；位置、朝向、动画阶段与相机均符合该步预期。
- 失败谓词是 `Time.GetTicksMsec() - tick < 85`，约97ms的观测跨度导致断言失败，测试子进程实际退出1。
- 该轮未发现与上一节相同的 hook/sessionabort 证据；它是正常用例失败，不是预期失败控制。后续三项控制测试和启动检查未执行，完整门禁未通过。

**尚未确定**：97ms分别花在输入交付、帧调度、实际更新还是测试观测恢复的哪一段。局部帧计数来自等待 ProcessFrame 的测试代码，不是每次 `Main._Process` 的完整采样。不能仅据97ms认定生产重新等待100ms，也不能据状态正确就认定生产无问题或必然是系统负载。

**验证方向**：Core 用公开 `Update(ticks)` 的确定性时间输入验证首步、截止边界与调度重置；Godot 集成测试验证有效输入交付后的首次实际更新及其完成时状态。实际回调顺序须查证。若首次真实更新已被外部延迟推到100ms之后，仅这次观测无法区分立即首步与错误冷却实现，需由 Core 边界测试补足。详见根 AGENTS.md 的时序规则。

**边界**：既不直接抬高85ms阈值或删除断言，也不靠重复全套获得通过。需要新运行或临时探针时，先明确批准范围；当前文档维护本身没有授权诊断、修复或重跑。

### 后续修复与一次门禁结果（2026-10-21）

用户随后明确批准修复判断方式并运行一次完整门禁。修复当时位于候选工作树 `task/m2-17-final-acceptance`，身份为 `98b138e` 加未提交的两文件补丁（SHA256 `8666084b0837451eef5a1b56cba6d28fe3af72fceca5d31bf7d683a2172fb37c`），当时尚未合入主工作树。后来该修复以 `597ef1ebba344d0d3a36907b80b517a3c4d37401` 提交并随 `04f8e91` 本地集成；不能只检查旧基线提交判断是否含修复。

- **已修正的范围**：`ExplorationObserver.FocusRecovery` 不再用60/85ms观测耗时判断首步或后续节拍。测试节点按已核实的Godot处理优先级在Main之后执行；在回调中交付新输入，下一次回调无条件检查首次真实更新状态，再保存held/released状态。精确时间边界仍由已有Core确定性测试负责。测试说明同步在 `src/Pal.Game/Tests/README.md`，未改生产移动规则、业务时钟或工具链。
- **已执行定点验证**：旧判据在正确首步后加入120ms观测暂停时两例失败；新判据同类暂停对照两例通过。取消Core调度重置、跳过首次Main更新的两个独立错误变异均被测试捕获。恢复所有临时变异和暂停后，两个焦点案例及两个相关回归共4项通过；独立代码审查OK。这证明修复具备抗观测延迟与捕获目标错误的证据，不证明历史97ms的精确成因，也不覆盖所有其它Observer模式。
- **完整门禁仍失败**：该候选唯一一次新门禁在09:03–09:09 UTC运行，info/restore/build通过，solution中的Core129、CLI300＋2准确跳过、Prepare52完成；Game仅返回5项通过后再次出现第1节的完整hook异常栈，runner退出 `-532462766`，随后120000ms会话中止。Game Summary为Failed，完整入口退出1，无PASS标记。
- **未完成项**：该轮5项是South及四向Arrow，不包含修复后的焦点案例；独立discovery、独立Game、重复轮、三个失败控制及共享启动检查均未执行。不能把定点结果拼接为完整门禁通过；独立证据终审仍为BLOCK。
- **崩溃遗留**：源工作树新增一份 `Assets/RuntimeTests/<id>/Scenes/Scene.tscn` 和一份未跟踪的 `Tests/RuntimeProfiles/<id>/Profile.tres`，原文件未变但整体保护比较不等。当时原件保留、未清理；进程释放不等于工作树恢复。具体身份见Issue证据记录；后续可恢复归档的授权与结果另见下文，不能倒写为本轮崩溃后已恢复。

上述修复与一次门禁授权执行完毕后，用户另行明确批准原样重试；不是自动循环重跑。重试准备阶段发生离线helper生成断言错误，获准修正后因代理剩余预算不足未启动门禁；准备完成与静态检查通过不是测试通过。

随后独立执行阶段在 **09:44:28–09:50:34 UTC** 实际运行同一候选、同一参数的完整入口一次：Core129、CLI300＋2原跳过、Prepare52完成，Game仅返回63项通过后再次出现相同hook异常栈及120000ms会话中止。原Game Summary为Failed，outer退出1、非外层超时、无PASS；独立审核仍为BLOCK，后续discovery/独立Game/repeat/controls/startup均未执行。未采用加等待、停捕获、减日志或升级等绕过。

这次旧scene/profile的字节与mtime保持，但又新增一份0字节synthetic scene和一份profile，累计四份遗留当时均保留；整体保护比较仍不等，当时不能称工作树已恢复。次数与结果说明该次原样重试没有解除当时阻塞，不能据此量化故障概率或推断与某个负例同因。完整证据及遗留身份记录在Issue中。

该次追加的重试授权已消耗。此检查点仍未批准工具链变更；进一步框架定位、清理遗留或再次运行门禁需要明确范围，不通过忽略异常、拼接历史结果或无限重试取得通过。

### 后续 #18 固定包、零字节污染与获批归档后的完整门

用户随后另行批准 #18 的固定源码包。其第一次完整门于 **2026-10-21T14:45:57.174495–14:48:41.103805Z** 失败：outer1、非timeout、无PASS；Core129、CLI300/302total＋2原跳过，Prepare52中51pass/1fail。Godot import 扫描到上述保留的0字节 `Assets/RuntimeTests/26042ccb7a38416e988e78f20782b105/Scenes/Scene.tscn` 并报告 Parse Error；即使子进程exit0，Prepare仍因ERROR严格失败。Game/discovery/repeat/controls/startup未执行。这不是预期负例、hook会话中止或完整通过。

用户再明确批准**恰四份历史 synthetic 残留工程外可恢复归档及额外一次原完整门**。原字节、mtime和creation-time历史凭据保留，hash读取的atime影响诚实记录；没有删除旧失败或放宽Prepare/测试检查。归档后的完整门于 **15:27:30.327803–15:33:44.046449Z** outer0/PASS，原9TRX/10主dotnet命令完整，独立审核OK、父核105/105证据文件；四归档与原证据保持保护。它解除的是当时的门禁阻塞，不证明 hook 根因或概率零；计数、命令、缓存/图形限制见 [M2 验收记录](../milestones/m2/acceptance.md)。这些具体授权已执行耗用，不授权新的归档、恢复或重跑。

## 3. 区分测试、日志显示与代理任务的失败

在修复前、仅受测提交 `98b138e` 的那次门禁中，实际共享入口已经退出1且原结果已保存，之后才发生两类收尾问题（并非修复后的新门禁）：

- 外层最终显示输出因 GBK 编码产生 `UnicodeEncodeError`；不是原测试退出1的原因。
- 报告代理达到1500000ms任务期限，正式作者报告缺失；不是实际门禁触发外层1200s超时。

恢复的独立审核确认原始证据足以判定门禁失败，但不代签缺失的作者报告，也不将工作流完成当成测试通过。排障时分别记录测试进程、包装器、代理任务的退出状态与时间线；报告超时不授权重执行已完成的测试。

## 4. 其它已知风险与运行前核对

- **分支差异与隐私**：M2 候选提交 `98b138e` 包含服务端过滤的进程采集修复；此前集成提交 `384e65b` 的 `Test-M0.ps1` 仍先获取全机详细 Win32_Process 信息再过滤。该差异是历史检查点：`98b138e` 的安全采集修复已随 `04f8e91` 本地集成。运行前仍检查实际脚本；若其他分支仍是旧采集方式，暂停运行并按授权流程处理，不能为了预检而先执行它。全局 Godot 检查仅限服务端 PID/Name 查询，其它详情仅限任务自有句柄或明确仓库执行器。
- **原生资源负例**：故意损坏 profile 的测试会产生解析／加载 ERROR，历史 TRX 将其记录在 `ResultSummary/Output/StdOut`，并非对应 case 的 stdout。结合选中URI、负例定义、退出码与用例结果核对；不能仅凭 ERROR 字样判框架崩溃，也不能把所有 ERROR 一律豁免。
- **Main退出清理**：候选修复 `5539426` 处理初始化早期失败时断开尚未连接的焦点信号，已有定点回归证据。它与输出捕获崩溃不是已证明的因果关系，应用范围也应核对当前分支。
- **音频**：历史桌面启动出现 WASAPI `ERR_CANT_OPEN` / `init_output_device error.` 后回退 dummy。图形启动成功或后续某次 stderr 为空均不能证明音频已验证；按实际验收范围保留此限制。

## 5. 远端 CI 跨盘夹具失败与 #19 回归

**历史失败**：[CI39731421949](https://github.com/fanxiao92/godot-pal/actions/runs/39731421949) 在 `e1bc730` 首次推送后失败；Prepare51pass/1fail，失败用例为 `Project_contained_input_is_rejected_unchanged_before_Godot_can_start`。异常是 `Directory.Move(Input, projectInput)` 抛出的 `Source and destination path must have identical roots. Move will not work across volumes.`，发生在fixture准备阶段、正式RunPrepare/断言之前，不是输入被产品按预期拒绝。人工PNG/JSON由 `M1Bundle.Write` 生成，不依赖未上传的原版数据；也不同于本页hook/sessionabort或旧零字节scene解析错误。

**获准修复与因果验证**：[#19](https://github.com/fanxiao92/godot-pal/issues/19#issuecomment-6335139243) 只改 `PreparePathSafetyTests.cs`，直接在工程内目标位置写入人工包，并在finally中删除本用例GUID目录；保留全部原断言、52项Prepare计数及产品路径安全政策，不改生产代码、依赖或CI配置。原代码同根PASS1、临时SUBST不同根RED1且异常一致；修复后原case不同根GREEN1，16项路径安全测试正常根/不同根各16PASS，无skip。临时映射已移除；SUBST仅模拟不同DOS路径根，不声称两物理盘，亦未启动本地Godot或重跑本地完整门。

**后续独立完整门**：修复 `a76f83b` 通过SSH推送main后，新CI39738074108自动执行并成功；父核51份原件/9TRX/10主命令、Prepare52及原失败FQN明确Passed，Game170×3、发现173、三个精确负控制、原生prepare/profile/import/Main启动和最终受限进程快照均符合预期。CI原件记录人工输入C:与工程输出D:。#19已关闭并回填父票；这是新运行证据，不改写失败CI、原源码或本地RED记录，也不新增音频/GPU人工/导出/其他平台验收。

本页不提供统一的“已知错误忽略列表”。每次新失败仍需核原始证据；未经确认的原因、未执行的检查和尚未验证的能力应分别标明。
