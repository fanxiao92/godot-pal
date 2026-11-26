# M7 验证基建退役/改写盘点与代理上下文审计

> 基线提交：`main@7f0567b`（2026-11-26）  
> 性质：**只盘点、不删除**（规划资产，仅存留于 `wayfinder/m7-retirement-inventory` 分支，不合入 `main`）。  
> 对应工单：[盘点：待退役的脚本、文档、测试与 AGENTS 条款](https://github.com/fanxiao92/godot-pal/issues/245)（隶属 [M7 验证基建路线图（wayfinder）](https://github.com/fanxiao92/godot-pal/issues/240)），并入 [工程反馈：AGENTS/docs 代理上下文一次性审计](https://github.com/fanxiao92/godot-pal/issues/230) 的全部审计诉求。

---

## 1. 执行摘要与总览

本次盘点覆盖 `main@7f0567b` 下全部 `scripts/`（39 文件）、脚本/环境回归（20 文件 + 夹具）、`docs/testing/`（16 文件）、`docs/agents/`（8 文件）、`docs/archive/`（10 文件）、`docs/adr/`（4 文件）、`AGENTS.md`、`README.md`、`docs/README.md`、`src/Pal.Game/Tests/README.md`、`GLOSSARY.md` 以及 `tools/long-running-agent/` 的测试调用耦合。

| 类别 | 盘点总数 | 建议删除 / 前门取代后删除 | 建议保留并改写 / 合并 | 建议原样保留 / 仅调链接 | 待用户/下游票决策 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `scripts/` 全部文件 | 39 | 26 | 4 | 6 | 3 |
| `tests/*.Tests.ps1` / `.mjs` | 20 | 17 | 2 | 1 | 0 |
| `docs/testing/` | 16 | 12 | 3 | 1 | 0 |
| `docs/agents/` | 8 | 1 | 1 | 6 | 0 |
| `docs/archive/` | 10 | 6 | 0 | 0 | 4（M0 交付归档） |
| `docs/adr/` | 4 | 0 | 2（标 Superseded） | 2 | 0 |
| 根/子模块入口文档 | 6 | 1（`src/Pal.Game/Tests/README.md`） | 4（`AGENTS.md`、`README.md`、`docs/README.md`、LRA README） | 0 | 1（LRA `EXPLICIT_PLAN.md`） |

### 给下游票（#244 / #246 / #247 / #248）的 5 个关键前置陷阱

1. **`Test-UnitBoundary.ps1` 会阻断根目录 `pal.ps1` 与 `coverlet.collector`**：[scripts/Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1) 第 12–25 行硬编码了仓库根目录白名单（`$AllowedRootFiles`），第 36 行硬编码了 Unit 工程仅允许 3 个包（`Microsoft.NET.Test.Sdk` / `xunit` / `xunit.runner.visualstudio`），第 40 行拒绝 `Directory.Build.props`。在 `unit-check` 尚在运行时新增根目录 `pal.ps1` 或引入 `coverlet.collector` 会直接炸红 CI，必须先改写或退役该守卫。
2. **`.gitattributes` + CI LFS 守卫耦合 `src/Pal.Game/Tests/Fixtures/**`**：[.github/workflows/ci.yml](../../.github/workflows/ci.yml) 第 66–80 行强校验 `src/tests/tools` 下禁止出现 Git LFS 指针（CI `lfs: false`），而 [.gitattributes](../../.gitattributes) 第 45 行目前只豁免了 `src/Pal.Game/Tests/Fixtures/**`。#244 将测试拆到 `tests/Pal.Game.Tests/` 时，若同步搬迁图片 fixture 而忘记更新 `.gitattributes`，Linux CI 会直接失败。
3. **LRA 硬耦合 3 个旧脚本**：`tools/long-running-agent/` 中 `checks.ts` 与 `github-agent.ts` 直接调用 `scripts/Test-Range.ps1`（及 `-FixedPackageDirectory`），`check-failure.ps1` 第 33 行直接 dot-source `scripts/GameTestBatches.ps1`，`checks.test.ts` 第 266 行直接调用 `scripts/Test-PowerShellSyntax.ps1`。退役这些脚本的叶票必须在 LRA 接口切换之后，或与 LRA 适配叶票串行编排。
4. **`src/Pal.Game/Tests/README.md` 单文件膨胀至 92.4 KB**：后 275 行（约 85 KB）全是 M4/M6 单票过程流水账与过时计数（与当前 1,100 个 `[TestCase]` 脱节），按旧 `AGENTS.md` → `game-test-layers.md` → `Game README` 路径读取时会无谓消耗大量上下文窗口，拆工程后应直接删除。
5. **#230 提到的 ADR-0005 在 `main@7f0567b` 不存在**：当前 `docs/adr/` 仅含 `0001`–`0004`；M7 新增 ADR 编号前须确认 `0005` 是否存在于未合入分支或已取消，以免撞号。

---

## 2. `scripts/` 全量盘点（39/39）

下表基于 `git ls-files scripts/` 与全仓活跃引用检索（排除 `reference/` 与 `docs/archive/`）。

### 2.1 离线生产资源准备脚本（8 个）

这类脚本属于离线内容转换链（`Pal.Extract` 输出 → Godot 原生资源），由 `Pal.Prepare.Tests` 直接覆盖，与旧测试编排器无关。

| 脚本 | 行数 | 用途 | 当前活跃调用方 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- | --- |
| `scripts/Prepare-GameScene.ps1` | 96 | M1 静态场景包 → Godot 原生场景/纹理 | `tests/Pal.Prepare.Tests/`（4 个测试类）、`Test-GameStartup.ps1`、`README.md`、设计文档 | **保留**（由 `pal.ps1 assets prepare` 调用或作下层原语） | 生产离线资产准备，被 `Pal.Prepare.Tests` 实测；需核对 Linux 路径兼容性 |
| `scripts/Prepare-BattleData.ps1` | 67 | 现代数值 JSON → 原生战斗数值包 | `tests/Pal.Prepare.Tests/`（2 个测试类）、`samples/authored-battle/README.md`、设计文档 | **保留** | 同上 |
| `scripts/Prepare-BattleContext.ps1` | 81 | 首战上下文 → `Profile.tres` | `tests/Pal.Prepare.Tests/BattlePrepareProcessTests.cs`、设计文档 | **保留** | 同上 |
| `scripts/Prepare-StoryPackage.ps1` | 58 | 现代故事包 → typed Story/Visual | `tests/Pal.Prepare.Tests/StoryPackagePrepareProcessTests.cs`、设计文档 | **保留** | 同上 |
| `scripts/Prepare-FireEffects.ps1` | 56 | 火焰战斗图像包 → 原生精灵 | `tests/Pal.Prepare.Tests/FireEffectsPrepareProcessTests.cs`、`battle-image-schema.md` | **保留** | 同上（注：旧 `script-inventory.md` 漏列） |
| `scripts/Prepare-ItemBitmaps.ps1` | 56 | 物品图标包 → 原生精灵 | `tests/Pal.Prepare.Tests/ItemBitmapsPrepareProcessTests.cs`、`battle-image-schema.md` | **保留** | 同上（注：旧 `script-inventory.md` 漏列） |
| `scripts/Prepare-DialoguePackage.ps1` | 47 | 现代对话包 → 原生资源并 import | 仅文档手工入口（`dialogue-display.md`、`story-package-preparation.md`）；内部调 `Get-TestProcesses.ps1` | **保留并改写** | 属 M3/T3 真实对话资产准备；须移除对已退役 `Get-TestProcesses.ps1` 的调用，纳入资源准备链 |
| `scripts/Write-StaticScene4Profile.ps1` | 79 | M3 一次性只读 `DATA.MKF` 生成 `Scene4Static.tres` | 无代码/CI/设计文档调用，仅 `script-inventory.md` 与 archive；内部调 `Get-TestProcesses.ps1` | **待定（建议删除）** | 一次性 M3 author 工具；若 T3 共享资源缓存无需单独生成 `Scene4Static.tres` 则删除，需要则并入资源准备流程 |

### 2.2 工具链下载与 GdUnit fork 锁（3 个）

| 脚本 / 数据 | 行数 | 用途 | 当前活跃调用方 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- | --- |
| `scripts/Get-Godot.ps1` | 22 | 下载官方固定 Godot 4.7.2 mono 并校验 SHA512 | `.github/workflows/ci.yml`、`tests/Test-CiWorkflow.Tests.ps1` | **保留并改写** | 按 #242 扩展支持 Linux `linux_x86_64.zip` + `chmod +x` + 双平台 SHA512 钉，供 Linux CI 与本地复用 |
| `scripts/Get-GdUnitPackages.ps1` | 72 | 校验并 bootstrap 本地四文件 GdUnit fork bundle | `Test-M0.ps1`、`Test-Range.ps1`、`tests/Pal.Game.Tests/GdUnitPackages.Tests.ps1`、`README.md` | **删除** | 约束 #13 退役 GdUnit fork，改用 nuget.org 官方包（前置：#244 prototype 验证官方包通过） |
| `scripts/gdunit-packages.lock.json` | 48 | 锁定 fork 四文件 SHA256/URL | `Get-GdUnitPackages.ps1`、`Test-Engineering.ps1`、`Test-Range.ps1`、`GdUnitPackages.Tests.ps1` | **删除** | 随 fork 退役一并删除；连带清理 `NuGet.Config` 的 `gdunit-local` 与 `.gitignore` 的 `/.packages/` |

### 2.3 重型测试编排、分批清点、范围回归与自制审计（12 个）

| 脚本 / 数据 | 行数 | 用途 | 当前活跃调用方 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- | --- |
| `scripts/Test-M0.ps1` | 217 | 重型完整门（3 轮 37 TRX + 负控制 + 空缓存） | `.github/workflows/ci.yml` (`full-check`)、`README.md`、多篇 `docs/testing/` | **由前门取代 → 删除** | 约束 #2、#7、#8：旧 `full-check` 与 `Test-M0` 整体退役 |
| `scripts/Test-Range.ps1` | 356 | 范围回归入口（CLI/Prepare/Game + native-map marker + 计数钉 + 警告基线） | **LRA `checks.ts` / `github-agent.ts` / `checks.test.ts` / `policy.test.ts`**、`Test-Range.Tests.ps1`、`GameBatches.Tests.ps1` | **由前门取代 → 删除** | 约束 #7：由 `pal.ps1` 取代；**必须等 LRA 切换至前门后再删** |
| `scripts/Test-Unit.ps1` | 185 | 维护用 Unit 强审计编排器（discovery 冻结 + TRX 图） | `tests/UnitEntry.Tests.ps1`、`docs/testing/unit-entry.md` | **由前门取代 → 删除** | 约束 #7、#8：退役自制审计与旧维护入口 |
| `scripts/GameTestBatches.ps1` | 386 | 源码正则清点 Game 用例、批次数量冻结、`Read-TestRunIdentity`、`Assert-HeadlessGpuEnvironment` | `Test-M0`、`Test-Range`、`Test-Unit`、`Invoke-CiUnit`、`Test-AgentEvidence`、`TestCostReport.psm1`、**LRA `check-failure.ps1` L33**、`GameBatches.Tests.ps1`、`RangeIdentity.Tests.ps1` | **删除** | 约束 #8 退役自制审计/数量冻结；#243 改用 `[TestCategory]`。**前置**：解耦 LRA `check-failure.ps1`；6 行 GPU 环境变量检查可按需内联进 `pal.ps1` |
| `scripts/TestRunDefinitions.ps1` | 249 | VSTest v7 discovery 日志解析与 `Read-XUnitTrxIdentity` | `Test-Unit`、`Test-Range`、`Test-M0`、`Invoke-CiUnit`、`Test-AgentEvidence`、`RangeIdentity.Tests.ps1` | **删除** | 约束 #8：退役自制 discovery/TRX 审计工具 |
| `scripts/DotnetCheckedDefinition.ps1` | 134 | 自制 dotnet root/双流/EOF/清理进程执行器 | `Test-M0`、`Test-Range`、`tests/Pal.Game.Tests/DotnetBoundary.Tests.ps1` | **删除** | 约束 #8：信任原生 `dotnet test` 与 CI/前门超时，不自制执行器 |
| `scripts/Get-TestProcesses.ps1` | 6 | Windows `Get-Process` 查 Godot 与仓库执行器 | `Test-M0`、`Prepare-DialoguePackage`、`Write-StaticScene4Profile`、3 个 GPU/物理脚本、`ProcessSnapshot.Tests.ps1` | **删除（跨平台逻辑内联至 `pal.ps1`）** | 仅 6 行且强耦合 Windows；LRA 自有 `engine-processes.ps1` 不依赖它 |
| `scripts/Get-TestCostReport.ps1` | 18 | 离线测试成本报告 CLI | `Test-CiFullProof.ps1`、`TestCostReport.Tests.ps1` | **删除** | 约束 #8：退役自制成本/审计框架，耗时由 CI job summary 提供 |
| `scripts/TestCostReport.psm1` | 331 | 成本解析与基线对比模块 | `Get-TestCostReport.ps1`、`TestCostReport.Tests.ps1` | **删除** | 同上 |
| `scripts/Test-GameStartup.ps1` | 109 | 人工资源 Godot import / Main 启动与负控制 | `Test-M0.ps1` | **由前门取代 → 删除** | 其「人工资源 headless import + Main 启动/拒绝控制」职责并入新 Game/E2E 冒烟测试或 `pal.ps1` |
| `scripts/Test-GodotCompatibility.ps1` | 165 | M0 一次性 Godot/net10 外部兼容探针 | 无任何代码/CI 调用，仅 `script-inventory.md` 与 archive | **删除** | M0 一次性调查探针，git 历史留档 |
| `scripts/warning-baseline.json` | 8 | 编译警告计数基线（`core:0, cli-unit:0, cli-integration:2, prepare:0, game:14`） | `Test-M0.ps1`、`Test-Range.ps1`、LRA 测试夹具（文本） | **待定（建议改用 MSBuild 规则后删除）** | 若新基建改用标准 `<TreatWarningsAsErrors>` 或常规构建，则删除；如保留警告防回退需在 #246/#248 决定归属 |

### 2.4 旧 CI 强审计与工程检查脚本（8 个）

| 脚本 | 行数 | 用途 | 当前活跃调用方 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- | --- |
| `scripts/Invoke-CiUnit.ps1` | 112 | ADR-0003 前的旧 CI Unit 执行器 | 无运行调用（已退出 `ci.yml`），仅文档 | **删除** | 死代码 + 约束 #8 |
| `scripts/New-CiCandidateInputs.ps1` | 46 | 旧 CI GitHub API 候选输入采集 | 仅 `tests/Test-CiCandidate-Inputs.Tests.ps1` | **删除** | 死代码 + 约束 #8 |
| `scripts/Test-CiCandidate.ps1` | 115 | 旧 CI checkout/parents/candidate 强校验 | 仅 `tests/Test-CiCandidate*.Tests.ps1` | **删除** | 死代码 + 约束 #8 |
| `scripts/New-CiCostInput.ps1` | 56 | 旧完整 CI 成本输入组装 | 仅 `Test-CiCostInput.Tests.ps1`、`Test-CiFullProof.Tests.ps1` | **删除** | 死代码 + 约束 #8 |
| `scripts/Test-CiFullProof.ps1` | 48 | 旧完整 CI 产物与成本证明校验 | 仅 `tests/Test-CiFullProof.Tests.ps1` | **删除** | 死代码 + 约束 #8 |
| `scripts/Test-Engineering.ps1` | 197 | `Source`/`Offline`/`OwnedProcess` 工程检查聚合器 | 仅 `Test-Engineering.Tests.ps1`、`engineering-checks.md`（CI 不调用） | **删除** | 下游被聚合脚本绝大部分退役；保留的轻量静态守卫直接由 `pal.ps1` / CI 调用 |
| `scripts/Test-UnitBoundary.ps1` | 192 | 扫描 Core/CLI Unit `.csproj` 形状、源码禁止 I/O 词及根目录文件白名单 | `.github/workflows/ci.yml`、`Test-Engineering`、`Test-Unit`、`UnitBoundary.Tests.ps1` | **保留并大幅改写** | 曾捕获真实回归（M5 #113 Unit 含文件 I/O、#53 根目录散落临时脚本），价值高；但必须移除与新基建冲突的僵硬规则（根目录放行 `pal.ps1`、包白名单放行 `coverlet.collector`、评估放行 `Directory.Build.props`） |
| `scripts/Test-PowerShellSyntax.ps1` | 84 | PowerShell AST 语法检查与可选无参 `WaitForExit` 检查 | `.github/workflows/ci.yml`、`Test-Engineering`、多处回归、**LRA `checks.test.ts` L266** | **保留并简化** | 跨平台轻量语法守卫，可由 `pal.ps1` / T1 直接调用；注意保持 LRA `checks.test.ts` 调用兼容 |

### 2.5 旧手工长任务交接与证据分类工具（5 个）

> 核实事实：LRA（`tools/long-running-agent/`）**不调用**以下 5 个文件（LRA 自有 `github-state.json`、`io.ts` 指纹与 `check-failure.ps1`）。它们仅服务于 M3–M5 期间的手工 `current.json` 协调流程。

| 脚本 / 数据 | 行数 | 用途 | 当前活跃调用方 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- | --- |
| `scripts/Test-AgentEvidence.ps1` | 167 | 校验旧 `current.json` 与 xUnit/Game RED/GREEN JSON | `Export-AgentHandoff.ps1`、`tests/AgentEvidence.Tests.ps1`（CI guard） | **删除** | 约束 #8：退役自制审计工具 |
| `scripts/Export-AgentHandoff.ps1` | 89 | 导出旧 `current.json` 精简交接包 | 仅 `tests/AgentWorkflow.Tests.ps1`、`docs/agents/runtime-workflow.md` | **删除** | 仅服务已淘汰手工 `current.json` |
| `scripts/Export-AgentSourceSnapshot.ps1` | 127 | 全仓原始字节 SHA256 快照 | 仅 `tests/AgentWorkflow.Tests.ps1`、`docs/agents/runtime-workflow.md` | **删除** | LRA 已有内置指纹，人工会话用 `git status/diff/rev-parse` |
| `scripts/AgentEvidenceJson.psm1` | 58 | 上述三脚本的共享严格 JSON 模块 | 仅上述三脚本及两项回归 | **删除** | 随上三脚本一并删除 |
| `scripts/agent-evidence.schema.json` | 120 | 旧 `current.json` 与反馈 JSON schema | 仅 `Test-AgentEvidence.ps1`、`Export-AgentHandoff.ps1` | **删除** | 同上 |

### 2.6 里程碑专用 GPU / 物理桌面验证脚本（3 个）

| 脚本 | 行数 | 用途 | 当前活跃调用方 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- | --- |
| `scripts/Test-RealDialogueGpu.ps1` | 100 | M3 独立对话 GPU 回读 | 仅文档手工入口（`dialogue-display.md`、`npc-interaction-host.md`）；调 `Get-TestProcesses.ps1` | **由前门取代 → 删除** | 约束 #4、#5、#7、#241：迁为 T3 E2E 场景经 `pal.ps1` 触发后删除 |
| `scripts/Test-M4FirstBattleGpu.ps1` | 100 | M4 首战链 GPU 像素验收 | 仅 `script-inventory.md`、`runtime-workflow.md`；调 `Get-TestProcesses.ps1` | **由前门取代 → 删除** | 约束 #4、#5、#7、#241：像素 oracle 零件可复用，待新 T3 E2E 覆盖并验收通过后删除 |
| `scripts/Test-M4PhysicalDesktop.ps1` | 183 | M4 物理桌面窗口 + `SendInput` 真实键盘验收 | 仅 `script-inventory.md`；调 `Get-TestProcesses.ps1` | **待 #247 决定** | #241 已列为待决项：物理 `SendInput` 是否退役（改由进程内 `ParseInputEvent` + 人工 5 分钟观感确认替代），若退役则删除 |

---

## 3. 工具自身回归、配置与历史测试产物盘点

### 3.1 `tests/*.Tests.ps1` 与 `tests/*.Tests.mjs`（20/20）

| 测试文件 | 行数 | 被测对象 | 当前 CI 调用 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- | --- |
| `tests/AgentEvidence.Tests.ps1` | 330 | `Test-AgentEvidence.ps1` / `AgentEvidenceJson.psm1` | `ci.yml` (`unit-check`) | **删除** | 随被退役工具删除 |
| `tests/AgentWorkflow.Tests.ps1` | 120 | `Export-AgentHandoff.ps1` / `Export-AgentSourceSnapshot.ps1` | `ci.yml` (`unit-check`) | **删除** | 同上 |
| `tests/Pal.Game.Tests/DotnetBoundary.Tests.ps1` | 116 | `DotnetCheckedDefinition.ps1`（含 `tests/Pal.Game.Tests/Fixtures/DotnetBoundary/` 3 文件） | 无（仅 `Test-Engineering`） | **删除（连带删其 Fixtures 子目录）** | 同上 |
| `tests/Pal.Game.Tests/GameBatches.Tests.ps1` | 1056 | `GameTestBatches.ps1`（数量冻结 + AST 切 `Test-Range`） | `ci.yml` (`unit-check`) | **删除** | 随 `GameTestBatches.ps1` 与数量冻结退役 |
| `tests/Pal.Game.Tests/GdUnitPackages.Tests.ps1` | 229 | `Get-GdUnitPackages.ps1` / `gdunit-packages.lock.json` | 无（仅 `Test-Engineering`） | **删除** | 随 GdUnit fork 退役 |
| `tests/Pal.Game.Tests/ProcessSnapshot.Tests.ps1` | 65 | `Get-TestProcesses.ps1` | 无（仅 `Test-Engineering`） | **删除** | 随 `Get-TestProcesses.ps1` 退役 |
| `tests/Pal.Game.Tests/TestCostReport.Tests.ps1` | 732 | `Get-TestCostReport.ps1` / `TestCostReport.psm1` | 无（仅 `Test-Engineering`） | **删除** | 随成本报告工具退役 |
| `tests/RangeIdentity.Tests.ps1` | 128 | `TestRunDefinitions.ps1` / `GameTestBatches.ps1` TRX 图 | 无 | **删除** | 随自制 TRX 审计退役 |
| `tests/Test-CiCandidate-Inputs.Tests.ps1` | 75 | `New-CiCandidateInputs.ps1` | 无 | **删除** | 死代码回归 |
| `tests/Test-CiCandidate.Tests.ps1` | 90 | `Test-CiCandidate.ps1` | 无 | **删除** | 死代码回归 |
| `tests/Test-CiCostInput.Tests.ps1` | 24 | `New-CiCostInput.ps1` | 无 | **删除** | 死代码回归 |
| `tests/Test-CiFullProof.Tests.ps1` | 86 | `Test-CiFullProof.ps1` | 无 | **删除** | 死代码回归 |
| `tests/Test-CiUnit.Tests.ps1` | 42 | `Invoke-CiUnit.ps1` | 无 | **删除** | 死代码回归 |
| `tests/Test-CiWorkflow.Tests.ps1` | 289 | 正则抽取旧 `ci.yml`（`windows-2022`、`Test-M0`、离线 guard） | `ci.yml` (`unit-check`) | **删除** | 强绑定已退役的 Windows CI 结构；新 Linux CI 靠实际 CI 运行与 `pal.ps1` 自测验证 |
| `tests/Test-Engineering.Tests.ps1` | 146 | `Test-Engineering.ps1` | 无 | **删除** | 随 `Test-Engineering.ps1` 退役 |
| `tests/Test-Range.Tests.ps1` | 225 | `Test-Range.ps1` | 无 | **删除** | 随 `Test-Range.ps1` 退役 |
| `tests/UnitEntry.Tests.ps1` | 137 | `Test-Unit.ps1` | 无 | **删除** | 随 `Test-Unit.ps1` 退役 |
| `tests/Test-PowerShellSyntax.Tests.ps1` | 109 | `Test-PowerShellSyntax.ps1` | `ci.yml` (`unit-check`) | **保留或精简** | 随语法脚本保留并精简（若并入 `pal.ps1` 则并入 `pal.ps1` 自测） |
| `tests/UnitBoundary.Tests.ps1` | 171 | `Test-UnitBoundary.ps1` | 无（仅 `Test-Engineering`） | **改写并保留** | 随 `Test-UnitBoundary.ps1` 同步更新（适配 `pal.ps1`、`coverlet.collector`） |
| `tests/AgentEnvironment.Tests.mjs` | 87 | `.pi/settings.json` 与已安装 `pi-subagents` worker override | 无（人工可选） | **保留** | 属 Pi worker 环境契约，与测试基建重构无关 |

> 小计：仅删除 17 个退役 PowerShell 回归，就可直接移除 **3,775 行**纯维护旧审计框架的脚本测试代码（另移除 26 个退役脚本约 **3,300+ 行**）。

### 3.2 其他构建/测试配置与历史单票测试（含约束 #14 单独清单）

| 文件 / 目录 | 说明 | 处置建议 | 依据 |
| --- | --- | --- | --- |
| `tests/Pal.Extract.Tests/Migration/baseline.json` | #32-A CLI 测试拆分的一次性历史对比基线（仅 `cli-test-migration.md` 和 `Test-Range.ps1` 引用） | **删除** | 单票历史迁移产物（注意：同父目录下 `tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs` 仍被两个 CLI 工程 `<Compile Include>`，**必须保留**） |
| `NuGet.Config` | 仅为了把 `gdUnit4.*` 映射到 `.packages/gdunit` | **删除或精简为纯 nuget.org** | 随约束 #13 退役 fork（同步清理 `.gitignore` L17–18 `/.packages/`） |
| `tests/Pal.Game.Tests/game.runsettings` | 当前被 `src/Pal.Game/Pal.Game.csproj` L9 引用 | **迁入拆分后的测试工程并改写** | #244 验证官方 rc7 + Linux headless 后按新参数重写 |
| `.gitattributes` L45 | 仅豁免 `src/Pal.Game/Tests/Fixtures/**` 的 LFS 追踪 | **同步改写** | 随 #244 拆工程迁移路径至新 `tests/Pal.Game.Tests/` 夹具路径 |
| **【约束 #14 交用户判断】** `src/Pal.Game/Tests/FailureControlTests.cs` + `ExpectedReadyException.cs/.tscn` | 3 个故意失败控制用例，仅服务 `Test-M0.ps1` 负控制，并迫使 `Pal.Game.csproj` L10 硬编码默认排除 `VSTestTestCaseFilter` | **建议删除（交用户确认）** | 退役 `Test-M0` 后，负控制不应污染业务测试工程；失败传播改由前门/CI 自测保障 |
| **【约束 #14 交用户判断】** `src/Pal.Game/Tests/M4FirstBattleGpuFixture.*`、`M4PhysicalDesktopFixture.*`、`RealDialogueFixture.*` | M3/M4 里程碑专用 GPU/桌面夹具场景（非 `[TestCase]` 自动化测试） | **待新 E2E 落地后退役或复用零件（交用户确认）** | #241 已确认 `M4FirstBattleGpuFixture` 的像素 oracle 零件可复用；新 `-- --pal-e2e` 覆盖原场景后再删旧夹具 |
| **【约束 #14 交用户判断】** `src/Pal.Game/Tests/RealFirstBattleFlowTests.cs` | 依赖私有未提交资产 `Assets/M4RealBattle53Closure`，原在 `GameTestBatches.ps1` 用 `$LocalOnlyRealMatches` 字符串硬排除 | **保留测试，改以 `[TestCategory("RealAssets")]` 归入 T3** | 符合约束 #14 原样迁移与 #243 类别过滤方案，不再在脚本里硬编码排除列表 |
| **【约束 #14 交用户判断】** `src/Pal.Game/Tests/ObserveMain.gd`、`ObserveProjectedBattle.*`、`ObserveStoryLost.*`、`ExplorationObserver.*` | 当前仍被 `MainProcessTests`、`StoryMainProcessTests`、`ExplorationProcessTests`、`SystemLoadSceneTests`、`ProjectedStoryChainTests` 实际调用 | **原样迁移保留** | 非死代码；未来 E2E 钩子成熟后可另行评估收敛 |
| **【约束 #14 交用户判断】** `tests/Pal.Core.UnitTests/EquipmentEntryRedTests.cs` | 文件名残留 `#48 unit 2` RED 过程命名，但内容为有效的装备闭包功能回归（1 `[Fact]`） | **保留测试逻辑**（不在 M7 删减；后续可并入 `TriggerScriptSessionTests`） | 功能有效，仅命名带单票过程痕迹 |

> 现状统计（`main@7f0567b`）：`Pal.Core.UnitTests` 637 个 `[Fact]/[Theory]`、`Pal.Extract.UnitTests` 380 个、`Pal.Extract.IntegrationTests` 155 个、`Pal.Prepare.Tests` 41 个、`src/Pal.Game/Tests/` **1,100** 行 `[TestCase]`（均按约束 #14 原样迁移，M7 不做去重删减）。

---

## 4. 文档去留与改写清单

### 4.1 `docs/testing/`（16/16，当前合计 1,240 行 / 153 KB）

| 文档 | 行数 / 大小 | 当前状态 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- |
| `docs/testing/agent-evidence.md` | 124L / 12.1 KB | 描述已淘汰 `Test-AgentEvidence.ps1` / `current.json` | **删除** | 随脚本退役（约束 #8、#12） |
| `docs/testing/ci-dispatch.md` | 51L / 7.1 KB | 描述旧 Windows `unit-check`/`full-check`，且与现 `ci.yml` 严重矛盾 | **删除** | 新 Linux CI 契约并入 `verification-matrix.md`（约束 #2、#10、#12） |
| `docs/testing/cli-test-migration.md` | 106L / 10.8 KB | #32-A 历史一次性拆分记录，含已更名路径 `Pal.Core.Tests` 与旧计数 | **删除** | 一次性迁移历史，git 历史留档（约束 #12） |
| `docs/testing/engineering-checks.md` | 116L / 12.7 KB | 描述 `Test-Engineering.ps1` 与 `#76 full-check`，含断锚点 | **删除** | 随 `Test-Engineering` 与旧 `full-check` 退役（约束 #2、#12） |
| `docs/testing/gdunit-source-packages.md` | 36L / 3.4 KB | 描述 GdUnit fork 锁与 bootstrap | **删除** | 随 fork 退役，ADR 记录取代关系（约束 #12、#13） |
| `docs/testing/native-script-binding-source-gate.md` | 144L / 21.1 KB | #176 单票源级研究门；**仓内 0 引用孤儿文档**，含大量本机 `C:/evidence/m6/...` 路径 | **删除** | 单票过程研究产物，不应长期留在活跃 `docs/testing/`（约束 #12） |
| `docs/testing/range-regression.md` | 85L / 13.0 KB | 描述 `Test-Range.ps1`、native-map marker、89 计数钉 | **删除** | 由 `pal.ps1` + `verification-matrix.md` 取代（约束 #7、#10） |
| `docs/testing/script-inventory.md` | 73L / 13.5 KB | #68「只盘点不删除」清单，且已漏列 3 文件 | **删除** | 彻底重构后统一入口为 `pal.ps1`，不再维护 39 脚本库存表（约束 #7、#12） |
| `docs/testing/test-cost-report.md` | 126L / 17.3 KB | 描述 `Get-TestCostReport.ps1` 成本协议 | **删除** | 随成本报告脚本退役（约束 #8、#12） |
| `docs/testing/unit-boundary.md` | 76L / 9.1 KB | 描述 `Test-UnitBoundary.ps1`，漏写实际根目录白名单与 `AssemblyAttribute` | **删除** | 精简后的静态边界说明浓缩为 `verification-matrix.md` 一小节（约束 #10） |
| `docs/testing/unit-entry.md` | 93L / 12.1 KB | 描述 `Test-Unit.ps1` 维护工具与旧 ADR-0003 细则 | **删除** | 由 `pal.ps1` 与 `verification-matrix.md` 取代（约束 #7、#8、#10） |
| `docs/testing/test-layers.md` | 26L / 4.5 KB | 旧「测试职责矩阵」（仅分层，缺四档触发与证据） | **由 `verification-matrix.md` 取代后删除** | 约束 #10：只维护一张「改了什么 → 跑哪档 → 交什么证据」矩阵，整合层（Layer）/ 档（Tier）术语 |
| `docs/testing/getting-started.md` | 57L / 4.0 KB | 教手写长 `dotnet test` 命令并引用 `GameTestBatches.ps1` | **改写或并入 `verification-matrix.md`** | 改为 `pal.ps1` 最短上手（若 `verification-matrix.md` 首屏即给常用命令，可直接删除本文件以减少入口分叉） |
| `docs/testing/game-test-layers.md` | 92L / 11.9 KB | SceneRunner 编码纪律 + 大量旧 `Test-M0`/fork/`DotnetCheckedDefinition` 段落 | **大幅改写并保留** | 保留高价值的 SceneRunner 编写契约（入树前 `Configure`、`UpdateCompleted` 边界、finally 释放、同 URI 隔离）；删除 fork 链接、`Test-M0`、`DotnetCheckedDefinition`、旧 `Game README` 指针 |
| `docs/testing/merge-protection.md` | 52L / 8.1 KB | 混杂 #38/#67/#69/#76 历史切换过程与操作守则 | **大幅重写并保留** | 改为新 Linux CI required checks 配置现状与受控修改/回读守则 |
| `docs/testing/time-boundaries.md` | 10L / 2.2 KB | 极精炼的核心时序验证硬纪律 | **保留（仅更新顶部链接指向 `verification-matrix.md`）** | 高密度无冗余硬规范 |

> 重构后 `docs/testing/` 从 **16 个文件 / 153 KB** 收敛为 **4 个文件**（`verification-matrix.md`、`game-test-layers.md`、`time-boundaries.md`、`merge-protection.md`，估算总计 < 20 KB）。

### 4.2 `docs/agents/`（8/8）

| 文档 | 行数 / 大小 | 处置建议 | 依据 / 需改写处 |
| --- | ---: | --- | --- |
| `docs/agents/runtime-workflow.md` | 52L / 6.0 KB | **删除** | 全文仅介绍即将退役的 `Export-AgentHandoff.ps1`、`Export-AgentSourceSnapshot.ps1` 和 `Test-M0`/`Test-Range` 的 GPU 环境变量检查 |
| `docs/agents/test-diagnostics.md` | 23L / 3.5 KB | **大幅改写并保留**（或精简后并入 `verification-matrix.md`） | 移除对 `Test-M0`、`gdunit-source-packages.md`、`archive/m3/test-diagnostics-history.md`、TRX 身份图的引用；纳入新「隔离清单 quarantine（门内不自动重试）」规则（约束 #6）；保留核心止损与失败分类纪律 |
| `docs/agents/large-task-handoffs.md` | 39L / 4.1 KB | **微调保留** | 移除 L24 对 `docs/testing/agent-evidence.md` 的指针与 L35 对 `runtime-workflow.md` 的指针 |
| `docs/agents/execution-environment.md` | 25L / 3.6 KB | **微调保留** | L14 注明 `engine-processes.ps1` 仅作 Windows LRA 局部守卫（与跨平台前门区分）；其余开工核对与编码规则保留 |
| `docs/agents/task-execution.md` | 35L / 5.4 KB | **保留** | 单任务 RED→GREEN 与有界纠正单源，与 M7 验证基建正交 |
| `docs/agents/domain.md` | 20L / 1.2 KB | **保留** | 领域文档规则 |
| `docs/agents/issue-tracker.md` | 46L / 2.5 KB | **保留** | GitHub Issue 与 Wayfinding 约定 |
| `docs/agents/triage-labels.md` | 11L / 0.4 KB | **保留** | 标签映射 |

### 4.3 `docs/archive/`（10/10，共 117 KB）

| 文档 | 行数 / 大小 | 分类 | 处置建议 | 依据 |
| --- | ---: | --- | --- | --- |
| `docs/archive/m2/gdunit-source-acceptance-history.md` | 155L / 9.3 KB | 测试工具链历史 | **删除** | 随 fork 退役，git 历史留档（约束 #12） |
| `docs/archive/m3/game-test-history.md` | 88L / 13.3 KB | 测试历史 | **删除** | 旧 M3 批次/TRX 身份快照，已被 M7 取代（约束 #12） |
| `docs/archive/m3/game-test-run-history.md` | 315L / 38.1 KB | 测试历史 | **删除** | 旧 M3 运行日志与命令墙（约束 #12） |
| `docs/archive/m3/test-diagnostics-history.md` | 99L / 14.9 KB | 测试历史 | **删除** | 旧 Windows 输出捕获/焦点诊断年表（约束 #12） |
| `docs/archive/engineering/spec31-retrospective.md` | 81L / 7.7 KB | 旧工程治理历史 | **删除** | #31 旧重型工程效率复盘（约束 #12） |
| `docs/archive/engineering/spec31-retrospective-data.json` | 252L / 7.4 KB | 旧工程治理数据 | **删除** | 伴随上文的统计 JSON，仓内 0 引用 |
| `docs/archive/m0/acceptance.md`、`extract-cli.md`、`game-startup.md`、`godot-compatibility.md` | 326L / 22.4 KB | M0 交付归档 | **待定（默认保留 M0 交付记录，或统一清零 `archive/`）** | 属 M0 里程碑产品历史而非纯 M2/M3 测试治理；其中 `godot-compatibility.md` 对应已删探针脚本，可在 #248 一并确认是否随 `archive/` 清空 |

### 4.4 `docs/adr/`（4/4）与入口文档

| 文档 | 行数 / 大小 | 处置建议 | 具体改写要点 |
| --- | ---: | --- | --- |
| `docs/adr/0001-faithful-event-transcription.md` | 5L / 0.8 KB | **保留** | 领域核心决策 |
| `docs/adr/0004-data-and-script-layers.md` | 10L / 1.1 KB | **保留** | 领域核心决策；补回 `docs/README.md` 索引 |
| `docs/adr/0002-unit-only-first-stage.md` | 15L / 2.0 KB | **保留原件，顶部标 `Superseded by ADR-000x`** | 约束 #12：仅在 ADR 记录取代关系；M7 改为 Linux CI 全量公开层（T1/T2） |
| `docs/adr/0003-native-unit-feedback.md` | 7L / 1.4 KB | **保留原件，顶部标 `Superseded by ADR-000x`** | 保留「信任原生工具」内核，由新 ADR 正式取代「保留全部强审计工具与完整门」过渡条款 |
| `src/Pal.Game/Tests/README.md` | 323L / **92.4 KB** | **删除** | 拆工程后原目录消失；后 275 行/85 KB 全是 M4/M6 单票过程记录与过时计数 |
| `README.md` | 155L / 16.6 KB | **改写** | 1) L13–21 当前状态更新至 M6 交付与 M7 调整；2) L33 CI 改 `ubuntu-24.04`；3) L35 删 GdUnit fork 锁；4) L37–60、L117–125 删 `Get-GdUnitPackages`/`Test-M0`/`unit-check`/`full-check`/`ci-dispatch` 段落，改为统一前门 `pal.ps1` + 指向 `verification-matrix.md`；5) L140–143 更新拆分后 `tests/Pal.Game.Tests/`，删 `script-inventory.md` 链接；6) L153 删 `ci-dispatch.md` 链接 |
| `docs/README.md` | 77L / 8.7 KB | **改写** | 1) L31–32 更新 M4 完成状态，补列 `m5/plan.md`、`m6/plan.md`、`m6/m6c-save-matrix.md`；2) L36–54 `testing/` 从 16 项收敛为 4 项，修复 L41 断锚点 `large-task-handoffs.md#结构化交接`；3) L55–69 同步清理已删 archive；4) L77 补列 `ADR-0004` 与 M7 新 ADR |
| `tools/long-running-agent/README.md` | 229L / 26.3 KB | **随 LRA 接入前门同步改写** | 更新 L129（强身份/gate_failure 白名单）、L166–167（`range_test` 调 `Test-Range.ps1`）、L176–177（`--fixed-package-directory`） |
| `tools/long-running-agent/EXPLICIT_PLAN.md` | 173L / 13.9 KB | **清理坏链或评估退役** | L5 引用即将删除的 `docs/testing/test-layers.md` 与旧 `current.json`；如不再保留旧 v1 显式计划模式可另立票移除 |

### 4.5 LRA 代码侧前门接口耦合点（供 #246 / #248 编排依赖）

以下 6 处是 `tools/long-running-agent/` 对旧测试脚本的硬依赖，删除 `Test-Range.ps1` / `GameTestBatches.ps1` / `warning-baseline.json` 前须按新前门接口完成切换：

1. `tools/long-running-agent/checks.ts` L10, L34–40, L43–92：硬编码 `gameBatches`、`--fixed-package-directory`、`scripts/Test-Range.ps1`、`range-run.json` 与 `[range:warning-delta|discovery|source-count]` 前缀。
2. `tools/long-running-agent/check-failure.ps1` L33：Prepare/Game 分支直接 `. (Join-Path $RepositoryRoot 'scripts/GameTestBatches.ps1')` 调用 `Read-TestRunIdentity`。
3. `tools/long-running-agent/github-agent.ts` L147, L196：路径授权与 `range_test` 工具描述绑定 `scripts/Test-Range.ps1`。
4. `tools/long-running-agent/checks.test.ts` L148–266, L346：测试旧 `Test-Range` / `GameTestBatches` gate 文本，并在 L266 直接调用 `scripts/Test-PowerShellSyntax.ps1`。
5. `tools/long-running-agent/github.test.ts` L674–704：测试 `[range:warning-delta]` 与 `scripts/warning-baseline.json`。
6. `tools/long-running-agent/policy.test.ts` L123：断言 `check.args` 包含 `Test-Range.ps1`。

---

## 5. `AGENTS.md` 逐条审查与改写建议

当前 `AGENTS.md` 共 67 行（6.65 KB）。下表按区块核对与 M7 约束及仓库现状的冲突：

| 行号 | 当前条款摘要 | 判定 | 改写建议 |
| --- | --- | --- | --- |
| L1–39 | 执行环境、查阅与事实来源、Issue tracker、Triage labels、Domain docs、单票执行、多票协调 | **保留** | 与 M7 正交；均属于稳定高频工作流指针 |
| L40–43 | 项目边界：`Pal.Core` 纯 .NET、`reference/README.md` 只读参考、忠实移植与扩展区分 | **保留** | 不可逆架构/版权硬边界 |
| **L44** | 「当前运行时按已交付 M2/M3 边界使用 Godot 原生 Resource……M3 独立 scene4/map10/owner60 切片……」 | **腐烂 → 改写** | M4/M5/M6 均已主线交付。删掉过时的里程碑能力边界列举，只保留不变硬规则：**运行时仅消费 Godot 原生资源，不读 JSON/MKF 或自动同步；复用已准备资源，真实原始数据及派生资源永不提交、不上传** |
| **L46–50** | 「测试」段落：分散指向 `getting-started.md`、`unit-entry.md`、`test-layers.md`、`game-test-layers.md`、`time-boundaries.md`、`test-diagnostics.md` 等 6 篇文档 | **冲突 → 压缩为单指针** | 约束 #10：收敛为指向单一权威 `docs/testing/verification-matrix.md`（改了什么 → 跑哪档 → 用什么 `pal.ps1` 命令 → 交什么证据），并保留 `time-boundaries.md` / `game-test-layers.md` 的触发指针 |
| **L52–57** | 「执行与证据」：前半要求核对共享验收脚本/runsettings/TRX 身份图/预期失败控制；后半规定本地 Godot 全局串行、进程隐私（仅 PID/Name）、崩溃/超时止损不自动重试、区分 headless/GPU/人工桌面 | **拆分：删冲突项，保留硬约束** | 删除「共享验收脚本」「TRX Summary/RunInfo 审计」「预期失败控制」等旧重型口径，改为按 `pal.ps1` + 标准退出码/TRX/CI summary 判定；**完整保留**本地 Godot 全局串行、不杀未知进程、进程隐私边界、崩溃/超时止损（门内不自动重试，偶发进 quarantine）及多面不互代 |
| **L59–61** | 「时序与外部调度延迟」锚点节 | **冗余 → 删除或并入测试节** | 与 L49 重复，仅为旧链接保留的空转发节；检查仓内无外部必须锚点后合并为一句 |
| **L65** | 「工具链按批准配置执行……#18 已另获批准交付固定 GdUnit 源码包与 bootstrap（`gdunit-source-packages.md`）……不自行统一不同测试项目的版本」 | **冲突 → 改写** | 删除 `#18` GdUnit fork 锁指针（约束 #13），保留「工具链/引擎/依赖基线按批准配置执行，升级需明确授权」 |
| **L66** | 「已知异常与误判边界：遇到 Windows GdUnit 输出捕获异常、焦点恢复时序断言失败……或准备运行 M2/M3 共享门禁时，先读 `test-diagnostics.md`」 | **冲突/过时 → 改写** | 删除历史 M2/M3 共享门禁与 Windows 特例罗列，改为指向精简后的失败分类与隔离清单（quarantine）规则 |
| **L67** | 服务端合并保护边界（`merge-protection.md`） | **保留（目标文档重写后保持指针）** | 核心原则有效（workflow / App 绑定 ≠ 服务端保护状态） |

---

## 6. 代理上下文一次性审计（并入 #230）

> 方法借用 `agent-context-audit`（盘点上下文面 → 五类横切检查 → 知识测验 → 报告），**严格遵守 #230 边界**：不借用「砍 80% 规则」哲学，所有不可逆/安全类硬约束受保护（见 6.4）。

### 6.1 上下文面体积盘点

| 层级 | 文件集合 | 文件数 | 总行数 | 总字节 | 评估 |
| --- | --- | ---: | ---: | ---: | --- |
| **Always-loaded**（每会话必读） | `AGENTS.md` (6.65 KB) + `docs/agents/execution-environment.md` (3.59 KB) | 2 | 92 | **10.2 KB** | 体量健康；M7 改写后可进一步缩至 ~7.5 KB，提高信噪比 |
| **高频按需（工作流/领域）** | 其余 `docs/agents/*` (7) + `GLOSSARY.md` + `docs/README.md` + `README.md` | 10 | 505 | **51.6 KB** | `README.md` 与 `docs/README.md` 存在明显里程碑状态滞后 |
| **LRA 工具文档** | `tools/long-running-agent/README.md` (26.3 KB) + `EXPLICIT_PLAN.md` (13.9 KB) | 2 | 402 | **40.2 KB** | 仅运行 LRA 时读；`README.md` 偏长，含 M6 期间多段补丁式长段落 |
| **重灾区 1：`docs/testing/*`** | 全部 16 篇测试文档 | 16 | 1,240 | **153.0 KB** | **严重膨胀**；历史治理文档占比 >75%，M7 收敛后预计降幅 >85% |
| **重灾区 2：Game 测试 README** | `src/Pal.Game/Tests/README.md` | 1 | 323 | **92.4 KB** | **最严重单文件陷阱**；被 `AGENTS.md` → `game-test-layers.md` 引用，其中 85 KB 为单票流水账 |
| **归档区：`docs/archive/*`** | 10 篇历史归档 | 10 | 1,288 | **117.0 KB** | 标为按需查阅，其中 6 篇测试/治理历史随 M7 退役可直接删除 |

### 6.2 横切审计发现（Findings）

#### A. 跨层冲突（Contradictions）

| ID | 严重度 | 位置 | 原始引文 / 事实对比 | 改写建议 |
| --- | --- | --- | --- | --- |
| **C-1** | **P1** | `docs/testing/ci-dispatch.md:17,31` vs `.github/workflows/ci.yml:41-80,103-110` | 文档称「Unit 自动路径（unit-check job）不运行 Source/Offline……artifact 仅上传两份本轮 TRX」；实际 `ci.yml` 在 `unit-check` 运行 `Test-UnitBoundary.ps1`、5 项离线 PS 回归与 LFS 守卫，并上传 7 组 artifact | 随 M7 删除 `ci-dispatch.md`，新 CI 结构以 `verification-matrix.md` 为唯一真相源 |
| **C-2** | **P1** | `README.md:41` vs `docs/testing/ci-dispatch.md:15` & `.github/workflows/ci.yml:112-177` | `README.md:41` 称「过渡期仍明确失败的旧 `full-check`」；实际 `ci.yml` 已在 #76 替换为 `Test-M0.ps1 -Scope committable` | 随 M7 重写 `README.md` 测试段落，反映新 Linux CI |
| **C-3** | **P1** | `docs/testing/unit-boundary.md:36-41` vs `scripts/Test-UnitBoundary.ps1:12-25,39` | 文档列举了项目 XML 允许规则，完全未提及脚本实装的 `Test-RootFiles` 仓库根目录白名单，也未提及已允许的 `AssemblyAttribute`（`InternalsVisibleToAttribute`） | 删除旧 `unit-boundary.md`；新静态守卫规则直接简明写在 `verification-matrix.md` |
| **C-4** | **P2** | `docs/testing/script-inventory.md:24` vs `.github/workflows/ci.yml:48` | 清单写 `Test-UnitBoundary.ps1` 的调用方仅 `Test-Engineering` / `Test-Unit` / `UnitBoundary.Tests.ps1`，漏掉 `ci.yml` 的直接调用 | 随 M7 删除 `script-inventory.md` |

#### B. 文档腐烂（Rot / Map ≠ Territory）

| ID | 严重度 | 位置 | 原始引文 / 现状偏差 | 改写建议 |
| --- | --- | --- | --- | --- |
| **R-1** | **P1** | `AGENTS.md:44` | 「当前运行时按已交付 M2/M3 边界……M3 独立 scene4/map10/owner60 切片……」——滞后 3 个里程碑（M4/M5/M6 已主线交付） | 改为只陈述不变的架构与真实资源边界，不写动态里程碑进度 |
| **R-2** | **P1** | `README.md:15-21`、`docs/README.md:31-32` | `README.md` 进度止步于 M3；`docs/README.md` 称 M4「尚未完成整个 M4 或主线交付」，且漏列已存在的 `docs/milestones/m5/plan.md`、`m6/plan.md`、`m6/m6c-save-matrix.md` | 更新至 M6 已合入 `main` 与 M7 验证基建（#232），补齐 M5/M6 导航链接 |
| **R-3** | **P1** | `src/Pal.Game/Tests/README.md:48-323` | 92.4 KB 文件中 85 KB 是 M4/M6 单票流水账，登记的过时计数（647/650/856/951/978/983）与实际 1,100 `[TestCase]` 脱节，且含大量本机绝对路径 `C:/evidence/m6/...` | 拆测试工程后删除本文件 |
| **R-4** | **P2** | `docs/README.md:41`、`docs/testing/engineering-checks.md:89` | **真实断锚点**：`[六项短收据](agents/large-task-handoffs.md#结构化交接)` —— 目标文件无 `#结构化交接` 标题 | `engineering-checks.md` 删除；`docs/README.md:41` 随 `runtime-workflow.md` 退役一并移除 |
| **R-5** | **P2** | `docs/testing/native-script-binding-source-gate.md:1-144` | 21.1 KB 孤儿文档（仓内 0 引用），绑定过时分支 `execution/m6-loop` 与本机目录 `C:/evidence/m6/...` | 删除（git 历史留档） |
| **R-6** | **P2** | `docs/testing/script-inventory.md` | 仅列 36/39 个 `scripts/` 文件，漏列 `Prepare-FireEffects.ps1`、`Prepare-ItemBitmaps.ps1`、`warning-baseline.json` | 删除（统一入口为 `pal.ps1`） |
| **R-7** | **P2** | `docs/testing/cli-test-migration.md:94` | 表格仍写已更名的 `tests/Pal.Core.Tests/Pal.Core.Tests.csproj` 及旧计数 328/142/188/52/242 | 删除 |
| **R-8** | **P3** | `docs/README.md:77` & Issue #230 | `docs/README.md` 漏列已存在的 `docs/adr/0004-data-and-script-layers.md`；另核实 #230 提到的「ADR-0005（signals 层）」在 `main@7f0567b` 下不存在（`docs/adr/` 仅 0001–0004） | `docs/README.md` 补列 ADR-0004；向维护者确认 ADR-0005 去向，避免 M7 新 ADR 撞号 |

#### C. 冗余（Redundancy）

| ID | 严重度 | 位置 | 问题描述 | 改写建议 |
| --- | --- | --- | --- | --- |
| **D-1** | **P2** | `AGENTS.md`、`execution-environment.md`、`test-diagnostics.md`、`getting-started.md`、`unit-entry.md`、`test-layers.md`、`game-test-layers.md`、`src/Pal.Game/Tests/README.md`、`README.md` | 「全局串行 / 不杀未知进程 / 崩溃超时停止不自动重试 / headless 不替代 GPU 和人工」在 **9 处**以不同措辞重复，改一处必然产生漂移 | 确定单点权威：原则放 `AGENTS.md`（3 行），操作细节放 `verification-matrix.md`，其余只留指针 |
| **D-2** | **P3** | `AGENTS.md:49` vs `AGENTS.md:59-61` | 同一 67 行文件内两次指向 `docs/testing/time-boundaries.md`（L59–61 为纯旧锚点转发节） | 合并为一条指针 |
| **D-3** | **P2** | `docs/testing/getting-started.md` vs `docs/testing/unit-entry.md:5-19` | 两文对默认 Unit 流程几乎逐条重复 | 随 M7 删除 `unit-entry.md`，合并至 `verification-matrix.md` |

#### D. Unknown-knowns（代码/CI 载重但无文档的事实）

| ID | 严重度 | 实际载重位置 | 隐性约束内容 | M7 应对建议 |
| --- | --- | --- | --- | --- |
| **U-1** | **P1** | `scripts/Test-UnitBoundary.ps1:12-25` | 仓库根目录文件/目录硬编码白名单，CI `unit-check` 每次必跑；任何未登记的根目录新文件（包括 `pal.ps1`）会直接令 CI 失败 | 改写 `Test-UnitBoundary.ps1` 时放行 `pal.ps1`（或明确写入 `verification-matrix.md`） |
| **U-2** | **P1** | `.github/workflows/ci.yml:66-80` + `.gitattributes:45` | CI 禁止 `src/tests/tools` 下出现 Git LFS 指针（`git grep --cached`）；仅 `src/Pal.Game/Tests/Fixtures/**` 在 `.gitattributes` 豁免 LFS | 保留该 LFS 守卫进新 T1 CI，并在 #244 拆工程时同步更新 `.gitattributes` 豁免新测试夹具目录，同时写入 `verification-matrix.md` |
| **U-3** | **P1** | `tools/long-running-agent/check-failure.ps1:33` & `checks.test.ts:266` | LRA 跨目录硬依赖 `scripts/GameTestBatches.ps1` 与 `scripts/Test-PowerShellSyntax.ps1` | 在 #248 叶票依赖图中显式排在删除旧脚本之前 |
| **U-4** | **P2** | `src/Pal.Game/Pal.Game.csproj:9-10` | 默认绑定 `game.runsettings` 与 `VSTestTestCaseFilter` 排除 `FailureControlTests` | #244 拆工程时同步移除，让 `Pal.Game.csproj` 回归纯生产工程 |
| **U-5** | **P2** | `tests/Pal.Extract.IntegrationTests/*.csproj:21-26` & `tests/Pal.Prepare.Tests/*.csproj:11,21-26` | 自定义 `CopyCliForProcessTests` / `CopyCliForProjectedChainTests` 构建目标，以及 `Pal.Prepare.Tests` 对 `Pal.Game.csproj` 的 build-only `ProjectReference` | #243 依赖图 / T0 增量选测与 #244 拆工程时须保留这些依赖边 |
| **U-6** | **P2** | `scripts/warning-baseline.json` | 编译器 warning 数量上限基线（未见于 `script-inventory.md` 或 `AGENTS.md`） | 在 #246/#248 决定转用 MSBuild 原生警告策略或删除 |
| **U-7** | **P3** | `tools/long-running-agent/checks.ts:157-267` | Godot 4.4+ 自动生成 `.cs.uid` 会触发 LRA 源码指纹漂移，LRA 会在引擎检查前运行 `stabilizeGodotUids` 做 `--editor --import --quit` 预热 | 已在代码注释清晰说明；若拆工程后测试脚本移出 Godot 工程根，可显著减少 `.uid` 噪声 |

### 6.3 知识测验（8 题，仅凭当前被审文档作答）

| # | 新会话必答问题 | 仅凭当前文档能否答对 | 结果与归因 |
| --- | --- | --- | --- |
| Q1 | 如何只跑 Core 单元测试？ | `AGENTS.md:48` → `getting-started.md:46` 给出完整 `dotnet test` 命令与 `unit.runsettings` | **通过** |
| Q2 | 哪些文件绝对不能提交或上传？ | `AGENTS.md:44` 与 `README.md:82` 能答出 `data/raw`、`data/extracted`、`src/Pal.Game/Assets` 与私密证据，但**答不出**根目录白名单禁令与 `src/tests/tools` 禁 LFS 指针 | **部分通过（缺口 U-1 / U-2）** |
| Q3 | 在仓库根目录新增 `pal.ps1` 或给 Unit 工程加 `coverlet.collector` 会触发哪项 CI 失败？ | 文档中查不到 `$AllowedRootFiles`，且 `unit-boundary.md` 未反映 `ci.yml` 调用链 | **答不出（缺口 U-1 / C-3）** |
| Q4 | 当前项目处于哪个里程碑？哪些能力已合入 `main`？ | `AGENTS.md:44`、`README.md:15-21`、`docs/README.md:31` 均指向「M3 已交付、M4 未完成」 | **答错（腐烂 R-1 / R-2）** |
| Q5 | 向 PR 推送新提交时，GitHub CI 实际跑哪些检查？ | `ci-dispatch.md:17,31` 与 `README.md:41` 称 `unit-check` 只跑 2 个 `dotnet test` 且 `full-check` 为显式失败占位 | **答错（冲突 C-1 / C-2）** |
| Q6 | 如何选择测试层（Core Unit / CLI Integration / Prepare / Game SceneRunner / 真实进程）？ | `AGENTS.md:48` → `docs/testing/test-layers.md` 表格清晰 | **通过**（M7 将合并进 `verification-matrix.md`） |
| Q7 | 修改了业务冷却或输入更新边界后应如何验证？ | `AGENTS.md:49,61` → `docs/testing/time-boundaries.md` 准确完整 | **通过** |
| Q8 | 测试发生崩溃、超时或输出不完整时，可以自动重跑看是否偶发吗？ | `AGENTS.md:57`、`test-diagnostics.md:11`、`task-execution.md:33` 一致明确：立即保全停止，禁止自动重试 | **通过**（M7 约束 #6 延续门内不自动重试） |

> 测验结论：8 题中 **4 通过、1 部分通过、1 答不出、2 答错**。全部失分集中在「CI 与边界守卫文档失真（C-1/C-2/U-1/U-2）」和「里程碑进度写死在静态文档导致腐烂（R-1/R-2）」，恰好印证了 M7 用单一 `verification-matrix.md` + 彻底删除过时文档的必要性。

### 6.4 不可逆与安全类硬约束保护清单（#230 边界明示）

M7 重写 `AGENTS.md` 与 `docs/testing/` 时，以下 **6 条硬规则不得被精简、弱化或改为软建议**：

1. **授权边界**：提交、推送、合并、依赖/工具链升级、分支保护修改与远端写操作必须有明确授权；禁止静默越权。
2. **版权与真实资产隔离**：`data/raw/`、`data/extracted/`、`src/Pal.Game/Assets/` 及任何私密证据永不提交、永不上传 CI artifact；参考源码 `reference/sdlpal/` 只读不编译。
3. **本地引擎串行与进程隐私**：本机 Godot / GdUnit / 资源准备执行全局串行；不擅自结束未知 Godot 进程；进程查询仅限 PID/Name，不采集外部项目命令行或日志。
4. **异常止损与证据完整性**：崩溃、中止、超时、未知退出或缺失结果立即保全现场停止；门内不自动重试（确认偶发走显式 quarantine 清单）；保留零测试/skip 报错；不以局部通过拼接全量。
5. **验证面不互代**：Unit 不替代引擎接线；headless 不替代 GPU 像素核对；GPU 截图不替代人工桌面观感确认；workflow 文件不等于服务端分支保护生效。
6. **核心架构边界与确定性时间**：`src/Pal.Core` 保持纯 .NET 零 Godot 依赖；精确业务时间在 Core 显式时间接口验证，Game 集成测试在实际更新完成边界（`UpdateCompleted`）断言。

---

## 7. 给规格定稿（#248）的退役顺序建议

为避免拆旧删新之间互相打断，建议在 #248 拆叶票时遵循以下顺序：

1. **前置解除阻断**：先改写 `Test-UnitBoundary.ps1`（放行根目录 `pal.ps1` 与 `coverlet.collector`）与 `.gitattributes`（覆盖拆分后的 `tests/Pal.Game.Tests/` 夹具路径）。
2. **拆工程与退役 GdUnit fork（#244 落地）**：验证官方 nuget.org 包通过后，随叶票删除 `Get-GdUnitPackages.ps1`、`gdunit-packages.lock.json`、`GdUnitPackages.Tests.ps1`、`gdunit-source-packages.md`、`NuGet.Config` fork 映射、`FailureControlTests`（若用户批准）。
3. **新前门 `pal.ps1` 与新 Linux CI（#246 / #242 落地）**：上线新 Linux CI 并完成合并保护切换后，删除旧 `full-check`、`Test-M0.ps1`、`Test-Unit.ps1`、`Test-GameStartup.ps1`、`Test-GodotCompatibility.ps1`、5 个旧 CI 强审计脚本、2 个成本报告脚本、5 个手工交接/证据脚本及 `Test-Engineering.ps1` 与其对应的全部 `.Tests.ps1`（16 个）。
4. **LRA 接口切换到 `pal.ps1`**：迁移 `tools/long-running-agent/` 的 `checks.ts`、`check-failure.ps1`、`github-agent.ts` 及测试后，再删除最后残留的 `Test-Range.ps1`、`GameTestBatches.ps1`、`TestRunDefinitions.ps1`、`DotnetCheckedDefinition.ps1`、`Test-Range.Tests.ps1`、`GameBatches.Tests.ps1`、`RangeIdentity.Tests.ps1`、`DotnetBoundary.Tests.ps1`。
5. **新 T3 E2E 落地（#241 / #247）**：覆盖 M3/M4/M6 场景并验证通过后，删除 `Test-RealDialogueGpu.ps1`、`Test-M4FirstBattleGpu.ps1`（及按 #247 决定处置 `Test-M4PhysicalDesktop.ps1`）与旧里程碑专用 GPU fixture。
6. **文档与 `AGENTS.md` 最终收口**：删除 12 篇旧 `docs/testing/*`、6 篇 `docs/archive/*` 测试历史、`docs/agents/runtime-workflow.md`、`src/Pal.Game/Tests/README.md`；发布 `docs/testing/verification-matrix.md`、重写 `AGENTS.md`/`README.md`/`docs/README.md`/`game-test-layers.md`/`merge-protection.md`/`test-diagnostics.md` 并为 ADR-0002/0003 标明取代关系。
