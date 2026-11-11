# 显式背包所有者（#180 / #132 前置）

`InventoryLiveState` 是纯 .NET 的宿主持有库存，沿用 `RoleLiveState` / `CashLiveState` 的显式所有权方式，不引入通用 GameState。构造必须传入按顺序排列的 `InventorySlot` 列表；空库存也显式传 `[]`。消费者共享同一实例，不能每次脚本调用重置或复制数量。#181 将显式 owner 接入 Core trigger 的 `addItem` / `itemShortageJump`；Main/native 尚未绑定，**这两张前置不是 #132 交付**。

## 来源与支持域

固定仓库参考只读：`palcommon.h:51` 的 `MAX_INVENTORY=256`，`global.h:245–250` 的 `INVENTORY`（WORD item、USHORT amount/in-use），`global.c:1022–1209` 的查找、`PAL_AddItemToInventory` 与 `PAL_GetItemAmount`；脚本实际操作数见 `script.c:970–1025,1859–1868`。这些库存函数不分经典/非经典战斗，项目目标仍是 `common.h` 的 PAL_CLASSIC / Windows 数据。

初始列表是 0–256 个**占用槽的连续前缀**，物品 ID 为唯一的非零 WORD，数量为 0–99，AmountInUse 为 0–65535。零数量但非零物品 ID 合法，仍占槽；列表后方补至 256 个全零槽。构造复制列表和不可变记录，拒绝 null、重复 ID、超长、非法域；这不是任意二进制/被修改存档的兼容加载器。不从资源内容或原始数据猜测玩家库存。

`Slots` 是固定 256 位置的只读实时视图，元素为不可变的 `InventorySlot(ItemId, Amount, AmountInUse)` 值；写操作替换值，已持有的旧值不被改写。`AmountInUse` 只作为保留 WORD 原样持有，本票不定义预留算法或要求其小于库存数量。

| 公开操作 | 语义 |
| --- | --- |
| `GetAmount(itemId)` | 只查库存数量，遇未使用 itemId=0 槽停止。不是库存减预留量，也不是含装备的 `PAL_CountItem`。itemId=0 返回0。 |
| `AddItem(itemId, amount)` | amount 是 0x001F 的 signed WORD，范围 -32768–32767；零转换为 +1。正数添加到已有槽或第一个未使用槽，数量最多99。负数走同一库存移除原语。 |
| `RemoveItem(itemId, amount)` | amount 是 0x0020 的 WORD，范围0–65535；零转换为移除1。保留65535的含义，不截成 SHORT。这里只做库存移除，**不**执行命令的装备计数预检、分支或装备回退。 |

两个变更操作均返回参考的整数结果，而非 bool：成功为1；itemId=0、移除未存在物品或添加新物品时槽满为0且不写；移除量大于已有库存时，将数量清零并返回**负的缺少量**，例如3移除5返回-2。已占用但数量0的槽移除1返回-1，不同于不存在物品的0。部分移除不是事务失败，不回滚已消耗的库存。

所有操作保留 AmountInUse；移除至0不清物品 ID、不移动槽位。即使256槽全部耗尽但 ID 非零，新物品仍不能添加；已有物品可原槽补货。参考的 `PAL_CompressInventory` 是另一个显式菜单操作（`itemmenu.c:340`），不能在库存变更时隐式调用。本票不加入菜单光标调整，也不实现压缩、使用/投掷、预留、商店或装备效果。

API itemId 必须是 WORD。非法参数抛 `ArgumentOutOfRangeException`，在任何写操作前拒绝；非法 seed 抛 `InvalidDataException`，null 列表抛 `ArgumentNullException`。AddItem 的支持域刻意限定为脚本 signed WORD，不承诺原 C INT 入口对任意大正数的 USHORT 中间回绕；单独的 RemoveItem 覆盖命令所需 unsigned WORD 移除。所有已支持数值的独立预期均来自上述固定参考。

## 验证与未闭合链路

`InventoryLiveStateTests.Explicit_inventory_preserves_reference_stock_slots_and_shortage_results` 只经公开 Core 构造、查询、变更及只读槽视图观察独立字面结果，覆盖容量、边界、部分移除、保留槽位、保留 in-use、参数拒绝与 owner 隔离。没有反射私有状态、运行参考或以实现输出生成预期。Core Unit 不证明 trigger 或正常 Main 接线。

## Core trigger 绑定（#181）

`TriggerScriptSession` 的各构造、`ForSharedRoles` 与静态 `ValidateEntry` 末尾追加可选 `InventoryLiveState? inventoryLiveState = null`，只读 `InventoryLiveState` 属性暴露实际绑定。既有源码调用不需迁移；没有隐式库存、重 seed 或 rebind，不承诺二进制 ABI。Main、PalTriggerDialogue 和 native/preview 的旧调用仍不传 owner，选中库存命令时明确拒绝，不能由未来 Game 宿主为绕过预检而创建空库存。

- `addItem`（0x001F）消费现代 `itemId` 与 signed `count`，调用同一 owner 的 `AddItem`；零计数加1、负计数只减库存。参考 `script.c:970–975` 忽略该调用的整数返回，因此满容量/不存在/部分不足不改 `ScriptSuccess`，也不回滚已耗库存或移除装备。
- `itemShortageJump`（0x0058）经既有具名字段校验消费 `itemId`、signed `count`、必需 `to`。按 `script.c:1859–1868` 比较 `GetAmount(itemId) < count`，不减 in-use、不计装备，不把零/负 count 替换成1。缺货与充足均不写 `ScriptSuccess`；只有实际到达的 `markFailed` 清它。`to` 支持跨文件与地址0；0终止而不执行归档位置0的命令。
- current/static requirements 与 Start 使用同一显式 owner 能力判断；保守闭包检查两条分支及 add 的后继，不因当前库存或 count 符号剪枝，不读写库存、消耗随机数或改变调用身份。现代语义字段执行，保留的 source WORDs 只作诊断。`removeItem` 仍是 content-only，即使已绑定库存也拒绝整个选中闭包，避免漏做装备回退。
- 变更在命令执行处立即提交，显示等待保留同一 owner；过早、外来、重复或旧 display ACK 不会重新执行此前的增减。新的显式 Start 是新的脚本调用，不是自动幂等化的请求重放策略。现有 event/role-hook/battle-end 共享解释路径可消费绑定，但不增加 call/goto/auto 或新上下文。

`TriggerScriptInventoryTests.Explicit_inventory_commands_preserve_reference_stock_flow_and_pure_preflight` 经公开 Core 构造/Start/requirements/显示回执验证独立字面数量、符号边界、容量与耗尽、共享/隔离、库存而非装备计数、ScriptSuccess 保留、跨文件/0目标、无 owner/错误内容/任一不支持分支预先拒绝。此证据不证明正常 Main、真实战斗库存消费或 Game 运行。

## 当前队伍的库存加装备查询（#182）

`BattleEquipmentState.CountItem(int itemId, InventoryLiveState inventory, IReadOnlyList<int> partyRoleIds)` 对应 `global.c:957–1019` 的 `PAL_CountItem`。它读取**当前实例**的既有六角色装备网格，叠加显式传入库存 owner 的当前数量，只计 `partyRoleIds` 选中的角色和各自六槽；不复制、缓存或重新初始化状态。角色身份不是列表位置，队外角色不计，装备槽中间的0不终止搜索，同类装备在多个槽中逐个计数。item0 返回0，不把空装备槽算作物品。

这不是 `GetAmount`：不减 AmountInUse，装备可令总数超过99；例如库存99、五名队员每人六槽同物品，结果129，而不是99、105或135。itemId 的 API 域为 WORD；当前队伍为显式有序、唯一的 RoleId0..5，最多5名（参考 `MAX_PLAYABLE_PLAYER_ROLES` 的探索容量，**不是五人战斗支持**）。空列表是现代 API 的安全 inventory-only 上下文，不冒称参考存在自然空队伍。所有参数先验证，即使 item0 也不绕过检查：null owner/列表抛 `ArgumentNullException`，非法 item/role 域抛 `ArgumentOutOfRangeException`，重复或超长队伍抛 `ArgumentException`。未声明装备的旧零状态继续只计库存。

`BattleEquipmentStateTests.Item_count_includes_only_current_party_equipment_without_consuming_or_caching_stock` 用公开 Initialize、库存变更与查询覆盖上述规则、队外/非连续身份、槽空洞、item65535、耗尽和满256槽末项、不同 owner/装备实例隔离、查询/拒绝无副作用及旧内容兼容。独立字面预期来自固定参考，人工装备和效果仅作测试哨兵，不是自然存档或真实资源。只读查询不改变装备、效果寄存器、持久状态或库存。

本前置不把 per-instance 装备网格升级为宿主共享活装备所有者。未来消费者必须绑定同一当前装备与库存状态，不能每次从冻结 author 重建；与 `RoleLiveState` 的装备记录和战斗结果同步、装备移除的提交点及重建责任仍须后续明确接通。本查询自身无提交点、不新增持久化字段。尚无 trigger/Main 调用本 API，`removeItem` 仍保持预先拒绝。

#132 仍须补齐：
- 0x0020 消费此查询的装备计数预检、分支和 ScriptSuccess 的真实参考行为；
- 按当前队伍角色/槽顺序回退移除装备、同步装备效果（包括 hand 双击状态、wear 毒清理）和唯一活状态；不得用库存返回0/负数直接代表脚本失败或退款；
- 宿主显式初始库存、同一 owner 的 trigger/战斗连续性、重复/过期请求保护，原生内容兼容与正常 Main/SceneRunner 证据。

未来持久化重建责任在宿主：从已批准的独立上下文或恢复记录构造一次，保留有意义的槽顺序/耗尽记录/in-use 字段，再重新绑定消费者。活动预留的保留/清除策略尚未由本票冻结；不定义槽位、schema、磁盘读写或自然剧情 save。#131 未完成的 Main/native 现金接线及 #129/#147 阻塞、#165/#168 旧失败不由本票解决；无故事或完整 M6 验收增加。
