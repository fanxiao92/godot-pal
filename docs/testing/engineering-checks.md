# 工程检查与分层回归

这些入口提供日常机械反馈，**不是**单元集合，也不代替 `Test-M0.ps1` 的完整验收。沿用 PowerShell 7、仓库 SDK 与既有测试依赖；不安装 hooks，不下载引擎，不统一或升级工具链。

## 调用范围

从任意工作目录均可使用入口的绝对路径；`RepositoryRoot` 默认入口所在仓库。

```powershell
# Draft / 日常轻量检查：不构建、不下载、不发现测试、不启动 Godot。
pwsh -NoProfile -File ./scripts/Test-Engineering.ps1 -Mode Source

# 离线人工回归：B 必须是另行准备、来源与锁已核对的四文件 bundle。
pwsh -NoProfile -File ./scripts/Test-Engineering.ps1 -Mode Offline `
  -GdUnitInputDirectory $B -EvidenceDirectory $NewOfflineEvidence

# 真实自有 SDK 进程回归：普通当前用户 NuGet 缓存，不是离线无 SDK 检查。
pwsh -NoProfile -File ./scripts/Test-Engineering.ps1 -Mode OwnedProcess `
  -EvidenceDirectory $NewProcessEvidence
```

| Mode | 实际步骤 | 前提／边界 |
|---|---|---|
| `Source`（默认） | PowerShell AST、既有 `Test-UnitBoundary.ps1` | 只读源码；XML／词法边界不证明任意间接调用无 I/O |
| `Offline` | Source 两步，加新 AST／聚合公开入口回归、UnitBoundary、GameBatches、ProcessSnapshot fake、GdUnitPackages、TestCostReport 回归 | 人工临时文件和隔离 pwsh 子进程；无真实 SDK／Godot／CIM 查询／联网下载 |
| `OwnedProcess` | Source 两步，加真实 `DotnetBoundary.Tests.ps1`、`UnitEntry.Tests.ps1` | 会构建／运行自有 net10 项目，使用批准的源及普通包缓存；不运行 Game／Prepare／solution |

三个模式不是别名。`OwnedProcess` 不重跑 `Offline`；完整工程工具链检查应显式串行调用后两者。离线回归使用 pwsh 子进程不等于 SDK 自有进程层。完整门、GPU／人工验收、远端 CI 是另外的证据，不因任一 mode 成功而通过。

## AST 与生成脚本预检

```powershell
pwsh -NoProfile -File ./scripts/Test-PowerShellSyntax.ps1
# 多文件参数用 PowerShell 数组；相对路径按 RepositoryRoot 解析，不按调用 cwd。
& ./scripts/Test-PowerShellSyntax.ps1 -Path @('scripts/Test-Unit.ps1', 'tests/UnitBoundary.Tests.ps1')
# 已明确拥有的单个外部生成文件也可预检；不会递归其个人目录。
& ./scripts/Test-PowerShellSyntax.ps1 -Path $GeneratedPowerShellFile
```

默认仅遍历 `scripts/`、`tests/` 下 `.ps1`／`.psm1`／`.psd1`；在进入前排除 `bin`、`obj`、`.godot`、`.packages`、`TestResults`、`reference`、`.pi`、`.git` 及链接目录。显式目录必须留在声明的 RepositoryRoot 内，显式缓存／链接目标拒绝。单个明确指定的外部文件用于临时生成脚本预检；不是扫描个人目录的许可。

只使用 `Parser.ParseFile`，不执行、加载或 dot-source 所选文件。错误保留文件、行列和原诊断；任一错误、缺失对象、非法范围、零对象、显式空 `Path` 均非零，后续好文件不能掩盖前错。语法成功不证明编码、业务、间接 I/O 或进程连接正确。

生成 PowerShell 应先明确 UTF-8 落盘，再调用上述入口，**仅零退出后**另行执行，并保留诊断和真实执行退出。已有解释器且确需生成 Python 的任务，可用其明确解释器先执行：

```powershell
python -c "import ast,pathlib,sys; p=pathlib.Path(sys.argv[1]); ast.parse(p.read_text(encoding='utf-8-sig'),filename=str(p))" $OwnedTemporaryPythonFile
```

这不新增项目 Python 依赖，不安装解释器、不建立空 Python job，不收编历史一次性交付工具。`ast.parse` 不写 `__pycache__`；预检成功同样不证明行为安全。

## 固定包输入与准备

Offline 必须显式给 `GdUnitInputDirectory`。预检对照本仓库 `scripts/gdunit-packages.lock.json` 的四个文件名、长度、SHA256；随后调用已有 `GdUnitPackages.Tests.ps1 -InputDirectory`，由其验证来源／pin、错误字节／部分 bundle、输入保持、下载 mock 和失败清理。不会在缺输入时调用 bootstrap 或真实网络。

bundle 准备和来源证明见 [固定源码包与 bootstrap](gdunit-source-packages.md)。已有来源核验的 bundle 可直接只读复用；长度/hash 通过本身不替代准备时的来源核对。其内容和真实游戏资源不随本票提交。

## 退出、日志和清理

- `0`：所选固定步骤均确认真实数字退出 0、root exit、双流 EOF，并出现各既有工具的完成标记；输出 JSON `status=complete`。
- 子步骤非零：保留该原数字退出，包括 7；停止后续步骤，不写成功状态。采证失败不能将已知子步骤非零改成成功或另一退出。
- `2`：缺完成结果、非法 UTF-8 或不能确认完整输出，JSON 标为 `incomplete`；不是通过。纯选择／输入／采证错误通常为 `1`。
- 每步原始 stdout／stderr 字节、argv／cwd、数字退出、时间和完成状态写入新证据目录，汇总为 `engineering-run.json`；源脚本与生成调用均先做 AST 预检，双端显式 UTF-8。
- 显式 EvidenceDirectory 必须不存在，永不覆盖用户文件或旧证据。省略时创建唯一自有目录：全部成功后删除；失败保留并报告路径。显式证据无论成败保留。
- 仅清理自有临时目录／本命令启动的进程树，不全局清缓存或查询／结束外部进程。root 与双流 EOF 沿用现有入口 600000ms 防挂起、5000ms 自有清理预算，不是性能指标或提速承诺。发生真实中止／崩溃／未知退出，保留原件停止，不自动重试。

完成标记是受信任固定脚本的局部连接检查，不是任意同名脚本的真实性证明；RepositoryRoot／锁定输入由调用方明确选择。不会从任意日志文件名或旧报告拼出成功。

## 检查器自身回归

```powershell
pwsh -NoProfile -File ./tests/Test-PowerShellSyntax.Tests.ps1
pwsh -NoProfile -File ./tests/Test-Engineering.Tests.ps1
```

两者只操作自有中文／空格临时目录，均从 foreign cwd 调公开入口。AST 回归包含可观测副作用脚本、模块／数据、括号／字符串、多文件混错、缺／空／无效范围、缓存排除与外部生成文件预检；故意坏脚本仅为数据，不提交永久坏源码。

聚合回归的显式人工仓库以 stub 工具验证接线，不能冒称其 SDK 或包来源是真实验收：核固定步骤、中文原始字节、真实退出 7、采证失败保留原失败、缺结果、损坏 UTF-8、缺／坏 bundle、既有目录不覆盖、默认清理和失败后正常调用无污染。真实仓库 Offline／OwnedProcess 必须另外执行并分别报告。

## CI committable-scope 完整门（full-check，#76）

`full-check` job 把 `Test-M0.ps1` 的**可提交子集**搬上 CI：以 `-Scope committable` 走与本地完整门同一入口、同一批计划、同一 TRX 身份断言与失败控制（三轮合成批、空缓存整建、警告基线、native-map 批、3 个 1-failure 控制、startup 检查）。先以 informational（非 required）上线，不用 `continue-on-error`：失败如实红，按两步上线设计不阻塞合并。结构与调度的本地回归是 `tests/Test-CiWorkflow.Tests.ps1`（真实 block 提取 + 真实 dotnet 夹具，不启动 Godot/远端 Actions）。

### 范围界定与私有资产边界

- **可提交子集 = Test-M0 全项减 local-only 真实分相例**：`RealFirstBattleFlowTests` 依赖 gitignored 私有资产 `src/Pal.Game/Assets/M4RealBattle53Closure`（M4 GPU/人工验收链在机外显式准备，永不提交/上传）。committable scope 由 `GameTestBatches.ps1` 的 `$script:LocalOnlyRealMatches` 单源声明：该类仍编译且可发现（Godot source/discovery 仍 626），仅退出执行计划（scene-realfull 3→2、game 正例 623→622）；local-only 声明必须留在 scene-realfull 批内、不得整批清空，否则 fail-closed。
- **本地仍全量跑**：默认 `-Scope full` 行为与输出句式逐字不变；真实分相例的本地验收收据仍由交付流程提供（资产不在仓库，CI 上按身份声明跳过，不是环境误判）。
- PASS summary：full 变体保留原句式（含「this PASS alone does not establish remote-CI execution identity」）；committable 变体以「committable-scope full gate」开头、声明排除正例数与私有资产本地收据边界、结尾声明远端 PASS 不替代被排除正例的本地全套门。
- job/step 名与 `PASS.txt` 回显携带 committable-scope full gate 身份；artifact `ci-full-evidence-<run_id>-<run_attempt>` 上传 37 份 TRX、PASS.txt 与身份 JSON（不含 NuGet 缓存；私有产物在 CI 上本就不存在）。

### 调度与预算

- 仅 `push`（main）、`pull_request` 的 `ready_for_review` 动作与显式 `workflow_dispatch` 触发；绝不 per-synchronize。`timeout-minutes: 35`（预算 25–35 分钟/次）；批内 session 超时钉（共享 120s、native-map/scene-realfull 300s）与本地同机制（同一 Test-M0 代码）。
- 引擎与 GdUnit：`Get-Godot.ps1`（官方 4.7.2 mono 归档，SHA512 钉死）与 Test-M0 内建 bootstrap（[固定源码包](gdunit-source-packages.md)）逐次下载核验，不缓存复用。

### 钉同步时机

非 Game 工程钉（core/cli-unit/cli-integration 的 passed/skipped）只被**完整门实际运行**验证；scoped 套件（Test-Range、单工程 `dotnet test`）不触发它们。M5 期间系列票只跑 scoped 套件，钉停留在 #53 同步值（914/142/279+2），本票按本 worktree 受控单跑实测同步为 928/464/359+2（先例 #53 `2e092ce`「sync gate pins to actual」；TRX 留本票回执）。**凡增加/迁移测试的票，要么同步钉，要么在其回执中声明钉未同步的完整门状态**，否则完整门（本地与 CI）在下一次运行前不可用。

### 转 required 的操作步骤（维护者后执）

1. informational 观察：连续 3 次绿（main push ×1 + PR ready ×2 或等价），无预算超限（Actions 可见时长不超 35 分钟）。保留 run/attempt 与 check-run 链接作为首绿证据。
2. 按[保护说明](merge-protection.md)的受控流程：GET 保存现保护对象 → 仅以 checks-only 载荷把 `full-check` 加入 required status checks（保留 `unit-check`、strict、enforce_admins、禁强推/删除不变）→ GET 回读比较。
3. 观察拒绝/允许行为（pending/取消/指定失败/缺失/错误候选）后如实记录；#69 的过渡显式拒绝已被本票替换为真实门，无需另行关闭。
4. 回滚同样按受控流程恢复原 required 集合；不因 informational 绿自动写入保护。
