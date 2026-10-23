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
- [M3 独立真实 NPC 对话](milestones/m3/plan.md)：scene4/map10/owner60闭环已实现，完成本地、GPU及用户Windows人工分层验证；不是正常剧情或全量事件系统。
  - [M3 分层验收检查点](milestones/m3/acceptance.md)：最新完整门569.930秒/exit0、15TRX、Game三轮各242；记录双轴复核、双语GPU及人工反馈原身份，保留CLI误报、旧143中止、旧取消CI及本次两个边界夹具失败CI。ACK等待依赖已用指定反例修复，预算及生产封装不变。精确候选CI/main/Issue交付按[#26](https://github.com/fanxiao92/godot-pal/issues/26)和[#20](https://github.com/fanxiao92/godot-pal/issues/20)最终回填核对；规格外Observer停用，私密原件限制不变。
  - [对话资源与显示](design/dialogue-display.md)、[trigger 执行](design/trigger-execution.md)、[搜索/返回规则](design/npc-interaction.md)、[Godot 宿主与启动配置](design/npc-interaction-host.md)：当前实际接口与支持/拒绝范围。

后续计划放在 `docs/milestones/<里程碑>/plan.md`。具体任务规格、讨论和结果留在 GitHub Issues。

## 测试规范与入口：`testing/`

- [Game 测试分层与 SceneRunner](testing/game-test-layers.md)：新增或改写集成测试、迁移进程用例前阅读；直接列出选层、共用初始化、实际更新边界、清理隔离及反例/测量要求，无需追读历史任务正文。
- [Game 测试说明](../src/Pal.Game/Tests/README.md)：当前入口、运行命令、覆盖映射及实际验证范围。
- [固定 GdUnit 源码包](testing/gdunit-source-packages.md)：包来源、固定源码及 bootstrap。

## 历史归档：`archive/`

仅在排障、环境升级或追溯历史结论时查阅，不是新会话或后续实现的默认必读材料。

M0 原有报告保留在此，内容描述当时的环境和验证结果，不随每次版本变动重写：

- [Godot C# 兼容性](archive/m0/godot-compatibility.md)（Issue #2）。
- [最小游戏启动链路](archive/m0/game-startup.md)（Issue #3）。
- [独立 CLI 入口](archive/m0/extract-cli.md)（Issue #4）。
- [M0 集成验收与 CI](archive/m0/acceptance.md)（Issue #5）。

验证仍由测试、启动检查和 CI 执行；普通执行结果记录在 CI 和 Issue 中，无需逐次新增报告。只有特殊兼容性调查、难以复现的问题等值得长期保留时，才另写记录。

## Agent 工作流：`agents/`

按根目录 [AGENTS.md](../AGENTS.md) 的入口读取，用于任务系统、标签映射及领域文档规则。

领域术语见根目录 [GLOSSARY.md](../GLOSSARY.md)。正式架构决策位于 `docs/adr/`，当前有 [ADR-0001：原版事件采用忠实低层转写](adr/0001-faithful-event-transcription.md)；进入相关设计或实现前阅读。
