# scripts 用途与生命周期清单

普通 work agent 反馈使用[短指南](getting-started.md)中的直接 Unit 命令，**本表没有默认本地测试编排入口**。按 #61/#68 首批决定只盘点、隔离默认路径，不移动、重命名、归档或删除工具。

## 搜索边界与读法

初始 #68 库存以 `d9bc96e4cb4166600cab6027a7d6370221d01d55` 为 base，覆盖 `scripts/` 全部已跟踪文件（含模块和锁文件）。本次原生 Unit 调整基于 `a93e9e3694ef0c8a286f4e85559edcf0f75dea3c` 复查受影响的 workflow/维护回归调用；脚本本体未改。搜索含 `.github/`、`scripts/`、`tests/`、`src/`、当前文档，排除 `reference/` 和 `docs/archive/`。下表区分实际运行调用、仅定义加载、回归夹具与文档入口；字符串命中不直接等同执行。

**外部调用方一律未知**：不覆盖未跟踪文件、工作区外工具、旧提交、其他仓库、用户手工命令或服务端临时调度。仓内无调用不证明可删除。源码/调用方变化时同步本表；当前运行能力仍以脚本、配置和对应任务证据为准，本次文档核对没有运行这些工具。

路径均相对 `scripts/`；“保留”指保留当前职责，不为未执行层补签通过。

## 公开入口（全部显式选择）

| 路径 | 职责 | 已知调用方／边界 | 入口身份与处置 |
| --- | --- | --- | --- |
| [`Test-Unit.ps1`](../../scripts/Test-Unit.ps1) | 维护用 Unit 边界、发现/执行集合与证据编排 | `tests/UnitEntry.Tests.ps1`；[Unit 专题](unit-entry.md)；CI 契约检查其不成为默认入口 | 显式维护；保留，不替代 agent 直接命令 |
| [`Test-Range.ps1`](../../scripts/Test-Range.ps1) | 明确选择 CLI integration、Prepare、Game 批次及范围证据 | `tests/Test-Range.Tests.ps1`；[范围入口](range-regression.md)与 Game README | 显式非完整门；保留 |
| [`Test-Engineering.ps1`](../../scripts/Test-Engineering.ps1) | Source、Offline、OwnedProcess 三类工程检查 | `tests/Test-Engineering.Tests.ps1`；[工程检查](engineering-checks.md)；当前自动 workflow 不调用 | 显式工程入口；保留，不进入默认 Unit |
| [`Test-PowerShellSyntax.ps1`](../../scripts/Test-PowerShellSyntax.ps1) | 仅解析选定 PowerShell 源码，拒绝语法错误 | `Test-Engineering.ps1`；`tests/Test-PowerShellSyntax.Tests.ps1` | 显式 Source／被调用工具；保留 |
| [`Test-UnitBoundary.ps1`](../../scripts/Test-UnitBoundary.ps1) | 校验 Unit 项目、引用与源码边界 | `Test-Engineering.ps1`、`Test-Unit.ps1`；`tests/UnitBoundary.Tests.ps1` | 显式边界检查；保留；静态边界不等于产品 Unit 通过 |
| [`Get-TestCostReport.ps1`](../../scripts/Get-TestCostReport.ps1) | 只读既有证据生成成本报告，不运行测试 | `Test-CiFullProof.ps1`；`tests/Pal.Game.Tests/TestCostReport.Tests.ps1`；[成本口径](test-cost-report.md) | 显式只读工具；保留 |
| [`Get-TestProcesses.ps1`](../../scripts/Get-TestProcesses.ps1) | 输出隐私受限的 Godot／本仓测试执行器快照 | `Test-M0.ps1`、`Prepare-DialoguePackage.ps1`、`Test-RealDialogueGpu.ps1`、`Write-StaticScene4Profile.ps1`；ProcessSnapshot 回归 | 显式诊断／准备前保护；保留，不用于全机详细采集 |

## 内部支持（由调用方加载定义）

| 路径 | 职责 | 已知调用方／边界 | 入口身份与处置 |
| --- | --- | --- | --- |
| [`DotnetCheckedDefinition.ps1`](../../scripts/DotnetCheckedDefinition.ps1) | dotnet root/双流共用预算、自有清理与原始退出留证 | `Test-M0.ps1`、`Test-Range.ps1`；DotnetBoundary 回归 | 内部定义；保留，不单独当测试入口 |
| [`GameTestBatches.ps1`](../../scripts/GameTestBatches.ps1) | Game 发现/批次及共享 TRX 身份图定义 | M0、Range、TestCostReport；Unit 维护工具及保留的旧 CI executor **仅加载定义**；GameBatches/RangeIdentity/强审计回归 | 内部定义；保留；原生 Unit workflow 不加载它，加载不表示启动 Godot |
| [`TestRunDefinitions.ps1`](../../scripts/TestRunDefinitions.ps1) | xUnit discovery、Unit TRX 与发现/执行集合校验 | `Test-Unit.ps1`、`Test-Range.ps1`、保留的 `Invoke-CiUnit.ps1`；RangeIdentity/Test-CiUnit 维护回归 | 内部定义；保留；原生 Unit workflow 不调用 |
| [`TestCostReport.psm1`](../../scripts/TestCostReport.psm1) | 成本解析、证据完整性与可比性逻辑 | `Get-TestCostReport.ps1` 导入；TestCostReport 回归 | 内部模块；保留 |

## 资源准备与工具链（均非 Unit 前置）

| 路径 | 职责 | 已知调用方／边界 | 入口身份与处置 |
| --- | --- | --- | --- |
| [`Get-Godot.ps1`](../../scripts/Get-Godot.ps1) | 下载固定 Windows .NET 引擎到新目录并核 SHA512，不安装/PATH | 搜索范围内未查到运行调用或当前文档入口；外部未知 | 显式工具链准备；保留，后续是否退役另核 |
| [`Get-GdUnitPackages.ps1`](../../scripts/Get-GdUnitPackages.ps1) | 校验锁定四文件 bundle 并 bootstrap | M0、Range；GdUnitPackages 回归；[包说明](gdunit-source-packages.md) | 显式 Game 前置；保留，普通 Unit 无需调用 |
| [`gdunit-packages.lock.json`](../../scripts/gdunit-packages.lock.json) | 固定包来源、recipe、文件长度/散列与地址 | Get-GdUnitPackages、Engineering、Range；GdUnitPackages 回归 | 资源身份数据，不可执行；保留 |
| [`Prepare-GameScene.ps1`](../../scripts/Prepare-GameScene.ps1) | M1 包显式离线转换为 Godot native 场景/资源，拒绝覆盖 | `Test-GameStartup.ps1`；PreparePathSafety/PrepareProcess 回归；根 README | 显式资源准备；保留，不自动提取/同步、不创建探索 profile |
| [`Prepare-DialoguePackage.ps1`](../../scripts/Prepare-DialoguePackage.ps1) | 显式转换现代对话包为 native 资源并 import | 搜索范围内无脚本运行调用；[对话文档](../design/dialogue-display.md)为手工入口；外部未知 | 显式资源准备；保留，复用已准备资源 |
| [`Write-StaticScene4Profile.ps1`](../../scripts/Write-StaticScene4Profile.ps1) | 只读 DATA.MKF role0 WORD，author 固定 scene4 profile，拒绝覆盖 | 搜索范围内未查到运行调用或当前文档入口；外部未知 | 显式一次性 author 工具；保留，退役另核；不上传真实输入/输出 |

## CI／验收证据（当前自动、显式与暂停分开）

当前原生 workflow 直接执行两个项目与 `tests/unit.runsettings`，不调用本节脚本。`tests/Test-CiWorkflow.Tests.ps1` 显式验证实际 block/真实 dotnet；下列强审计工具保留原契约，不因退出默认调用链而删除，取舍见 [ADR-0003](../adr/0003-native-unit-feedback.md)。

| 路径 | 职责 | 已知调用方／边界 | 入口身份与处置 |
| --- | --- | --- | --- |
| [`Invoke-CiUnit.ps1`](../../scripts/Invoke-CiUnit.ps1) | 旧 CI 的两工程执行及强审计/成本采证 | 已退出当前 workflow/Workflow 回归；搜索范围内无其余运行调用，外部未知 | 显式旧能力；保留，使用时仍需原候选接口，不是原生反馈入口 |
| [`New-CiCandidateInputs.ps1`](../../scripts/New-CiCandidateInputs.ps1) | 原事件与服务端身份传输，保存 API 原件/有证据的 merge 等价关系 | Candidate-Inputs 维护回归；已退出 workflow start/end | 显式强审计；保留 |
| [`Test-CiCandidate.ps1`](../../scripts/Test-CiCandidate.ps1) | 校验实际 checkout/tree/parents、run/attempt/mode 与候选证明 | Candidate/Candidate-Inputs 维护回归；已退出 workflow start/end | 显式强审计；保留，不把 Unit 身份当 FullProof |
| [`New-CiCostInput.ps1`](../../scripts/New-CiCostInput.ps1) | 旧完整 CI 的成本输入与候选证据关联 | 当前无 workflow/脚本运行调用；CiCostInput、CiFullProof 回归仍消费；外部未知 | 原自动路径暂停；保留兼容证据工具，恢复另立任务 |
| [`Test-CiFullProof.ps1`](../../scripts/Test-CiFullProof.ps1) | 旧完整验收候选、产物和成本证明校验，拒绝 Unit mode | 当前无 workflow/脚本运行调用；CiFullProof 回归与 CI 文档仍消费；外部未知 | 原自动路径暂停；显式维护保留，不以旧成功满足 Unit 要求 |
| [`Test-M0.ps1`](../../scripts/Test-M0.ps1) | 唯一保留的重型完整门：缓存/solution、各测试层、Game 重复轮/负控制及 startup | 根 README、Game README；DotnetBoundary/GdUnitPackages 回归检查定义；无当前自动调用 | 显式完整验收；保留，Unit 阶段不默认运行 |
| [`Test-GameStartup.ps1`](../../scripts/Test-GameStartup.ps1) | 人工资源真实 Godot import/Main 启动与指定拒绝控制 | `Test-M0.ps1` 直接调用，内部用 Prepare-GameScene | 显式验收子入口；保留，不证明 GPU/物理桌面 |
| [`Test-GodotCompatibility.ps1`](../../scripts/Test-GodotCompatibility.ps1) | 外部人工项目、生成器/net10 构建/引擎兼容与负控制探针 | 搜索范围内未查到运行调用或当前文档入口；外部未知 | 特殊兼容调查；保留，不能从历史探针推断当前运行成功 |
| [`Test-RealDialogueGpu.ps1`](../../scripts/Test-RealDialogueGpu.ps1) | 选定语言/可选真实 Main 的 GPU 回读及程序输入；保护真实资源身份 | 无仓内脚本运行调用；[对话](../design/dialogue-display.md)和[NPC 宿主](../design/npc-interaction-host.md)为显式入口；外部未知 | 显式 GPU 证据，不是物理/人工验收；保留，不自动 prepare/build/清理 |

## 待核实退役项

**本次没有已批准退役项，也没有删除项。** 无仓内运行调用、旧 CI 暂停或一次性 author 性质只构成后续调查线索，不构成退役决定。后续若要处理 Get-Godot、兼容探针、静态 profile author 或旧 Full 证据工具，须分别核对存续调用方与外部使用、替代入口、原历史证据保留方案并另获批准；工程测试基础设施的物理更名也不混入本批。
