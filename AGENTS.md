# Agent instructions

## 按需触发的上下文指针

每个分支仅在命中触发条件时读取，避免无关常驻上下文：

- **每次 fresh 会话开工前** → 必读[执行环境契约](docs/agents/execution-environment.md)，核对当前仓库、分支、HEAD、已有改动与工具能力。
- **读取/操作 GitHub Issues（`fanxiao92/godot-pal`）** → 读[Issue 工作流](docs/agents/issue-tracker.md)与[Triage 标签](docs/agents/triage-labels.md)；实时状态以目标 Issue 正文、评论与直接依赖为准。
- **查阅架构总图、实时能力账本或领域术语/决策** → 读根目录 [ARCHITECTURE.md](ARCHITECTURE.md)、[能力账本](docs/generated/capabilities-ledger.md)、[GLOSSARY.md](GLOSSARY.md)、`docs/adr/` 与[领域文档规则](docs/agents/domain.md)；项目导航见 [docs/README.md](docs/README.md)，仓内工程 Skills 真源位于 `.agents/skills/`（`verify`、`garden`）。
- **实施单票、处理失败或 review repair** → 读[单任务执行与有界纠正](docs/agents/task-execution.md)；本机长任务编排见 [long-running-agent](tools/long-running-agent/README.md)。
- **协调多票或恢复协调会话** → 读[多票协调与恢复](docs/agents/large-task-handoffs.md)；里程碑合入后显式清理隔离 worktree 与已合并分支（丢弃未提交/未合并残留须先获授权）。
- **选择测试档位、执行验证或回填证据** → 读[验证矩阵](docs/testing/verification-matrix.md)（统一前门 `./pal.ps1`，CI 为 `ubuntu-24.04` 3 并行 job：`dotnet` / `prepare` / `game`）。
- **新增、迁移或排查 Game 集成测试** → 读[Game 测试分层与 SceneRunner](docs/testing/game-test-layers.md)。
- **改动业务时间、输入更新边界或排查调度偶发** → 读[时序与外部调度延迟](docs/testing/time-boundaries.md)。
- **遇到测试崩溃、中止、超时或未知退出** → 读[测试异常处理规则](docs/agents/test-diagnostics.md)。
- **查看或受控修改 `main` 合并保护** → 读[合并保护操作说明](docs/testing/merge-protection.md)。
- **执行 `/code-review`（Standards 轴）** → 读根目录 [CODING_STANDARDS.md](CODING_STANDARDS.md)。

## 不可逆与安全硬约束

1. **授权边界**：保留用户改动；提交、推送、合并、依赖/工具链升级、分支保护修改与远端写操作须获明确授权，不自行跨项目统一测试框架版本。
2. **版权与真实资产隔离**：`data/raw/`、`data/extracted/`、`src/Pal.Game/Assets/` 及真实原始/派生资源、版权截图与本机私密路径永不提交、永不上传；参考源码 `reference/sdlpal/` 只读、不编译不运行（路径/文本检索默认排除 `reference/sdlpal/`，导航见 [reference/README.md](reference/README.md)）。忠实移植与扩展分开说明。
3. **本地引擎串行与进程隐私**：本地 Godot、GdUnit 与资源准备全局串行（worktree 只隔离文件不隔离进程）；发现已有 Godot 进程先暂停确认归属，不擅自结束未知进程。全局 Godot 检查仅获取 PID/Name，不采集外部项目命令行、父链、文件或日志。
4. **异常止损与证据完整性**：测试结果同时核对进程退出码与 TRX `outcome`/`failed`（及 `run-summary.json`）；门内不自动重试、不设 quarantine，部分通过或 `aborted=0` 不等于完成。发生崩溃、中止、超时、未知退出或结果缺失立即保留命令、提交、原日志和部分结果并停止；重试、续跑或放宽断言/时限须明确批准。
5. **验证面不可互代**：Unit 不替代引擎接线；headless 不替代 GPU 像素核对；GPU 截图不替代人工桌面观感确认；workflow 文件存在或仓库 public 不等于服务端分支保护生效。
6. **核心架构边界与确定性时间**：`src/Pal.Core` 保持纯 .NET 零 Godot 依赖，运行时仅消费 Godot 原生 Resource/PackedScene/Texture2D（不读 JSON/MKF 或自动同步）；精确业务时间在 Core 公开显式时间接口验证，Game 集成测试在实际更新完成边界（`UpdateCompleted`）断言。
