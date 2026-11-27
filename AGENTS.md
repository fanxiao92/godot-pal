# Agent instructions

## 执行环境

每个 fresh 会话开工前必读[执行环境契约](docs/agents/execution-environment.md)，核对本次 host/cwd/工具能力；普通 Pi worker 同样适用，不假定继承私人全局规则。

## 查阅与事实来源

- README 用于项目概览与常用入口；实现前按下方工作流读取目标 Issue 正文、评论及直接依赖，核对实时状态，不据旧路线描述重做已完成任务。
- 版本、命令和现有行为分别核对当前工作树的配置、脚本与实现；任务是否验收通过需核对对应提交的实际证据。区分已批准规格、已实现行为与未来设计，文档冲突时先核实，不把现有实现自动当作正确规格。
- 开工前确认仓库、分支、HEAD 与已有改动；隔离工作树中的修复不等于已合入当前分支。保留用户改动，提交、推送、合并和依赖升级按明确授权执行。

## Agent skills

### Issue tracker

规格与任务使用 GitHub Issues，目标仓库为 `fanxiao92/godot-pal`。
操作约定见 `docs/agents/issue-tracker.md`。

### Triage labels

使用五个默认 triage 标签。
角色映射见 `docs/agents/triage-labels.md`。

### Domain docs

采用 single-context：根目录 `GLOSSARY.md` 与 `docs/adr/`。
读取规则见 `docs/agents/domain.md`。

## 单票执行

实施单票、处理失败或 review repair 时，读取[单任务执行与有界纠正](docs/agents/task-execution.md)。本机长期编排入口与权限见 [long-running-agent](tools/long-running-agent/README.md)。

## 多票协调

仅协调多个 ticket 或恢复协调会话时，读取[多票协调与恢复](docs/agents/large-task-handoffs.md)；单票实施不默认读取。

- 里程碑交付合入 main 后，显式收尾清理本轮隔离 worktree 与已合并分支（`git worktree remove` / `git worktree prune`、删除已合并分支）；有未合并或未提交残留时单独列出，丢弃前取得明确授权。pi-subagents 的 worktree 清理是显式受审操作，不是自动 post-action，须由协调者主动发起。

## 项目边界

- `src/Pal.Core` 保持纯 .NET 业务逻辑；Godot 节点、输入、渲染和平台 I/O 留在外层。测试优先使用公开构造、事件与更新接口，预期值来自独立规格或参考核对，而非被测实现生成。
- 原版行为按 `reference/README.md` 导航查阅 SDLPal；参考源码只读，不编译或运行（路径/文本检索默认排除 `reference/sdlpal/`）。忠实移植与新增扩展分开说明，扩展需有明确决定，不将其宣称为原版行为。
- 运行时仅消费 Godot 原生 Resource、PackedScene、Texture2D，不从运行时读取 JSON/MKF 或自动同步；M1 PNG/JSON 包仅作显式离线准备与独立核对输入。复用已准备资源，真实原始数据及派生资源不提交、不上传。

## 测试

- 统一前门入口为根目录 `./pal.ps1`（`test affected|unit|integration|game|lra|full`、`ci dotnet|prepare|game`、`godot`、`assets`、`report`）；CI 为 `ubuntu-24.04` 3 并行 job（`dotnet` / `prepare` / `game`）。
- 选层与职责读[测试职责矩阵](docs/testing/test-layers.md)与[短指南](docs/testing/getting-started.md)；新增、迁移或排查 Game 集成测试（更新边界、timeout、SceneRunner）读[Game 测试分层与 SceneRunner](docs/testing/game-test-layers.md)。各层证据不可互换，Unit 反馈不是完整验收。
- 时序变更、Game 迁移、调度偶发或变异断言前，先读[时序验证细则](docs/testing/time-boundaries.md)；输出捕获、异常分类与停止顺序进入[当前诊断规则](docs/agents/test-diagnostics.md)。普通任务不递归读取历史。

### 执行与证据

- 实际 Godot、GdUnit、资源准备及包含它们的测试在本地全局串行。工作树只隔离文件，不隔离进程或共享管道；发现已有 Godot 进程时暂停，确认归属，不擅自结束未知进程。
- 进程诊断仅收集任务自有句柄或服务端筛选的明确仓库执行器。全局 Godot 检查仅获取 PID/Name；不先获取全机详细信息再过滤，不收集外部项目命令行、父链、文件或日志。
- 结果同时核对进程退出码与 TRX `outcome`/`failed`（及 `run-summary.json` 分步明细）；部分通过或 aborted=0 不等于会话完成。排查 Game 挂死或崩溃时使用 `./pal.ps1 test game --blame-hang`（CI `ci game` 默认开启 `--blame-hang-timeout 2m`）。
- 发生会话中止、崩溃或未知退出时，保留实际命令、提交、原日志和部分结果后停止；重试、续跑或改变运行参数需明确批准。区分 headless 行为、GPU 像素核对、人工桌面验收和远端 CI，不相互代替。

## 其它

- **工具链按批准配置执行**：沿用仓库配置中的依赖与引擎基线（`tests/Pal.Game.Tests` 使用 nuget.org 官方 `gdUnit4.api` / `gdUnit4.test.adapter`）。遇到测试异常先分类、保留证据并定点诊断；进一步升级、源码构建或补丁应用仍需另行立项和授权，不自行统一不同测试项目的版本。现有失败仍按门禁规则阻断。
- **已知异常与误判边界**：遇到 GdUnit 会话异常、焦点恢复时序断言失败或门禁／代理超时，先读 [测试异常说明](docs/agents/test-diagnostics.md)。历史现象不是忽略新失败、放宽断言或自动重跑的许可。
- 服务端合并保护的实际配置、主体和恢复边界见[合并保护操作说明](docs/testing/merge-protection.md)；它不把 workflow、App 绑定或仓库 public 当作分支保护，也不替代实时拒绝/允许验收结果。
