# CI 分流、候选身份与证据

## 交付边界

工作流为 `.github/workflows/ci.yml`，稳定最终检查名为 `full-check`。以下是实现契约及本地回归入口，**不表示 Draft／Ready／取消已在 GitHub 上验收，也不表示 main 已配置合并保护**。推送、创建 PR、手动调度、实际合并及保护配置均需另行明确授权。本地夹具、YAML 解析、PowerShell AST 和历史成功不能代替远端事件验收。

## 事件与结果

| 事件 | quick-unit | engineering-offline | full-gate | 最终完整凭据 |
| --- | --- | --- | --- | --- |
| Draft PR 更新／转回 Draft | unit＋Source 工程检查 | 不安排 | 不安排 | 不成功；跳过完整门不能成为完整成功 |
| 无提交 Draft→Ready（`ready_for_review`） | unit＋Source | Offline＋workflow 契约回归 | 唯一共享 M0 | 三条需要的 job 成功且本轮完整证据匹配 |
| Ready PR 更新 | unit＋Source | 对新候选重新执行 | 对新候选重新执行 | 同上，不借旧 head／base／候选成功 |
| main push | 可按条件跳过 | Offline＋workflow 契约回归 | 完整 M0 | 当前离线／完整证据匹配 |
| workflow_dispatch | 可按条件跳过 | Offline＋workflow 契约回归 | 对实际检出的 ref 完整执行 | 当前证据匹配，不冒充 PR merge 结果 |

PR 触发包含 `opened`、`synchronize`、`reopened`、`ready_for_review`、`converted_to_draft`、`edited`。`edited` 包含 base 目标变化；没有新 commit 不等于候选未变。Draft 的 quick 路径不下载 Godot／GdUnit，不执行 Game 或完整门。

并发组是 workflow＋PR number（非 PR 使用 ref），不是 SHA／attempt。较新运行可取消同组旧运行，取消仍应如实失败／cancelled，不自动重试。`full-check` 使用 `always()` 并依赖 **quick-unit、engineering-offline 和 full-gate**；Ready 的 quick 失败、取消、跳过或未知均不能被 full 单独成功掩盖。main/manual 仅允许 quick 的预期跳过或成功，仍必须 offline 和 full 成功。offline 失败、取消、跳过、缺结果或不同 run/attempt/candidate/code 的 proof 一律拒绝；不能只等引擎 job 变绿。Draft 最终 job 不以 skipped/success 作为凭据，因为 GitHub required checks 可将 skipped/neutral 看作满足。

这些本地条件不证明 GitHub 在排队、needs 等待及无提交 Ready 转换时何时创建新 check-run。旧 success 是否存在服务端放行窗口，必须在受控远端矩阵中检查，不能仅看 YAML 下结论。

## 候选身份链

quick、offline、full 三个 job 的 checkout 均使用固定 checkout action 的 `fetch-depth: 2`，保留当前提交及其直接父层；候选身份校验因此可读取 merge candidate 的有序两个 parent，同时继续关闭凭据持久化和 LFS。该边界是有限直接父历史，不是全历史下载。

1. 原始 `GITHUB_EVENT_PATH` 通过 env 传入并复制为 `original-event.json`，不把 branch、workflow ref 等数据插进 PowerShell 源码引号。
2. `New-CiCandidateInputs.ps1` 接收显式 repo/run/attempt/workflow/env 上下文，生成 event、metadata、snapshot。PR 使用只读 API GET 核对实际 head/base/merge candidate 和 Draft；非 PR 使用实际 checkout。`workflow_dispatch.inputs` 必须存在，允许对象或 `null`，不把合法 null 误判为不支持。
3. `Test-CiCandidate.ps1` 在本地 Git 对照实际 checkout、tree、完整有序 parents；PR 候选须是 base/head 两个正确有序 parent 的 merge candidate，不能以 head 自测替代。metadata／snapshot／event 根须为对象，单元素数组也不能自动展开冒充对象。run、attempt、workflow、event、action、mode、Draft 与快照一致，失败不写成功身份，输出不覆盖已有身份文件。
4. M0 前后分别重新生产和验证候选。CI 身份 action 来自实际已验证候选：PR 原事件 action、push 的 `main`、manual 的 `manual`，不是字面值 `event-derived`。结束 API 快照变化使旧候选失败。
5. `Test-CiFullProof.ps1` 只给当前 run/attempt/workflow/event/action/mode、实际候选及 checkout 一致的完整证据输出 `complete`。PR 必须 Ready；非 PR 保留实际 Git parent 列表，不假设 main/manual 一定是单 parent。

最小权限保持 `contents: read`、`pull-requests: read`；checkout 不保留凭据，不下载真实 LFS 游戏数据。Actions app 的同名检查不是 workflow 路径/ref 身份认证；管理员或有权限者修改工作流、改规则等残余边界由服务端保护票另行说明。

## 唯一 M0、原退出与墙钟

完整路径的 `engineering-offline` 与 `full-gate` 在不同 hosted runner 上独立开始，没有相互 `needs`。两者各自下载并校验固定四文件 GdUnit bundle；下载有单独步骤名，不把下载隐藏在 Offline 内。offline 不安装仓库 SDK 或 Godot，执行 `Test-Engineering.ps1 -Mode Offline` 和实际 workflow 契约回归；full 只调用一次 `scripts/Test-M0.ps1`，不重复 Offline，也不另造裁剪完整门。发现、Prepare、启动、Game 三轮串行分批、三个指定负控制、空缓存 restore/build 和原预算均保留，未引入并行引擎。

offline 同样在检查前后验证实际候选。`offline-proof.json` 仅在 start/end 身份语义一致、匹配本轮 run/attempt/workflow/event/checkout、非 Draft，且 Offline journal 的九个准确步骤均为数字 exit0、root exit／双流 EOF／清理已知完成时输出；数组根、错型字段、遗漏或重复步骤不能成为成功对象。proof 记录 journal SHA256；workflow 回归或上传步骤失败仍使整个 job 失败，即使前面的 proof 已生成，最终 guard 也不能放行。它不替代 M0 FullProof 或服务端保护验收。

workflow 用一个 native PowerShell host 执行唯一 M0 调用，明确 UTF-8、保留独立 `task-stdout.log`／`task-stderr.log`。取的是**原 M0 host 退出码**，不是 M0 内最后一个 dotnet 命令的 `$LASTEXITCODE`：成功内层命令后再抛出异常，不能被错记为原任务 0。启动前清除 stale exit；无法获得原数字退出时记录 `exitKnown=false`、`exitCode=null` 并失败关闭，不补成 0。

UTC／Stopwatch 只包该单次原任务（含该 native host 启动），不包括后续 API、hash、成本生成、上传，也不声称是整个 job 墙钟。原 7 不被报告／采证异常改成 0 或 1；原 0 但采证失败时，原 0 保留在 task 证据，外层工程步骤可失败，最终不发凭据。取消、崩溃或没有 finally 证据不得反推完成。

## 即时产物绑定与成本

M0 返回后先尽力写 `task-exit.txt`，随即记录原产物的 M0 根相对路径、SHA256、整数 byte length，再写 `task-measurement.json`。扫描在下降前拒绝 reparse point；既有证据根不复用、不清空。共享 M0 要求目录尚不存在，由它创建空缓存根；caller 的原两流先保留在全新兄弟目录 `m0-streams`，M0 返回后才原字节复制进证据根并绑定，不能提前创建 M0 目录。兄弟目录亦 always 上传，以保留前置失败或取消时尚未归档的原流。快照覆盖 commands、PASS、task exit、原日志/两流/TRX，发生在结束 API／成本工具之前，不混入后生成的成本文件。

测量带本轮 run/attempt/code、显式时区起止、正有限墙钟、原退出及已知性、M0 证据根与采证错误。FullProof 检查命令时间在测量外层窗内、当前 index／原产物 hash/length、完整任务状态；缺字段、类型伪装、小数长度、无时区测量、错误证据根、跨轮拼接、产物变更都不能发完整凭据。

`New-CiCostInput.ps1` 仅从当前 `commands.json` 的真实 argv 和显式 TRX 位置构造已交付 [成本报告接口](test-cost-report.md) 输入，保留原 argv；绝对/相对 results 及外部 index 路径保持绑定原证据根。未知 machine/toolchain/cache/build 为 `null`，不伪称某 hosted image、空缓存或已知构建环境。quick 的原 unit/Source 记录保留各自时耗；这些阶段耗时不能相加冒充完整 job 墙钟。

成本报告用独立 native CLI 捕获真实数字工具退出与原 UTF-8 输出，不能用 PowerShell 对象管道捕获 `[Console]` 输出而得到空报告。FullProof 再通过现有只读报告器重新生成，以内置 `JsonNode.DeepEquals` 比较语义 JSON，不重复实现 TRX 解释器，也不因字段换序误拒绝。

报告工具 exit 0 只说明报告生成，不代表原任务通过。指定负控制的 Failed 行仍失败；严格满足成本分类时任务可为 `completed-with-expected-failures`，但负控制身份／诊断契约仍由唯一 M0 验收。历史真实资料只能证明读取兼容性，不能当本次 CI 测量或优化效果。

## 成功／失败上传

artifact 名称同时带 run ID 和 attempt，手动重跑不覆盖或混用上一 attempt 的包。always 尽力上传原事件、候选生产输入和 start/end proof、原身份、task exit/measurement、M0 logs/两流/TRX、成本 index/report/错误日志及完整 proof；full-gate 的 acceptance evidence 还上传 `m0-evidence/boundary-tests/**`，保留各 boundary 测试的原始日志、双流、命令、结果和监督记录。该显式子树不包含 `packages` 或 `http-cache`，也不扩大为整个 M0 根目录。报告／上传失败单列工程失败，不能改写原测试退出和日志；缺报告/产物不生成 PASS。步骤还没产生文件时允许上传器明确警告，不将缺失当完整。

Offline 和 workflow 回归证据独立上传为 `ci-offline-evidence-<run>-<attempt>`，包含离线 job 的原事件、start/end 候选输入和 proof、`offline-proof.json`、`offline-evidence/**`、`workflow-evidence/**`；不从另一个 runner 复用旧目录或下载旧 artifact 补齐。full-gate 原验收 artifact 保留其 M0 原件，移除已迁出的 Offline 路径。

只上传任务自有工程证据；不上传 token、真实游戏资源、reference 私有输入或全机观察数据。日志和快照不构成被加载字节的绝对认证。

## 本地回归与剩余验收

在仓库根用当前 PowerShell 7 工具链运行，各 `-EvidenceDirectory` 必须是全新路径；建议放仓库外，原失败证据不清理：

```powershell
pwsh -NoProfile -File tests/Test-CiCandidate.Tests.ps1 -EvidenceDirectory <new-candidate-evidence>
pwsh -NoProfile -File tests/Test-CiCandidate-Inputs.Tests.ps1 -EvidenceDirectory <new-transport-evidence>
pwsh -NoProfile -File tests/Test-CiCostInput.Tests.ps1 -EvidenceDirectory <new-cost-input-evidence>
pwsh -NoProfile -File tests/Test-CiFullProof.Tests.ps1 -EvidenceDirectory <new-proof-evidence>
pwsh -NoProfile -File tests/Test-CiWorkflow.Tests.ps1 -EvidenceDirectory <new-workflow-evidence>
```

候选夹具使用自有 Git 和受控 API stub；FullProof 使用真实公开成本工具和明确人工 TRX，不执行 SDK/引擎/M0。workflow 回归按本仓库固定 step/indent 提取**当前实际 run block**，先 AST 后执行自有 fake M0：单次调用、原 7、UTF-8 两流、未知启动、内层 0 后异常、采证写入失败、即时快照 tamper、真实成本/FullProof 互通及最终 guard；另有自有 Git 的实际 manual 候选生产→消费链。回归还提取实际 offline start/end/proof block，验证自有 shallow Git/API 的候选→journal→proof 链、完整 journal 与负例，以及最终 guard 对 offline job 各种失败/缺失/错配的拒绝；该回归本身接入 offline CI，证据独立保留。固定格式提取不是 YAML parser，更不是 GHA 表达式/调度器。

FullProof 回归含三合法完整模式、候选/报告字段换序正例和明确阶段的有限单变量负例；每次保留独立调用参数、输入副本和两流。当前 JSON 接口使用 `NoEnumerate`、`DateKind String` 与内置 `JsonNode.DeepEquals`；本地现有运行环境已验证，较旧 PowerShell 7 minor 不在已验证兼容性范围，不为测试自动升级工具链。

发布前还需匹配当前字节的 YAML/PS 检查与独立 Standards/Spec review。实际 Draft 更新、无提交 Ready、Ready 新候选、main/manual、取消与旧成功错配必须在明确授权的远端计划下留证；服务端保护配置及普通合并拒绝/允许验收单独进行。本文件不授予执行这些动作的权限。
