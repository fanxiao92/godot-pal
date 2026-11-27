# 文档导航

项目概览、技术基线与 `./pal.ps1` 常用命令见根目录 [README.md](../README.md)；当前态三段管道、物理模块拓扑与编译期机械护栏见根目录 [ARCHITECTURE.md](../ARCHITECTURE.md)；代码派生的实时指令/模块支持状态见 [`sdlpal` 对位与能力账本](generated/capabilities-ledger.md)。文档按职责归类，动态任务进度与远端状态以 GitHub Issues 与实时 CI 为准。

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

## 有效格式契约与可行性：`design/`（5 篇）

仅保留仍为当前态真源的格式契约与项目可行性基线（历史切片过程稿与 M6 就绪度核对稿已归档至 [历史文档归档索引](archive/README.md)）：

- [背景与可行性](design/feasibility.md)
- [数据层 schema](design/data-schema.md)
- [事件格式契约](design/events-schema.md)
- [现代战斗数据](design/modern-battle-data.md)
- [战斗图像 schema](design/battle-image-schema.md)

引擎逻辑的只读参考源码导航见 [reference/README.md](../reference/README.md)。

## 自动生成文档：`generated/`

- [`sdlpal` 对位与能力账本](generated/capabilities-ledger.md)（由 `./pal.ps1 test check-ledger` 零漂移校验，严禁手改）

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

仅在排障或追溯历史阶段过程稿时按需查阅，完整分类见 [历史文档归档索引](archive/README.md)：

- [M0 初始交付记录](archive/README.md#目录分类)：`m0/`（4 篇）
- [早期总览与切片过程设计稿](archive/README.md#目录分类)：`design/`（15 篇）
- [M6 切片就绪度核对稿](archive/README.md#目录分类)：`m6-readiness/`（6 篇）
