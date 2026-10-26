# 合并保护操作说明（#38）

本页记录 #38 已执行的**配置事实**和可复核的操作边界；受控拒绝/允许结果以 [Issue #38](https://github.com/fanxiao92/godot-pal/issues/38) 的最新验收评论为单点来源。本页不预言尚未完成的受控验证，也不把配置请求成功、workflow 文件、Actions App 绑定或仓库 public 当成合并保护验收。

## 当前配置事实

配置对象为 `main` 的 classic branch protection，已应用配置的公开回填见 [Issue #38 配置检查点](https://github.com/fanxiao92/godot-pal/issues/38#issuecomment-6399533676)。以下是已回读配置摘要，不替代每次操作前的实时 GET：

- required status check：`full-check`（来源为 Actions App `15368`）；strict=true，要求分支与 base 最新提交同步；checks-only 接口实际生效。
- enforce_admins=true 是已回读配置字段，不是所有主体均已受验证的证明。实际主体、管理员/例外边界及普通拒绝/允许结果，以 Issue #38 已完成的受控证据为准；未测项保持待验。
- force push 与删除分支均关闭（false）。未增加 review、签名、线性历史、权限或 ruleset 要求。
- 保护对象是 `main`，不是所有分支；适用主体和 GitHub 权限仍以服务端实时回读为准。

第一次 payload 同时发送 `contexts[]` 与 `checks[]` 被服务端以 HTTP 422 拒绝；未写入的失败请求/响应按原结果保留，不能称为配置成功。随后确认无残留配置（GET 404），使用 checks-only payload 成功写入并回读。不要把旧 contexts 建议复制到后续操作。

## 作用与限制

`full-check` 是当前稳定检查名，但检查名出现不等于 workflow 路径或 ref 身份已认证；候选身份、Ready/Draft 语义和完整门仍按 [CI 分流](ci-dispatch.md) 核对。required check 会阻止缺失、失败、取消或不完整的必要证据进入普通合并路径；实际拒绝/允许仍须查看 Issue #38 的受控结果，不能由本页推断。

管理员 enforcement 减少普通管理员路径的绕过，但有足够仓库权限的主体仍可能修改规则或工作流，且服务端能力有其例外边界。因此本页不宣称“绝对不可绕过”，也不将 Actions App 的 check-run 绑定描述为 workflow 路径认证。

## 受控操作与恢复

1. 先 GET 保存当前保护对象、required checks、管理员 enforcement、删除/强推开关及未配置字段；保存到任务自有的新证据根，不覆盖旧记录。
2. 只使用 checks-only 载荷更新必要检查，立即 GET 回读并比较实际生效字段。遇到 4xx、身份不明或回读不一致，保留请求/响应并停止，不以本地 YAML 或 issue 状态代替。
3. 受控拒绝/允许场景、无新 commit 的 Ready 转换、Ready 更新、取消/失败/不完整以及候选身份错配，均只在获授权的远端操作中验证；每个结果保留实际候选、check-run、mergeability 和时间线。不要合入无关或故意损坏代码。
4. 验收后的保护保留启用，不作为清理项删除。确需恢复时，仅在明确授权下处理已保存、由本任务写入且未被外部修改的规则对象；先 GET 比对实际字段，发现外部变化就停止并交由维护者处理。恢复请求成功仍需 GET 回读，失败保留原始响应。

本页不授予远端写入、受控 PR、合并、Issue 收尾或 SDK/引擎运行授权；这些操作按各自明确授权及串行规则执行。普通管理员路径与实际非管理员身份不是同一测试，已配置 enforcement 不等于已验证所有主体或绝对不可绕过。
