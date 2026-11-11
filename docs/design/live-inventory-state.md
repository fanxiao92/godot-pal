# 显式背包所有者（#180 / #132 前置）

`InventoryLiveState` 是纯 .NET 的宿主持有库存，沿用 `RoleLiveState` / `CashLiveState` 的显式所有权方式，不引入通用 GameState。构造必须传入按顺序排列的 `InventorySlot` 列表；空库存也显式传 `[]`。后续消费者共享同一实例，不能每次脚本调用重置或复制数量。当前 Main/trigger 尚未绑定此 owner，**本前置不是 #132 交付**。

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

#132 仍须补齐：
- 具名命令解析、保守两分支闭包、0x0058 的 signed count / inventory-only 比较、0x0020 分支和 ScriptSuccess 的真实参考行为；
- `PAL_CountItem` 的当前队伍装备计数，以及按角色/槽顺序回退移除装备、同步装备效果；不得用库存返回0/负数直接代表脚本失败或退款；
- 宿主显式初始库存、同一 owner 的 trigger/战斗连续性、重复/过期请求保护，原生内容兼容与正常 Main/SceneRunner 证据。

未来持久化重建责任在宿主：从已批准的独立上下文或恢复记录构造一次，保留有意义的槽顺序/耗尽记录/in-use 字段，再重新绑定消费者。活动预留的保留/清除策略尚未由本票冻结；不定义槽位、schema、磁盘读写或自然剧情 save。#131 未完成的 Main/native 现金接线及 #129/#147 阻塞、#165/#168 旧失败不由本票解决；无故事或完整 M6 验收增加。
