# GitHub-backed autonomous coding（Pi Agent SDK）

**人维护 GitHub 计划；程序循环启动 fresh coding session。** 不需要手工准备逐票 JSON、文件白名单、依赖收据配置或 `init`。入口与 autonomous-coding demo 对齐：指定同一个任务来源启动、查看进度，正常暂停后用同一命令继续。区别是计划在 GitHub Issues，而不是本地 feature_list.json。

## 最短使用路径

首次安装本工具的锁定依赖（不改 Pi 全局安装，不是游戏运行/CI 的前置）：

```powershell
npm ci --prefix tools/long-running-agent --ignore-scripts
```

在实际业务仓库中，先看父票及所有嵌套叶子的实时状态；这一步**不调用模型、不修改源码、不创建运行状态**：

```powershell
node tools/long-running-agent/cli.ts status --issue 126
```

确认实际集成基线正确、工作区干净且没有其它 writer，再启动：

```powershell
# 允许本批在检查/独立评审后做本地提交，并继续下一张可执行票。
# 绝不 push、merge、认领或关闭 GitHub Issue。
node tools/long-running-agent/cli.ts run --issue 126 --commit
```

这是一次明确的执行授权：消费所选父票下运行时已批准、无认领且依赖可核实的叶子，包括运行期间新发布并符合条件的叶子；不扩大到父票之外，不自动拆票或批准新需求。没有可执行叶子时退出，不轮询等待新票。

第一次真实模型试运行建议只跑一个新 attempt：

```powershell
node tools/long-running-agent/cli.ts run --issue 126 --commit --max-iterations 1
```

`--issue` 也接受完整的 `https://github.com/fanxiao92/godot-pal/issues/126`。使用现有 Pi 登录、provider/model 和项目/全局 AGENTS.md；不再填写 sdkRoot、agentDir 或 state 路径。SDK 作为局部依赖固定为 1.5.0，不自动升级或切换版本。

**当前仓库有未提交工作时，不能直接开跑。** 工具不擅自提交、stash、覆盖或忽略已有改动。应先由操作者确定需要包含哪些代码的实际干净基线。另一个已准备好的业务工作树可以直接指定：

```powershell
node tools/long-running-agent/cli.ts run --issue 126 --cwd C:/worktrees/approved-checkout --commit
```

不要默认从旧 main 新建工作树就包含了当前票所需的前置能力。

## 它每轮做什么

1. 从 GitHub 读取所选父票、原生 sub-issues 和嵌套子票；只有显式 task-list 子票可以作为回退，绝不把正文里所有 `#123` 引用猜成子票。回退会显示警告。
2. 寻找 `ready-for-agent`、OPEN、未分配、没有子票的实施叶子。按可执行依赖、再按票号选择，不直接实现父规格。已分配票不自动接管。
3. 读取该票、祖先规格及直接依赖的**实时正文和全部评论**。GitHub closed 只是远端进度，不是本地集成证明；模型必须从 GitHub 验收依据找到实际祖先提交，并通过 `verify_dependency`，否则不允许编辑或测试。
4. 启动 fresh implementation session：协调者维护批准契约、公共测试 seam、前置与验收，不先代写业务。实施者沿调用链核对旧拒绝域，先迁移失效的旧测试契约并新增独立能力用例；通过 `declare_red` 冻结新行为、公共 seam、测试/fixture/旧契约迁移路径及精确测试身份/字面诊断，取得原生真实 RED 后才写生产，再按契约选择完整 Unit 或已授权的 Prepare/Game 范围取得当前源码 GREEN。
5. 原生检查成功且绑定当前源码后，分别启动独立 fresh Standards / Spec 会话；两者不继承实施者或彼此的 verdict。正常完整失败可按[有界纠正单源](../../docs/agents/task-execution.md)定位修复；问题身份、轮次与停止条件均以该单源为准，fresh attempt不重置。
6. 再核对源码、Issue/父规格/依赖未变。精确候选通过后，只有本次命令含 `--commit` 才自动本地提交；无改动但已存在的能力也须有当前检查及双轴评审，不制造空提交。
7. 更新本地执行索引、简短 progress.md 和原始证据，创建下一张票的新会话。不会把前面几十张票的完整对话传下去。
8. 所有叶子均已远端关闭或本地交付后，额外启动只读父规格审查，核对全部嵌套条款、跨叶消费者和实际运行证据。父规格缺口显示 blocked，不自动发布/批准新票。`specStatus=passed` 才是父规格本地业务收口；叶子统计、GitHub closed和退出0不代表main合入、全门或里程碑验收。

中央状态只保留源码身份、attempt/证据引用和短摘要；完整源码文件图留在各 attempt 的冻结输入/gate 原件里，不随票数增长反复加载进主状态或 agent 上下文。

## 查看进度（status）

`run` 的终端会实时显示当前阶段、agent 文本、工具开始及完成/阻塞摘要；完整会话由 SDK 持久化。另开终端随时查询：

```powershell
node tools/long-running-agent/cli.ts status --issue 126
node tools/long-running-agent/cli.ts status --issue 126 --json
```

包含：

- 叶子总数、GitHub 已关闭数、本地已验证/集成数（不重复计数）。
- ready、依赖等待、未批准、已分配、工作中、等待提交、blocked、interrupted/未知 owner、集成或证据漂移。
- 当前/最后观察到的阶段、工具、时间和 session 路径；PID 不存在时不把旧 working 冒充活跃执行。
- 各票阻塞原因、前置待核实项、原件目录、会话引用和下一步操作提示。
- 父规格 `specStatus`、具体覆盖缺口/审查摘要和原件目录；父审查原件漂移不复用旧通过，显式 `--resume` 才可建立新审查。

**“43/60 leaves accounted for”不是“43 张票已在本地验收”**。其中 GitHub closed 与 local completed 分列；也不代表父规格/里程碑完成。

默认 status 实时读取 GitHub，不静默使用旧数据。已启动过运行且网络不可用时，可明确查看保存的快照：

```powershell
node tools/long-running-agent/cli.ts status --issue 126 --offline
```

输出显著标记 `OFFLINE / STALE` 和原快照时间；本地状态仍是当前读到的值。首次纯在线 status 不会生成缓存或其它状态。

默认目录自动选择为 `<Pi agentDir>/long-running/<checkout-hash>/<root-issue>/`。不同 checkout 和父票不会混用；status 会打印实际路径。Git common directory 下的 writer lock 复用既有排他机制，跨父票/工作树的 Host 也不并行写同一仓库。

## 正常暂停与继续

在 `--max-iterations` 边界结束后，重新执行同一个命令即可继续；不恢复旧对话，而是根据 GitHub、Git 和磁盘原件创建新会话。

如果不希望自动提交：

```powershell
node tools/long-running-agent/cli.ts run --issue 126
```

候选通过检查和评审后显示 `awaiting_commit`，不推进下一张票。人工审阅并按批准流程提交**精确候选**，然后重跑同一命令即可；不再要求手工编写 receipt 文件或运行 accept。也可以随后明确加 `--commit`，让 Host 提交已经冻结且未变化的候选。

手工改动过候选、父规格/评论变化、旧 gate 缺失或被修改时不会复用旧通过；需要重新核实并显式启动新 attempt。

## 失败、中断与恢复

不照搬 demo 的无条件自动重试。新记录持久保留 RED 责任：没有声明前拒绝源码写入，声明后、精确 RED 证实前只允许该阶段的测试/fixture/旧契约迁移路径。路径不按生产/测试目录硬编码分类、没有文件数上限，也不是 RED 后的永久授权白名单。`unit_test` / `range_test` 的 `expectedFailure` 必须与冻结声明一致；原生分类器继续负责精确身份/字面诊断匹配。Host 拒绝缺 RED 或只有 RED 之前 GREEN 的变更候选，repair / resume 不重置声明或责任，不能靠 dirty / 新 attempt 豁免。无改动的能力复核仍用当前检查及独立双审，不制造伪 RED。

已批准的历史记录（例如原 #207）没有这一新增责任字段，保留原候选及原证据，不回滚生产或追补假 RED；这不是新记录的豁免入口。模型没有历史证据写工具，`check_evidence` 只读原件。

事前声明精确测试身份/字面诊断的 RED 命中后可继续实现；其它正常完整的编译/断言/fixture失败，先读原件定位，通过 `request_correction` 记录原因、实质修正和不变契约后才允许写入/必要重验。没有源码修正不重复原检查或审查。两轴的同契约 `changes_requested` 须提供结构化 `findings: [{ problemId, evidenceKey, cause, evidence: [...] }]` 才能自动回实施修复，禁止从摘要猜问题身份。`evidenceKey` 使用brief提供的共同original源码身份加 `:规范源码路径#稳定契约锚点`，不使用各review会话目录；同finding两轴合并登记，原件路径保留在 `evidence[]`。`request_correction` 使用同形 `findings` 加 `change/contract`，host绑定证据与ID，按问题持久计数与验证结果。后续证据是否同根因须由诊断/独立审查确认；不确定则停止请求决定，host不声称算法能识别所有语义改名。

崩溃、中止、超时、未知退出/副作用、结果不完整、provider失败、额度耗尽或新业务决定仍保全停止。恢复不等于重置额度，不能事后把已观察的失败改签为RED。

普通 Unit 的失败分类沿用原生会话结果和计数，不套用 Prepare/Game 的逐案例 TRX 身份图：xUnit theory 可能用同一 testId/definition 记录多个 execution。正常断言失败反馈包含具体测试、原始断言及 `failure-classification.json`。分类器/报告校验失败仍 blocked，并保留原生退出码、底层错误、TRX/日志路径；它本身不等于进程静默未知，不单因分类失败设置 unknownQuiescence。真实中止/崩溃/timeout/未知退出仍维持原硬停止和锁恢复要求；Prepare/Game 的强身份检查不变。

先 `status`，检查相应 session、检查日志和 Git 副作用。已知失败处理完成后，显式批准新会话：

```powershell
node tools/long-running-agent/cli.ts run --issue 126 --commit --resume
```

旧 owner 退出、检查超时或本地提交结果不明时，先确认任务自己的进程/后代、writer 和副作用均已核实，再：

```powershell
node tools/long-running-agent/cli.ts run --issue 126 --commit --resume --confirm-quiescent
```

不要求手填源码散列；上述命令明确绑定当时实际观察到的当前源码并留证，保留原单一行为契约和纠正账目。`--extra-corrections` 现在明确拒绝，旧授权仍留作证据，不扩问题上限。不会恢复旧模型对话、复用失败检查或重复一个已经确认完成的提交。仍有活跃 PID、锁来自另一执行目录或身份不明时拒绝恢复，不结束未知进程。PID 复用也按活跃处理。

遗留 `corrections` 没有问题映射时安全阻塞，不自动迁移。parent-only `GitHubHost.migrateCorrections({ issue, stateHash, sourceFingerprint, approval: { file, sha256 }, mappings })` 仅供有明确授权的协调者调用，不暴露给模型工具或自动run选项。`stateHash` 是原 `github-state.json` 字节SHA256；`sourceFingerprint` 是当前 `snapshot` 身份；approval为树外原授权文件。mappings逐一覆盖全部旧条目的 `index`，含结构化 `findings` 与明确 `outcome: passed|failed`；一条可映射多个实际问题。未开始条目改为 `notStarted: { file, sha256 }`，须有树外原件证明且当前源码仍等于该条目失败源码。迁移保留原条目/原件，单列映射且不可由agent重映射刷新次数；已开始旧轮计入实际问题，不复制旧整票总数。运行中/未知锁、身份变化、缺项/重复项或第二次迁移均拒绝。此入口不批准恢复、清锁或引擎运行。

Ctrl+C / SIGTERM 请求取消并等待 SDK 停止；若中断原生命令，后代静默未核实则继续保留 writer lock。**不会凭超时自动抢锁。**

## Fresh session 环境与规则

每次 GitHub implement / Standards / Spec 会话，host 从本进程采集 OS/platform/release/arch、cwd、Node 版本/可执行路径和实际工具名；不运行版本探针、不转储环境变量或探测未授权引擎。工具名说明能力，不代替任务授权。必要执行规则单源见[执行环境契约](../../docs/agents/execution-environment.md)。

SDK `agentsFilesOverride` 保留原有 AGENTS 发现，再把契约全文和当次环境记录加入实际 system prompt；会话目录保存 `environment-contract.txt` 与 `environment.json`（含契约来源及 SHA-256），可经 `check_evidence` 读取原件。不改变模型选择、fresh、受限工具或停止规则；旧显式计划模式不扩展。

普通 pi-subagents worker 走项目 `.pi/settings.json` 的必要逐字段 override，保留内置 `context.md` / `plan.md` 与 fresh、模型、工具，并增读环境契约；不继承私人 global context。需信任项目配置并重载/新建会话。这里的 `defaultReads` 是存在文件的 `[Read from: ...]` 读取指令，不是全文注入，也不是不可绕过的执行限制；项目 AGENTS 另有开工必读指针。

## 模型工具与权限

- `declare_red` 仅登记当前叶的 behavior、seam、paths、expectedFailure；阶段声明跨 repair / resume 冻结，不授予额外路径或执行权限。先迁移旧契约测试，再新增能力 RED；编译错误、旧断言冲突、未匹配失败、零执行或异常不能冒充该 RED。
- 提供受保护的源码 read/list/search/edit/write 和固定的只读 Git 检查；没有任意 shell、MCP、子代理、扩展调度器或远端写工具。
- 不由人预列每个可改文件，也不设文件数硬上限。超过10文件是聚焦/完整性风险提示：说明为什么属于同一契约及拆分后的中间状态；生产者、消费者、测试、样例、注册与必要文档不能为凑数漏迁。授权路径/行为/权限仍是硬边界。
- 拒绝工作树外路径、链接、隐藏 index 标志、忽略文件写入、常见凭据路径、`.git`/`.pi`、真实 raw/派生资产及游戏 Assets；不把真实数据发给模型。
- `unit_test` 沿原生 dotnet CLI，Core/CLI明确工程、`tests/unit.runsettings`、隐式restore/build、全工程或单个 `FullyQualifiedName=`/`~` 方法、类前缀筛选/新日志/TRX，不接受 DisplayName 或 Boolean 组合筛选。只有当前源码真实成功才记录通过；预期RED和正常失败单列，不当通过。Unit失败只核原生完整会话/计数及必要的指定RED身份，不要求逐案例一对一图；Prepare/Game失败继续复用既有 `Read-TestRunIdentity` 强检查，不另造平行图解析器。
- `range_test` 仅在显式引擎授权后提供，调用既有 `scripts/Test-Range.ps1`：Prepare整层、Game既有正向批次及单个FQN方法/类前缀，保留原发现/身份/诊断/清理契约，不能传DisplayName、任意Boolean筛选、超时放宽或跳过guard。
- Engine运行另持OS临时目录下的全局锁；Windows仅查Godot PID/Name，有任何现存进程先停。异常后保留锁，显式静默确认只解除本执行目录且owner已退出的锁。全局锁只协调本Host，不能替代与外部Godot运行者的唯一执行确认。当前未实现其它平台的引擎PID守卫。
- 私有真实资源、GPU、人工、远端CI和完整门不自动化执行；需要它们时blocked，不能用Unit、headless或评审代签。
- 没有 OS sandbox：批准的 dotnet 测试会执行仓库代码，批准的 git commit 会运行现有 hooks；工具路径守卫依赖唯一 writer，不能抵御恶意并发文件替换。运行前必须信任仓库和命令。

Prepare/Game 的显式授权示例（复用已批准的引擎与已验证固定包，仅走既有入口的固定包消费，不重建/升级工具链或准备真实资源）：

```powershell
node tools/long-running-agent/cli.ts run --issue 126 --commit --engine-checks `
  --godot-path C:/approved/godot.console.exe --fixed-package-directory C:/evidence/verified-gdunit-bundle
```

可选高级参数有 `--provider/--model`（必须成对）、`--max-turns`（每会话，默认 100）、`--session-timeout-ms`（每会话，默认 30 分钟）、`--state`。普通使用不需要配置它们。没有全局 token/账单预算，不承诺服务商费用硬上限。

## 证据与退出码

```text
<automatic-state-directory>/
  github-state.json             # 精简执行索引，不是第二套规格/任务系统
  github-snapshot.json          # 上一轮 GitHub 图；offline 明确标旧
  progress.md                   # 简短本地交接，仅摘要+引用
  attempts/<issue>-<uuid>/
    input.json                  # 实时 Issue/祖先/直接依赖、完整源码图、命令授权
    events.jsonl                # 生命周期事件；不逐 token 重复保存文本
    implement/                  # brief、SDK session、report、原生 Unit 原件
    standards/
    spec/
    gate.json                   # 冻结源码、独立评审、按层检查、纠正账目和前置证明
  attempts/parent-<uuid>/
    input.json / intent.json    # 当前源码、完整嵌套规格、只读派发意图
    report.json / events.jsonl  # 父规格结论及原始会话引用；失败/中断不自动重跑
```

0：叶子均为 GitHub closed 或 local completed，且当前源码/实时规格的只读父规格审查通过；**不是main合入、全门或里程碑验收通过**。
1：失败、blocked、未知/中断执行或集成证据漂移。
2：等待提交、依赖/批准/认领，或达到本次迭代上限且还有工作。

不承诺跨 Git/GitHub/文件系统的 exactly-once 或断电事务；恢复依赖实际副作用核对。原件和工作树不自动删除。

## 验证与兼容入口

```powershell
npm --prefix tools/long-running-agent run typecheck
npm --prefix tools/long-running-agent run test:unit # tight纯策略检查
npm --prefix tools/long-running-agent test          # 含真实Git/PS/SDK接线的离线集成，不冒称快速Unit
```

普通 worker 的实际安装包配置→读取指令、SDK 项目上下文→子代理 global 过滤可另行离线核对（需显式选择已安装包，不安装依赖，不派发 child、不请求 provider）：

```powershell
$env:PAL_SUBAGENTS_ROOT = '<installed pi-subagents package root>'
$env:PAL_PI_PEER_ROOT = '<installed Pi coding-agent package root>'
node --test tests/AgentEnvironment.Tests.mjs
```

未提供上述路径时该可选安装包核对会标 skip；不把 skip 算作该接线通过。LRA 全离线回归中的既有 SDK smoke 应显式设置 `PAL_PI_SDK_ROOT` 为已安装的局部 SDK 根以避免跳过。

回归使用合成 GitHub 与自有临时 Git 仓库，覆盖发现/状态/连续推进/人工提交续跑/失败/真实进程中断/未知 writer/精简索引，并包含真实已安装局部 SDK 工具和会话接线；不调用模型、不运行 Godot。原件保存在输出所示 OS 临时目录。

独立 `long-running-agent.yml` CI运行锁定依赖、typecheck和离线回归（含已安装SDK接线，不请求模型/启动Godot）；不改变游戏Unit的依赖链。真实模型、多票真实业务、实际Prepare/Game、GPU/人工和远端CI结果仍分别执行留证。本工具不自动处理当前dirty工作树或把#136隔离候选当已合入。

旧版显式计划模式保留原安全兼容（自主纠正额度0、无引擎/父规格自动化），见 [EXPLICIT_PLAN.md](EXPLICIT_PLAN.md)，但它不再是默认推荐流程；`init --plan ... --state ...` 和旧 `run/status --state ...` 仍可使用，不能与新 GitHub 状态目录混用。
