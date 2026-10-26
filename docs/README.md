# 文档导航

当前进度、技术版本和构建运行命令见 [项目 README](../README.md)。文档按职责归类，不按产生顺序编号。

## 设计背景：`design/`

理解项目目标和架构时阅读：

- [背景与可行性](design/feasibility.md)：为什么做、目标版本与参考项目。
- [架构设计](design/architecture.md)：离线转换与运行时边界、核心系统分层。
- [开发计划](design/development-plan.md)：M0～M8 总体路线，区分场景资源、Godot 场景构建和事件里程碑。
- [决策记录与待定问题](design/decisions.md)：已确定的方向与待讨论事项。
- [事件格式契约](design/events-schema.md)：原版忠实转写的文件结构、基础命令、引用与验证要求；处理事件转换或执行任务时阅读。

引擎逻辑的参考文件地图见 [reference/README.md](../reference/README.md)。

## 实施计划：`milestones/`

开始某个里程碑时，读取其计划及对应 GitHub Issue 正文、评论；新里程碑先制定计划，再编码。

- [M0 工程骨架与工具链](milestones/m0/plan.md)：已完成，保留原始计划。
- [M1 场景资源提取](milestones/m1/plan.md)：只读检查、Windows 地图/精灵/场景转换和完整包已完成本地验收及独立双轴审查；只包含静态场景资源，不含事件、对话、语言包或字库。
  - [完整静态资源包 v1](milestones/m1/extract-scene.md)：最终 CLI、来源/参数/产物散列与相对引用契约、实际验证及未执行边界。
  - [地图切片契约](milestones/m1/export-map.md)：原始字段、PNG/索引/alpha 和瓦片预览约定。
- [M2 Godot 场景构建与探索](milestones/m2/plan.md)：Windows 单领队、静态对象、原生资源/独立 `.tres` profile、320×200 整数缩放、键盘与焦点处理已实现，自动/人工验收及收尾文档独立终审已完成，成果已合入并推送 main，远端完整 Windows headless CI 已通过；#11/#17/#19 均已关闭。计划提供真实入口/配置与任务表。
  - [M2 分层验收记录](milestones/m2/acceptance.md)：本地规则/headless、GPU、操作者反馈、主干交付及 #19 修复后远端 CI 的分层证据和未验收范围；追溯 #11/#17/#19 时阅读。
  - [固定 GdUnit 源码包](testing/gdunit-source-packages.md)：#18 包身份、发布与 bootstrap；直接还原/构建或排查包来源时阅读。
- [M3 独立真实 NPC 对话](milestones/m3/plan.md)：已交付 scene4/map10/owner60 有限切片的批准范围；不是正常剧情或全量事件系统。
  - [M3 当前状态与分层验收](milestones/m3/acceptance.md#当前状态)：最终 Issue/CI 的单点索引，以及保持原身份的本地、GPU、用户反馈、失败和 Observer 限制；普通测试选层无需重读历史。
  - [对话资源与显示](design/dialogue-display.md)、[trigger 执行](design/trigger-execution.md)、[搜索/返回规则](design/npc-interaction.md)、[Godot 宿主与启动配置](design/npc-interaction-host.md)：当前实际接口与支持/拒绝范围。

后续计划放在 `docs/milestones/<里程碑>/plan.md`。具体任务规格、讨论和结果留在 GitHub Issues。

## 测试规范与入口：`testing/`

- [测试职责矩阵](testing/test-layers.md)：按契约、依赖和证据选择 Core/CLI unit、CLI 文件集成、Prepare、SceneRunner 或真实进程；不按耗时分类。
- [工程检查与分层回归](testing/engineering-checks.md)：Source、Offline、OwnedProcess 的边界、退出和证据规则。
- [单元快入口](testing/unit-entry.md)：只选择 Core 与 CLI unit；不调用 Game、Prepare 或 GdUnit bootstrap。
- [显式范围回归](testing/range-regression.md)：选择 CLI integration、Prepare 或 Game 现有批次；明确参数、准备顺序和非完整门证据。
- [时序验证细则](testing/time-boundaries.md)：业务时间、输入更新边界、相关断言、Game 迁移或调度偶发时先读；普通选层不加载长细则。
- [CLI 测试迁移](testing/cli-test-migration.md)：unit/integration 项目边界、映射和完整门接线。
- [Game 测试分层与 SceneRunner](testing/game-test-layers.md)：新增或改写 Game 集成测试、迁移进程用例时阅读；当前初始化、更新边界、隔离、反例和完整门规则。
- [Game 测试当前入口](../src/Pal.Game/Tests/README.md)：当前 Game 入口、覆盖口径和失败判定；只在选中 Game 层时阅读。
- [M3 Game 历史运行记录](archive/m3/game-test-run-history.md)：旧命令、变异、性能、交付和历史限制，按需追溯。
- [CI 分流与候选身份](testing/ci-dispatch.md)：Draft/Ready/main/manual 的实际调度契约、候选绑定与证据边界；远端状态仍以 GitHub 实时结果为准。
- [固定 GdUnit 源码包](testing/gdunit-source-packages.md)：固定包的来源和消费；仅在还原、排查或获准运行 Game 时阅读。
- [测试异常说明](agents/test-diagnostics.md)：当前处理规则与历史症状分开；遇到中止、崩溃、输出捕获或已知诊断时按指针进入。
- [合并保护操作说明](testing/merge-protection.md)：#38 已应用的配置、适用主体和恢复边界；不代替 GitHub 受控拒绝/允许验收结果。

## 历史归档：`archive/`

仅在排障、环境升级或追溯历史结论时查阅，不是新会话或后续实现的默认必读材料。当前操作文件与历史记录分离；移动/收口后的旧入口保留为兼容跳转。

- [M3 Game 历史运行记录](archive/m3/game-test-run-history.md)：旧 Game 命令、批次、变异、性能和交付年表。
- [M3 历史诊断记录](archive/m3/test-diagnostics-history.md)：Windows 输出捕获、时序、Prepare/CI 症状和原结论。
- [固定包来源与验收年表](archive/m2/gdunit-source-acceptance-history.md)：包来源、发布、固定包验收及其限制。
- [#31 长任务执行复盘](archive/engineering/spec31-retrospective.md)：治理任务的耗时口径、上下文膨胀、采证返工与后续改进；仅在规划长任务或追溯本次结论时阅读，附固定截止点的统计数据。

M0 原有报告保留在此，内容描述当时的环境和验证结果，不随每次版本变动重写：

- [Godot C# 兼容性](archive/m0/godot-compatibility.md)（Issue #2）。
- [最小游戏启动链路](archive/m0/game-startup.md)（Issue #3）。
- [独立 CLI 入口](archive/m0/extract-cli.md)（Issue #4）。
- [M0 集成验收与 CI](archive/m0/acceptance.md)（Issue #5）。

验证仍由测试、启动检查和 CI 执行；普通执行结果记录在 CI 和 Issue 中，无需逐次新增报告。只有特殊兼容性调查、难以复现的问题等值得长期保留时，才另写记录。

## Agent 工作流：`agents/`

按根目录 [AGENTS.md](../AGENTS.md) 的入口读取，用于任务系统、标签映射及领域文档规则。

领域术语见根目录 [GLOSSARY.md](../GLOSSARY.md)。正式架构决策位于 `docs/adr/`，当前有 [ADR-0001：原版事件采用忠实低层转写](adr/0001-faithful-event-transcription.md)；进入相关设计或实现前阅读。
