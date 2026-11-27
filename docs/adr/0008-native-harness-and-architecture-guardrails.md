# ADR-0008：原生 Harness、模块拓扑与编译期架构机械护栏

- **状态**：Accepted
- **日期**：2026-11-28
- **关联规格**：[原生 Harness 与代码地形整备规格（PRD）#270](https://github.com/fanxiao92/godot-pal/issues/270)（决策路线图 [#264](https://github.com/fanxiao92/godot-pal/issues/264)、调研 [#266](https://github.com/fanxiao92/godot-pal/issues/266)、原型 [#267](https://github.com/fanxiao92/godot-pal/issues/267)、决策 [#268](https://github.com/fanxiao92/godot-pal/issues/268) / [#269](https://github.com/fanxiao92/godot-pal/issues/269)）
- **后续预留**：[LRA 单票终态自动沉淀回路（retro 触发 + signals 生命周期 + 注入闭环）#229](https://github.com/fanxiao92/godot-pal/issues/229) 预留编号 **`ADR-0009`**。

## 背景

在完成 M0–M6 主线交付与 M7 验证基建（`ADR-0005`–`ADR-0007`）之后、进入后继全场景运行时阶段（[#125](https://github.com/fanxiao92/godot-pal/issues/125)）之前，仓库累积了三类典型工程熵增：

1. **代码拓扑平铺与微环**：`src/Pal.Core/` 平铺 40+ 个 `.cs` 文件且夹杂冻结的 M4 首战切片与 3 处域内微环；`src/Pal.Game/Scripts/Dialogue/` 错放了 6 个战斗脚本、2 个菜单/商店脚本与 `TriggerEventLoader`，存在 `Dialogue ↔ Profiles` 与 `Map ↔ Profiles` 微环。
2. **软约束漂移**：仅靠 `AGENTS.md` 自然语言约定与手写能力进度文档，难以在编译期实时拦截跨层反向依赖、测试开关渗入生产源码或能力矩阵漂移；`docs/design/` 积压 26 篇过程稿与就绪度文档。
3. **多宿主 Agent 技能分散**：Antigravity、Pi（`pi-subagents`）与 Claude Code 的本地技能发现路径存在差异，且 Windows 默认 `core.symlinks=false` 会破坏入库的 Git 符号链接（`120000`）。

## 决策

1. **保持 2 个核心 `.csproj`，项目内按子目录 + 子命名空间物理归位（零业务行为变更）**：
   - `src/Pal.Core/` 保持纯 .NET 10 单库，拆分为 `Shared`、`State`、`Exploration`、`Dialogue`、`Battle`、`Events`、`Menu`、`Save`、`Legacy`（冻结历史切片），根仅留 `Startup.cs`。
   - `src/Pal.Game/Scripts/` 保持单工程，成对迁移 `.cs` 与 `.cs.uid`；严格保护 `Scripts/Resources/`（`Pal.Game.Resources`）路径与命名空间稳定以守护样本与本地 T3 缓存 `.tres` 绑定。
   - 保护 36 个稳定 `Resources`，冻结隔离 M3/M4 切片，定向删除生产 0 引用的 `DialogueLanguageSelection.cs` 及 11 个纯 M4 过程自证 `*Identity`/`*Proof` 资源类。
2. **分层机械护栏：`.editorconfig` + `BannedApiAnalyzers` + 仓内极薄 `tools/Pal.Analyzers`（不引入 `ArchUnitNET`/`NetArchTest`）**：
   - `Microsoft.CodeAnalysis.BannedApiAnalyzers`（`RS0030=error`）承接固定 API 封禁（`Pal.Core` 与 `*UnitTests` 禁磁盘 I/O、进程、墙钟时间、P/Invoke、`Godot`/`GdUnit4`；`Pal.Game` 全禁 `InternalsVisibleToAttribute`）。
   - `tools/Pal.Analyzers`（`netstandard2.0`、`OutputItemType="Analyzer"`、`ReferenceOutputAssembly="false"`、`GeneratedCodeAnalysisFlags.None`）承接关系与品味规则：`PAL0001`（4 条轻红线）、`PAL0002`（友元白名单仅允许 `Pal.Core -> Pal.Extract`）、`PAL0003`（禁 `src/` 生产代码测试开关与可变静态测试钩子）；所有诊断统一采用“违规事实 + 架构理由 + 自修复指引”格式。
   - `scripts/Test-UnitBoundary.ps1` 保留零 MSBuild 根目录与 `.csproj` 窄形状守卫，放行 `.editorconfig`、`ARCHITECTURE.md`、`.agents`、`.claude`、`signals`、`BannedApiAnalyzers`、`AdditionalFiles` 与编译期 Analyzer `ProjectReference`。
3. **代码派生的 `sdlpal` 对位与能力账本（方案 A）**：
   - `TriggerScriptSession.SupportedOpcodes` 与 `DynamicEventSession.SupportedOpcodes` 作为轻量静态真源，由 `Pal.Core.UnitTests` 双向守卫单测锁定。
   - `Pal.Extract export-capability-ledger` 编译期直读 `OpcodeRegistry.ByOpcode`（165 条）与 `SupportedOpcodes`，输出四段确定性 LF Markdown 至 `docs/generated/capabilities-ledger.md`，并经 `./pal.ps1 test check-ledger [--update]` 接入 `./pal.ps1 ci dotnet` 零漂移红灯门禁。
4. **文档地形收敛与强卫生门禁**：
   - 根目录新增 [ARCHITECTURE.md](../../ARCHITECTURE.md)；`docs/design/` 收敛为 5 篇有效契约与可行性基线，21 篇过程稿与 M6 就绪度文档归档至 `docs/archive/`。
   - `scripts/Test-RepoHygiene.ps1` 升级为含 `#anchor` 片段校验与活动文档孤儿可达性校验，并在 `./pal.ps1 ci dotnet` 默认以 `-AllDocs` 运行。
5. **前门聚合、脱敏自省与单一真源工程 Skills**：
   - 保持 `./pal.ps1` 现有 5 个顶层子命令，新增 `./pal.ps1 test check-arch` 与严格脱敏的 `./pal.ps1 assets inspect`。
   - 工程 Skills 以 `.agents/skills/<name>/SKILL.md` 为唯一入库真源（首批落地 `verify`、`garden`），`.pi/settings.json` 增配 `"skills": ["../.agents/skills"]`，`scripts/Sync-AgentSkills.ps1` 在本地幂等创建 `.claude/skills -> .agents/skills`（Windows NTFS `Junction` / POSIX `SymbolicLink`）且 `/.claude/` 加入 `.gitignore`。
   - 高频工程信号经 `garden` 评审优先晋升为机械护栏或测试夹具；通用工程术语不写入游戏领域 [GLOSSARY.md](../../GLOSSARY.md)。

## 后果与权衡

- **收益**：架构红线在 `dotnet build` 编译期实时给出精确 `file(line,col)` 与自修复指引，零运行时依赖增加且与 Godot 4.7.2 `Godot.SourceGenerators` 无缝共存；能力账本与代码真相永不漂移；多宿主技能零重复维护。
- **代价**：新增 `tools/Pal.Analyzers` 与 `tests/Pal.Analyzers.Tests` 两个轻量 `.NET` 工程；新增或调整子系统命名空间时需同步核对 `PalArchitectureAnalyzer` 轻红线与 `docs/generated/capabilities-ledger.md`。
