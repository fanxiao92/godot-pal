# 验证矩阵（Verification Matrix）

本页是 `godot-pal` 验证体系的**唯一当前态权威快照**。所有本地验证、CI 门禁、资源准备与证据报告均经仓库根目录跨平台前门 `./pal.ps1`（`pwsh` 7）执行；历史演进与架构取舍见 [ADR-0005](../adr/0005-m7-verification-infrastructure.md)、[ADR-0006](../adr/0006-split-game-tests-and-official-gdunit.md) 与 [ADR-0007](../adr/0007-t3-gpu-e2e-and-shared-asset-cache.md)。

## 1. 术语：层（Layer）与档（Tier）

### 测试层（Layer，按技术依赖划分）

| 层 | 工程 / 入口 | 依赖 | 证明什么 |
| :--- | :--- | :--- | :--- |
| **Core Unit** | `tests/Pal.Core.UnitTests`（`./pal.ps1 test unit --project core`） | 纯 .NET，无 Godot / 外部进程 | 领域规则、状态机、精确显式时间边界 |
| **Extract Unit** | `tests/Pal.Extract.UnitTests`（`./pal.ps1 test unit --project extract`） | 纯 .NET，无 Godot / 外部进程 | MKF/图像/脚本/数据表内存解码与往返逻辑 |
| **CLI Integration** | `tests/Pal.Extract.IntegrationTests`（`./pal.ps1 test integration --project extract`） | 文件系统与真实 CLI 进程 | CLI 参数、退出码、文件输出与散列清单 |
| **Prepare Integration** | `tests/Pal.Prepare.Tests`（`./pal.ps1 test integration --project prepare`） | 固定版 Godot headless | 离线资源准备脚本与 Godot 原生资源导入 |
| **Game Integration** | `tests/Pal.Game.Tests`（`./pal.ps1 test game`） | 固定版 Godot headless + 官方 GdUnit4 | 场景装配、真实输入/信号、`UpdateCompleted` 边界与最小真实进程契约 |
| **LRA** | `tools/long-running-agent`（`./pal.ps1 test lra`） | Node / npm | 长任务编排器单元测试与本地契约 |
| **T3 GPU E2E** | `tests/Pal.Game.Tests/E2E`（`./pal.ps1 test e2e --gpu`） | 本地 GPU + 真实原版数据共享缓存 | 真实原版资源渲染、YAML 端到端闭环、探针断言与本地金图容差比对 |

### 验证档位（Tier，按触发时机与范围划分）

| 档位 | 时机 | 执行命令 | 交付证据 |
| :--- | :--- | :--- | :--- |
| **T0** | 本地开发 / agent 日常迭代 | `pwsh -NoProfile -NonInteractive -File ./pal.ps1 test affected`（可加 `--explain` 预览） | 退出码 `0/1`、`run-summary.json`、命中层的 TRX |
| **T1** | PR 自动门禁（Linux `ubuntu-24.04` 3 并行 job） | Job `dotnet`：`./pal.ps1 ci dotnet`<br>Job `prepare`：`./pal.ps1 godot install` → `godot import` → `ci prepare`<br>Job `game`：`./pal.ps1 godot install` → `godot import` → `ci game` | 3 项必需检查全绿 + `$GITHUB_STEP_SUMMARY` + CI artifact（`run-summary.json` / TRX / JUnit / Cobertura 覆盖率） |
| **T2** | `main` push / 手动 `workflow_dispatch` / 本地全量复现 | CI 同源 3 并行 job；本地复现：`./pal.ps1 test full` | 同 T1（本地另附 `.artifacts/pal/<run>/run-summary.json` 与 TRX） |
| **T3** | 按下方触发规则显式执行（本地真实资源 + GPU） | `./pal.ps1 assets prepare` → `./pal.ps1 test e2e --gpu --out <dir>` → `./pal.ps1 report --from <dir> --out <dir>/pr-comment.md` | PR 证据评论（探针与金图 diff %、缓存键）+ 本地 `contact-sheet.png` 人工 5 分钟审阅（**不上传真实资产或版权截图**） |

## 2. 改动 → 档位 → 命令 → 证据速查矩阵

T0 映射规则由 [`tests/affected-map.psd1`](../../tests/affected-map.psd1) 与静态 `.csproj` 反向 `ProjectReference` 闭包共同驱动（映射自检命令：`./pal.ps1 test check-map`）。

| 改动范围 | T0（本地默认） | T1 / T2（Linux CI） | T3（本地 GPU E2E） | 需交付证据 |
| :--- | :--- | :--- | :--- | :--- |
| 纯文档 / 只读参考（`docs/**`、`*.md`、`reference/**`） | `test affected` 自动判定 no-op（退出 `0`） | T1 全量（含仓库文档断链护栏 `Test-RepoHygiene.ps1`） | 不触发 | T1 全绿 |
| `src/Pal.Core/**` 或 `tests/Pal.Core.UnitTests/**` | `test affected`（反向依赖闭包，命中 `core-unit` 及受影响下游） | T1 全量（含 coverlet 覆盖率展示） | 改动影响战斗/存档/对话/探索主链行为时触发 | T0 摘要 + T1 全绿（+ 按需 T3 评论） |
| `tools/Pal.Extract/**` 或 `tests/Pal.Extract.*` | `test affected`（命中 `extract-unit` / `extract-integration` 及受影响下游） | T1 全量（含 coverlet 覆盖率展示） | 改动真实数据提取输出格式且影响 T3 场景时触发 | T0 摘要 + T1 全绿 |
| `src/Pal.Game/**`（脚本/场景/资源/样例） | `test affected`（`Profiles/Resources/Editor/Samples` 命中 `game:Profiles`、`game:Map`、`prepare`；其余命中 `game:*`、`prepare`） | T1 全量 | **触发**（渲染、主链交互、对话字体、战斗演出、存档恢复或 UI 布局改动） | T0 摘要 + T1 全绿 + T3 PR 评论 |
| `tests/Pal.Game.Tests/**` | `test affected`（按对应 `[TestCategory]` 收窄，或回退 `game:*`） | T1 全量 | 改动 `tests/Pal.Game.Tests/E2E/**` 时触发 | T0 摘要 + T1 全绿（+ 按需 T3 评论） |
| `tools/long-running-agent/**` | `test affected`（命中 `lra`，等价 `./pal.ps1 test lra`） | T1 全量 | 不触发 | T0 `lra` 通过 + T1 全绿 |
| 构建/配置/前门/CI（`*.sln`、`*.csproj`、`global.json`、`NuGet.Config`、`*.runsettings`、`project.godot`、`pal.ps1`、`scripts/**`、`tests/affected-map.psd1`、`.github/**`） | `test affected` 自动回退 `full` | T1 全量（含语法/边界/映射自检） | 改动 T3 资源准备、E2E 驱动或 `e2e-gpu.runsettings` 时触发 | T0 `full` + T1 全绿（+ 按需 T3 评论） |

### 常用定向调试命令

```powershell
# 预览增量选测决策（不执行测试）
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test affected --explain

# 单独运行指定层或过滤用例
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test unit --project core --filter "FullyQualifiedName~Battle"
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test integration --project prepare
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test game --category Smoke --blame-hang

# 固定版 Godot 定位 / 安装 / 导入预热
pwsh -NoProfile -NonInteractive -File ./pal.ps1 godot locate
pwsh -NoProfile -NonInteractive -File ./pal.ps1 godot install --destination <dir>
pwsh -NoProfile -NonInteractive -File ./pal.ps1 godot import
```

## 3. T3 显式触发规则与执行闭环

T3 用于验证公开 Linux CI 无法覆盖的**真实原版数据 + 真实 GPU 渲染**行为，默认包含 5 个 YAML 场景（位于 `tests/Pal.Game.Tests/E2E/Scenarios/`）：`boot-and-explore`、`real-dialogue-bilingual`、`battle-closure`、`save-load-restore`、`dynamic-scene`。

| 触发条件 | 建议范围 | 命令 |
| :--- | :--- | :--- |
| 改动渲染、相机缩放、字体/对话排版、战斗视觉、UI 布局或真实资源加载链 | 全量 5 场景 | `./pal.ps1 test e2e --gpu` |
| 改动特定子系统（探索、双语对话、战斗闭环、存档读档、动态场景） | 对应单场景（合并前建议补全量） | `./pal.ps1 test e2e --gpu --scenario <name>` |
| 改动 `tests/Pal.Game.Tests/E2E/**` 或 `scripts/pal/T3Assets.psm1` | 全量 5 场景 | `./pal.ps1 test e2e --gpu` |
| 已确认视觉变更需要刷新本地金图 | 对应场景或全量（须明确授权） | `./pal.ps1 test e2e --gpu --update-golden` |

执行步骤（与 [验证工单模板](../../.github/ISSUE_TEMPLATE/verification.md) 一致）：

1. 确认本机工作树干净且无冲突 Godot 进程；设置环境变量 `PAL_RAW_DATA_DIR`（可选 `PAL_CACHE_DIR` 覆盖默认共享缓存目录）。
2. 执行 `./pal.ps1 assets prepare`（命中或构建仓库外共享缓存并链接至 `src/Pal.Game/Assets/T3Real`；可用 `./pal.ps1 assets status` 查看状态）。
3. 执行 `./pal.ps1 test e2e --gpu --out .artifacts/pal/verify-t3`。
4. 执行 `./pal.ps1 report --from .artifacts/pal/verify-t3 --out .artifacts/pal/verify-t3/pr-comment.md`，将生成的 Markdown 贴至目标 PR 评论区。
5. 本地打开 `<out>/contact-sheet.png` 完成人工 5 分钟观感审阅。**禁止提交或上传真实原版数据、派生资源、版权截图或本机私密绝对路径。**

## 4. 双平台覆盖边界与硬底线

- **平台互补**：
  - **Linux CI（`ubuntu-24.04`）**：覆盖跨平台构建、大小写敏感路径、POSIX 文件锁/符号链接语义与公开全量 headless 回归（`dotnet` / `prepare` / `game`）。
  - **Windows 本地（T0 / T3）**：覆盖 Windows 本地开发增量反馈、真实 GPU 渲染金图比对与人工桌面观感确认。
- **二态 0/1 原则**：测试只有通过（`0`）与失败（`1`）；**门内不自动重试，不设 quarantine 清单**，偶发即缺陷、直接定位修复。同时核对进程退出码与 TRX `outcome`/`failed`（保留 `TreatNoTestsAsError=true`，零测试或跳过均判失败）。
- **验证面不可互代**：Unit 不替代引擎接线；headless 不替代 GPU 像素核对；GPU 截图不替代人工桌面观感确认；workflow 文件存在不等于服务端分支保护生效（见[合并保护操作说明](merge-protection.md)）。
- **异常止损与引擎串行**：本地 Godot / GdUnit / 资源准备执行全局串行，不擅自结束未知 Godot 进程（进程查询仅取 PID/Name）。发生崩溃、超时、中止或结果缺失立即保留原件并停止，详见[测试异常处理规则](../agents/test-diagnostics.md)。

## 5. 相关专题文档

- [Game 测试分层与 SceneRunner](game-test-layers.md)：新增、迁移或排查 `tests/Pal.Game.Tests` 用例时阅读。
- [时序与外部调度延迟](time-boundaries.md)：改动业务时间、输入更新边界或排查调度偶发时阅读。
- [合并保护操作说明](merge-protection.md)：`main` 分支经典合并保护的当前配置与受控操作守则。
- [测试异常处理规则](../agents/test-diagnostics.md)：失败分类、停止顺序与异常留证。
