# M6-C 逐字段保留/清除/重建矩阵与 v1 存档契约（#221）

- 父规格：#148（M6-C 正式父规格，Decision 10–17）；跟踪入口 #124；A/B 父规格 #126（已闭环为 C 的前置）。
- 本文件冻结 C 阶段的**逐字段保留/清除/重算矩阵**与 **v1 存档契约**（必填/可选字段、数值/容量/大小上限、兼容规则）。实现落点：`src/Pal.Core/PlayerSaveContract.cs`（公共不可变快照类型 + 构造期 fail-closed 校验），字面夹具：`tests/Pal.Core.UnitTests/PlayerSaveContractTests.cs`。
- 固定参考（只读）：SDLPal `global.c` `PAL_SaveGame_Common`（global.c:735，保存域）、`PAL_LoadGame_Common`（载入域；global.c:630 `memset(rgPoisonStatus)` 清毒）、`PAL_InitGameData`（global.c:915；`iCurInvMenuItem = 0`、`memset(rgPlayerStatus)`、global.c:953 `PAL_UpdateEquipments()` 装备重算）、`PAL_UpdateEquipments`（global.c:1333）、`PAL_SetPlayerStatus` 好状态取较长值（global.c:2255）、`PAL_RemovePlayerStatus` 保留 >999（global.c:2297）、`script.c` 0x0075 setParty 清毒（script.c:2195）、`palcommon.h` 各容量、`global.h` `SAVEDGAME_COMMON`/`EVENTOBJECT`/`SCENE`/`ALLEXPERIENCE`。
- 本票只冻结契约与快照数据类型；**不做** FileAccess、菜单、稳定资格判定、捕获、恢复或接线（后续 C 叶）。矩阵描述的是快照视图契约，不新建第二份活状态引擎，也不改动任何 A/B 状态所有者的现有行为。

## 九要素定义

每域逐项给出：**所有者**（唯一活状态类型）→ **引用身份**（该域以什么身份被引用/恢复）→ **存储域**（v1 快照字段）→ **捕获时点**（何时读出）→ **保存**（存什么、什么形态）→ **清除**（载入时清什么）→ **重算**（载入时按规则重 derive 什么）→ **恢复顺序**（相对其他域的次序）→ **独立验收**（在哪一层、以什么独立预期核对）。

捕获时点总则：普通保存仅在稳定探索边界捕获（无活跃战斗、trigger、对话、移动等待、转场、未完成提交/回执；当前生产更新已完成）。捕获/校验不推进脚本、不补世界 tick、不抽玩法随机（#148 Decision 7）。

## 逐域矩阵

### D0 格式头

- 所有者：宿主会话控制状态（非 Core 活状态）。
- 引用身份：`PlayerSaveV1.FormatVersion` / `SaveSlot` / `SavedTimes`。
- 存储域：`$.formatVersion`（必填，恒为 `PlayerSaveContract.CurrentVersion = 1`）、`$.saveSlot`（1..5）、`$.savedTimes`（WORD 0..65535）。
- 捕获时点：宿主确认槽位后写入头字段；保存次数由宿主维护（参考 `wSavedTimes`）。
- 保存：三个 WORD 级字段全部必填。
- 清除：无。
- 重算：无（不得补版本、不得迁移；见"兼容规则"）。
- 恢复顺序：最先校验；头失败即整体拒绝。
- 独立验收：`PlayerSaveContractTests.Contract_identity_freezes_v1_version_five_slots_and_reference_limits`、`Rejects_header_violations_fail_closed`、`Reference_diagnostic_paths_name_the_violating_field`。参考函数：`PAL_SaveGame_Common` 头字段、`PAL_SaveSlotMenu` 五槽。

### D1 玩法内容身份

- 所有者：外层正式内容加载链（`TriggerBattleContent`/`ModernBattleContent`、语言包、场景静态规则、装备对象等）。
- 引用身份：`PlayerSaveV1.ContentIdentity`（1..256 非空白字符；语义引用，不含路径语义）。
- 存储域：`$.contentIdentity`（必填）。
- 捕获时点：宿主装配会话时确定，保存时随头写入。
- 保存：仅身份字符串；**不存**任何静态内容字节、纹理、字形或节点（#148 Decision 15）。
- 清除：无。
- 重算：恢复时由现有原生加载链按身份重建全部静态内容，并做引用闭包核对（见"恢复顺序"）。
- 恢复顺序：头校验后、任何状态域重播种前。
- 独立验收：字面夹具钉定长度/非空域；跨语言文件散列差异不得误判（恢复票验收）。

### D2 六角色记录（身份/等级/HP/MP/主与隐含经验/基础面板/CoveredBy）

- 所有者：`RoleLiveState`（六条 `BattleRoleSnapshot`；角色身份是 `RoleId`，与队伍顺序、战斗席位无关）。
- 引用身份：`PlayerSaveRole.RoleId`（0..5，六条记录完整且唯一）。
- 存储域：`$.roles[*]`：`roleId/level/hp/maxHp/mp/maxMp`（WORD；hp≤maxHp、mp≤maxMp，与 `RoleLiveState` 种子一致）、`primaryExperience`、`hiddenExperience`（≤7 条命名轨道，仅七规范 Ordinal 名：health/magic/attack/magicPower/defense/dexterity/flee；域外键构造期拒绝）、`equipment`（恒 6 个 WORD）、`magics`（≤32 个 WORD）、`coveredByRoleId`（0..5）、`attack/magicPower/defense/dexterity/fleeRate`（永久**基值**面板 WORD）。
- 捕获时点：稳定边界一次性读出全部六条（含未参战角色）。
- 保存：全六条完整保存；**不存** `Statuses` 九词（D7 清除域）。
- 清除：九个状态词（参考 `PAL_InitGameData` `memset(rgPlayerStatus)`）；毒另列（D8）。
- 重算：无（等级、经验、HP/MP、基值面板是永久状态，逐字恢复；`wReserved` 不是玩法状态）。
- 恢复顺序：内容身份闭包核对后、装备重算（D9）前重播种 `RoleLiveState`。
- 独立验收：`Literal_fixture_round_trips_every_required_field`（独立字面值逐字段回读）、`Rejects_role_domain_violations_fail_closed`。参考：`SAVEDGAME_COMMON.Exp`/`PlayerRoles`、`ALLEXPERIENCE`（8 轨道=主+7 隐含）。

### D3 装备网格

- 所有者：`RoleLiveState` 记录内的 `Equipment` 六槽 + `BattleEquipmentState` 的 `(role, slot)` 网格视图。
- 引用身份：`(RoleId, 部位 0..5)` → 装备对象 WORD（0=空，非零须为已声明装备对象——**引用闭包在恢复期对照已准备内容**，构造期只校验形态域）。
- 存储域：`$.roles[*].equipment`（恒 6 个 WORD）。
- 捕获时点：随 D2。
- 保存：逐格保存对象号；不存任何效果寄存器（D9）。
- 清除：无（装备网格是永久状态）。
- 重算：无（网格本身原样恢复；寄存器另列 D9）。
- 恢复顺序：D2 重播种后、以网格为输入重算装备效果（`BattleEquipmentState.Initialize`，即 `PAL_UpdateEquipments` 语义）。
- 独立验收：字面夹具钉定 6 槽形态与 WORD 域；恢复票按 `BattleEquipmentState.Initialize` 覆盖核对重算一致。

### D4 已学法术槽

- 所有者：`BattleRoleSnapshot.Magics`（六角色各一行，菜单/战斗共享读）。
- 引用身份：法术对象 WORD（0..65535；恢复期对照已准备法术内容闭包）。
- 存储域：`$.roles[*].magics`（≤ `MAX_PLAYER_MAGICS` = 32 个 WORD，含 0 空位）。
- 捕获时点：随 D2。
- 保存：整行保存（含 0 空位形态，保真 authored 形态）。
- 清除：无。
- 重算：无。
- 恢复顺序：随 D2 重播种后，菜单（`ExplorationMenuSession` 魔法页）与战斗以同一行消费。
- 独立验收：字面夹具（role5 行 `[65, 0]`）；`MaxLearnedMagics` 越界拒绝。

### D5 参战队伍身份/顺序

- 所有者：`DynamicEventWorld.PartyRoles`（script.c 0x0075 三槽）＋ `PartyPoisonState.PartyRoleIds`（同一顺序的另一只读视图）。
- 引用身份：队伍成员 `RoleId` 序列。
- 存储域：`$.partyRoleIds`（1..3 个互异 0..5）。
- 捕获时点：稳定边界读出当前队伍。
- 保存：有序身份列表。
- 清除：无（队伍身份恢复；毒行另行清零，D8）。
- 重算：无。
- 恢复顺序：D2 后重播种 world 队伍与 poison 持有者（毒行=清零后的空行）。
- 独立验收：`Rejects_party_violations_fail_closed`（空/4 人/重复/越界拒绝）。参考：`SAVEDGAME_COMMON.rgParty`、`MAX_PLAYERS_IN_PARTY`。

### D6 背包（256 槽，含占用零数量槽）

- 所有者：`InventoryLiveState`（`Capacity = 256`；占用但耗尽的槽保持占用直到显式压缩）。
- 引用身份：占用槽顺序列表（ItemId 唯一、非零 WORD）。
- 存储域：`$.inventory[*]`：`itemId`（1..65535）、`amount`（0..99，参考数量上限，**不夹值**）、`amountInUse`（0..65535 保留 WORD）。
- 捕获时点：稳定边界整表读出（仅占用槽；运行期占用集合是稳定前缀，无空洞）。
- 保存：占用槽按序保存，**耗尽槽（amount=0）照存**——它与缺席不同（`GetAmount`/交换/删除语义依赖 ItemId 保留）。
- 清除：无（尾随空槽不存、恢复时补零）。
- 重算：无（恢复后不做隐式压缩；压缩是未来显式菜单动作——此为以 `InventoryLiveState` 为锚的现代偏差，见下方独立验收注记，非参考载入行为）。
- 恢复顺序：D2/D5 后重播种 `InventoryLiveState`；金钱（D10）独立。
- 独立验收：`Literal_fixture_round_trips_every_required_field`（`(65535, 0, 65535)` 字面行）、`Rejects_inventory_violations_fail_closed_without_clamping`。参考：`rgInventory[MAX_INVENTORY]`、`PAL_CompressInventory`（**真实存储压缩，非视图压缩**：`PAL_LoadGame_Common` 每次载入 global.c:636 与物品菜单打开 itemmenu.c:340 均调用；函数体 global.c:1212 起原地重写 `rgInventory`，`nAmount>0` 行前移，尾部行 `wItem/nAmount/nAmountInUse` 全部清零）。**现代偏差（#148 Decision 4 显式说明）**：v1 恢复后不隐式压缩、耗尽槽带 ItemId 保留占用，锚定 A/B 活状态所有者 `InventoryLiveState` 的占用语义，不是 SDLPal 载入行为；原版在每次载入与物品菜单打开时都会清掉这类行。

### D7 金钱

- 所有者：`CashLiveState`（DWORD）。
- 引用身份：`PlayerSaveV1.Cash`。
- 存储域：`$.cash`（uint，DWORD 域）。
- 捕获时点：稳定边界读出。
- 保存：DWORD 全量。
- 清除/重算：无。
- 恢复顺序：随 D6 批次重播种。
- 独立验收：字面值 `123456789u`。参考：`SAVEDGAME_COMMON.dwCash`。

### D8 毒（清除域）

- 所有者：`PartyPoisonState`（每当前队伍角色一列 16 个 `(PoisonId, ScriptEntry)`；毒定义级别表是静态内容）。
- 引用身份：毒对象 WORD → 已准备毒内容（恢复期闭包）。
- 存储域：**v1 不存任何毒行**。`PlayerSaveContract.PoisonCapacity = 16` 冻结清零恢复的列形状（每角色 16 个 ID/脚本 WORD 对）。
- 捕获时点：不捕获。
- 保存：不保存（参考虽把 `rgPoisonStatus` 写进文件，但 `PAL_LoadGame_Common` 载入即 `memset` 清零——存储与载入规则分离，v1 直接不存这些字节）。
- 清除：恢复时全列清零（`ResetForPartyChange` 语义的零行重建；与 script.c 0x0075 party 变更清毒一致）。
- 重算：无（毒由战斗/脚本重新施加；**不得**借恢复复活毒）。
- 恢复顺序：D5 队伍重播种后建零行；毒定义表由内容身份重建。
- 独立验收：本票以 `PoisonCapacity` 常量 + 文档冻结；恢复票验收零行与定义重建。参考：global.c:630、script.c:2195。

### D9 装备效果寄存器与有效面板（重算域）

- 所有者：`BattleEquipmentState`（九组寄存器 + 持久状态覆盖）。
- 引用身份：`(RoleId, 部位寄存器)`；效果行来自已声明装备对象。
- 存储域：**不存**。既不存寄存器，也不存任何 `Effective*` 有效面板。
- 捕获时点：不捕获。
- 保存：不保存。
- 清除：载入时寄存器全零起点。
- 重算：以 D3 网格为输入重放 `BattleEquipmentState.Initialize`（= `PAL_UpdateEquipments` 重放 equip 脚本效果行）；持久好状态（>999）经 `PAL_SetPlayerStatus` 取较长值分支重建，**只重建一次，禁止叠加**；永久基值（D2）与有效面板（本域投影）严格分离。
- 恢复顺序：D3 之后、状态九词覆盖（D2 清除 + 本域覆盖）一并完成。
- 独立验收：本票不实现重算；恢复票以"读档两次不叠加"独立验收（#148 Testing Decision 3）。

### D10 场景身份与领队

- 所有者：`DynamicEventWorld.SceneId` + `Leader`（`StaticLeader`）；`ExplorationSession.Current` 是其投影。
- 引用身份：场景 WORD + 领队位置/方向/层/步相位 WORD。
- 存储域：`$.world.sceneId`、`$.world.leader.{x, y, direction(0..3), layer, stepPhase(0..3)}`。
- 捕获时点：稳定边界（非行走中间步）读出。
- 保存：仅上述 WORD；**不存** `StaticScenePlan`、`IsWalking`、`WalkFramesWord`/`Sprite`（静态内容）、尾随队员轨迹（本模型未建模，参考 `rgTrail` 列为不支持）。
- 清除：行走中间状态清除（恢复为非行走、新时基）。
- 重算：恢复后由 `StaticSceneRules.BuildPlan` 重建绘制计划；调度用新会话时基，不补跑停机更新。
- 恢复顺序：world 重建（D12）内；先场景/领队，后对象与脚本槽。
- 独立验收：字面夹具 `PlayerSaveLeader(-3192, 1032, 3, 1, 2)`。参考：`wNumScene`、`wPartyDirection`、viewport。

### D11 事件对象动态字（含 trigger/auto 入口与 idle 计数）

- 所有者：`DynamicEventWorld.Objects`（`DynamicEventObject`；静态身份留在 `Initial`/`StaticSceneRules`）。
- 引用身份：对象全局唯一 WORD id（1..65535）。
- 存储域：`$.world.objects[*]`：`objectId/x/y/direction/currentFrame`（WORD）、`state/vanishTime`（有符号 SHORT；负 state=0x0052 隐藏）、`triggerMode/triggerEntry/autoEntry`（WORD）、`autoIdleCount`（WORD，`wScriptIdleFrameCountAuto`）、`triggerIdleCount`（WORD，`nScriptIdleFrame`；参考 `global.h:109` 定义，`PAL_SaveGame_Common`/`PAL_LoadGame_Common` 以 `global.c:869/402` 整体 memcpy 保存/恢复 `EVENTOBJECT`，故 trigger 计数必然往返）。
- 捕获时点：稳定边界整场景读出。
- 保存：全部动态字，含 trigger/auto 入口（自动重定向保留的地址就是入口字）与每对象两个 idle 计数（参考把整个 `EVENTOBJECT` 存档）。
- 清除：无（动态字是保留数据）。
- 重算：sprite 身份、帧数、layer 等静态字由内容重建；负 state 的离屏恢复与 vanish 倒数继续由世界更新推进（play.c `PAL_GameUpdate`）。
- 恢复顺序：随 D12。
- 独立验收：字面对象行（含 `state = -2`、`vanishTime = 800`、`triggerEntry = 392`）。参考：`SAVEDGAME_COMMON.rgEventObject`、`EVENTOBJECT`。

### D12 场景脚本槽与对象脚本槽（跨场景保留）

- 所有者：`DynamicEventWorld.SceneScripts`（sceneId → (onEnter, onTeleport)）与 `ObjectScripts`（(objectId, slot 0..5) → entry）。
- 引用身份：场景 WORD / (对象 WORD, 槽 0..5)。
- 存储域：`$.world.sceneScripts[*]`（唯一 sceneId，≤ `MAX_SCENES` = 300 条）、`$.world.objectScripts[*]`（唯一 (id, slot)，≤ 600×6 = 3600 条）。
- 捕获时点：稳定边界读出**全部已记录修改**，含非当前场景（保存不截取可见节点）。
- 保存：仅被修改过的槽（保留数据，载入不执行它们）。
- 清除：无。
- 重算：无。
- 恢复顺序：D10 后写回 world 字典。
- 独立验收：字面 `(13, 40, 0)` 跨场景行与 `(1, 0, 328)` 对象槽行。参考：`rgScene[MAX_SCENES]`、`rgObject` `rgwData[2+slot]`（script.c 0x0090、0x006D）。

### D13 探索控制字

- 所有者：音乐 `TriggerScriptSession.Music`（`TriggerMusicState(MusicId, Mode)`）；战场号 `DynamicEventWorld.Battlefield`。
- 引用身份：WORD 控制值。
- 存储域：`$.world.music?`（可空：首支 setMusic 前为 null）、`$.world.battlefield`（WORD）。
- 捕获时点：稳定边界读出。
- 保存：仅当前控制字。参考另存 `wNumBattleMusic/wCollectValue/wScreenWave/wChaseRange/wChasespeedChangeCycles/wLayer/nFollower/bBattleSpeed`——A/B 未支持的所有者**不存**，逐项列为 v1 未支持域（不冒称已保存）。
- 清除：无。
- 重算：无。
- 恢复顺序：随 D12 写回。
- 独立验收：字面 `(3, 1)` 音乐行与 null 音乐合法断言。参考：`wNumMusic`、`wNumBattleField`。

## 不存清单（#148 Decision 14 + 实际边界）

以下一律不进 v1 快照，载入不承诺重建：

1. 引擎对象与句柄：节点、纹理、`PackedScene`、`FileAccess` 句柄、事件订阅、输入按住状态。
2. 请求/回执现场：请求 GUID、旧完成回执、活动战斗队列、脚本调用栈、对话打字进度、`PendingDisplay/PendingBattle/PendingShopRequest` 等所有 parked 请求（`DialogueDisplaySession`/`ExplorationShopSession`/`NpcInteractionSession` 均为瞬态，保存资格判定直接排除）。
3. 会话游标与展示：菜单游标（参考 `PAL_InitGameData` 载入即 `iCurInvMenuItem = 0`，主菜单/系统菜单游标同属会话态）、`StaticScenePlan`、`IsWalking`、`nextDeadline/hasUpdated`、`FadedToRed`、绝对进程时钟。
4. 派生投影：`BattleEquipmentState` 全部寄存器与 `Effective*` 面板（D9 重算域）、`ExplorationMenuStatusView` 等纯投影视图。
5. RNG 内部状态：v1 不承诺跨存读随机序列一致（#148 Decision 16）；确定性规则测试继续用独立显式序列。
6. 静态内容：场景静态规则/贴图/字形/语言包/战斗内容/装备效果行——只存 D1 内容身份引用。
7. 参考保存但 A/B 未支持的域：`wNumBattleMusic`、`wCollectValue`、`wScreenWave`、`wChaseRange`、`wChasespeedChangeCycles`、`wLayer`（层以领队字携带）、`nFollower`、`bBattleSpeed`、`rgTrail`（尾随队员轨迹未建模）。

## 恢复顺序（冻结）

1. 头校验：版本恰为 1、槽号 1..5、内容身份非空（失败即整体拒绝，不部分应用）。
2. 内容身份闭包核对：场景、事件对象、装备/法术/毒对象、商店与菜单绑定、语言包对照已准备内容；不匹配进入可关闭失败终态（D4=A），不默认新游戏。
3. 六角色记录重播种（`RoleLiveState`），状态九词清零。
4. 队伍重播种（world 队伍 + `PartyPoisonState` 零行）。
5. 装备网格生效 + `BattleEquipmentState.Initialize` 重算寄存器与持久状态覆盖（一次、不叠加）。
6. 背包与金钱重播种。
7. world 重建：场景身份与领队 → 对象动态字 → 场景/对象脚本槽 → 控制字。
8. 探索会话重建：新 `BuildPlan`、非行走、新请求/回执/调度时基；旧会话在此之后才退役（候选切换细节归 c5/c6 接线票）。

## 上限与数值域总表（`PlayerSaveContract`）

| 常量 | 值 | 依据 |
| --- | --- | --- |
| `CurrentVersion` | 1 | v1 冻结；未知版本拒绝 |
| `FirstSlot/LastSlot/SaveSlotCount` | 1/5/5 | D3=A 五手动槽（`PAL_SaveSlotMenu`） |
| `InventoryCapacity` | 256 | `MAX_INVENTORY` = `InventoryLiveState.Capacity` |
| `PoisonCapacity` | 16 | `MAX_POISONS` = `PartyPoisonState.Capacity`（清零列形状） |
| `RoleCount` | 6 | `MAX_PLAYER_ROLES` = `BattleEquipmentState.RoleCount` |
| `EquipmentSlotCount` | 6 | `MAX_PLAYER_EQUIPMENTS` = `BattleEquipmentState.SlotCount` |
| `RoleStatusCount` | 9 | `kStatusAll` = `BattleEquipmentState.StatusCount`（不存，载入清零） |
| `MaxLevel` | 99 | `MAX_LEVELS` |
| `MaxPartyMembers` | 3 | `MAX_PLAYERS_IN_PARTY`（script.c 0x0075） |
| `MaxHiddenExperienceTracks` | 7 | `ALLEXPERIENCE` 隐含轨道 |
| `MaxLearnedMagics` | 32 | `MAX_PLAYER_MAGICS` |
| `MaxEventObjects` | 5500 | `MAX_EVENT_OBJECTS` |
| `MaxSceneScriptEntries` | 300 | `MAX_SCENES` |
| `MaxObjectScriptEntries` | 3600 | `MAX_OBJECTS` × setObjectScript 槽域 0..5 |
| `MaxContentIdentityChars` | 256 | 防御上限 |
| `MaxFileBytes` | 1 MiB | 解码前文件长度上限（Decision 20）；有界最坏内容以余量覆盖 |

WORD 域字段（0..65535）：经验三字、装备/法术/毒/对象/脚本入口字、领队 layer、savedTimes。有符号 SHORT：对象 `state`、`vanishTime`。方向/步相位 0..3。数量 0..99（不夹值）。金钱 uint。构造期逐项 fail-closed 拒绝并给出 `$.` 前缀可定位诊断（`InvalidDataException`），不夹值、不静默修复。

## 兼容规则

1. 版本缺失、未知或 ≠ 1 一律拒绝；无自动迁移、无部分应用、无补版本（迁移须另立批准契约）。
2. 字段缺失、重复键、类型错误、越界数值、维数不符、悬空引用（形态层）在构造期拒绝；引用闭包（对象/法术/装备/毒/场景是否在已准备内容中）在恢复期对照内容身份拒绝。
3. 坏档/不兼容不触发默认新游戏、不静默恢复默认数据；进入有明确原因、可关闭的失败终态（D4=A）。
4. 存档内容不能指定写入路径；槽号→`user://` 命名空间映射由外层受控 Adapter（c3 票）实现。
5. 本票不改变任何既有 A/B 行为；`PlayerSaveContract` 常量自既有所有者单源引用（`Frozen_limits_come_from_the_live_owners_single_source` 钉定，防漂移）。

## 分层验收与证据

- 本票（c1）：`Pal.Core.UnitTests` 全工程绿绑定候选；契约类型 + 字面夹具即上述各域"独立验收"条目；无引擎层改动，不跑 game 批次，未触碰 guarded content（无需 native-map 重建）。
- 下游：c2（最小格式与稳定资格）、c3（真实五槽 FileAccess 读写）、c5（正式系统菜单/恢复槽）、c6（requestReload）、c7（拒绝/失败保护）、c8（关闭重开与阶段整合）阻塞于本契约（#148 Decision 10）。
- 真实 FileAccess、真实进程重启、GPU/人工验收不在本票范围；本票证据不转签下游层。
