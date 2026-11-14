# Long-running agent host（Pi Agent SDK）

一个独立的 TypeScript 本机开发工具：**普通程序长期管理执行，短生命周期 Pi session 完成单票实施与独立评审**。不需要一个常驻 main agent；关闭交互对话不会删除执行状态。直接嵌入 SDK，不经过 RPC，也不依赖 pi-subagents。

这是旧显式计划的兼容入口，保留原人工恢复协议（自主纠正额度0：v1计划没有授权有界纠正/引擎/父规格执行）。默认推荐[GitHub模式](README.md)，新能力集中在那里；不把旧计划的退出0当父规格通过。它不改变 [跨票协调协议](../../docs/agents/large-task-handoffs.md)、[Issue 工作流](../../docs/agents/issue-tracker.md)或[测试职责](../../docs/testing/test-layers.md)。当前工作树已有的 handoff/source snapshot 工具保持原样；这里的源码指纹只用于本 Host 自身的候选核对，不冒充其强审计收据，也不替换既有 current.json 契约。

## 责任与执行流程

- **GitHub**：规格、正文/评论、原生直接依赖。只读，不认领、不发评论、不关闭 Issue。
- **Host**：批准任务集合、依赖选择、单 writer、状态转换、检查、证据关联和恢复核对。
- **Pi SDK**：每次实施、Standards、Spec 均创建全新持久 session；没有父会话历史继承。
- **Git/证据**：源码身份、原生命令输出、冻结候选、独立评审和实际本地集成位置。

```text
pending → running → 原生检查 → Standards → Spec → awaiting_acceptance → accepted
             └──────────────────────────────────────────→ blocked
```

`accepted` **仅表示本地候选已通过该票配置的检查和两个独立评审，并已提交到当前本地基线**，不是 GitHub 关票、父规格完成、完整门、GPU/人工或 CI 验收。独立评审不是测试的替代，原生命令退出 0 也不是任意 TRX/诊断的强审计。

默认不提交：在 `awaiting_acceptance` 停止，由操作者提交精确候选、提供独立验收依据，再调用 `accept`。只有运行计划显式批准 `localCommits: true` 时，Host 才在检查及双轴评审后使用原生 `git add`/`git commit`。`autoAdvance: true` 另外批准在本地接受后继续选择下一张票。两者分别声明，没有隐式授权。绝不 push、merge、stash、reset、删除 worktree 或升级 SDK。

## 安装与离线验证

Node >= 22.19，直接运行 Node 原生 TypeScript，不需要运行时转译器。本工具只新增局部开发依赖，不是产品/CI 的 Node 前置。

```powershell
npm ci --prefix tools/long-running-agent --ignore-scripts
npm --prefix tools/long-running-agent run typecheck
npm --prefix tools/long-running-agent test
```

SDK 使用显式指定的**已有安装**，当前固定适配 `@earendil-works/pi-coding-agent@1.5.0`；不复制 SDK、不修改全局安装、不自动安装或降级。升级适配需另行验证。

真实 SDK 接线回归（不请求模型、不登录、不运行 Godot）：

```powershell
$env:PAL_PI_SDK_ROOT = 'C:/explicit/pi-install/node_modules/@earendil-works/pi-coding-agent'
npm --prefix tools/long-running-agent test
```

未提供该变量时，真实 SDK 案例明确 skip；其余使用合成 Issue、合成执行器及自有临时 Git 仓库，不访问网络。测试原件保存在输出标明的 OS 临时目录，便于失败调查；不清理其它任务的文件。

## 准备一个明确批准的运行计划

需要 Git、已授权的 `gh` CLI，以及指定 provider/model 可用的 Pi 凭据。凭据仅由 SDK 从 `agentDir` 读取，不放进计划、任务书或命令参数。

先准备一个**干净且唯一 writer 的仓库根目录**，明确它的完整 HEAD。Host 不会自动处理已有改动；另建干净工作树、提交或保留哪些改动必须由操作者批准。本仓库当前的未提交工具/业务修改不会自动进入另一工作树。

将计划保存在工作树外。以下是模板，不是对示例 Issue、路径、命令或期限的执行授权；替换占位符并核对真实 Issue 及全部直接依赖后再使用：

```json
{
  "version": 1,
  "repository": "fanxiao92/godot-pal",
  "cwd": "C:/worktrees/explicit-clean-checkout",
  "baseHead": "<full-40-character-Git-HEAD>",
  "sdkRoot": "C:/explicit/pi-install/node_modules/@earendil-works/pi-coding-agent",
  "agentDir": "C:/Users/explicit-user/.pi/agent",
  "provider": "<configured-provider>",
  "model": "<explicit-model-id>",
  "approval": {
    "id": "<operator-approval-reference>",
    "approvedBy": "<operator>",
    "deadlineUtc": "<approved-UTC-deadline-ending-in-Z>",
    "localCommits": false,
    "autoAdvance": false
  },
  "limits": { "maxTurns": 40, "timeoutMs": 1800000 },
  "prerequisites": [],
  "tasks": [
    {
      "issue": 999001,
      "dependsOn": [],
      "contextIssues": [],
      "allowedWritePaths": ["src/Pal.Core/Example.cs", "tests/Pal.Core.UnitTests/ExampleTests.cs"],
      "readPaths": ["AGENTS.md", "GLOSSARY.md", "docs", "src/Pal.Core", "tests/Pal.Core.UnitTests", "reference"],
      "checks": [
        {
          "kind": "non_engine",
          "executable": "C:/Program Files/dotnet/dotnet.exe",
          "args": ["test", "tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj", "--logger", "trx", "--results-directory", "{checkDir}/trx"],
          "timeoutMs": 300000
        }
      ]
    }
  ]
}
```

- 任务集合必须由操作者明确选定；Host 不会因为出现 ready-for-agent 就无限扩大范围。
- `contextIssues` 显式列出父规格等必读 Issue；直接依赖的正文及评论也自动读取。开始、评审后、接受前再次核对快照，改变则 STOP。
- `dependsOn` 使用当前计划里的 ticket，或显式 `prerequisites`。GitHub 新增了未绑定的直接阻塞关系时拒绝派发；原生 dependencies 不可读时不静默退回猜测正文。
- 既有前置可填 `{"issue": 999000, "commit": "<full-SHA>", "receipt": "C:/evidence/accepted-upstream.txt"}`。操作者须保证它是真实的独立验收依据；工具复制并固定收据散列、检查提交实际是当前 HEAD 的祖先，不根据 Issue closed 自签能力。
- 每票声明**精确授权文件路径**，跨 attempt 保留；文件数量不设硬上限，超过10个时审查单一行为和完整影响集。目录、忽略文件、链接和越界变更仍拒绝。必要的新增文件也必须事先列明。源码身份包含原始文件字节、执行位和 index 指纹；拒绝 assume-unchanged / skip-worktree 及未解决冲突，避免 clean 状态隐藏实际源码差异。
- `readPaths` 限定模型可读取的文件/目录；SDK 常规 AGENTS.md 发现仍启用。相应领域规范及参考入口仍按项目规则阅读。
- 模型只有 `scoped_read`、`scoped_list`，实施者另有 `scoped_edit`、`scoped_write`。没有 shell、MCP、全局/项目扩展、技能执行器、子代理或调度器能力。显式目录列举非递归，拒绝链接。
- `.git`、`.pi`、node_modules、常见凭据文件、`data/raw`、`data/extracted`、游戏 Assets 不暴露给模型工具。私有真实资源不上传。
- `maxTurns` 是**每个 session**的上限；`timeoutMs` 是一次 attempt 的整体期限，实际执行同时受批准到期时间约束。未设置 token/费用预算，也不声称严格限制服务商账单；如有需要应另行实现并批准。
- `{checkDir}` 只在参数字符串中替换为本次全新检查目录，不进行 shell 展开。stdout、stderr、实际 argv/cwd/PID/退出码及时间分别留证。

## 启动、观察与人工接受

```powershell
node tools/long-running-agent/cli.ts init --plan C:/evidence/approved-plan.json --state C:/evidence/new-host-run
node tools/long-running-agent/cli.ts run --state C:/evidence/new-host-run
node tools/long-running-agent/cli.ts status --state C:/evidence/new-host-run --source
```

`init` 要求全新状态目录、干净源码和匹配的 baseHead。不会自动创建/切换分支。`run` 全程持有 Git common directory 下的专用 writer lock，同一仓库的多个 Host/工作树也不能互相绕过。外部编辑器和其它 agent 不受该锁强制约束，开跑前仍须确认唯一 writer；双次源码枚举/散列只检测观察期间的不稳定，不是 OS 级文件事务。

人工模式完成检查后，审阅 evidence、diff 和独立评审，再通过既有批准流程提交精确候选，然后：

```powershell
node tools/long-running-agent/cli.ts accept --state C:/evidence/new-host-run --issue 999001 --head <actual-full-commit> --receipt C:/evidence/operator-acceptance.txt
node tools/long-running-agent/cli.ts run --state C:/evidence/new-host-run
```

`accept` 拒绝未提交、提交后仍 dirty、与冻结文件图不同、Issue 已变化或 gate 收据被更改的候选。不会替操作者提交。若本地提交已发生而结果记录失败，也先核实实际副作用；只在存在完整 gate 且精确候选可验证时允许人工接受，不自动重做提交。

`run` 退出码：0=计划中全部本地接受；1=失败/阻塞；2=正常停在人工关口或尚有未执行任务。0 不证明里程碑完成。

## 暂停、异常与恢复

Ctrl+C / SIGTERM 请求取消，等待 SDK 停止后保存结果。一个会话失败不自动换 fresh session 重试；失败、未知退出、额度耗尽和规格变化都停止推进。SDK provider retry 与自动 compaction 也关闭，避免底层自动续跑冒充新的批准 attempt。

进程退出时可能留下 writer lock 和 `running` 状态。**不会凭租约超时自动抢锁**。操作者先检查本任务的进程、检查原件、Git 状态和已经发生的副作用，确认没有活动 writer/检查后：

```powershell
node tools/long-running-agent/cli.ts recover --state C:/evidence/new-host-run --confirm-quiescent --reason "已核对原 owner、任务子进程和 Git 副作用"
node tools/long-running-agent/cli.ts status --state C:/evidence/new-host-run --source
```

`recover` 只解除可识别且 owner PID 已不存在的旧锁，并把中断 attempt 标记 blocked；不派发、不运行测试、不回滚。PID 仍存在（包括复用 PID）、锁来自另一个 state 或无法确认身份时拒绝处理，不结束未知进程。没有旧锁时仍要求明确静默确认。

再次运行必须单独批准，绑定 `status --source` 显示的精确文件图指纹：

```powershell
node tools/long-running-agent/cli.ts retry --state C:/evidence/new-host-run --issue 999001 --fingerprint <actual-source-SHA256> --reason "明确批准在当前源码上建立新 attempt"
node tools/long-running-agent/cli.ts run --state C:/evidence/new-host-run
```

新 attempt 使用新 session，不恢复旧对话或复用失败检查。仍保留最初整票源码范围约束。HEAD 漂移、越界改动、staged 残留等必须先按批准流程核实，不由 retry 自动清理。若批准范围、模型、额度或任务集合需要变化，应停止并另行建立批准计划，不直接改活跃 plan.json。

原生检查超时/中断，或自动本地提交结果不明时，可能存在未核实的后代进程或副作用：状态 blocked，**保留 writer lock**，必须退出该 Host 后按上述恢复步骤处理。

## 证据布局

```text
state-directory/                 # 必须在源码工作树外
  plan.json                     # 明确批准的不可变输入
  state.json                    # Host 自身执行索引；不是第二套 Issue tracker
  attempts/<issue>-<uuid>/
    input.json                  # 来源身份、实时 Issue/依赖和批准引用
    events.jsonl                # 有界生命周期事件，不复制全部工具参数/对话
    implement/                  # brief、独立 SDK JSONL session、report
    check-0/                    # intent/started/result、原始 stdout/stderr、用户指定 TRX
    standards/
    spec/
    gate.json                   # 精确候选与本次检查/评审结果
  receipts/                     # 前置、人工依据及本地集成收据
  recovery-<uuid>.json
```

状态采用临时文件 + fsync + 同目录 rename 替换；失败保留原件，不先删除旧状态。派发意图在创建 SDK session 前持久化。它不承诺跨 Git/网络/文件系统的 exactly-once，也不提供断电下的分布式事务；恢复依赖实际事实核对。

## 明确未覆盖的范围

1. **没有完整沙箱。** 工具路径守卫依赖唯一 writer，不能防恶意并发文件系统 TOCTOU；批准的原生命令拥有当前用户权限，可能执行项目构建目标/测试及 Git hooks。计划应由可信操作者维护，不能把模型输出当作批准计划。
2. **首版只自动执行 non_engine 检查。** 操作者必须保证这些命令不会启动 Godot、Prepare、Game、solution 全门或 detach 后台进程；声明不等于静态证明。需要这些检查的票应使用项目原有串行/诊断入口，不能用本 Host 的 Unit/评审结果替代其验收。本版不提供全机引擎协调或未知进程处置。
3. **没有自动业务决策、修复循环、任务发布和里程碑关票。** worker/reviewer 报 blocked 后交给操作者，首次失败不消耗隐式重试额度。
4. **没有自动 worktree 供应与清理、远端集成、费用计量或多机调度。** 不引入数据库/队列平台；确有需求后再扩展，而不是重做现成的 Issue/验收系统。
5. **离线和 SDK 接线通过不等于真实模型执行通过。** 真实 Issue、多票模型运行、完整工程/Game、GPU/人工及远端 CI 须分别批准、执行和留证。该独立工具尚未接入共享 CI，避免擅自修改当前工作树已有的 CI 改动。
