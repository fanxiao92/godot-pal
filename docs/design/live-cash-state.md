# 宿主持有的现金状态（#177、#178 / #131 前置）

`CashLiveState` 是纯 .NET 的显式现金所有者，沿用 `RoleLiveState` 的宿主持有方式，但不合并成通用 GameState。构造时必须给出 `uint initialCash`，只读 `Cash` 暴露完整 DWORD 域 `0..4294967295`。同一宿主的后续消费者应共享同一个实例，而不是复制余额或每次调用重建；另一个宿主应拥有独立实例。

## 已实现的参考规则

| 公开操作 | 行为 | 固定仓库参考 |
| --- | --- | --- |
| `TryChange(int amount)` | 仅接受 signed WORD `-32768..32767`。负增量不足时返回 false 且不写余额；成功返回 true。零增量成功。正增量按 uint32 回绕，不夹值。非法参数抛 `ArgumentOutOfRangeException`，余额不变。 | `script.c:952–968`，0x001E 的 SHORT/WORD affordability 与 DWORD `+=` |
| `Halve()` | unsigned 整数除以二，奇数向下取整。 | `script.c:2600–2605`，0x008F |
| `Credit(uint amount)` | 已获调用方授权的非负 DWORD 增量，按 uint32 回绕；不设现代钱包上限。 | `battle.c:1054`，PAL_BattleWon 的 `dwCash += iCashGained` |

类型依据为 `global.h:544` 的 `dwCash`、`common.h:145` 的 unsigned DWORD。负增量的绝对值在先验证 signed WORD 后计算，因此 `-32768` 的不足比较是32768，不发生 SHORT 取负溢出。以上是经典/Windows 参考的现金规则，不承诺新战斗能力、并发修改安全或跨存档随机一致性。参数域拒绝属于托管 API 边界，不是原版新增玩法分支。

`Credit` **只是算术，不是结算入口**：它不验证所属 lifecycle、结果类型或完成回执，不去重、不自动发奖。未来正式宿主必须先通过原有会话/请求所有权和一次性完成门，且仅在应发奖的分支调用。不得因为本类已实现，就把重复回执、非胜利不发奖或 Main 接线算作已验收。

## 测试边界与未交付链路

`CashLiveStateTests.ExplicitOwnerPreservesDwordCashAndReferenceArithmetic` 只使用公开构造、操作和余额，独立字面值覆盖 signed WORD 两端、不足/恰够/零、uint32 回绕、大于 int.MaxValue 的余额、奇偶减半、非法参数无变更及不同 owner 隔离。它不执行 trigger、Godot、Main 或真实资源。

#177 仅关闭 Core 状态/算术前置；#178 增加现有 `TriggerScriptSession` 的显式现金执行能力。**#131 保持未完成**，正常 Main 尚未接线，`Main.StoryCashGranted` 仍只是既有奖励展示累计。后续 #131 必须补齐：
- 合法当前战斗完成的唯一提交点、过期/foreign/replay 拒绝、非胜利不发奖、后续事件消费同一余额；
- 独立上下文显式初始现金与旧未声明上下文的兼容策略、必要的原生准备和正常 Main/SceneRunner 实测；
- 未来重建责任：由宿主从已批准内容或恢复记录显式构造一次并重新绑定。这里不定义存档 schema、槽位、磁盘读写或自然剧情 save。

## trigger 现金执行契约（#178）

现有 session 构造、`ForSharedRoles` 和静态 `ValidateEntry` 尾部可显式传入可空的 `cashLiveState`；只读 `CashLiveState` 返回同一实例，不复制余额、不重置、不默认构造。旧调用不传入时仍拒绝所选路径上的现金命令，保留原 content-only/context 诊断并指出缺失 owner。独立 owner 互不影响；后续宿主重建仍须显式构造并重新绑定。本票没有增加 profile/原生声明，也没有让 Main/Dialogue 自动取得现金状态。

解析器沿用已冻结具名字段校验，保留源 WORD 只作诊断，执行消费现代语义字段：

- `changeCash.amount` 是 signed WORD；`to` 是必须存在的已解析引用，支持跨文件和指令零。成功增减后走 next；不足时不扣钱、跳 to。两种分支都不改 `ScriptSuccess`，只有原来的 `markFailed` 等指令可改变它。to=0 结束当前执行上下文，不执行归档的指令零（参考 `wScriptEntry = operand1 - 1` 与函数尾 return+1）。
- `halveCash` 对同一 owner 调用 unsigned 整除，然后走 next。
- 预检在 `Start` 写 call/caller 状态前检查完整 next/to 两分支，连正金额当前不会走的 shortage 分支也检查；不按当前余额剪枝，不改变现金或消费随机数。静态及当前 requirements 使用相同能力门。没有新增 auto、call/goto、脚本上下文或通用退款。

`TriggerScriptCashTests.Explicit_shared_cash_commands_preserve_reference_flow_and_preflight_before_mutation` 从公开 Core 构造、requirements、Start、显示回执及余额观察这些行为，独立字面值来自固定 `script.c:952–968,2600–2605,3085`。它也验证过早/重复/过期显示回执不会重放现金变更、无 owner 的旧调用拒绝、两个可能分支中的不支持命令均在前缀写现金之前拒绝。它不是正常 Main/SceneRunner、战斗奖励所有权或原生准备证据。

#129/#147、#165 原 cold 失败和 #168 fatal/abort 均不由此解锁或改判。Core Unit 证据不是完整 M6、共享门、GPU、人工或远端 CI 证据。
