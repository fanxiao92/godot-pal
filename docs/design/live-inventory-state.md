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

## 装备效果寄存器清零（#183）

`BattleEquipmentState.ClearEffectRegister(int roleId, int registerIndex)` 是 `PAL_RemoveEquipmentEffect`（`global.c:1395–1400`）**首个寄存器清零循环**的有界原语，不是整个函数。它在同一实例内清零角色0..5、寄存器0..6的全部已建模效果字：attack、defense、dexterity、battleSprite、magicPower、fleeRate、poisonResistance，以及 AttackAll / CooperativeMagic。后两者清为显式0而非null，忠实表达零写入，不开放这些战斗能力。索引6是 extra 寄存器，不是第七个装备槽；参考 `battle.c:1829` 也调用效果清理处理 extra。非法索引在任何写入前抛 `ArgumentOutOfRangeException`；重复调用幂等。

清理只影响指定角色/寄存器，不重新 Initialize、不重放装备脚本、不扫描剩余装备重算状态。数值面板和精灵查询即时消费清理后的寄存器；最后非零精灵寄存器被清后按原顺序回落。装备ID、库存、RoleLiveState、持久状态 overlay、其余角色/寄存器及冻结来源均不改。未声明装备的旧零实例仍保持 `Declared=false`；对其中未写过的 nullable 字段清零也会留下显式0。已有视图仍为之前的不可变快照，而非可写入口。

公开 Core Fact `Clearing_one_effect_register_preserves_gear_statuses_and_other_registers_without_reinitializing` 以独立字面效果哨兵覆盖全部六角色/七寄存器、九类已建模效果字、精灵回落、WORD面板回绕、nullable未写与显式0、重复清理/非法拒绝/旧视图、跨实例和author隔离、库存及装备计数不变、旧未声明状态兼容。人工夹具不是自然存档或真实装备来源证据。

这是供未来完整移除消费者组合的操作，不是 test-only 替代宿主，也不定义新的装备/毒真状态。未来消费者必须在同一活状态上完成 hand 的实际 dualAttack 状态归零、wear 的当前队员毒槽整理（保留level<99及脚本入口、清空尾槽）、装备ID移除和角色记录同步；**不能把本方法或全量重新初始化当成完整效果清理**。现有 `BattleRoleSnapshot` 尚无毒脚本/等级状态，本票不伪造其所有者或存档字段。清理中的持久状态故意原样保留，不能据此启用 `removeItem`。没有新增 trigger/Main 调用，现有未绑定行为和预先拒绝不变；也不授权修改已开始战斗内部投影副本。Core寄存器操作的提交点是本方法，完整玩法提交/重建责任仍待后续宿主接线冻结。

## 当前队伍毒槽与 wear 效果整理（#184）

`PartyPoisonState` 补足完整装备移除所缺的 `POISONSTATUS` 表达：`PoisonSlot(PoisonId, ScriptEntry)` 同时保留毒对象 ID 和当前脚本入口 WORD。现有 `BattleRoleSnapshot` 没有毒字段，`ClassicBattlePlayer.Poisons` 只是静态内容的 ID 列表且非零毒仍被所选战斗拒绝；它们都不是可复用的活毒槽 owner。因此新增最小纯 Core 所有者，不扩展冻结 author 或为测试另建宿主。本前置**尚未接入 trigger、战斗或 Main，不代表 #132 或完整 PAL_RemoveEquipmentEffect 已交付**。

构造显式传入有序 `partyRoleIds`、与其逐行对应的 `initialPoisons`、静态 `poisonLevels`；全部复制，禁止隐式空 seed、每次调用重新初始化或借用可写数据。角色为不同的 RoleId0..5，最多5名（探索容量而非五人战斗支持），每行必须恰好16个 ID/脚本 WORD 对，行数与队伍一致。`PartyRoleIds` 是只读固定成员顺序，`Slots(roleId)` 是该角色16槽的只读实时视图；旧的不可变 `PoisonSlot` 值不随变更改写。有效但非队员角色查询抛 `ArgumentException`，非法角色抛 `ArgumentOutOfRangeException`。空队伍 `[]` 是安全的现代 API 上下文，不冒称参考存在自然空队伍。

静态等级表的键是非零 WORD 毒对象 ID，值是 WORD 等级；所有非零槽 ID（包括首个空槽后的陈旧记录）都必须在构造时解析到等级。重复毒 ID、空洞、ID0而脚本非0的槽原样保留，不在 seed 时排序、去重或归一化。非法形状/域/缺失引用抛 `InvalidDataException`，顶层 null 参数抛 `ArgumentNullException`。完整预校验避免整理到一半才发现等级缺失；这不是任意原版存档加载器。

`RemoveEquipmentPoisons(roleId)` 严格对应 `global.c:1413–1455` 的 wear 分支（结构见 `global.h:210–216,495–499,528,547`，容量见 `palcommon.h`）：先按角色身份定位**当前队伍列**，非队员有效角色无操作；只扫描首个 PoisonId0 之前的前缀，将等级**小于99**的 ID/脚本对按原顺序前移，其余全部尾槽的两个字都写0。等级99、100、65535均移除，不是仅等于99；首个0后的非零 ID 不参与保留。满16槽也扫描最后一项，低等级脚本65535或0原样保留。重复整理幂等，不排序、不执行或重置保留项脚本，不影响其它角色/owner/静态声明。提交点就在本方法，不依赖帧或外部时钟。

公开 Core Fact `Wear_cleanup_preserves_low_level_poison_script_pairs_in_current_party_order_and_clears_the_tail` 以独立字面夹具覆盖阈值、非连续身份、顺序/重复、ID及脚本WORD边界、首槽空和隐藏尾项、全16槽、非队员/非法拒绝、显式空/五人、只读实时视图/旧值、seed及等级防御复制和实例隔离。人工夹具不是自然存档或真实资源；Core 反馈不证明正常 Game 或毒执行能力。

未来宿主必须从已批准上下文显式重建并绑定这一 owner，不得从旧角色快照缺字段推断“无毒”，也不得同时维护另一份活毒 ID/脚本真状态。当前成员固定；队伍变化时毒状态迁移、毒加入/排序/抗性/脚本推进以及战斗投影需后续按真实消费点接通，不能靠新建空状态丢弃毒。完整装备移除仍需将此 wear 操作与同一装备寄存器清零、hand 实际 dualAttack 清零、装备 ID/角色记录同步组合；本方法不清装备/状态、不开放 `removeItem`，也不授权修改活动战斗副本。不定义存档 schema、自然 save 或自动恢复。

## 同步的单槽装备移除（#185）

`BattleEquipmentState.RemoveEquippedItem(roleId, slotIndex, roleLiveState, partyPoisons)` 将 #183/#184 的原语组合为一个完整的单槽操作，对应 `global.c:1372–1455` 的效果清理及 `script.c:1006–1007` 的随后装备 ID 清零。复用现有装备实例、`RoleLiveState` 和 `PartyPoisonState`，不新增总状态、另一份装备 owner 或隐式 seed。尚无 trigger/Main 生产接线，**不是 #132 或完整装备菜单的交付**。

- roleId 为0..5，slotIndex 为实际装备槽0..5；extra6只有效果寄存器，不是可移除装备槽。null owner 抛 `ArgumentNullException`，非法索引抛 `ArgumentOutOfRangeException`。任何写入前，要求选中 live role 存在、其六个装备 WORD 与当前装备实例完全一致、九个状态 WORD 合法；缺失/错配/非法形状抛 `InvalidDataException`。只需要选中角色记录，不强求六角色或当前参战成员。
- 清指定寄存器的全部已建模效果字，指定装备 ID 写0并同步冻结的 live role 记录。hand3 **无条件**将实际 `Statuses[8]` 清0（临时或持久 dualAttack 均如此），同时清该实例的初始化 overlay[8]，避免 `ApplyStatuses` 再将它恢复。其它槽即使也曾授予 dualAttack，也不重扫/重放；其它持久状态不被泛化清除。
- wear5 使用显式 `partyPoisons.PartyRoleIds` 的当前成员关系，调用既有稳定毒整理；非队员仍清装备/寄存器，但不改任何队员毒。其它部位不清毒，hand以外部位不清 dualAttack。空装备槽也执行清理；重复调用幂等，不向库存返还物品、不减库存、不重建装备效果。
- 保留角色 HP/MP、永久面板、等级/经验、法术与其它状态、其它装备/寄存器/角色，以及库存和冻结声明。原公开记录和装备/status快照不可变；`RoleLiveState.Roles` 的既有只读字典则能观察新记录。先准备并经既有 `MergeSettledRoles` 冻结记录，再完成同步无回调的状态写入，方法返回为此操作提交边界；不是通用脚本回滚或退款。

`EquipmentRemovalTests.Single_slot_removal_synchronizes_live_gear_status_and_wear_poisons_without_reseeding` 经公开真 owner/API 验证独立字面预期：六角色/六槽、hand临时/持久/空槽、其它装备仍授予双击但不重算、wear按角色清毒对和隐藏尾槽、非队员、数值与nullable WORD/精灵、重复清理、未声明旧零装备、旧记录与只读视图、owner隔离、库存不变和非法/错配输入无部分写入。它不调用私有接口或另建玩法宿主，人工数据不是自然存档或真实资源证据。

**所有权与接线责任**：现有网格/寄存器是装备的可变真值，live role 的装备数组是同步记录，不允许作为另一可独立编辑的网格；实际角色状态以 `RoleLiveState` 为准，overlay 只保留装备初始化贡献并同步 hand 清除。未来宿主必须在生命周期内绑定同一组三个实例，不能每次从 author 初始化或用空毒 owner 绕过能力检查。此方法本身不绑定宿主、不修改已开始战斗的投影副本；调用方须在合法玩法边界操作，并让下次正式战斗消费当前装备/角色值，避免旧 author 闭包重新覆盖。未来从合法内容/恢复记录重建一次并重绑，不定义存档 schema 或自然 save。

#132 仍须补齐：
- 生产 0x0020 消费下述完整 Core 移除操作，接通现代语义字段、shortageTarget 分支及 ScriptSuccess 保留；
- 在 trigger/正式战斗中绑定唯一活库存/装备/角色/毒状态，不能每次从 author 重建；Core 有序回退已具备，但尚未形成生产宿主接线，不得用库存返回0/负数直接代表脚本失败或退款；
- 宿主显式初始库存、同一 owner 的 trigger/战斗连续性、重复/过期请求保护，原生内容兼容与正常 Main/SceneRunner 证据。

## 计数预检与库存优先的有序装备回退（#186）

`BattleEquipmentState.TryRemoveItem(itemId, amount, allowShortage, inventory, roleLiveState, partyPoisons)` 组合上述现有操作，交付 `script.c:977–1025` 的完整 Core 移除规则。没有新状态 owner，也没有另建脚本执行器。前面各节描述的是各自交付版本；本节增加组合能力，**trigger 的 removeItem 仍拒绝，Main/native/活动战斗未接线，#132 尚未交付**。

- itemId、amount 均为 WORD0..65535，amount0 表示1，不截成 SHORT。`partyPoisons.PartyRoleIds` 是计数和移除共用的唯一当前队伍顺序，按角色身份读取网格，不扫描队外六角色记录。
- `allowShortage=false` 且包含当前装备的 CountItem 不足时返回 false，所有状态不变；未来 trigger 必须据此跳到现代 shortageTarget。其余情况返回 true，表示**移除分支已经执行，不保证足量**。`allowShortage=true` 对应参考 shortageTarget0：允许耗尽后仍不足，不退款、不回滚。布尔返回不是库存原语的1/0/负数，也不是 ScriptSuccess；参考0020不写 ScriptSuccess，生产调用方以后仍须保留该标志。
- 先扣库存：原语结果正数表示已足量，不碰装备；负数转成实际缺少量；0保留整个请求数量。之后按当前队伍角色顺序、每个角色槽0..5的顺序，逐个调用完整 RemoveEquippedItem，数量满足立即停，否则扫完即止。保留耗尽槽、AmountInUse、未选装备/队外角色/extra寄存器和旧不可变记录；hand/wear 真实状态及效果清理沿用单槽语义，不重算剩余装备、不初始化或复制另一份真状态。
- **item0 的参考边界不可简化**：CountItem0仍是0，有短缺目标必定拒绝；没有目标时库存返回0，参考回退比较装备ID0，因此会清理匹配的空槽效果，包括实际走到的空 hand 的双击和空 wear 的装备毒。只读计数不计空槽，但执行回退不能擅自跳过 item0。本行为来自固定源码，不宣称真实游戏必然使用此边界。
- null owner 抛 ArgumentNullException，非法 item/count 抛 ArgumentOutOfRangeException。移除分支写入前预先校验所有**实际将被回退选中**角色的六个一致装备WORD和九个合法状态WORD；缺角色/错配/非法维度或域抛 InvalidDataException，连库存前缀也不先扣。复用单槽校验，不要求制造六角色，库存足量或计数拒绝时也不强求无关 live gear 一致。这是现代绑定完整性拒绝，不是给有效脚本增添失败退款。同步 owner 无回调，方法返回为本操作的提交边界。

公开 Core Fact `InventoryEquipmentRemovalTests.Count_gated_removal_consumes_stock_then_current_party_gear_in_reference_order_without_refund` 通过真 owner/API 验证独立字面数量及角色/槽结果：库存2移除5只退角色5首三个匹配槽，保留wear和角色3；下次调用从当前状态继续；拒绝与允许不足、库存精确/部分/不存在/耗尽、65535无SHORT截断、满256槽末项、空及五人队伍/倒序、item0空hand/wear、效果/实际角色/毒对同步、旧视图与实例隔离、后续角色错配写前拒绝、非法参数及未触及角色无需全量seed。人工数据不是自然存档或真实资源证据，普通 Core Unit 不证明 trigger/Main/战斗消费和请求幂等。

本操作只是一项可复用的真实领域规则，不负责判断活动战斗安全边界、推进脚本地址或验证显示/战斗回执；重复显式调用就是另一项移除，不自动去重。后续生产接线必须将同一组三个既有 owner 和库存绑定在宿主整个生命周期内；#187 已提供下述显式 Core 战斗消费者，Main/native 仍须接入，避免下一场战斗从旧 author 重新覆盖装备。现阶段不开放活动战斗副本修改、毒执行或成员迁移，不新增菜单、预留或保存字段。

未来持久化重建责任在宿主：从已批准的独立上下文或恢复记录构造一次，保留有意义的槽顺序/耗尽记录/in-use 字段，再重新绑定消费者。活动预留的保留/清除策略尚未由本票冻结；不定义槽位、schema、磁盘读写或自然剧情 save。#131 未完成的 Main/native 现金接线及 #129/#147 阻塞、#165/#168 旧失败不由本票解决；无故事或完整 M6 验收增加。

## Core trigger 战斗消费当前装备（#187）

`TriggerScriptSession.ForSharedRoles` 末尾追加可选 `BattleEquipmentState? equipmentState = null`；只读 `EquipmentState` 暴露实际绑定身份。只有这个要求显式 `RoleLiveState` 的既有 factory 能绑定装备，world 存在时仍必须共享同一角色 owner；没有新状态框架、隐式 seed、运行中 rebind 或二进制 ABI 承诺。其它构造、未传装备的 factory 和静态 `ValidateEntry` 继续按原 author 检查/构造，既有源码调用不必迁移。

对显式装备绑定，`GetCurrentEntryRequirements`、`Start` 的保守闭包和实际 `startBattle` 都读取同一当前装备实例及当前参与角色记录。`SettledPlayers` 只在这条路径把玩家装备改为当前 live role 的装备，现有 declared-panel/legacy-base-panel、HP/MP 和角色身份规则不变。预检不复制另一份真状态、不 Initialize(author)、不改库存/装备/角色/毒、CallId 或随机数；参与者装备必须与当前网格一致，缺角色/错配、非法 live 状态或当前不支持的 field4/65/精灵覆盖仍由既有校验在副作用前拒绝。已合法移除的旧 author 装备不复活，未参与角色也不要求制造新记录。

沿用现有 lifecycle/combat 构造中的有效面板及状态投影，不新增公开投影 API。玩家内容的 `Statuses` 保留原先零状态能力门；live role 的持久状态由既有 lifecycle 按**当前**装备 overlay 校验并投影，不能把非零持久状态数组直接塞进另一个零状态专用入口，也不放宽未支持的临时状态或毒能力。hand 移除已经清掉的 actual/overlay dualAttack 不会在入战重现；其它效果/状态保留既有单槽语义，不重扫装备重算。

装备网格/寄存器仍是宿主当前真值，角色装备是同步记录；每场战斗只在构造时读取并形成既有战斗局部数值/状态/boost 数组。**战斗活动期间不得外部修改这些共享 owner**；须真实完成并 ACK 后才执行下一项装备操作，再入战重新读当前值。此票不支持活动战斗投影修改或装备/毒脚本；结果仍仅凭所属 completion 合并角色一次，不回写/重建装备寄存器。新 session 若显式绑定同一 owners，也消费当前装备，而不是旧 author。

`TriggerScriptBattleBindingTests.Explicit_current_equipment_flows_into_next_trigger_battles_without_author_reseeding` 经公开真 owner、完整移除原语、requirements/Start/真实 lifecycle 攻击和 completion 验证独立字面预期：库存2+hand1移除后，head 贡献 attack10/defense4/dexterity20，declared base40 得50而 legacy base70得80；完成/ACK后移除head，第二场得40/70，不重置或叠加。覆盖 world/无world、角色与装备 owner 隔离/历史记录、队外角色、持久hand清除与未移除overlay、错/旧/重复回执、缺失/错配拒绝、已移除的 field4/65/精灵不再阻塞以及未声明零装备兼容。测试使用人工数据，不冒充自然存档、真实资产或正常 Main 证据。

这是 #132 的必要 Core 消费链前置，不是整票交付。#187 的交付版本仍拒绝 `removeItem`；下节 #188 增加显式完整绑定下的 Event 消费者。唯一库存/角色/装备/毒宿主生命周期接线、Main/native 显式 seed/preview 隔离/重复过期请求与正常 Main/SceneRunner 连续性仍开放。Core 反馈不签 Game、Prepare、私有/GPU/桌面/CI、故事或 fullM6；未来重建仍由宿主从合法上下文一次构造再绑定，不新增保存字段。

## Event 的现代 removeItem 消费者（#188）

既有 `TriggerScriptSession.ForSharedRoles` 在 `equipmentState` 后追加可选 `PartyPoisonState? partyPoisons = null`；只读 `PartyPoisons` 暴露所绑定的同一实例。不增加其它构造、隐式 seed、复制 owner 或运行中 rebind；未传新参数的旧调用和静态 `ValidateEntry` 保持 `removeItem` 能力拒绝。只在显式共享的库存、当前装备、角色与毒四个 owner 齐备时支持 Event 上下文，复用 #186 的 `TryRemoveItem`，不另写移除算法或脚本执行器。

解析和执行消费现代 `itemId`/unsigned WORD `count`/`shortageTarget`；归档 source operands 只作诊断。`shortageTarget=null` 对应参考0，允许耗尽仍不足；非null须为非零同文件或跨文件地址。引用地址0必须写null，不允许以指向归档0位置的字符串代替。`count=0` 由现有完整原语转换成1，65535不截成SHORT；false只选择短缺目标，true继续后继，均不写 `ScriptSuccess`，不把允许的部分消耗退款。`addItem` 负数仍只减库存，`itemShortageJump` 仍比较库存和 signed count，不被0020的含装备计数替代。

requirements/Start 的保守闭包检查后继和每个非null目标，不因数量、当前库存或必然足量而剪枝。预检不读写库存、模拟装备移除、重播装备初始化、消耗随机数或修改 CallId。被实际选中回退角色的装备/状态一致性仍由 #186 在命令执行的写前校验负责；这不是对整个脚本做预测或事务回滚。库存/装备变更立即提交，显示完成回执只推进后继；过早、外来、重复、旧显示/战斗回执不重放命令。新的显式 Start 则是新操作，不新增请求自动去重承诺。

**固定成员与活动战斗边界**：计数/回退共用 `PartyPoisons.PartyRoleIds` 的固定当前顺序；绑定 world 时，选中含0020闭包要求其与 `world.PartyRoles` 当前顺序完全相同。任何同时可达 `setParty` 的0020闭包（包括成员不变的写入）均在副作用前拒绝，不能在未迁移毒槽时静默使用旧队伍。没有0020的既有 `setParty` 闭包不受此限制；若另一调用已改队伍，下一次0020重新检查并拒绝错配。`RoleHook` 与 `BattleEnd` 中的0020即使四个 owner 齐备也预先拒绝，以免活动战斗的局部投影与外部真值分离。未来宿主不得在活动 battle 或显示/事件暂停期间外部改写这些共享 owner；当前票不开放活动战斗装备/毒修改、成员迁移或这些上下文的完整物品能力，这些仍是 #132 及后续必需消费链的开放契约。

公开 Core `TriggerScriptRemovalTests.Modern_removal_branches_preserve_script_success_and_share_current_stock_gear_roles_and_poisons` 使用人工独立字面夹具和真实四 owner、trigger/display/lifecycle：库存2+hand1请求3，清手部实际/overlay双击，显示后正式战斗读取剩余wear贡献attack10（base40得到50）；完成并ACK后新调用移除wear并整理毒ID/脚本对，再按当前成员顺序消耗另一角色装备，队外角色及author不变。覆盖零/65535/item0、允许不足与目标拒绝、成功标志保留、signed0058、跨文件/非法目标、完整绑定拒绝、两分支闭包、world顺序/setParty和两个battle脚本上下文拒绝及旧回执。它不模拟自然save、真实资产或正常 Main。

本前置只闭合 Event 生产0020的现代字段/分支及公开 Core 消费链。#132 保持开放：Main/native 初始来源及唯一生命周期绑定、preview 隔离、宿主重复过期请求保护、正常 Main/SceneRunner 连续性与所需活动战斗/成员迁移消费者仍未交付；#131 现金 Main/native、#129/#147 与旧 #165/#168 停止失败不由本票解决。无菜单、预留、存档schema或故事/fullM6验收增加。

## setParty 的毒表清零边界（#189）

固定参考 `script.c:2164–2197` 的 `0x0075` 先替换队伍，再对**整张** `rgPoisonStatus` 执行 `memset(0)`，然后 `PAL_UpdateEquipments`（`global.c:1333–1368`）重跑装备脚本。因此上述各票的“成员迁移”是未闭合链路的历史简称，不能据此实现按保留角色搬运旧毒：同成员、换序或离队都会清旧毒，装备毒只能在后继真实装备脚本中重新加入。本票不跳过这个后继，也不把装备初始化数据拷贝冒充完整脚本重算。

`PartyPoisonState.ResetForPartyChange(IReadOnlyList<int> partyRoleIds)` 增加**同一 owner** 的显式清零操作。输入为已解析的有序唯一 RoleId0..5，最多5名，不是命令的 operand+1 WORD；`[]` 是安全的现代空上下文，未来 trigger 仍须自行按参考把全零操作数解析成 `[0]`。输入先复制且全部校验：null 抛 `ArgumentNullException`、非法角色抛 `ArgumentOutOfRangeException`、重复/超容量抛 `ArgumentException`，拒绝不改变任何成员或槽值。成功时清除全部旧行（含移出角色、首ID0的非零脚本及其后隐藏项）的16对ID/脚本字，新成员获得显式全零行；同成员写入、换序和重复调用也执行清零，而非按等级保留或迁移。无需制造新毒定义或重新 seed 所有者，已有静态等级/初始输入/其它实例不变。

`PartyRoleIds` 由固定快照变为同一只读**实时成员视图**；已捕获的视图也观察新顺序。仍在队中的 `Slots` 视图保持身份且观察清零，移出成员的旧视图被清零后脱离，不重绑其它角色，重新入队获得新零行/视图；旧不可变 `PoisonSlot` 值不改。有效非成员 `Slots` 查询继续拒绝，wear清理继续无操作。既有 #186 消费者直接读取当前成员顺序，公开测试以重排 `[3,5]` 验证下一次同物品回退先移除角色3而非旧队首5，并在切换 `[0]` 后不计/移除队外装备；这不是触发链已获准改变队伍的证据。

方法返回是**毒表清零**的提交边界，不负责 world、装备、角色/库存或资源 I/O；无回调、脚本、隐式恢复或另一个活毒真值。未来宿主须在合法边界协调 world 顺序、清毒与装备脚本重算，并从获准独立来源/恢复记录重建一次再绑定，不能从旧快照缺字段推断无毒，也不在活动事件/显示/战斗暂停期间外部调用本操作。#188 的 world/party一致性、含0020+setParty闭包及RoleHook/BattleEnd拒绝**全部保留**，没有开放完整生产setParty、装备毒重建、毒执行或活动战斗投影。本票是这些消费者的必要 Core 前置，不是 #132/Main/native 或故事验收，不定义存档schema或自然save。

`PartyPoisonStateTests.Party_reset_clears_all_poison_pairs_and_updates_the_same_owner_without_migrating_old_rows` 经公开真 owner/API 验证上述字面预期、全部六角色身份、空/五人、完整16槽与WORD边界、读视图/防御复制/实例隔离、非法输入写前拒绝，以及既有计数/有序回退消费者读取新顺序。人工夹具不是自然存档或真实资源；Core Unit 不替代正常 Main、Game/Prepare、GPU/人工或完整 M6。
