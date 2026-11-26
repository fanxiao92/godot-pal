# 原型（#246，已定稿）：`pal.ps1` 前门命令形态、验证矩阵与 PR 证据评论

> **性质**：一次性规划原型（已获用户确认定型，归档于 throwaway 分支 `prototype/pal-front-door`，不合入 `main`）。  
> **对应决策票**：[原型：pal.ps1 前门命令形态、验证矩阵与 PR 证据评论](https://github.com/fanxiao92/godot-pal/issues/246)（隶属 [M7 验证基建路线图（wayfinder）](https://github.com/fanxiao92/godot-pal/issues/240)）。

---

## 一、`pal.ps1` 前门命令形态（帮助文本）

仓库根目录单一入口 `./pal.ps1`（`pwsh` 7 跨平台，Windows / Linux 共用）。

```text
Usage:
  pwsh -NoProfile -NonInteractive -File ./pal.ps1 <command> [subcommand] [options]

Commands:
  test affected          [T0] 增量选测：静态 .csproj 依赖图 + tests/affected-map.psd1（目标 < 2 min）
       unit              运行 Core / Extract 单元测试（xUnit）
       integration       运行 Extract CLI 与 Prepare 集成测试
       game              运行 tests/Pal.Game.Tests（Godot headless + GdUnit）
       e2e               [T3] 运行真实游戏进程端到端场景（--pal-e2e）
       full              本地运行公开可跑全量（unit + integration + game + 映射自检）

  ci   dotnet            [T1/T2 Job 1] Core/Extract Unit（含 coverlet）+ CLI 集成 + 映射自检
       prepare           [T1/T2 Job 2] Prepare 集成（需 Godot）
       game              [T1/T2 Job 3] Pal.Game.Tests headless 单轮（需 Godot）

  godot install          下载官方 4.7.2-stable mono（按 OS 选 win64/linux_x86_64，固定 SHA512，chmod +x）
        import           对 src/Pal.Game 与 tests/Pal.Game.Tests 执行 --headless --import 预热

  assets prepare         按「原始数据散列 + Prepare 版本」命中或构建仓库外共享缓存，并链接/复制到工作树
         status          查看共享缓存命中状态（不泄露本机原始数据绝对路径到公开报告）

  report                 从一次或多次运行的 run-summary.json / TRX / 覆盖率 / 截图目录生成标准 PR 证据评论

Common Options:
  --project <name>       test unit: core|extract；test integration: extract|prepare
  --category <name>      test game: 指定按功能区命名的 GdUnit [TestCategory]（如 process|dialogue|exploration|battle|story|native-map，可重复）
  --filter <FQN>         精确测试过滤（仅允许 FullyQualifiedName= 或 FullyQualifiedName~）
  --base <ref>           test affected: 对比基线（默认 merge-base origin/main HEAD + 工作区）
  --explain              test affected: 仅打印「文件 -> 命中规则 -> 目标集」，不执行
  --all                  test affected: 忽略增量，回退到 full
  --coverage             test unit: 启用 coverlet 采集（ci dotnet 默认开启）
  --scenario <name|all>  test e2e: 指定 E2E 场景（默认 all）
  --gpu                  test e2e: 启用真实 GPU 渲染与截图/金图容差比对（仅本地 T3）
  --godot-path <path>    显式指定 Godot 可执行文件（覆盖 GODOT_BIN）
  --out <dir>            产物输出目录（默认 .artifacts/pal/<timestamp>-<cmd>/）
  --json                 stdout 额外输出结构化 JSON 摘要路径（供 LRA / CI 脚本消费）

Exit Codes:
  0  通过（或 test affected 确认无改动 / 仅文档改动，明确输出 no-op 说明）
  1  失败（具体原因与 errorMsg 直接输出到终端与 run-summary.json，不维护多套自定义错误码）
```

### `run-summary.json` 最小结构（机器可读事实）

```json
{
  "command": "test game",
  "status": "passed | failed | error",
  "errorMsg": null,
  "durationSec": 42.3,
  "counts": { "total": 85, "passed": 85, "failed": 0, "skipped": 0 },
  "trxFiles": ["trx/Pal.Game.Tests.trx"],
  "coverageFiles": [],
  "artifactsDir": ".artifacts/pal/20261126-2050-game"
}
```

### 本地 / CI / LRA 三方共用对照

| 调用方 | 实际调用命令 | 消费的产物 | 备注 |
| :--- | :--- | :--- | :--- |
| **本地开发 / 普通 agent** | `./pal.ps1 test affected`（日常）<br>`./pal.ps1 test affected --explain`<br>`./pal.ps1 test full`（按需）<br>`./pal.ps1 test e2e --gpu` → `./pal.ps1 report`（T3） | 退出码 `0/1` + 终端 `errorMsg` + `run-summary.json` + TRX + 截图包 | 无需记多个 `Test-*.ps1` |
| **GitHub CI（Linux `ubuntu-24.04`）** | Job 1：`./pal.ps1 ci dotnet`<br>Job 2：`./pal.ps1 godot install && ./pal.ps1 ci prepare`<br>Job 3：`./pal.ps1 godot install && ./pal.ps1 ci game` | 退出码 `0/1` + TRX + Cobertura 覆盖率 + 自动写入 `$GITHUB_STEP_SUMMARY` + 上传 artifact | 3 个并行 job 均为 PR 必需检查，零 CI 专用逻辑分叉 |
| **LRA 执行器（`checks.ts`）** | Unit / Prepare / Game 全部统一调用 `./pal.ps1 test <unit\|integration\|game> ... --out <dir> --json` | 退出码 `0/1` + 原生 TRX + `run-summary.json`（含 `status` / `errorMsg`） | 取代 `Test-Range.ps1` / `GameTestBatches.ps1` / 自制身份审计 / `-FixedPackageDirectory`；LRA 仅保留自身并发锁、Godot PID/Name 守卫与 TDD 状态机 |

---

## 二、`docs/testing/verification-matrix.md` 单张验证矩阵草稿

```markdown
# 验证矩阵（唯一入口）

> 术语：**层（Layer）** = 测试验证什么（`Unit` / `集成` / `端到端`）；**档（Tier）** = 何时何处运行（`T0` / `T1` / `T2` / `T3`）。

## 1. 四档触发与预算

| 档位 | 触发时机 | 运行位置 | 覆盖范围 | 目标耗时 | 门禁性质 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **T0** | 本地日常开发 / agent 迭代 | 本地（Windows/Linux） | `pal.ps1 test affected`（受影响工程与类别） | < 2 min | 快速反馈，非完整验收 |
| **T1** | Pull Request | GitHub CI（`ubuntu-24.04`，3 并行 job） | 公开可跑全量（Unit + CLI/Prepare 集成 + Game headless 单轮 + 映射自检 + 覆盖率展示） | < 15 min（估 10–11 min） | **PR 必需检查**（3 job 全绿） |
| **T2** | `main` push | GitHub CI（`ubuntu-24.04`，3 并行 job） | 与 T1 相同 | < 15 min | 主干健康基线 |
| **T3** | `e2e` 标签 / 里程碑交付 / 规则表大改动 / 夜间 | 本地（agent 或人工 QA，真实资源 + GPU） | `pal.ps1 test e2e --gpu`（真实游戏进程 + 探针断言 + 截图/金图容差比对） | 见 #247 预算 | 里程碑/大改动验收（附 PR 证据评论） |

## 2. 改了什么 → 跑哪档 → 交什么证据

| 改动范围 | 本地必跑（T0） | PR / 主干（T1/T2） | 大改动/里程碑（T3） | 交付证据 |
| :--- | :--- | :--- | :--- | :--- |
| 纯文档（`docs/**`、`*.md`） | `test affected`（返回 `0` 并明示 `no-op`） | CI 照常通过 | 不需要 | 无测试变更说明 |
| `tools/long-running-agent/**` | `test affected`（命中 `lra` 目标，跑 `npm test`） | `long-running-agent.yml` | 不需要 | LRA 离线回归通过 |
| `src/Pal.Core/**` | `test affected`（Core/Extract Unit + CLI/Prepare 集成 + 快速 Game 冒烟类别如 `process`） | T1 全量 | 涉及核心玩法主链时按规则表触发 | T0 摘要 + T1 必需检查全绿 |
| `tools/Pal.Extract/**` | `test affected`（Extract Unit + CLI 集成 + Prepare） | T1 全量 | 改变离线产物格式时触发 | T0 摘要 + T1 全量 + 覆盖率展示 |
| `src/Pal.Game/**`（脚本/场景/资源） | `test affected`（命中功能区类别或 `game:*` + Prepare） | T1 全量 | 改动渲染/主链交互/存档恢复/UI 布局时触发 | T0 摘要 + T1 全量（+ T3 PR 评论） |
| `tests/Pal.Game.Tests/**` | `test affected`（仅对应 `[TestCategory]`） | T1 全量 | 仅改 E2E 场景时触发 | T0 摘要 + T1 全量 |
| 构建/配置/前门/CI（`*.sln`、`*.csproj`、`*.runsettings`、`project.godot`、`pal.ps1`、`tests/affected-map.psd1`、`.github/**`） | `test affected` 自动回退 `full` | T1 全量（含映射自检） | 改动 E2E 钩子或资源准备链时触发 | T0 全量 + T1 全量 |

## 3. 平台覆盖与硬底线

- **二态原则（0 / 1）**：测试只有通过或失败，无中间态；**门内不自动重试，不设隔离清单（quarantine）**，偶发即缺陷、直接定位修复。
- **平台互补**：Linux CI 覆盖跨平台构建、大小写敏感路径分支与公开全量；本地 Windows T0/T3 覆盖 Windows 专属分支、真实 GPU 渲染与人工桌面观感。
- **不可互代**：Unit ≠ 引擎接线；headless ≠ GPU 像素核对；GPU 截图 ≠ 人工桌面 5 分钟审阅；workflow 文件存在 ≠ 服务端合并保护已生效。
- **异常止损**：遇到崩溃、超时、中止或结果缺失（`status = "error"`）立即保全原件并停止，禁止自动重试。
```

---

## 三、验证工单模板 + 标准证据输出样例

### 3.1 验证工单模板（`.github/ISSUE_TEMPLATE/verification.md` 草稿）

```markdown
---
name: 验证工单（Verification）
about: 指定 PR/分支与验证档位，由 agent 或人工 QA 使用 pal.ps1 执行并回填标准证据评论
labels: verification
---

## 验证目标
- **目标 PR / 分支**：#___ / `branch-name`
- **基线提交（HEAD SHA）**：`_______`
- **要求档位**：[ ] T1（本地全量复现）  [ ] T3（真实资源 + GPU 截图）
- **重点关注场景 / 回归项**：（留空表示按档位默认集）

## 执行命令（agent 与人工 QA 共用）
1. 检出目标提交并确认工作树干净、无冲突 Godot 进程。
2. 若跑 T3：执行 `pwsh -NoProfile -NonInteractive -File ./pal.ps1 assets prepare`。
3. 执行：`pwsh -NoProfile -NonInteractive -File ./pal.ps1 test <full|e2e --gpu> --out <dir>`。
4. 生成评论：`pwsh -NoProfile -NonInteractive -File ./pal.ps1 report --from <dir> --out <dir>/pr-comment.md`。
5. 将 `pr-comment.md` 内容贴到目标 PR 评论区（**禁止上传真实资产或私密路径**）。
```

### 3.2 T1 CI Job Summary 样例（自动写入 `$GITHUB_STEP_SUMMARY`，不刷屏 PR 评论）

```markdown
### 验证证据（T1 · Linux CI）

- **提交**：`7f0567b`（PR #255）
- **环境**：`ubuntu-24.04` / .NET `10.0.x` / Godot `4.7.2-stable` mono headless
- **结论**：✅ PASS（总墙钟 `10m 24s`）

| Job / 层 | 状态 | 通过 | 失败 | 跳过 | 耗时 | 产物 |
| :--- | :---: | ---: | ---: | ---: | ---: | :--- |
| `dotnet`（Core/Extract Unit + CLI 集成 + 映射自检） | ✅ | 2,718 | 0 | 0 | `3m 12s` | `trx-dotnet` |
| `prepare`（Prepare 集成） | ✅ | 94 | 0 | 0 | `5m 48s` | `trx-prepare` |
| `game`（`Pal.Game.Tests` headless 单轮） | ✅ | 1,097 | 0 | 0 | `10m 24s` | `trx-game` |

- **覆盖率（仅展示，无门槛）**：`Pal.Core` 行覆盖 `84.2%` / 分支 `76.5%`；`Pal.Extract` 行覆盖 `79.1%` / 分支 `70.8%`
- **CI Run**：[Actions Run #123456](https://github.com/fanxiao92/godot-pal/actions/runs/123456)
```

### 3.3 T3 本地验证工单 PR 证据评论样例（由 `pal.ps1 report` 生成并贴至 PR）

```markdown
### 验证证据（T3 · 本地端到端 + GPU）

- **提交**：`7f0567b`（验证工单 #270，执行者：agent / QA）
- **环境**：Windows 11 / NVIDIA RTX 4070 / Godot `4.7.2-stable` mono（`gl_compatibility`）
- **真实资源缓存键**：`raw:sha256-9a3f1c... + prepare:v1.2`（缓存命中，无真实资产提交/上传）
- **结论**：✅ PASS（`7/7` 场景通过，总耗时 `3m 41s`）

| 场景 | 探针断言 | 截图/金图比对（容差 ≤ 1.0%） | 耗时 | 截图文件 |
| :--- | :---: | :---: | ---: | :--- |
| `boot-new-game` | ✅ | ✅ max diff `0.00%` | `24s` | `01-title.png`, `02-scene4.png` |
| `explore-collision` | ✅ | ✅ max diff `0.12%` | `31s` | `03-walk.png` |
| `npc-dialogue` | ✅ | ✅ max diff `0.00%` | `28s` | `04-dialogue.png` |
| `battle-loop` | ✅ | ✅ max diff `0.21%` | `52s` | `05-battle-turn.png` |
| `save-load-restore` | ✅ | ✅ max diff `0.00%` | `45s` | `06-save.png`, `07-restored.png` |

- **本地产物与截图包**：`.artifacts/pal/20261126-2015-e2e/`（`run-summary.json`、`probes/`、`screenshots/`、`contact-sheet.png`）
- **人工 5 分钟审阅清单**：[ ] 打开 `contact-sheet.png` 核对画面无花屏/错位/字体缺字；[ ] 抽查变更相关场景原图。
```
