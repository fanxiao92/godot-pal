# 长任务当前状态与指定 RED/GREEN 的只读分类

在协调多票任务、需要指定 RED/诊断核对时使用。本工具不运行测试、不采集进程、不写入状态、不提交或发布；不是默认 Unit 入口。普通 Unit 仍按[短指南](getting-started.md)直接运行。任务组织及有界恢复见[长任务信息分工](../agents/large-task-handoffs.md)。

## 调用与边界

```powershell
pwsh -NoProfile -File ./scripts/Test-AgentEvidence.ps1 -InputPath 'C:/evidence/current.json'
pwsh -NoProfile -File ./scripts/Test-AgentEvidence.ps1 -InputPath 'C:/evidence/unit-001/red-feedback.json'
```

PowerShell 7；相对证据路径按 InputPath 所在目录解析，与 shell cwd 无关。JSON 契约单源为 [`agent-evidence.schema.json`](../../scripts/agent-evidence.schema.json)，固定结构的未知字段及重复 JSON 属性拒绝；scope、receipts 等描述性对象保留扩展字段。ISO 日期保持字符串及原精度。xUnit 结果/定义/entry 身份复用 `TestRunDefinitions.ps1` 的 `Read-XUnitTrxIdentity`，不重建解析器；允许多个 Theory 案例共享 testId，以 executionId 和 entry 配对识别执行。

输出为短 JSON：`status=valid/failed/incomplete`、分类和失败原因。退出 0 仅说明**给定输入的结构和分类成立**；1 为已知不匹配；2 为给定 native 记录含未知退出、超时、流/清理不完整。指定 RED 的原 native 退出 1 与分类器退出 0 是两件事，不能混用。

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

## 回归与接线

```powershell
pwsh -NoProfile -File ./scripts/Test-PowerShellSyntax.ps1
pwsh -NoProfile -File ./tests/AgentEvidence.Tests.ps1 -EvidenceDirectory 'C:/evidence/new-agent-evidence-regression'
```

回归从中文/空格 foreign cwd 调用真实公开检查器，使用合成 JSON/TRX/日志。覆盖 RED/GREEN、FAIL 回显、warning/编译错误、身份共享/重复、计数/时间/缺报告、UTF-8、无权状态及历史散列/日期保真。子分类器的预期非零不是回归失败；每例实际 argv/cwd/数字退出及原输入输出保留。显式 EvidenceDirectory 必须全新；失败不删除，默认成功仅清理自有临时目录。

以上为 SOURCE/OFFLINE 检查，不是实际 Unit、Game 或 M6 验收。`Test-Engineering.ps1 -Mode Offline` 与 CI 的接线属于 #169 后续叶；本叶尚未接线或运行（NOT RUN）。普通 Unit 默认入口保持原生 `dotnet test`，真实远端执行仍以对应 CI run 为准。
