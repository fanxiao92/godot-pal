# 已有测试证据成本报告（#35）

公开入口：[`scripts/Get-TestCostReport.ps1`](../../scripts/Get-TestCostReport.ps1)。这是 **PowerShell 7 离线只读报告**，不是测试入口、采证器或第二套完整门。它不执行 dotnet/Godot、资源准备、网络、HEAD 查询或进程枚举。只读取调用者明确列出的本轮命令记录、日志和 TRX；不搜索目录补齐结果，不读取旧私密 Observer。

## 调用与退出（供维护者及 #36）

先对新增/生成 PowerShell 做 AST 预检，解析错误不得执行：

```powershell
$paths = @('./scripts/Get-TestCostReport.ps1', './tests/Pal.Game.Tests/TestCostReport.Tests.ps1')
foreach ($path in $paths) {
    $tokens = $null; $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        (Resolve-Path $path).Path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
}
# PowerShell 的 native stderr 解码取调用端 Console.OutputEncoding，不能仅指定输出文件编码。
$previousEncoding = [Console]::OutputEncoding
try {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    pwsh -NoProfile -File ./scripts/Get-TestCostReport.ps1 -InputPath 'C:/my evidence/report-input.json' `
        1> 'C:/my reports/report.json' 2> 'C:/my reports/report.stderr.log'
    $reportExit = $LASTEXITCODE
} finally {
    [Console]::OutputEncoding = $previousEncoding
}
```

从任意 cwd 调用时，脚本和 `InputPath` 使用绝对路径即可。输入 JSON 内的 `commandsPath`、TRX `path` 相对 **输入 JSON 目录**；命令记录中的 `log/stdout/stderr` 相对 **commands.json 目录**。路径按字面值读取，支持中文、空格、`[1]`；不使用 glob。stdout 仅包含 UTF-8 JSON，stderr 为 UTF-8 报告工具错误；脚本在两条路径输出前明确编码。PowerShell 调用端也须按上例明确 UTF-8 接收；仅用 UTF-8 读最终文件不能恢复 native stderr 解码时已损坏的中文。直接接收进程流的调用端应按 UTF-8 解码原字节。报告器不管理输出文件；调用者必须使用新的、自有输出路径，**不可将 stdout/stderr 重定向到任何输入/原日志**。

- 工具退出 `0`：生成了报告，**不代表原任务成功**。失败、取消、缺项、损坏 TRX 也可以成功生成诊断报告。
- 工具退出 `2`：输入 JSON/命令记录不可读、格式错误、无命令、选择无效、基线报告错误等报告失败。stderr 解释原因；不输出成功 JSON，不改原记录。
- JSON 的 `toolExitCode` 与 `taskResult.exitCode/status` 分开。原命令数字退出及原始字段保留在 `commands[].exitCode/originalRecord`；未知退出为 `null`，不补 0。
- **必须先保存原门退出**，报告失败不能覆盖原测试失败或成为放行依据。例如完整门调用后立即 `$gateExit = $LASTEXITCODE`，再运行报告并保存 `$reportExit = $LASTEXITCODE`；原门失败仍以 `$gateExit` 返回。原门通过而报告失败也要单列工程失败，不把空 JSON 当凭据。报告不调用完整门，也不验证其完整覆盖/负控制诊断/启动链；权威门仍为 `Test-M0.ps1`。

## 最小输入契约 v1

输入是对**一轮已有证据**的显式索引，不是新采证格式。`commandsPath` 直接引用 `Test-M0.ps1` 的现有 `commands.json`（单记录对象及数组均可），保留字段；缺少字段的旧记录允许报告为未知。下列例子的数值/身份都是**人工示例，不是实测**：

```json
{
  "schemaVersion": 1,
  "runId": "my-own-run-2026-01-01",
  "evidenceKind": "synthetic",
  "context": {
    "codeIdentity": "explicit-checkout-and-dirty-patch-identity",
    "machine": "explicit-machine-profile",
    "toolchain": "explicit-SDK-engine-adapter-profile",
    "scope": "CLI mixed selected regression",
    "cache": "warm packages; existing outputs",
    "build": "explicit build before test; test --no-build --no-restore"
  },
  "task": { "completion": "completed", "exitCode": 0, "wallClockMs": 12000 },
  "commandsPath": "commands.json",
  "selections": [
    {
      "log": "01-test.log",
      "category": "test",
      "scope": "CLI mixed; exact project/filter in original command arguments",
      "round": "solution",
      "batch": "cli",
      "trx": [{ "path": "solution/cli.trx", "runId": "explicit-trx-run-id" }]
    }
  ]
}
```

必填数值 `schemaVersion=1`（不接受布尔、字符串或数组版本）、非空白字符串 `runId` / `commandsPath`、标量字符串 `evidenceKind`（`synthetic` 人工 / `historical` 历史引用 / `measured` 当前自有实测）、`commandsPath`、非空 `selections`。**此处 measured 是调用者的来源声明，不是报告器另做实测或认证。**

- 每条 selection 用原命令的唯一 `log` 精确关联，不依赖命令/项目位置下标。`category` 为 `restore/build/prepare/test/discovery/startup/other`。未映射命令、计划但缺少的命令均阻止完整成功。准备/构建必须单列命令；没有独立测量时无法从大命令拆出其内部成本。
- `scope/round/batch` 可省略并保留未知；不要从当前 HEAD、文件名、目录位置或固定案例总数补身份。仅对真实分批命令填写 batch；旧未分批命令不会虚构批次。不同轮用不同 round。
- `trx` 是每个命令明确列出的本轮产物，`runId` 可选但推荐固定。`test` 类别及真实 `dotnet test`（不含 `--list-tests`）缺 TRX 时不成功；缺键、null、空数组或无有效 path 的引用均不算已有结果；一条旧 solution 命令可列多份 TRX，新拆分程序集可对应各自命令/TRX。不限制程序集名或总数。
- TRX 使用已有 `Read-TestRunIdentity` 核验 results/definitions/entries；案例以 testId/executionId 关联，**同显示名不同 ID 保留为不同案例**。记录 assembly/class/method/displayName/testId/executionId、原结果和 duration。跨文件 run/execution ID 重用或重复路径不计为完整成功。
- TRX Times 必须位于所关联命令 `startedUtc..finishedUtc` 内，时间需有时区；缺少可绑定时间、旧轮、错 runId、缺图/摘要/计数、零执行、坏会话计数或 Summary/RunInfo 异常标 `incomplete`。不与旧轮拼齐。结构完整的失败结果仍为 `failed`，不会因文件名或原命令 exit0 变绿。
- `context`、`task` 可缺失。代码/机器/工具链/范围/缓存/构建身份缺失为 `null`；不从程序集路径推断 checkout，不用当前分支给旧产物签名。可在输入/原任务记录说明 dirty patch、PR head/候选/实际 checkout 身份；必须有独立来源。
- `task` 只填写明确自有任务记录的数字退出、完成状态和整条命令墙钟；没有该记录就省略/填 null。完成状态须为单个字符串 `completed/cancelled/unknown/incomplete`，不接受数组/布尔/数字隐式等值。命令完成须有实际 bool=true 的 `rootExited/stdoutComplete/stderrComplete/outputComplete/cleanupComplete` 和标量 `phase=completed`；存在的 `exitKnown/cancellationRequested/timedOut` 也须为实际布尔值且与完成一致。旧记录缺项或错型仍保守报告 incomplete，保留原命令/退出和已观察成本；声明不是独立 gate 证据。不能把日志中一条 PASS 或多个子命令退出当原外层数字退出。

## 输出与成本口径

所有成本字段的单位都是 **毫秒**；`null` 是 unknown，字面数值 0 仅在输入明确测得 0 时保留。已知成本须为有限非负数值标量；bool、字符串、数组、对象、负数、NaN/Infinity 均不能参与比较，也不能隐式当作 0。该规则统一检查当前及旧 baseline 的墙钟和完整 duration，差值只使用验证后的数值，不改 baseline 原件。

| 输出 | 含义 / 限制 |
|---|---|
| `taskResult.wallClockMs` | 外层自有任务的显式墙钟；不由 duration 或子阶段求和生成 |
| `commands[].wallClockMs` | 原 `elapsedMs`；逐条显示 restore/build/prepare/test 等阶段，不取 TRX Times 冒充命令墙钟 |
| `batches[]` / `slowestBatches` | 显式 round/batch/commandLog、命令墙钟和案例 duration 合计；按墙钟列最多 10 个已知最慢批次 |
| `cases[]` / `slowestCases` | 每例原身份/结果/durationMs；按已知 duration 列最多 10 个最慢案例；不合并重复显示名 |
| `caseDurationMs` | 所列结果完整且 duration 都已知时的案例 duration 合计；结果/命令缺项、不完整或任何 duration 未知则 null |
| `knownCaseDurationMs` | 仅已观察到的已知 duration 小计；可能来自部分结果，**绝不等于完整成本/墙钟** |
| `artifacts` | 输入索引、commands、原日志/流/TRX 的字面路径和 SHA256；不存在的文件 hash=null。原文件保持只读 |
| `diagnostics` / 各条 diagnostics | 缺项和损坏原因；原失败日志/数字退出同时保留，不被报告错误替代 |

结果完整性与命令成败独立判断：原命令 `failed` / 数字退出仍保留，但缺结果、无效引用或不完整 TRX 时，对应批次及完整 `caseDurationMs` 必须为 null；已观察 `knownCaseDurationMs`、其他完整命令的案例成本及原墙钟不被抹去。反之，结构完整且 duration 已知的失败 TRX 仍可贡献确知的案例 duration 合计，不能仅因失败就把已知成本改为未知。

阶段、批次、案例可互相包含或重叠，**没有“所有墙钟相加”的总成本字段**。同一案例跨轮执行是不同执行，不是同一时刻的成本；其 duration 小计也不是端到端墙钟。没有记录的进程成本/数量不补造；脚本不做系统采集。

`taskResult.status`：`success` 仅表示显式 task completed/exit0 且已列命令/结果满足报告的完成判据；不是完整门签字。原非预期失败、TRX 失败为 `failed`；任务取消为 `cancelled`；缺项/坏会话/命令未完成为 `incomplete`；完成但外层数字退出未知为 `unknown`。原命令按记录 expectedExit 命中的非零退出标 `expected-failure`，任务最多 `completed-with-expected-failures`，不冒充 positive success；控制 TRX 的 Error/坏会话仍保守标不完整，报告器不豁免指定诊断。`declaredTask` 保留外层原声明，便于与保守派生状态对照。

## 同口径对照

可选 `-BaselinePath 'C:/my reports/previous-report.json'`，输入必须是本脚本 v1 生成的报告，不是旧 commands.json。无请求为 `not-requested`。只有以下条件均满足才 `comparable` 并给出 **当前减基线** 的 `taskWallClockDeltaMs/caseDurationDeltaMs`：

- 两侧为同 evidenceKind 的完整 positive success，整条命令墙钟及完整案例 duration 均已知；旧 baseline 的 scalar status/completion、原命令布尔完成证据及数字 exit0 也须复核，不能只信 success 文本。baseline 顶层版本/生成状态/工具退出等错型不能冒充生成的 v1 报告，按既有输入边界报工具错误；未知任务完成或成本则 not-comparable、差值 null；
- 两侧六项 context（codeIdentity/machine/toolchain/scope/cache/build）都必须是非空白字符串，去除两端空白后不能是 `unknown`（大小写不敏感）；缺失、null、布尔、数字、数组、对象均不是已知身份，不隐式转成字符串。当前无效值规范化为 null，旧 baseline 的原值也按同一规则复核；未知口径不抹掉原退出或可知成本。有效 codeIdentity 可以不同，其他五项 profile 必须逐字一致；
- 命令/选择/项目/filter/构建参数及实际案例身份/结果集合一致（保守保留 testId、程序集身份，不只比总数或显示名）。命令必须是非空字符串，arguments 必须是已知的字符串数组；缺失、null 或错误类型即不可比，即使两侧相同也不能证明参数已知。合法的空 argv 数组仍是已知参数。不因此丢弃可报告的成本或原退出。只在 `dotnet test` 的受支持选项/值位置忽略证据目的地：`--results-directory <非空路径>`，以及 `--logger` / `-l` 后恰为 `trx;LogFileName=<文件名>` 的值；`--` 后不再识别选项。其他命令、settings 路径、MSBuild 参数、非 TRX logger、附加属性或其他未知格式都逐字保留，宁可保守不可比，不对所有参数泛化擦除 `;LogFileName=`。原 `originalRecord` 始终不改写。

否则 `not-comparable`、列 reasons、差值为 null。程序集迁移、换机器、缓存不同、范围缩小、构建方式不同、缺身份、部分结果均不能冒称同集合代码优化。即使 comparable 也只是一次描述性差值，**不证明因果、稳定趋势或完整门提速**；不设置硬毫秒预算。

## 独立人工回归与历史演示边界

```powershell
# AST 预检同上；不启动 SDK、引擎、CIM 或网络。
pwsh -NoProfile -File ./tests/Pal.Game.Tests/TestCostReport.Tests.ps1
# 保留人工输入/每次真实 CLI stdout+stderr：只能用新的自有目录
pwsh -NoProfile -File ./tests/Pal.Game.Tests/TestCostReport.Tests.ps1 `
    -EvidenceDirectory 'C:/my evidence/new cost fixture run'
```

回归预期来自独立人工字面值：12000ms 外层、10000ms 测试命令、5000ms 两案例合计（2000/3000）、1500/2500ms 重叠构建/准备、拆分程序集共四案例/10000ms，不能反推为真实测试成本。覆盖正常、部分、缺项、损坏、取消/未知、失败后正常、失败与缺结果组合及完整失败结果成本正例、负控制意外 exit0、显示名重复、旧混合/新拆分、身份/轮次重用、对照门槛（含完成 flags/标量状态及旧 baseline 错型、双侧成本类型/非有限值与合法数值0、六 context 字段双侧/单侧错误类型与旧 baseline、有效不同 codeIdentity、双侧未知 command/arguments 与合法空 argv、非 logger 参数逐字保留及真实 TRX/结果目录变化正例）、中文 UTF8/空格路径/任意 cwd、native stderr 及严格 UTF8 原字节中的原中文路径、输入字节/mtime/文件集合不变。默认仅清理成功回归的自有 GUID 目录；显式 EvidenceDirectory 保留；失败保留原件。

本次实现的历史只读演示来源是 [`docs/milestones/m3/acceptance.md`](../milestones/m3/acceptance.md) 明确引用的历史公开门产物目录（本文以 `<historical-evidence-root>/gate` 表示；精确本机路径及演示原件索引只留在工程外的 #35 本地交接），只列入其 commands/log/TRX，**不读同级 Observer/外层/进程账本**。该旧版本含 10 命令、9 TRX、1423 条案例结果，已观察案例 duration 小计 `418480.5963ms`；命令数字退出为 `0,0,0,0,0,0,0,1,1,1`。20 个明确原文件 hash/长度/mtime 前后相同。工具 exit0，但旧 commands 缺墙钟/完成 flags/时区绑定时间，外层数字退出、代码/机器/缓存/构建口径不另行猜测，因此本报告 `taskResult=incomplete`、完整 duration/墙钟为 null、无性能对照。这里**不是新实测通过，也不改判原历史验收**。机器外目录不是清洁检出依赖；重现脚本能力只需上述人工回归，不复制/上传真实资源或历史私密记录。
