---
name: verify
description: "Tiered verification and evidence capture for godot-pal via ./pal.ps1 (T0/T1/T3, serial Godot execution, fail-stop without auto-retry, copyright-safe PR evidence)."
---

# Verify — 分级验证与取证

本技能是 `godot-pal` 变更验证与取证的标准执行流程，权威规则见 [docs/testing/verification-matrix.md](../../../docs/testing/verification-matrix.md) 与 [docs/agents/test-diagnostics.md](../../../docs/agents/test-diagnostics.md)。

## 1. 判定验证档位（T0 / T1 / T3）

1. 先预览增量选测决策（零副作用）：
   ```powershell
   pwsh -NoProfile -NonInteractive -File ./pal.ps1 test affected --explain
   ```
2. 按改动范围匹配档位：
   - **纯文档 / 只读参考**：`test affected` 为 no-op；改链接或结构时补跑 `./pal.ps1 test check-arch` 或 `Test-RepoHygiene.ps1 -AllDocs`。
   - **架构/护栏/账本/脚本改动**：先跑 `./pal.ps1 test check-arch`，再跑 `./pal.ps1 ci dotnet`。
   - **运行时 / `Pal.Core` / `Pal.Game` 改动**：跑 `test affected`（或对应 `ci dotnet` / `ci prepare` / `ci game`）。
   - **渲染 / 字体 / UI / 战斗演出 / 真实资源加载改动**：按验证矩阵触发本地 **T3 GPU E2E**（`./pal.ps1 assets prepare` → `./pal.ps1 test e2e --gpu --out <dir>` → `./pal.ps1 report --from <dir> --out <dir>/pr-comment.md`）。

## 2. 执行铁律

- **本地 Godot 全局串行**：执行 `ci prepare`、`ci game`、`test game`、`test e2e` 或 `assets prepare` 前，仅检查 `PID/Name` 确认无冲突 Godot 进程；发现未知 Godot 进程立即暂停确认归属，绝不擅自结束。
- **二态 0/1 与异常止损**：门内严禁自动重试、严禁设 quarantine。同时核对进程退出码、TRX `outcome`/`failed` 与 `run-summary.json`。遇崩溃、中止、超时、未知退出或结果缺失立即保留命令、提交、原日志和部分结果并停止。
- **验证面不可互代**：Unit 不替代引擎接线；headless 不替代 GPU 像素核对；GPU 截图不替代人工桌面观感确认。

## 3. 脱敏证据交付

- 使用 `./pal.ps1 report --from <artifacts-dir> --out <file>` 生成标准 Markdown 摘要。
- **绝不提交或上传**真实原版数据、派生资源、版权截图或本机私密绝对路径。
