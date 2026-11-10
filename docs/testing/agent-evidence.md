# 长任务当前状态与指定 RED/GREEN 的只读分类

在协调多票任务、需要指定 RED/诊断核对时使用。本工具不运行测试、不采集进程、不写入状态、不提交或发布；不是默认 Unit 入口。普通 Unit 仍按[短指南](getting-started.md)直接运行。任务组织及有界恢复见[长任务信息分工](../agents/large-task-handoffs.md)。

## 调用与边界

```powershell
pwsh -NoProfile -File ./scripts/Test-AgentEvidence.ps1 -InputPath 'C:/evidence/current.json'
pwsh -NoProfile -File ./scripts/Test-AgentEvidence.ps1 -InputPath 'C:/evidence/unit-001/red-feedback.json'
```

PowerShell 7；相对证据路径按 InputPath 所在目录解析，与 shell cwd 无关。JSON 契约单源为 [`agent-evidence.schema.json`](../../scripts/agent-evidence.schema.json)，固定结构的未知字段及重复 JSON 属性拒绝；scope、receipts 等描述性对象保留扩展字段。ISO 日期保持字符串及原精度。xUnit 结果/定义/entry 身份复用 `TestRunDefinitions.ps1` 的 `Read-XUnitTrxIdentity`，不重建解析器；允许多个 Theory 案例共享 testId，以 executionId 和 entry 配对识别执行。

输出为短 JSON：`status=valid/failed/incomplete`、分类、失败原因及 `classifierExitCode`。反馈保留独立的 `native` 记录（数字/null 退出、root/双流/清理完成标志）；退出 0 仅说明**给定输入的结构和分类成立**，1 为已知不匹配，2 为给定 native 记录含未知退出、超时、流/清理不完整。指定 RED 的原 native 退出 1 与分类器退出 0 是两件事，不能混用。进程 lease/执行权限不由反馈授予或续期，须另核当前协调状态。

该工具信任调用方提供的原始观察和事前批准预期，不认证日志真伪、实际命令是否运行、源码冻结或完整 discovery 集合；原命令/数字退出/双流、source binding 与批准矩阵必须另留原件。不根据本工具通过补签 clean commit、整票、Game、GPU/人工或 CI 验收。

## 当前状态

`kind=derived_execution_index_not_issue_tracker`，仅校验已有 current 文件，不另建 tracker。顶层为：

| 字段 | 内容 |
|---|---|
| `schemaVersion`, `kind` | 固定版本和类型 |
| `scope`, `integration` | 当前目标及实际集成身份；非逐片过程 |
| `active_ticket` | number、state、source、authority、lastAttempt、checks、openContracts、limits、inputs |
| `receipts` | 已验收能力/版本的收据索引；完整故事视图注明其快照版本 |
| `history` | 原 current 文件的 snapshot 路径及精确 sha256 |
| `nextAction` | action、reason、plan；旧计划引用不是执行权 |
| `updatedUtc` | 可解析的时间字符串 |

source 只有 worktree、branch、40 位 head/tree、dirty 与 binding。authority 只有 sdkRemaining、writer、runId、deadlineUtc。只有 running 可持有执行权限，且必须有 runId/期限；其它状态权限必须为 0/null。blocked 不可 dispatch。active_ticket 不允许 `completed_*` 或其它历史字段；lastAttempt 保留失败 run，不复活权限。history 的实际文件 SHA256 必须相符。

替换步骤：保存旧文件原始字节并记 SHA → 写新候选 → 运行检查器 → 再核旧文件未变 → 原子替换。任一步失败不覆盖旧文件；备份、检查结果和迁移原件保留。运行时 Git/调度器身份、期限是否仍有效、输入是否仍适用由协调者另核；schema 校验不是执行授权。

## 共享 JSON 读取

状态解析复用 `scripts/AgentEvidenceJson.psm1`（严格 UTF-8、重复键拒绝、日期字符串保真）。JSON 与 stdout/stderr 允许 UTF-8 BOM；UTF-16/32 BOM 不会触发自动转码，仍按严格 UTF-8 拒绝。源码快照与精简交接工具属于 #169 后续叶，不属于本叶已验证能力；本分类器不授予 SDK/派发权限。

### 命令账本容器读取

```powershell
Import-Module ./scripts/AgentEvidenceJson.psm1
$commands = Read-AgentCommandLedger -Path 'C:/evidence/run/commands.json'
# 无需 @(...): 单对象、单元素数组、多元素数组均返回 System.Object[]。
```

`Read-AgentCommandLedger` 接受单个 JSON object 或非空 `array<object>`，保持顺序、所有 producer 字段及日期字符串，不添加默认值。null、空数组、标量、混合数组、嵌套数组元素均拒绝；任意深度重复 JSON 键仍由严格读取器拒绝。空对象 `{}` 是合法容器项：这里**只检查容器形状**，不规定各 producer 的字段 schema，不补造退出码、EOF 或清理标志，不证明原始命令已运行或其观察可信。需要协议验证时仍须使用对应 producer 的契约与原件；不能以读取成功替代反馈分类或进程完整性证据。

### 显式无参 WaitForExit AST 规则

```powershell
pwsh -NoProfile -File ./scripts/Test-PowerShellSyntax.ps1 -Path ./scripts/AgentEvidenceJson.psm1 -RejectParameterlessWaitForExit
pwsh -NoProfile -File ./tests/Test-PowerShellSyntax.Tests.ps1 -EvidenceDirectory 'C:/evidence/new-syntax-regression'
```

默认仍为 syntax-only，输出及文件选择规则不变。显式 `-RejectParameterlessWaitForExit` 在所选 PowerShell AST 中查找 `InvokeMemberExpressionAst`：字面成员名为 `WaitForExit`（忽略大小写，含引号字面成员名）且参数数为零时，以 `[syntax:parameterless-wait-for-exit]`、文件/行/列及非零退出拒绝；嵌套脚本块同样检查。带毫秒参数的 `WaitForExit(...)`、`WaitForExitAsync(...)`、普通字符串/注释/属性读取及目录内非 PowerShell 文本不触发该规则。显式选择不支持的扩展名仍按既有 range 规则拒绝。

此规则只解析、不执行、不导入所选文件；不解析动态计算的成员名，不推断接收者类型或验证超时值，也不证明等待/EOF/清理协议完整。按需显式选范围，不默认全仓启用或修订存量 Prepare 的进程协议。本规则与合成回归仅为 SOURCE/OFFLINE 证据。

## 指定 RED/GREEN

输入示例（纯示例，不是运行收据）：

```json
{
  "schemaVersion": 1,
  "kind": "agent_test_feedback",
  "phase": "red",
  "assembly": "tests/Example.Tests.dll",
  "total": 3,
  "native": {
    "exitCode": 1,
    "rootExited": true,
    "stdoutComplete": true,
    "stderrComplete": true,
    "cleanupComplete": true,
    "timedOut": false,
    "startedUtc": "2026-11-07T13:00:00.1234567Z",
    "endedUtc": "2026-11-07T13:01:00.1234567Z"
  },
  "files": { "trx": "results.trx", "stdout": "stdout.txt", "stderr": "stderr.txt" },
  "expectedFailures": [
    { "name": "Example.Case(value: 1)", "messageContains": "approved business assertion", "stackContains": "ExampleTests.cs:line 12" }
  ],
  "allowedWarnings": []
}
```

- RED 预期原 native=1、Summary=Failed，expectedFailures 非空；每个非 Passed 必须是事前指定的 Failed，名称精确对应且消息/stack 含指定字面量；按多重集合匹配，不去重掩盖额外案例。遗漏或多余失败、skip、未知 outcome 均拒绝。
- GREEN 预期原 native=0、Summary=Completed，expectedFailures 为空，全部 Passed。
- 两者核对必需 Counters、唯一结果/entry 配对、总数、Times 和原 native 时间窗。缺报告、损坏 UTF-8、编译错误或非白名单编译 warning 阻断。allowedWarnings 是完整诊断行的字面集合，不是 warning 代码或正则；没有打印的旧 warning 不要求出现。
- RED stderr/RunInfo 可空；非空时各自必须是与实际指定失败一一对应的完整 xUnit FAIL 回显集合。RunInfo 必须为 Error 且处于本轮 TRX 时间窗。GREEN 两者均须空。未知诊断不得因含某个测试名便被接受。
- 该分类不提供案例迁移前后全集相等、原程序集未变化或任意新诊断的自动语义识别。调用方仍须核原始 stdout、事前失败原因及 source 身份；不要扩大工具结论。

## 显式 Game 反馈

使用 `kind=agent_game_test_feedback`；旧 `agent_test_feedback` 仍按上述 xUnit 契约处理。Game 的原生/时间/文件字段与上例相同；移除 `assembly`、`allowedWarnings`，增加事前独立冻结的完整 `expectedTests` FQN 数组，`total` 为该批准集合的案例数。预期来自批准矩阵或冻结的 source/discovery，不从本轮实际 TRX 生成。Game 的 `expectedFailures` 使用精确字面字段 `name`、`message`、`stack`（**不是** xUnit 的 `messageContains`/`stackContains`）：

```json
{
  "expectedTests": ["Pal.Game.Tests.Fixture.Alpha", "Pal.Game.Tests.Fixture.Beta"],
  "expectedFailures": [
    { "name": "Pal.Game.Tests.Fixture.Alpha", "message": "approved assertion", "stack": "FixtureTests.cs:line 12" }
  ]
}
```

这是 Game 特有字段的节选，不是完整 manifest。GREEN 仍须 `expectedFailures=[]`、原 native 数字 0、全 Passed 与 Completed；RED 须原 native 数字 1、Failed Summary，失败名称/完整消息/完整 stack 按多重集合准确匹配。多余、缺失或位置不符的失败不算指定 RED。复用 `GameTestBatches.ps1` 的 `Read-TestRunIdentity` 和精确名称断言；GREEN 额外调用 `Assert-GameBatch`，不另建 XML 身份算法，也不改变固有批次/预算/门禁。

Game 采用严格诊断模式：stderr 必须空、Summary 下 RunInfo 必须不存在；stdout 含独立单词 warning 或 error（忽略大小写）即拒绝，编译错误同样拒绝。没有字面 diagnostic 豁免输入，CaseMismatch 也不豁免；当前模式**不支持含这些真实警告或正常 Error 回显的原件**，不声称兼容任意 Game 日志。该限制不修改 xUnit 的既有诊断契约。

身份图可读时，短报告给出 `total`（实际）、`expectedTotal`、`missingTests`、`extraTests`；即使 native 未知或双流不完整，也保留观察到的覆盖，但不授予有效分类。合成 466 Passed / 冻结预期 474 / Failed Summary / 300000ms abort 在 GREEN 与 RED 均拒绝，列出八个缺失；stdout 中的 child 数字退出不填充未知 root native。缺报告/损坏图仍拒绝；未知 native/EOF 的状态独立为 incomplete。这里的日志扫描与合成反例都不能证明真实进程完整性。

## 回归与接线

```powershell
pwsh -NoProfile -File ./scripts/Test-PowerShellSyntax.ps1
pwsh -NoProfile -File ./tests/AgentEvidence.Tests.ps1 -EvidenceDirectory 'C:/evidence/new-agent-evidence-regression'
```

回归从中文/空格 foreign cwd 调用真实公开检查器及导出的账本读取函数，使用合成 JSON/TRX/日志。账本回归覆盖单对象/单元素数组/多对象数组的稳定数组形状、未知字段及缺失字段保真，以及 null/空/标量/混合/重复键反例；语法回归覆盖显式 AST 规则的实际调用与文本提及反例，并用副作用标记确认未执行被扫描代码。保留全部 xUnit Theory/FAIL 回显、warning/编译错误、计数/时间/UTF-8/状态回归，并覆盖显式 Game GREEN、精确指定 RED、错位置/额外失败/多重集合、独立 FQN 集、skip/缺失/abort、重复身份图、未知 RunInfo/stderr、严格诊断，以及 466/474+300000ms 合成反例。子分类器的预期非零不是回归失败；每例实际 argv/cwd/数字退出及原输入输出保留。显式 EvidenceDirectory 必须全新；失败不删除，默认成功仅清理自有临时目录。

以上为 SOURCE/OFFLINE 检查，不是实际 Unit、Game 或 M6 验收。`Test-Engineering.ps1 -Mode Offline` 与 CI 的接线属于 #169 后续叶；本叶尚未接线或运行（NOT RUN）。普通 Unit 默认入口保持原生 `dotnet test`，真实远端执行仍以对应 CI run 为准。
