# 固定源码快照与精简交接

当主会话的历史增长、需要冻结源码身份或准备新主会话时，使用本页两个固定入口；信息分工见[长任务交接](large-task-handoffs.md)。这些是显式维护工具，不是任务系统或执行授权。普通 Unit 仍直接使用原生 dotnet；唯一 writer、独立评审和验收层级不变。

## 导出交接候选

1. 归还执行权限：状态不能为 running，sdkRemaining 为 0，writer、runId、deadlineUtc 均为 null。另行核对实际 fleet、pending 请求和租约；导出器不读取调度器。
2. 明确选择当前验收范围需要的能力收据，导出到全新目录：

```powershell
$sha = (Get-FileHash -LiteralPath $Index).Hash.ToLowerInvariant()
# 数组调用使用 PowerShell，不经 native -File 展开。
& ./scripts/Export-AgentHandoff.ps1 -InputPath $Index -OutputDirectory $NewPacket `
  -ReceiptKey @('acceptedLeaves', 'storyCoverage') -ExpectedSha256 $sha
```

3. 审阅 handoff.md、current.compact.json 和 validation 原件。默认不修改输入；需要替换权威 index 时，使用另一个全新输出目录，加 `-Apply` 和同一个已核旧 SHA。流程是原始字节备份 → 新候选 → 既有 Test-AgentEvidence 校验 → 旧文件未变 → 同目录原子替换。替换 API 另留同目录 replaced.raw 备份。失败保留产物，不自动重试。
4. 切换前独立核对 Git/dirty 身份、最新 source binding、current SHA 和新鲜 Issue 状态。由操作者建立新主会话，携带 handoff.md、compact index、适用规范和下一片输入；导出不执行 `/new`，也不移除当前上下文。

原 receipts 全部留在带 SHA 的 before.raw.json，选定能力收据进入 compact index。scope、integration、active_ticket（含失败、约束、未闭合契约）及 nextAction 完整保留，日期仍为字符串。原始 index 中的相对引用仍以原输入目录为解释基准；原目录、历史快照与日志继续保留。旧 inputs/limits 可能描述历史片，不给新任务续租；新规划先核最新 binding 与规格。

## 冻结源码或比较精确恢复

```powershell
& ./scripts/Export-AgentSourceSnapshot.ps1 -RepositoryRoot $TicketWorktree `
  -GitExecutable $ApprovedGit -EvidenceDirectory $NewSnapshot
& ./scripts/Export-AgentSourceSnapshot.ps1 -RepositoryRoot $TicketWorktree `
  -GitExecutable $ApprovedGit -EvidenceDirectory $NewComparison `
  -BaselinePath $BaselineBinding -AllowedChangedPath @('src/Example.cs', 'tests/ExampleTests.cs')
```

RepositoryRoot 必须是实际 Git worktree 根目录，不能使用子目录缩小文件全集。要求 quiescent writer 和空 index；输出在工作树外且全新，避免合并进既有输出。记录只读 Git argv/cwd、数字退出、双 EOF、UTC、清理和原始流。

两次枚举/散列核稳定身份：包括 tracked 和非忽略 untracked，已删除 tracked 文件记录 null，链接路径拒绝。原始字节 SHA256 不归一 BOM/CRLF。比较兼容既有 binding 的 worktree/branch/head/tree/files，归档 baseline 原字节；路径并集区分缺失键与 null，允许变更仍明确列出。无需从 unified patch 重建文件。

快照不是跨线程锁，也不认证 DLL、marker mtime、原件真伪或任何运行验收。精确逆变后可用空允许列表比较源码；运行时仍属于旧版本，重新构建/验证需另行授权。

## 本叶覆盖与验证边界

`tests/AgentWorkflow.Tests.ps1` 是公开 SOURCE/OFFLINE 回归：合成 current 的日期精度、原件/历史保留、归还权限、旧 SHA、分类器拒绝和显式应用；以及当前工作树的只读 Git 快照、根目录边界、同源与路径并集 delta。它独立于 CI 接线，不运行 SDK、Godot、网络或创建测试 Git 仓库。

本叶只交付快照和交接工具；尚未接入 Engineering Offline/CI（后续接线叶负责，当前 NOT RUN）。反馈分类仍是既有 xUnit 契约，不宣称任意 Game 原件分类、命令账本形状校验、AST 规则或 headless GPU 继承保护已经交付。派发预检、自动链执行和 Pi 全局配置不属于这两个工具。

SOURCE/OFFLINE 通过不等于完整 Engineering、Game、GPU/人工或远端验收。独立评审仍读冻结 diff 和适用原件；实际端到端成本变化需未来同类任务测量。
