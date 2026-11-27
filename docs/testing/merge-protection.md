# 合并保护操作说明

本页记录 `main` 分支经典合并保护的当前真实配置与受控修改/回读守则。最近切换由 [Issue #252](https://github.com/fanxiao92/godot-pal/issues/252)（[PR #259](https://github.com/fanxiao92/godot-pal/pull/259)）于 2026-11-27 完成；历史切换记录见 [Issue #38](https://github.com/fanxiao92/godot-pal/issues/38)（首次 `full-check`）与 [Issue #67](https://github.com/fanxiao92/godot-pal/issues/67)（过渡期 `unit-check`）。配置请求成功、workflow 文件、Actions App 绑定或仓库 public 都不是行为验收，也不预测所有主体/例外。

## 当前已记录的配置事实（2026-11-27 / #252）

配置对象为 `main` 的 classic branch protection，已通过 checks-only 载荷从旧 `unit-check` 切换为全新 3 并行 Linux CI（`ubuntu-24.04`，经 `./pal.ps1 ci` 驱动），并已完成实时 GET 回读比对（后续任何修改前仍须先实时 GET 核对）：

- **required status checks**：`dotnet`、`prepare`、`game`，均绑定实时确认的 GitHub Actions App `15368`。
- **`strict=true`**：要求分支与 `main` 最新提交同步。
- **`enforce_admins=true`**：普通仓库 owner/admin 路径同样受限（不使用 bypass 标志）；这不等于已验证所有非管理员主体或绝对不可绕过。
- **其余开关保持不变**：`allow_force_pushes=false`、`allow_deletions=false`、`required_signatures=false`、`required_linear_history=false`、`block_creations=false`、`required_conversation_resolution=false`、`lock_branch=false`。
- 保护对象仅限 `main`，不是所有分支；适用主体与服务端例外仍以 GitHub 实时回读为准。

### 历史演进简记

- **#38**：首次建立 `main` 保护，要求 `full-check`（首次同时发送 `contexts[]` 与 `checks[]` 被 HTTP 422 拒绝，后续一律采用 checks-only 载荷）。
- **#67 / #69**：第一阶段切换为独立 `unit-check`（Windows），随后由 [ADR-0003](../adr/0003-native-unit-feedback.md) 精简为原生 Unit 反馈。
- **#252（当前）**：上线 3 并行 Linux CI（`dotnet`、`prepare`、`game`），退役 Windows CI、旧 `full-check` 与旧 `Test-M0`/`Test-Unit`/强审计脚本，并将 `main` 保护切换为要求三项 Linux check 全部通过。

## 作用与限制

- 检查名与 Actions App 绑定（`15368`）不认证 workflow 路径或 ref；服务端能力存在权限例外边界，本页不宣称“绝对不可绕过”。
- 本地修改 workflow 文件不等于远端已生效，也不替代真实 PR 实跑与 `main` push 实跑结果。
- 管理员 enforcement 减少普通管理员路径误合并，但有足够仓库权限的主体仍可修改规则或工作流。

## 受控操作与恢复守则

1. **操作前先 GET 保存基线**：实时 GET 保存当前保护对象、required checks 及 `app_id`、strict、管理员 enforcement、强推/删除开关及其余原字段；保存到任务自有的新证据目录，不覆盖旧记录。不把历史 `app_id` 硬编码当作当前事实，切换前须从目标提交的实时 check-runs 核对。
2. **仅使用 checks-only 载荷更新**：PATCH `required_status_checks` 时只发送 `strict` 与 `checks[]`（不混发 `contexts[]`），立即 GET 回读并逐字段比较。遇到 4xx、身份不明或回读不一致，保留请求/响应并停止，不以本地 YAML 或 issue 状态代替。
3. **验证拒绝与允许行为**：在获授权的远端操作中分别核验未就绪/失败时的普通 merge 拒绝（HTTP 405）与三项 required checks 全绿时的普通 merge 允许；不合入无关或故意损坏的代码。
4. **保留保护与受控恢复**：验收后的保护保持启用，不作为清理项删除。确需恢复时，仅在明确授权下核对当前 GET 与已保存基线无外部漂移后执行，并在恢复后再次 GET 回读。

本页不授予远端写入、受控 PR、合并、Issue 收尾或 SDK/引擎运行授权；每类操作须分别获得明确授权。
