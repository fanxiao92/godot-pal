# 原生 Unit CI 与证据边界

## 默认调用链

自动反馈采用 [ADR-0003](../adr/0003-native-unit-feedback.md) 的原生基线：checkout → setup-dotnet → HEAD/GITHUB_SHA 比较 → 两个独立 `dotnet test` step → artifact。没有 Python、CI 专用执行器、候选 JSON 协议或外部审计前置。工作 agent 按[短指南](getting-started.md)自行选择项目，不需要学习 CI 编排。

| 事件 | 执行 |
| --- | --- |
| Draft/Ready PR 的 opened、synchronize、reopened、ready_for_review、converted_to_draft、edited | Core Unit → CLI Unit，不按 Draft 状态跳过 |
| `main` push | 同上 |
| `workflow_dispatch` | 同上，对手动选择的实际 ref，不冒充 PR merge 结果 |

固定工程为 `tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj` 和 `tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj`。每工程独立 step，直接 `dotnet test <csproj> --configuration Debug --settings tests/unit.runsettings --logger 'trx;LogFileName=results.trx' --results-directory <本轮目录>`；无 filter/path 筛选、独立 discovery、solution、`--no-build` 或 `--no-restore`。前项失败后后项按 Actions 默认成功条件不执行，上传仍尽力运行。

同一 workflow 的 `full-check` job（#76 起）独立调度：仅 push(main)、PR `ready_for_review` 动作与显式 `workflow_dispatch`，绝不 per-synchronize；以 `Test-M0.ps1 -Scope committable` 执行可提交范围完整门（真实退出码、TRX 身份断言、三轮合成批、失败控制，与本地同一入口与超时钉），引擎由 `Get-Godot.ps1` 钉死校验下载，GdUnit 由 Test-M0 内建 bootstrap 准备。范围界定、私有资产边界与转 required 步骤见[工程检查](engineering-checks.md)；它与 unit-check 无 needs 关系，artifact 为 `ci-full-evidence-<run_id>-<run_attempt>`。

Unit 自动路径（unit-check job）不运行 Source/Offline、Integration、Prepare、Godot 或完整门，也不下载 Godot/GdUnit；它们继续是显式选择（full-check job 是例外，见上节）。

## 工具链、身份与失败

- checkout/setup-dotnet/upload-artifact 沿用固定 commit。checkout 保持 `persist-credentials: false`、`fetch-depth: 2`、`lfs: false`；SDK 由 `global.json` 与 dotnet 解析，实际 `dotnet --version` 留在日志，不重复实现补丁匹配。
- 实际 HEAD 必须等于运行的 `GITHUB_SHA`。PR 保留 Actions 的 merge checkout 语义，不指定 PR head；main/manual 核对自己的运行 SHA。记录以 GitHub 的 run/attempt 和日志为准，不在开始/结束查询实时 PR、Draft 或另一个 merge SHA，也不再生成 `pr-unit`/`push-unit`/`manual-unit` 快照。
- 每项目结果目录必须尚不存在；命令原生非零退出原样传播，不让随后文件检查覆盖它。`tests/unit.runsettings` 的 `TreatNoTestsAsError` 和 `xUnit.FailSkips` 使零执行与 skip 由现有 VSTest/xUnit 拒绝。
- 原生退出 0 后还检查该工程的本轮 `results.trx` 存在。中止、取消、超时、未知退出或缺报告不能当成功；不重试、不换源、不缩范围。SDK/runner 输出与 Actions 结果是默认判断来源，不额外解释每种宿主诊断。
- 并发组仍是 workflow + PR number（非 PR 用 ref），新运行可取消同组旧运行。取消不是成功，也不能用局部项目的通过补齐整轮。

原生基线**不提供**全仓文件 hash/长度清单、TRX Results/Definitions/Entries/GUID/时间窗审计、任意 RunInfo 的独立拒绝、前后 API 身份桥接或自定义 root/双流 EOF 证明。它也不证明运行中所有 tracked 文件完全未变。需这些额外保证时明确选择维护层，不能把普通 Unit 成功描述成同等独立审计。

## 日志、报告和预算

Actions 日志保留实际步骤输出和退出；artifact `ci-unit-evidence-<run_id>-<run_attempt>` 仅上传两份本轮 TRX：`unit-results/core/results.trx`、`unit-results/cli-unit/results.trx`。不生成 `unit-run.json`、`unit-start`/`unit-end`、原事件副本或第二份权威报告。`always()` 尽力保存失败现场，`if-no-files-found: error` 拒绝空上传；有部分产物不表示两个项目通过。不上传缓存、真实资源或 token。

单项目 step 10 分钟，job 20 分钟，交给 Actions 管理超时；不保留自定义命令 600000ms + 清理 5000ms/EOF 的实现，也不宣称两种机制完全等价。成本使用 Actions step/job 的可见时长并标注口径，不再要求每轮额外记录精确命令/round Stopwatch 或 5/10 分钟告警。TRX duration 仍不是 restore/build 的端到端墙钟；未知分项不补零。本地宿主的执行预算由调用者记录，不从 CI 时长推断。

## 保留工具与历史

`scripts/Invoke-CiUnit.ps1`、`New-CiCandidateInputs.ps1`、`Test-CiCandidate.ps1`、共享 TRX reader、`Test-Unit.ps1` 和完整门仍保留，但不被当前默认 Unit workflow 调用。旧模式和报告的严格契约没有改成原生基线；显式使用时仍按原接口核验。用途见[脚本清单](script-inventory.md)。

[#69](https://github.com/fanxiao92/godot-pal/issues/69)/[#67](https://github.com/fanxiao92/godot-pal/issues/67) 在 [PR #70](https://github.com/fanxiao92/godot-pal/pull/70)、[PR #72](https://github.com/fanxiao92/godot-pal/pull/72) 的原交付及远端控制是历史事实；包括 merge 等价修复、负控制 PR #71 和 [main push 41444146011](https://github.com/fanxiao92/godot-pal/actions/runs/41444146011) 的独立核验。不改写旧原件，也不要求复跑。**这些成功不补签本次原生 workflow 的远端运行或已退出默认路径的审计保证。** 修改本地文件不等于已发布；新候选的远端结果必须单独按提交确认。

required check 仍为 `unit-check`；本次不修改服务端保护。#76 已把 `full-check` 从 #69 的过渡显式拒绝恢复为真实的 informational committable-scope 完整门（非 required；调度、范围界定与两步上线步骤见[工程检查](engineering-checks.md)），其失败如实红但按两步上线设计不阻塞合并。配置及普通拒绝/允许历史、权限限制和未来恢复条件见[保护说明](merge-protection.md)。

## 显式本地回归

```powershell
pwsh -NoProfile -File tests/Test-CiWorkflow.Tests.ps1 -EvidenceDirectory <全新目录>
```

回归提取实际 run block 并先做 AST 检查，在自有 Git/人工项目中使用真实 dotnet 与仓库固定 xUnit 包验证：正常两工程、指定测试失败/skip/零执行及后项停止、缺失 TRX、拒绝已有结果、checkout 错配和旧 full 拒绝。缺报告控制只替换 logger 为 console，验证真实成功测试之后的原报告存在性检查；不模拟 SDK。此维护回归不进入自动 Unit job，不计入产品 Unit。

固定缩进的 block 提取不是 YAML parser 或 Actions 调度器；本地测试不能证明真实事件调度、并发取消、artifact 服务或服务端合并行为。`Test-CiUnit.Tests.ps1`、Candidate/Inputs/FullProof/成本工具回归保留为强审计维护能力，不再是默认原生反馈的前置。
