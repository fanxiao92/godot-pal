# Agent instructions

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

## 项目边界

- `src/Pal.Core` 保持纯 .NET 业务逻辑；Godot 节点、输入、渲染和平台 I/O 留在外层。测试优先使用公开构造、事件与更新接口，预期值来自独立规格或参考核对，而非被测实现生成。
- 原版行为按 `reference/README.md` 导航查阅 SDLPal；参考源码只读，不编译或运行。忠实移植与新增扩展分开说明，扩展需有明确决定，不将其宣称为原版行为。
- 当前运行时按已交付 M2/M3 边界使用 Godot 原生 Resource、PackedScene、Texture2D：M1 PNG/JSON 包仅作显式离线准备与独立核对输入，不是运行时 JSON/MKF 加载或自动同步源；M3 独立 scene4/map10/owner60 切片可消费显式准备的现代事件/语言/字形资源，但不扩大为全量 VM、auto、正常场景4自由探索或完整剧情。复用已准备资源，真实原始数据及资源不提交、不上传。

## 测试

- 按契约和依赖选择层：先读[测试职责矩阵](docs/testing/test-layers.md)；新增或迁移 Game 集成测试再读[Game 测试分层与 SceneRunner](docs/testing/game-test-layers.md)。Core/CLI unit、CLI 文件集成、Prepare、SceneRunner 和真实进程各自证明的内容不可互换，完整门仍只有共享入口。
- 时序变更、Game 迁移、调度偶发或变异断言前，先读[时序验证细则](docs/testing/time-boundaries.md)；输出捕获或完整门异常进入[当前诊断规则](docs/agents/test-diagnostics.md)。普通任务不递归读取历史。
- 具体 Game 更新边界、timeout 分类、SceneRunner 生命周期和完整门操作见 [Game 测试分层](docs/testing/game-test-layers.md)；异常分类与停止顺序见 [测试异常处理规则](docs/agents/test-diagnostics.md)。触发时再按其中的历史指针追溯原症状、变异和交付年表。

### 执行与证据

- 先核对当前分支的共享验收脚本和 runsettings，再选择与改动匹配的检查；实际 Godot、GdUnit、资源准备及包含它们的 solution 测试全局串行。工作树只隔离文件，不隔离进程或共享管道；发现已有 Godot 进程时暂停，确认归属，不擅自结束未知进程。
- 进程诊断仅收集任务自有句柄或服务端筛选的明确仓库执行器。全局 Godot 检查仅获取 PID/Name；不先获取全机详细信息再过滤，不收集外部项目命令行、父链、文件或日志。
- 门禁核对实际退出码、TRX Summary/RunInfo、完整性与预期诊断；部分通过或计数中 aborted=0 不等于会话完成。预期失败控制必须是指定失败原因，不能由崩溃或超时代替。
- 发生会话中止、崩溃或未知退出时，保留实际命令、提交、原日志和部分结果后停止；重试、续跑或改变运行参数需明确批准。区分 headless 行为、GPU 像素核对、人工桌面验收和远端 CI，不相互代替。

### 时序与外部调度延迟

修改业务时间、输入更新边界或相关断言前，必须读取[时序与外部调度延迟](docs/testing/time-boundaries.md)；该处是 Core 确定性边界、真实更新完成、反例、超时分类与定点诊断的完整权威规则。本锚点保留供旧链接进入，不另维护副本。

## 其它

- **工具链按批准配置执行**：沿用仓库配置中的依赖与引擎基线；#18 已另获批准交付[固定 GdUnit 源码包与 bootstrap](docs/testing/gdunit-source-packages.md)。遇到测试异常先分类、保留证据并定点诊断；进一步升级、源码构建或补丁应用仍需另行立项和授权，不自行统一不同测试项目的版本。现有失败仍按门禁规则阻断。
- **已知异常与误判边界**：遇到 Windows GdUnit 输出捕获异常、焦点恢复时序断言失败、门禁／代理超时，或准备运行 M2/M3 共享门禁时，先读 [测试异常说明](docs/agents/test-diagnostics.md)。其中历史现象不是忽略新失败、放宽断言或自动重跑的许可。
- 服务端合并保护的实际配置、主体和恢复边界见[合并保护操作说明](docs/testing/merge-protection.md)；它不把 workflow、App 绑定或仓库 public 当作分支保护，也不替代实时拒绝/允许验收结果。
