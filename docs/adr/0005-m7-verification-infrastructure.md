# ADR-0005：M7 验证基建重构与四档分层模型

> Status: Accepted（2026-11-27，取代 [ADR-0002](0002-unit-only-first-stage.md) 与 [ADR-0003](0003-native-unit-feedback.md)）
> 关联规格：[M7 规格 #249](https://github.com/fanxiao92/godot-pal/issues/249)、[M7-2 #251](https://github.com/fanxiao92/godot-pal/issues/251)、[M7-3 #252](https://github.com/fanxiao92/godot-pal/issues/252)、[M7-4 #253](https://github.com/fanxiao92/godot-pal/issues/253)、[M7-6 #255](https://github.com/fanxiao92/godot-pal/issues/255)

## 背景

在 M0–M6 演进期间，项目先后采用过全量重型 Windows 门（`scripts/Test-M0.ps1`）与第一阶段 Unit-only 过渡门槛（ADR-0002 / ADR-0003）。随着 M5/M6 交付，该体系暴露出四个结构性问题：

1. **入口碎片化**：`scripts/` 下并存 20+ 个测试、范围回归、强审计与交接脚本，人与代理（含 LRA）需记忆多套命令和参数。
2. **CI 覆盖缺口与平台耦合**：`main` 仅由 Windows `unit-check` 保护，Prepare 与 Game 集成测试长期未纳入自动 PR 门禁，且跨平台（Linux 大小写敏感、文件锁、符号链接）问题无法在 PR 阶段暴露。
3. **文档与规则膨胀**：`docs/testing/` 膨胀至 16 篇，静态记录的里程碑进度与旧门禁说明互相冲突，导致代理上下文腐烂（#230）。
4. **本地反馈缺乏增量选测**：改动单个模块时缺乏统一的增量受影响测试入口。

## 决策

1. **四档验证分层（T0–T3）**：
   - **T0（本地增量快反馈）**：`./pal.ps1 test affected`，基于静态 `.csproj` 反向 `ProjectReference` 闭包与 `tests/affected-map.psd1` 选测，纯文档改动返回 `0`（no-op），构建/配置/前门/CI 改动自动回退 `full`。
   - **T1（PR 自动门禁 · Linux CI）**：`ubuntu-24.04` 上 3 个并行必需 job（`dotnet` / `prepare` / `game`），由 `./pal.ps1 ci <dotnet|prepare|game>` 统一驱动；`main` 分支经典合并保护切换为要求三项全绿（见[合并保护操作说明](../testing/merge-protection.md)）。
   - **T2（`main` push / 手动复现）**：与 T1 同源 3 并行 job（`push: main` 与 `workflow_dispatch`），不维护第二套 CI 编排。
   - **T3（本地真实资源 + GPU 端到端回归）**：按需显式触发，见 [ADR-0007](0007-t3-gpu-e2e-and-shared-asset-cache.md)。
2. **单一跨平台前门 `./pal.ps1`**：使用 PowerShell 7（`pwsh`）统一封装 `test`、`ci`、`godot`、`assets`、`report`，本地开发者、普通代理、LRA 与 GitHub CI 共享同一入口与机器可读 `run-summary.json`。
3. **二态 0/1 原则，无自动重试、无 quarantine**：测试只有通过（`0`）与失败（`1`）；门内不自动重试，不维护隔离清单，偶发即缺陷、直接定位修复；保留 `TreatNoTestsAsError=true` 与 TRX `outcome`/`failed` 双核对。
4. **彻底退役旧自制审计与旧 `full-check`**：退役 `Test-M0.ps1`、`Test-Unit.ps1`、`Test-Range.ps1`、`GameTestBatches.ps1`、旧强审计/成本报告/手工交接脚本及 12 篇过时测试文档；由单一权威快照[验证矩阵](../testing/verification-matrix.md)承接当前态说明。

## 代价与边界

- 放弃旧自制脚本的全仓散列、TRX 完整身份图审计与多套自定义退出码；换取前门极简、CI 零分叉与代理上下文低噪。
- Linux CI 覆盖跨平台构建、大小写敏感路径与公开全量 headless 回归，但不能替代 Windows 本地 GPU 像素核对与人工桌面观感；验证面不可互代的硬约束保持不变。
- `ADR-0002` 与 `ADR-0003` 作为历史演进记录原件保留，头部标记 `Superseded by ADR-0005`。
