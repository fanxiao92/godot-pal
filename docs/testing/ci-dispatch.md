# Unit-only CI、候选身份与证据

## 三个交付阶段

[规格 #69](https://github.com/fanxiao92/godot-pal/issues/69) 的 [#67](https://github.com/fanxiao92/godot-pal/issues/67) 将自动反馈改为 Unit-only，接受阶段性覆盖降低，不把 Unit 称为完整验收。

| 阶段 | 当前候选的含义 | 不可据此宣称 |
| --- | --- | --- |
| 本地 workflow candidate | `.github/workflows/ci.yml` 提供独立 `unit-check`，由本地有界契约及直接 Unit 验证 | GitHub 调度、check-run 或保护已生效 |
| 远端检查 | integration 候选已发布；opened、无提交 Ready、Ready 标题 edited、转回 Draft 及 manual 已有真实运行，剩余矩阵继续验证 | required check 已切换 |
| 服务端切换 | 尚待单独授权，配置回读及受控拒绝/允许验收见[保护操作说明](merge-protection.md#unit-only-分阶段切换67) | “Unit 通过即可合并”已生效 |

以上描述当前分支候选，不代表已经发布到 `main`。已验证的事件分别有独立原件与 run/attempt：例如 [Ready/edited](https://github.com/fanxiao92/godot-pal/actions/runs/41426078216)、[converted_to_draft](https://github.com/fanxiao92/godot-pal/actions/runs/41428519546) 和 [manual](https://github.com/fanxiao92/godot-pal/actions/runs/41430853131)。这些运行不替代 Ready 新提交、main push、负向矩阵或服务端拒绝/允许；manual 检出 integration ref，不冒充 PR merge candidate。

本地具体命令、提交、原始证据及验收结论保存在任务交接；不能仅凭本页或历史成功补签新候选。完整 spec 的 Standards/Spec 双轴评审留给最终 candidate，不在每张子票重复。

## 自动事件与工程集合

| 事件 | Unit 模式 | 执行 |
| --- | --- | --- |
| Draft PR 更新、转回 Draft | `pr-unit` | Core Unit → CLI Unit |
| Ready PR 更新、无新提交 `ready_for_review` | `pr-unit` | 同上，核对当前 merge candidate |
| `main` push | `push-unit` | 同上，核对实际 checkout |
| `workflow_dispatch` | `manual-unit` | 同上，对实际检出的 ref，不冒充 PR merge 结果 |

PR 事件包含 `opened`、`synchronize`、`reopened`、`ready_for_review`、`converted_to_draft`、`edited`。不以 Draft 状态跳过 Unit，也没有 path/filter 筛选。固定批准集合为：

- `tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj`
- `tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj`

CI 专用 `scripts/Invoke-CiUnit.ps1` 按上述顺序直接启动 `dotnet test <csproj> --configuration Debug --logger 'trx;LogFileName=results.trx' --results-directory <本轮独立目录>`，正常隐式 restore/build、普通 NuGet 缓存，不用 `--no-build`、`--no-restore`、discovery 或 solution。它只补 CI 所需的固定集合执行/采证，不是新的 agent 默认入口；agent 仍按[直接 Unit 协议](unit-entry.md#默认反馈协议)自行选择明确项目。`Test-Unit.ps1` 保留为显式维护工具。

自动 workflow 不运行 Source/Offline 工程编排、Integration、Prepare、Godot、完整门，也不下载 Godot/GdUnit。`Test-M0.ps1`、`Test-CiFullProof.ps1`、旧成本/身份工具及历史证据保留，不因保留就视为当前完整门运行兼容性已验收。恢复重型自动检查须另立任务，验证工具链、候选绑定、重复运行、指定失败 controls 与完整证据，再分别批准调度及保护切换。

## 候选身份与失败封闭

- checkout/action、setup-dotnet/action、upload-artifact/action 保持批准的固定 commit；SDK 由 `global.json` 选择，沿用其补丁策略，不自动升级依赖。checkout 使用 `fetch-depth: 2`、`persist-credentials: false`、`lfs: false`，保留直接父层而非全部历史。
- `GITHUB_EVENT_PATH` 通过 step env 传入并保存原件。`New-CiCandidateInputs.ps1` 从原事件派生 action；PR 在开始和结束分别只读 GET 核对 head/base/merge candidate/Draft，不把表达式数据拼进脚本源码。manual 的 `inputs` 允许对象或 null。
- `Test-CiCandidate.ps1` 对照实际 Git checkout/tree/有序 parents 与 run/attempt/workflow/event/action/mode/Draft。PR 必须是有序 base/head 双亲的 merge candidate，不能用 head 自测替代；非 PR 不假定只有一个 parent。错型对象、旧源码候选、旧 run/attempt 或非等价身份变化失败，不覆盖成功身份文件。
- GitHub 在 PR 重开或重新计算 merge ref 时可能返回不同的合成 merge SHA，即使 H/B、tree 和有序 parents 未变。仅 `pr-unit` 允许这一**有证据的等价重算**：实际受测提交仍是该运行的 `GITHUB_SHA`；若当前 API M 不同，再 GET 该 commit 并严格核对返回 SHA、完整 tree 及有序 `[B,H]`。`api-pull-request.json`、`api-merge-commit.json` 保留 API 身份，规范化 snapshot 的 `serverMergeCandidate` 单独记录等价关系，validator 再校验其类型/tree/parents。不能把测试提交重标为 API 新 M，也不能只因 H 相同而接受不同 tree/base/parents。缺证明或证明错误即失败；旧 Full 模式不启用此例外。
- 新的 `pr-unit`/`push-unit`/`manual-unit` 与保留的 `draft-quick`/`ready-full`/`push-main`/`manual-full` 分开；Unit 身份不能作为旧 FullProof 的完整凭据。
- executor 要求 clean checkout，绑定所有 tracked 文件的路径/SHA-256；命令前后及结束 block 再核对源码。原始命令、SDK 输出、真实数字退出、root exit 和双流 EOF 与本轮 TRX 一并保留。未知退出为 null，不能借内层/旧 `$LASTEXITCODE=0` 补成功。
- 每项目通过后才执行下一项目。非零退出、restore/build/宿主错误诊断、零执行、skip、会话中止、未知/不完整输出或缺少必要证据均阻断；后续工程保持 `not-run`。原非零退出不被报告错误改成零；原零但采证失败仍使 job 失败。不自动重试、换源、缩范围或加时限。
- TRX 校验复用共享身份图，检查 Times、唯一 Summary/Counters、Completed、无 RunInfo、全部标准 counters 和 Results/Definitions/Entries 一致性，全部 Passed 且非零，绑定预期测试程序集及当前命令时间窗；不同项目不复用 run/execution IDs。**不执行独立 discovery，因此不宣称发现集合与执行集合逐项等价**。
- 结束 block 重新校验候选，要求开始/结束/report 身份相同、两个批准项目及全部命令完整、源码清单完整、必要原件存在且 hash/长度未变。缺日志/报告有明确诊断；局部 PASS、旧轮/部分项目或缺证据不能拼成整个检查成功。

并发组仍为 workflow＋PR number（非 PR 用 ref），新运行可以取消同组旧运行。取消/timeout 没有完整证据时不能宣称成功；本地脚本无法证明 GitHub 创建 check-run 的时机、无提交 Ready 转换或旧 success 的服务端放行窗口，须另行受控远端验证。

## 成本、预算与上传

`unit-evidence/unit-run.json` 记录固定选择、候选/source manifest、SDK、cache、逐命令 argv/cwd/UTC/Stopwatch 墙钟/退出与双流状态、项目状态和原始 artifact 的相对路径/hash/长度。

- 项目命令墙钟从原进程启动前至 root exit、双流落盘及资源释放，包含隐式 restore/build；不是 TRX duration。未独立测量的 restore/build 为 null，不补零或伪拆成本。
- round 墙钟从初始源码身份留证后到最后判定/失败停止，包含 SDK、项目间检查与采证；当前内部测量在 finally 的最终 artifact 索引/终态报告写入前停止，不是完整 job/外层 host 墙钟。其余结束 API/上传成本不混入该值；需要完整端到端成本另记外层测量，不将子项相加冒充。
- 单项目 >5 分钟、round >10 分钟只告警，不杀进程、不改变功能结论、不重试。沿用每命令 root exit＋双流 EOF 共用 600000ms、仅自有进程树清理 5000ms、job 20 分钟的预算；这些 timeout 与告警分列，不是性能承诺。

`always()` 尽力上传 `ci-unit-evidence-<run_id>-<run_attempt>`：原事件、`unit-start/**`、`unit-end/**`、`unit-evidence/**`。上传失败或一个文件都没有时 job 不能成功；部分目录可为诊断上传，但不能成为完整检查凭据。新目录不复用旧 artifact，不下载旧轮补齐。不上传 token、真实游戏资源、缓存、私有 reference 输入或全机观察数据。

## 过渡期的旧 full-check

当前候选保留一个**明确抛错**的 `full-check`，不运行重型检查、不依赖 Unit 成功，也不以 skipped/neutral/空成功满足旧 required check。它会使整个 workflow 显示失败，即使独立 `unit-check` 成功；这是切换前允许的明确阻断，不是 Unit 冒充 full。

在旧保护仍生效时，不允许无保护窗口；不能凭这个 blocker 就断言旧成功不会被服务端复用。发布、实际远端事件/拒绝矩阵、required check 切换及实际合并均分别授权，完整顺序见[保护操作说明](merge-protection.md#unit-only-分阶段切换67)。

## 有界本地回归

在仓库根运行，证据目录均须全新；工程回归与产品 Unit 分开，不进入自动 Unit job：

```powershell
pwsh -NoProfile -File tests/Test-CiWorkflow.Tests.ps1 -EvidenceDirectory <new-workflow-evidence>
pwsh -NoProfile -File tests/Test-CiUnit.Tests.ps1 -EvidenceDirectory <new-trx-evidence>
pwsh -NoProfile -File tests/Test-CiCandidate.Tests.ps1 -EvidenceDirectory <new-candidate-evidence>
pwsh -NoProfile -File tests/Test-CiCandidate-Inputs.Tests.ps1 -EvidenceDirectory <new-transport-evidence>
# 按候选/FullProof兼容影响选择；人工证据，不执行完整门：
pwsh -NoProfile -File tests/Test-CiFullProof.Tests.ps1 -EvidenceDirectory <new-full-proof-evidence>
```

workflow 回归从**实际 run block**提取代码并先做 AST 检查，使用自有 Git merge、受控 API 与一次本地构建的人工 native SDK executable，覆盖事件/全项目 argv、原退出/UTF-8、失败停止、零/skip/aborted、未知启动、部分项目、旧 run/attempt/candidate、缺失/篡改证据和旧 full 拒绝。TRX 回归覆盖正常完整图与指定损坏形状。现有 `Test-CiCostInput.Tests.ps1`、FullProof/成本工具仍可按需维护；不借它们补签本次 Unit 或完整验收。

固定格式 block 提取不是 YAML parser，更不是 Actions 表达式/调度器。静态 YAML/AST 与人工控制也不证明真实 restore/build/test 每一种宿主故障或实际取消/timeout；这些限制及本地实测命令应保留在交接。当前 JSON 接口沿用本地已验证的 PowerShell 7 API，不自动升级工具链。远端 Draft/Ready/main/manual、取消、失败、缺失/不完整及错误候选的行为保持待验，不能以本地通过或保护 GET 代替。
