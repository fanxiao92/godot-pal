# 合并保护操作说明

本页记录已完成 #67 的阶段性配置/行为检查点及可复核的操作边界；当前交付回执见 [Issue #67](https://github.com/fanxiao92/godot-pal/issues/67)，原 full 要求与首次保护建立见 [Issue #38](https://github.com/fanxiao92/godot-pal/issues/38)。配置请求成功、workflow 文件、Actions App 绑定或仓库 public 都不是行为验收，也不预测所有主体/例外。

## 最近已记录的配置事实

截至 #67 在 2026-11-02 的交付检查点，配置对象仍是 `main` 的 classic branch protection，**已实际由 full 改为 Unit**，不是只发布了本地候选。以下均已完整 GET 回读比较，后续仍须操作前实时核对：

- required status check：`unit-check`，绑定实时确认的 GitHub Actions App `15368`；strict=true，要求分支与 base 最新提交同步。仅 PATCH required status checks，采用 checks-only 载荷。
- enforce_admins=true、禁止强推/删除及其余保护字段均与切换前保持一致。普通仓库 owner/admin 的拒绝/允许已验证，不使用 bypass 标志；这不等于实际非管理员身份或所有例外已验证。
- force push 与删除分支均关闭（false）。未增加 review、签名、线性历史、权限或 ruleset 要求。
- 保护对象是 `main`，不是所有分支；适用主体和 GitHub 权限仍以服务端实时回读为准。

### #38 历史身份

#38 最初要求的是 `full-check`，公开配置回填见 [#38 原检查点](https://github.com/fanxiao92/godot-pal/issues/38#issuecomment-6399533676)；该历史记录不改写为 Unit。第一次 payload 同时发送 `contexts[]` 与 `checks[]` 被服务端以 HTTP 422 拒绝；未写入的失败请求/响应按原结果保留，不能称为配置成功。随后确认无残留配置（GET 404），使用 checks-only payload 成功写入并回读。不要把旧 contexts 建议复制到后续操作。

## 作用与限制

当前阶段性测试要求是独立 `unit-check`；旧 `full-check` 仍精确失败，但已不是 required check。检查名及 Actions App 绑定不认证 workflow 路径或 ref；候选身份及 Unit/完整门区别按 [CI 分流](ci-dispatch.md)核对。

#67 原件分别证明：旧 full 要求仍在时普通 merge 实际 405 拒绝；切换后对 pending/不完整、取消、指定失败、缺失、错误来源同名 success、旧候选和错误实际 checkout 的普通 merge 拒绝；已知良好 [PR #70](https://github.com/fanxiao92/godot-pal/pull/70) 在 Unit success、旧 full failure 时普通 merge 实际允许，合入 `d9bc96e4cb4166600cab6027a7d6370221d01d55`，随后[真实 main push](https://github.com/fanxiao92/godot-pal/actions/runs/41440000263)全量 Unit/原件核验通过。控制 PR #71 关闭且未合并；失败原件和独立恢复均保留，#55 未操作。实际 run/attempt、检查绑定、HTTP 响应和证据限制见 #67 回执，不以本页摘要代替。

后续[原生 Unit 基线](../adr/0003-native-unit-feedback.md)保留检查名和服务端保护，不再做运行前后 API 双重证明或完整证据图审计；旧 #67 的强审计/负向原件不自动证明新 workflow 的远端行为。当前调用与少量 checkout 检查见 [CI 分流](ci-dispatch.md)，本地修改不等于远端已发布；本页历史切换步骤不需要因此重做。

管理员 enforcement 减少普通管理员路径的绕过，但有足够仓库权限的主体仍可能修改规则或工作流，且服务端能力有其例外边界。因此本页不宣称“绝对不可绕过”，也不将 Actions App 的 check-run 绑定描述为 workflow 路径认证。

## Unit-only 分阶段切换（#67）

[规格 #69](https://github.com/fanxiao92/godot-pal/issues/69) 接受第一阶段 Unit-only 覆盖，执行票为 [#67](https://github.com/fanxiao92/godot-pal/issues/67)。本地 workflow candidate、远端 Unit 检查验证、服务端保护切换是三个独立状态，#67 已分别完成并实际合入 main，才释放后继 #68。不能从其中任一阶段推断另外两项，也不将“Unit 满足阶段性测试要求”扩张为取消其它合并限制或完整验收成功。

候选自动运行 Core/CLI Unit，不安排重型门；旧 `full-check` 明确失败阻断，不以 skipped/neutral/空成功代表完整验收。整个 workflow 因此可能失败而独立 Unit 成功，这是允许的过渡状态，不是取消旧保护或放宽为无保护窗口的理由。旧成功是否可能被误用仍须实际远端矩阵证明，本地 blocker 不能替代该验收。

以下是 #67 已完成的切换顺序与未来同类操作的守则，**不是待执行清单，不据此重复写入**；重做、恢复及每类远端操作仍分别获得明确授权：

1. 实时 GET 保存旧保护对象、required checks 及来源、strict、管理员 enforcement、强推/删除开关和其它原字段；核对主体、候选及外部漂移，保存可恢复原件。旧 Actions App `15368` 是历史记录，切换前须实时确认，不能硬编码当作当前事实。
2. 获 push/PR/手动调度的对应授权后取得新 `unit-check` 的真实证据：Draft、无提交 Ready 转换、Ready 新候选、main/manual 及受控失败路径；保存 check-run、run/attempt、候选及时间线。发布不等于验证，Unit success 不自动意味着旧 full 要求满足。
3. 确认旧保护仍生效，旧 full 没有由 skipped/neutral/空成功或旧候选误放行；不能暂停保护等新检查。在没有所需证据时允许明确阻断，未测边界保持待验。
4. 获保护修改的单独授权后，仅把 required check 从 `full-check` 换成 `unit-check`，用 checks-only 载荷绑定实时确认的 Actions App；保持 strict、管理员 enforcement、禁止强推/删除及其它字段不变。立即 GET 回读比较实际字段。
5. 在受控条件验证成功允许，以及失败、取消、缺失/不完整、错误候选的拒绝；配置 GET 不替代行为验收，实际合并仍单独授权。未验证主体、权限例外和场景如实保留。
6. 异常、外部漂移或未知写入结果时先核实并停止；不自动重复写入或回滚。恢复也需授权，按保存的原件核对目标对象及无外部修改后处理。

重型自动验收的恢复须另立任务，验证工具链、候选绑定、重复运行、指定失败 controls 和完整证据，再分别批准调度与保护切换；历史完整门或 #64 spike 成功不构成恢复许可。

## 受控操作与恢复

1. 先 GET 保存当前保护对象、required checks、管理员 enforcement、删除/强推开关及未配置字段；保存到任务自有的新证据根，不覆盖旧记录。
2. 只使用 checks-only 载荷更新必要检查，立即 GET 回读并比较实际生效字段。遇到 4xx、身份不明或回读不一致，保留请求/响应并停止，不以本地 YAML 或 issue 状态代替。
3. 受控拒绝/允许场景、无新 commit 的 Ready 转换、Ready 更新、取消/失败/不完整以及候选身份错配，均只在获授权的远端操作中验证；每个结果保留实际候选、check-run、mergeability 和时间线。不要合入无关或故意损坏代码。
4. 验收后的保护保留启用，不作为清理项删除。确需恢复时，仅在明确授权下处理已保存、由本任务写入且未被外部修改的规则对象；先 GET 比对实际字段，发现外部变化就停止并交由维护者处理。恢复请求成功仍需 GET 回读，失败保留原始响应。

本页不授予远端写入、受控 PR、合并、Issue 收尾或 SDK/引擎运行授权；这些操作按各自明确授权及串行规则执行。普通管理员路径与实际非管理员身份不是同一测试，已配置 enforcement 不等于已验证所有主体或绝对不可绕过。
