# THROWAWAY PROTOTYPE: `sdlpal` 对位与能力账本（#267）

> **注意**：本目录为 [原型：代码派生的 sdlpal 对位与 Opcode/子系统能力账本及零漂移校验](https://github.com/fanxiao92/godot-pal/issues/267) 的一次性原型归档分支（`prototype/capability-ledger`），**不合入 `main`**。已确认的正式规格结论记录在 `#267` 评论与路线图 `#264` 中。

## 运行原型脚本

```powershell
pwsh -NoProfile -NonInteractive -File ./prototypes/capability-ledger/generate-ledger-prototype.ps1
```

## 本原型已验证的三项结论

1. **信息结构与可读性**：四段紧凑布局（能力覆盖总览、`reference/sdlpal` 核心模块对位索引、已接入运行时 Opcode 速览、`<details>` 折叠的全量 165 条 Opcode 明细），使用 `✅` / `⚠️ (...)` / `❌` / `—` 区分支持状态与原版适用性；正式入库路径定为 `docs/generated/capabilities-ledger.md`。
2. **真源提取机制（方案 A）**：利用 `tools/Pal.Extract` 已引用 `src/Pal.Core` 的编译期关系，由 `Pal.Extract` 新增账本导出子命令直接读取 `OpcodeRegistry.ByOpcode` 与 `TriggerScriptSession` / `DynamicEventSession` 的极轻量静态 `SupportedOpcodes` 元数据；`Pal.Core.UnitTests` 加双向一致性单测防元数据漂移；`Pal.Game` 侧差异清单与 `sdlpal` 模块映射保持轻量强类型声明并由测试校验。
3. **前门与零漂移门禁契约**：保持 `./pal.ps1` 单一对外前门（维持现有 5 个顶层子命令），对内按 `scripts/pal/*.psm1` 模块化；采用 `./pal.ps1 test check-ledger`（校验）与 `./pal.ps1 test check-ledger --update`（重生成），并接入 `./pal.ps1 ci dotnet` Guard 链（漂移时输出精确重生成命令并 `exit 1`）。
