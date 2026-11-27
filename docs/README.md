# 文档导航

项目概览、技术基线与 `./pal.ps1` 常用命令见根目录 [README.md](../README.md)。文档按职责归类，动态任务进度与远端状态以 GitHub Issues 与实时 CI 为准。

## 测试与验证：`testing/`（4 篇）

- [验证矩阵](testing/verification-matrix.md)：**唯一当前态权威入口** —— 层（Layer）与档（Tier）术语、改动 → 档位 → `./pal.ps1` 命令 → 证据速查表、双平台覆盖边界与 T3 显式触发规则。
- [Game 测试分层与 SceneRunner](testing/game-test-layers.md)：新增、迁移或排查 `tests/Pal.Game.Tests` 用例时的生命周期、输入、更新边界与隔离规则。
- [时序与外部调度延迟](testing/time-boundaries.md)：改动业务时间、输入更新边界、断言变异或排查调度偶发时必读。
- [合并保护操作说明](testing/merge-protection.md)：`main` 分支经典合并保护的当前配置（`dotnet` / `prepare` / `game`）与受控修改/回读守则。

测试失败分类与异常停止顺序见 [测试异常处理规则](agents/test-diagnostics.md)。

## 架构决策记录：`adr/`

- [ADR-0001：原版事件采用忠实低层转写](adr/0001-faithful-event-transcription.md)
- [ADR-0002：第一阶段采用 Unit-only 默认反馈与测试合并门槛](adr/0002-unit-only-first-stage.md)（`Superseded by ADR-0005`）
- [ADR-0003：Unit 默认反馈信任原生工具，不重复构建审计框架](adr/0003-native-unit-feedback.md)（`Superseded by ADR-0005`）
- [ADR-0004：数据层与脚本层分层转写](adr/0004-data-and-script-layers.md)
- [ADR-0005：M7 验证基建重构与四档分层模型](adr/0005-m7-verification-infrastructure.md)
- [ADR-0006：游戏测试拆出 `tests/Pal.Game.Tests` 并切回官方 GdUnit4 包](adr/0006-split-game-tests-and-official-gdunit.md)
- [ADR-0007：T3 真实资源 GPU 端到端回归与仓库外共享缓存](adr/0007-t3-gpu-e2e-and-shared-asset-cache.md)

领域术语见根目录 [GLOSSARY.md](../GLOSSARY.md)。

## 实施计划：`milestones/`

开始或回溯某个里程碑时，结合对应 GitHub Issue 正文与评论阅读：

- [M0 工程骨架与工具链](milestones/m0/plan.md)（已交付）
- [M1 场景资源提取](milestones/m1/plan.md)（已交付）：附 [完整静态资源包 v1](milestones/m1/extract-scene.md)、[地图切片契约](milestones/m1/export-map.md)
- [M2 Godot 场景构建与探索](milestones/m2/plan.md)（已交付）：附 [M2 分层验收记录](milestones/m2/acceptance.md)、[框架兼容性记录](milestones/m2/framework-compatibility.md)
- [M3 独立真实 NPC 对话](milestones/m3/plan.md)（已交付）：附 [M3 分层验收](milestones/m3/acceptance.md)
- [M4 首个必经剧情战](milestones/m4/plan.md)（已交付）
- [M5 数据与脚本全量转换](milestones/m5/plan.md)（已交付）：附 [opcode 清单](milestones/m5/opcode-inventory.md)
- [M6 完整战斗、菜单与存读档](milestones/m6/plan.md)（已交付并合入 `main`）：附 [M6-C 存档保留矩阵](milestones/m6/m6c-save-matrix.md)

M7（agent 适配与验证基建）规格与叶票跟踪见 GitHub Issue [#249](https://github.com/fanxiao92/godot-pal/issues/249)。

## 设计背景：`design/`

按需查阅具体模块设计，不默认通读全部文档：

- 核心背景与路线：[背景与可行性](design/feasibility.md)、[架构设计](design/architecture.md)、[开发计划](design/development-plan.md)、[决策记录与待定问题](design/decisions.md)
- 离线数据与事件契约：[数据层 schema](design/data-schema.md)、[事件格式契约](design/events-schema.md)、[主线剧情往返](design/main-story-roundtrip.md)
- 对话与场景交互：[对话资源与显示](design/dialogue-display.md)、[trigger 执行](design/trigger-execution.md)、[搜索/返回规则](design/npc-interaction.md)、[Godot 宿主与启动配置](design/npc-interaction-host.md)
- 战斗与系统设计：[首战战前上下文](design/first-story-battle-context.md)、[现代战斗数据](design/modern-battle-data.md)、[经典战斗会话](design/classic-battle-session.md)、[原生剧情内容](design/native-story-content.md)、[故事包准备](design/story-package-preparation.md)、[战斗图像 schema](design/battle-image-schema.md)、[原生战斗视觉](design/native-battle-visuals.md)、[金钱状态](design/live-cash-state.md)、[背包状态](design/live-inventory-state.md)

引擎逻辑的只读参考源码导航见 [reference/README.md](../reference/README.md)。

## Agent 工作流：`agents/`

由根目录 [AGENTS.md](../AGENTS.md) 按条件触发进入：

- [执行环境契约](agents/execution-environment.md)
- [Issue 工作流](agents/issue-tracker.md) 与 [Triage 标签](agents/triage-labels.md)
- [领域文档规则](agents/domain.md)
- [单任务执行与有界纠正](agents/task-execution.md)
- [多票协调与恢复](agents/large-task-handoffs.md)
- [测试异常处理规则](agents/test-diagnostics.md)

评审期 Standards 规则见根目录 [CODING_STANDARDS.md](../CODING_STANDARDS.md)。

## 历史归档：`archive/`

仅在排障或追溯 M0 初始交付记录时按需查阅：

- [Godot C# 兼容性](archive/m0/godot-compatibility.md)（Issue #2）
- [最小游戏启动链路](archive/m0/game-startup.md)（Issue #3）
- [独立 CLI 入口](archive/m0/extract-cli.md)（Issue #4）
- [M0 集成验收与 CI](archive/m0/acceptance.md)（Issue #5）
