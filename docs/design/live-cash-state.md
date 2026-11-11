# 宿主持有的现金状态（#177、#178、#179、#195 / #131 前置）

`CashLiveState` 是纯 .NET 的显式现金所有者，沿用 `RoleLiveState` 的宿主持有方式，但不合并成通用 GameState。构造时必须给出 `uint initialCash`，只读 `Cash` 暴露完整 DWORD 域 `0..4294967295`。同一宿主的后续消费者应共享同一个实例，而不是复制余额或每次调用重建；另一个宿主应拥有独立实例。

## 已实现的参考规则

| 公开操作 | 行为 | 固定仓库参考 |
| --- | --- | --- |
| `TryChange(int amount)` | 仅接受 signed WORD `-32768..32767`。负增量不足时返回 false 且不写余额；成功返回 true。零增量成功。正增量按 uint32 回绕，不夹值。非法参数抛 `ArgumentOutOfRangeException`，余额不变。 | `script.c:952–968`，0x001E 的 SHORT/WORD affordability 与 DWORD `+=` |
| `Halve()` | unsigned 整数除以二，奇数向下取整。 | `script.c:2600–2605`，0x008F |
| `Credit(uint amount)` | 已获调用方授权的非负 DWORD 增量，按 uint32 回绕；不设现代钱包上限。 | `battle.c:1054`，PAL_BattleWon 的 `dwCash += iCashGained` |

类型依据为 `global.h:544` 的 `dwCash`、`common.h:145` 的 unsigned DWORD。负增量的绝对值在先验证 signed WORD 后计算，因此 `-32768` 的不足比较是32768，不发生 SHORT 取负溢出。以上是经典/Windows 参考的现金规则，不承诺新战斗能力、并发修改安全或跨存档随机一致性。参数域拒绝属于托管 API 边界，不是原版新增玩法分支。

`Credit` **只是算术，不是结算入口**：它不验证所属 lifecycle、结果类型或完成回执，不去重、不自动发奖。#179 由现有 lifecycle 的所属 Won 结算路径调用一次；不是让外部宿主拿完成回执再调用 `Credit`。参考要求现金先于敌方战后脚本入账，最终完成门只交接结果，不再次发奖（详见下节）。不得因为算术或 Core 结算已实现，就把正常 Main 接线算作已验收。

## 测试边界与未交付链路

`CashLiveStateTests.ExplicitOwnerPreservesDwordCashAndReferenceArithmetic` 只使用公开构造、操作和余额，独立字面值覆盖 signed WORD 两端、不足/恰够/零、uint32 回绕、大于 int.MaxValue 的余额、奇偶减半、非法参数无变更及不同 owner 隔离。它不执行 trigger、Godot、Main 或真实资源。

#177 仅关闭 Core 状态/算术前置；#178 增加现有 `TriggerScriptSession` 的显式现金执行能力。**#131 保持未完成**，正常 Main 尚未接线，`Main.StoryCashGranted` 仍只是既有奖励展示累计。后续 #131 必须补齐：
- 正常 Main 持有/绑定同一现金 owner，沿现有当前战斗和完成回执门交接；不再从 `StoryCashGranted` 或完成回执重复发奖。Core 入账顺序与回执拒绝已由 #179 验证，仍须正常 Main 证据；
- 独立上下文显式初始现金、必要的原生准备和正常 Main/SceneRunner 实测；按 #126 的 `m6-no-legacy-compat-20261111` 统一当前格式并迁移活跃调用方，不再要求兼容旧缺字段资源。缺失是否允许由入口实际能力决定，不能默认可用余额0；
- 未来重建责任：由宿主从已批准内容或恢复记录显式构造一次并重新绑定。这里不定义存档 schema、槽位、磁盘读写或自然剧情 save。

## trigger 现金执行契约（#178）

现有 session 构造、`ForSharedRoles` 和静态 `ValidateEntry` 尾部可显式传入可空的 `cashLiveState`；只读 `CashLiveState` 返回同一实例，不复制余额、不重置、不默认构造。旧调用不传入时仍拒绝所选路径上的现金命令，保留原 content-only/context 诊断并指出缺失 owner。独立 owner 互不影响；后续宿主重建仍须显式构造并重新绑定。本票没有增加 profile/原生声明，也没有让 Main/Dialogue 自动取得现金状态。

解析器沿用已冻结具名字段校验，保留源 WORD 只作诊断，执行消费现代语义字段：

- `changeCash.amount` 是 signed WORD；`to` 是必须存在的已解析引用，支持跨文件和指令零。成功增减后走 next；不足时不扣钱、跳 to。两种分支都不改 `ScriptSuccess`，只有原来的 `markFailed` 等指令可改变它。to=0 结束当前执行上下文，不执行归档的指令零（参考 `wScriptEntry = operand1 - 1` 与函数尾 return+1）。
- `halveCash` 对同一 owner 调用 unsigned 整除，然后走 next。
- 预检在 `Start` 写 call/caller 状态前检查完整 next/to 两分支，连正金额当前不会走的 shortage 分支也检查；不按当前余额剪枝，不改变现金或消费随机数。静态及当前 requirements 使用相同能力门。没有新增 auto、call/goto、脚本上下文或通用退款。

`TriggerScriptCashTests.Explicit_shared_cash_commands_preserve_reference_flow_and_preflight_before_mutation` 从公开 Core 构造、requirements、Start、显示回执及余额观察这些行为，独立字面值来自固定 `script.c:952–968,2600–2605,3085`。它也验证过早/重复/过期显示回执不会重放现金变更、无 owner 的旧调用拒绝、两个可能分支中的不支持命令均在前缀写现金之前拒绝。它不是正常 Main/SceneRunner、战斗奖励所有权或原生准备证据。

## 正式胜利结算与现金顺序（#179）

`TriggerScriptSession` 创建正式 `ClassicBattleLifecycleSession` 时，经现有 internal 构造尾部传递同一个可空现金 owner；没有新增公共 lifecycle API、默认 seed 或余额副本。原公共 lifecycle 构造仍未绑定，旧 trigger 调用未传 owner 时，原回执总额及展示行为不变。预检/查询只检查能力，不进行结算，也不消费 RNG。

现金提交点在现有 lifecycle 的 `SettleGrowth`：只有实际战斗 Won 才调用，取正式 combat 的 KO 累计 `CashGained`，按 DWORD 规则入账一次，随后成长、敌方战后脚本、恢复和完成回执沿原链执行。固定参考 `battle.c:1054` 先写 `dwCash`，`1334–1337` 才调用各敌方脚本；`1809–1811` 仅 Won 调用 `PAL_BattleWon`。零经验也发现金，不能用奖励 UI 是否出现作为发奖条件。Core 数值结算顺序不证明 Godot 奖励框的显示/输入时序。

敌方战后脚本和返回的调用事件立即消费同一个余额；已执行脚本的现金变化不回滚。`VictorySettlement.CashGranted` / `CompletionReceipt.CashGranted` 保持原奖励事实，不是脚本运行后的余额。`AcknowledgeBattle` 继续验证当前 request 与所属 completion，合并角色并恢复调用；它**不发现金**。未完成/null、foreign、wrong-ID、旧请求及重复回执不能再次发奖。每场合法的新战斗仍可各自结算一次，不用跨战斗全局去重集。Lost 不走 Won 结算，现金不变；尚未实现的成功逃跑等结果不由本票补造。

`TriggerScriptBattleBindingTests.Shared_cash_victory_settles_once_before_enemy_scripts_and_owned_completion_never_regrants` 使用公开 Core 会话、真实行动、脚本和回执：独立字面预期10+7+11=28先于两段战后脚本，第一段扣28并等待显示、第二段加1，返回调用事件扣1至0，再战奖励5；另覆盖部分 KO 不发奖、零经验、无战后脚本、DWORD 回绕、owner 隔离、旧未绑定调用、Lost、预检与回执拒绝。没有直接调用私有结算或替代战斗引擎。

重建责任仍在外层宿主：从已批准的独立上下文或未来恢复记录显式构造现金 owner 一次，再绑定 trigger（及未来菜单）。不从展示累计/原奖励回执重建余额，不在最终 ACK 重发已入账奖励；活跃战斗/脚本的持久化策略不在这里冻结。正常 Main、Dialogue 的显式绑定与 preview 隔离、原生初始现金声明/准备/活跃资源迁移和正常 Main/SceneRunner 证据仍是 #131 未闭合项。

## 首战纯 Core 静态闭包的显式现金能力（#195）

`FirstMandatoryBattleClosure.Verify` 在既有签名尾部接受 `CashLiveState? cashLiveState = null`，复用 `TriggerScriptSession.ValidateEntry` 的能力门，并对中文、英文两次检查传入同一个显式 owner。这里只读取**有无 owner**：不执行现金命令、不按当前余额或金额符号剪枝、不创建战斗、不调用随机数，也不将钱包保存到闭包回执。后续运行会话仍需宿主显式绑定自己的 owner，不能把 receipt 当作运行时钱包或默认0的许可。

空 owner 对包含现金命令的所选闭包仍明确拒绝；不含现金命令的 Core 调用无需伪造钱包。这是纯 Core 的能力边界，不是保留另一套旧 native loader。现有 native 调用仍未传现金；本票没有改变原生格式、Prepare、Main、旧资源接受策略或冻结 author/envelope/source 身份，也没有选择初始声明的编码/位置。追加可选参数保留已知源码调用形式，但已编译外部调用方需要重编译，不承诺二进制 ABI。

`FirstMandatoryBattleClosureTests.Explicit_cash_capability_preflights_both_languages_and_all_branches_without_execution` 经公开 Verify 验证完整人工闭包：显式余额 `0`、`10`、`2147483648`、`4294967295` 连续预检不变；缺 owner、第二语言缺消息11及正增量也不会实际走到的 raw shortage 分支 `@0A33` 仍拒绝。完整 Core Unit 新增1项，1349项通过；这不是原生保存重载、Main/SceneRunner、宿主重建或故事验收。

#129/#147、#165 原 cold 失败和 #168 fatal/abort 均不由此解锁或改判。Core Unit 证据不是完整 M6、共享门、GPU、人工或远端 CI 证据。
