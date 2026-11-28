---
name: garden
description: "Milestone/phase gardening and anti-entropy audit for godot-pal: runs ./pal.ps1 test check-arch, audits ARCHITECTURE.md/docs/design/signals/, and outputs a read-only pruning & guardrail promotion report."
---

# Garden — 里程碑除草与保鲜审计

本技能用于里程碑收尾或新阶段开工前的**只读抗熵增审计**，产出结构化除草与规则晋升建议供维护者拍板（不擅自删除业务代码或弱化硬约束）。

## 1. 运行全量静态架构与卫生聚合检查

```powershell
pwsh -NoProfile -NonInteractive -File ./pal.ps1 test check-arch
```

确认以下 6 项全绿并记录任何漂移或失败：
1. `Test-PowerShellSyntax.ps1`
2. `Test-UnitBoundary.ps1`
3. `Test-RepoHygiene.ps1 -AllDocs`（含 `#anchor` 片段与孤儿文档可达性）
4. `test check-map`（MSBuild 依赖图与 `tests/affected-map.psd1`）
5. `test check-ledger`（`docs/generated/capabilities-ledger.md` 零漂移）
6. `dotnet build GodotPal.sln`（编译期 `RS0030` 与 `PAL0001`–`PAL0003`）

## 2. 巡检文档与信号候选池（只读）

1. **`ARCHITECTURE.md` 与 `docs/design/`**：核对三段管道、模块拓扑图与有效契约（5 篇）是否与当前源码一致；检查是否混入过程性草稿或过时切片状态。
2. **冻结历史切片**：核对 M3/M4 冻结切片是否未被正式域反向依赖或偷偷扩展新功能。
3. **`signals/` 候选池**（若存在）：盘点高频重复踩坑信号，评估升级路径。

## 3. 输出只读除草与规则晋升报告

按以下优先级提出建议：
1. **优先晋升为机械护栏或测试夹具**：能落入 `BannedSymbols.*.txt`（`RS0030`）、`tools/Pal.Analyzers`（`PAL*`）、`Test-UnitBoundary.ps1`、`Test-RepoHygiene.ps1` 或单元测试的高频问题，不要只靠堆砌 `AGENTS.md` 自然语言规则。
2. **归档过时过程文档**：将已完成切片的过程稿移入 `docs/archive/` 并保持 `docs/archive/README.md` 可达。
3. **领域边界保护**：通用工程术语（Harness / Signal / Retro / Ledger）留在 `docs/agents/` 与 `ADR`，绝不污染游戏领域 `GLOSSARY.md`。
