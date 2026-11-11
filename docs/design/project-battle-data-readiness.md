# #133 M5 战斗数值投影：来源与消费边界（#191 SOURCE）

## 结论 / 证据等级

固定调查基线：`execution/m6-loop@02103e2fe83551ac0fdef1c6aaaa2aba894bc667`。实时 #133 OPEN、原生 blocker 原为空，#113 CLOSED；本报告是新增必要前置 #191，不是 `project-battle-data` 的实现或验收。本文不改变任何代码、格式、公共接口、支持域或测试计数。

**现有 M5 索引不能单独证明现代 JSON 与原 chunk 的值相符。** 其散列对象是原归档/原 chunk，而非输出 JSON。新 CLI 可以严格校验现代包、记录输入文件身份，却不能把这两件事称为原 chunk SHA 验证成功。不读原档、不重新提取、不违反 ADR-0004 重新编码数据的前提下，需要先明确“已验证 M5 包”的信任前提。下文的建议不是该决定的批准。

另外必须区分：六角色身份与五列升级法术、`FFFF` 跳过与 `0` 空槽、格式合法与当前可消费、基表独立初始值与自然剧情存档。不能通过清零未支持字段获得假阳性。#131/#190 的现金声明待决与本票无关，仍保留。

## 1. 实际生产链与可复用能力

以下路径/行均绑定上述基线；不是运行证据。

| 所有者 | 已有能力 / 实际限制 |
| --- | --- |
| `tools/Pal.Extract/Program.cs:56` | 正式 `export-data`，尚无 `project-battle-data`；后者的四选项形状已由 #133 指定。不要改成重新提取入口。 |
| `tools/Pal.Extract/DataGameData.cs:19`、`:25`、`:35`、`:70`、`:83` | 从两个归档提取，先 Decode/Validate，输出13表和索引；sources 是整个归档 SHA，tables 是原 chunk 元信息。它不是现代包读取器。 |
| `tools/Pal.Extract/DataTableSchema.cs:94`、`:113` | `DescribeIndexEntry` 计算 chunk hash/长度/记录数；公开 `ValidateDocument` 校验文档字段、维度和值域，不核 index/hash，也不核 records.Count 对声明长度。 |
| `tools/Pal.Extract/ObjectUnionTable.cs:240`、`:258` | 独立 union validator 按声明 chunk 长度识别 WIN/DOS，再检查每条 view/字段；不重建视图证据，不证明 records 数量或源值对应声明 SHA。目标 Windows，不应默默接收 DOS 或猜测84倍数的 variant。 |
| `tools/Pal.Extract/StrictJson.cs:14`、`:32`、`:47` | 复用严格 UTF-8、递归重复键、精确字段、WORD；下划线注释按现有契约允许。不是完整包认证器；文件大小/稳定读取/索引关系须调用方负责。 |
| `tools/Pal.Extract/ExportPaths.cs:5`、`tools/Pal.Extract/PathLinkGuard.cs` | 复用路径/link 检查；ExportPaths 已拒绝输出等于/位于输入内、非空覆盖。新调用不能拿 requiredNames 的“archive”名称/诊断假称现代JSON已经检查，也须补齐其实际读取文件及反向重叠边界。 |
| `tools/Pal.Extract/BattleContextAuthor.cs:12`、`:42` | 已有 Core 引用与 `CreateNew` 写入先例；这是冻结 author，不是本票的基表投影器，不复用/篡改其profile或自然save身份。 |
| `src/Pal.Core/ModernBattleContent.cs:10`、`src/Pal.Core/ModernBattleContentValidation.cs:6` | 公开 Parse/Validate 是纯数值格式门，允许一些尚不能执行的合法字段；不能单独证明普通战可消费。 |
| `src/Pal.Core/TriggerBattleContent.cs:35`、`:61`、`:92` | 当前1–2角色/密集1–2敌位；角色按身份关联、Players保留队序；内部 Validate 复用 lifecycle/装备门。不能照旧文档的单角色描述，也不能预先放宽到3/5。 |
| `src/Pal.Core/ClassicBattleLifecycleSession.cs:353`、`:364`、`src/Pal.Core/ClassicBattleSession.cs:481` | 内部真实能力门：角色/装备/EXP域和普通物理敌人限制；没有一个现成公开的、仅收数值且不构造会话的全组合能力门可供工具直接调用。 |
| `src/Pal.Core/TriggerScriptSession.cs:185` | 公开 ValidateEntry 还需要真实事件/语言/入口；本票四选项没有这些输入。不要为借用此门在生产 CLI 内伪造事件、语言或dummy宿主。 |
| `src/Pal.Game/Editor/PrepareBattleData.cs:52`、`:120` | 后续 Prepare 输入严格恰一份battle-data.json。来源/选择收据必须在其数值目录外，不放宽这个接口。本票不运行或修改 Prepare。 |

搜索边界：`src`、`tools`、`tests`、`scripts` 的直接 validators、`project-battle-data`、数值消费与 CLI 调用；未发现已有只读 M5 包认证/投影实现。固定仓库外调用者 `C:/evidence/m6/loop/M6Loop.psm1`、`C:/evidence/m6/loop/Start-M6Loop.ps1` 无 `project-battle-data`、DataTableSchema、ObjectUnionTable 或 ModernBattleContent 直接消费者。未覆盖未知外部二进制/工具，不承诺ABI。

## 2. 三种身份不能混用 / 真正待决项

1. **来源声明**：`sources[].sha256` 是 SSS/DATA 归档 SHA（该层没有 archive byteLength）；`tables[].sha256/byteLength` 是源 chunk 的 SHA/长度，不是 `.json` 文件的 hash/长度。文件名→archive/chunk→recordCount×固定宽度、13项覆盖/重复、版本/profile/WORD/引用均应校验，但这些是结构与声明的一致性。
2. **本次输入身份**：读取现代文件的实际字节 hash/长度，以及 index 的实际 hash，可以冻结本次输入并检测后续改动，供选择/输出收据追溯。不能拿JSON hash与源chunk SHA直接比较；也不能把此处新算的输入hash称为独立来源认证。
3. **独立绑定证明**：先前被实际核对并冻结的文件身份+验收材料，才可能证明当前现代文件是那份被核对的包。#113 历史验收本身不认证任意新路径/被修改的文件，不能仅按其CLOSED放行。文件身份必须实际相等且证据等级如实标明。

**独立反例（SOURCE逻辑证明，未运行）**：把已合法 `enemy.json.records[k].attackStrength=10` 改成11，保留原 index 的 archive/chunk SHA、长度和记录数；字段仍是WORD、所有现有结构验证仍成立，但其值不再是原chunk的值。再次散列修改后的JSON只能证明“11版文件”的身份。合法改 `object.enemyId` 指向另一存在行也一样；引用存在不是来源相符。重算一份自称正确的索引更不能补认证。

需要明确的唯一前置决定是 #133 的**输入信任强度**：

| 方案 | 能交付什么 / 成本 |
| --- | --- |
| A：外部已验证来源是调用者前提；CLI严格校验包/引用/声明，记录实际现代输入字节与原来源声明，清楚注明二者并非独立认证 | 不改M5 v1、不读raw、无需重提取。推荐先澄清 #133 的“来源散列/长度核查”是否指这种一致性/追溯；**没有批准前不把较弱语义冒称满足原要求**。 |
| B：CLI必须机械验证现代文件与独立已验收身份匹配 | 需要可信、实际冻结的现代文件摘要输入/来源及其兼容策略；现有四选项/index没有这个独立根。不能静默增加第五选项、重解释chunk SHA、给旧v1追加强制字段，或生成自签清单冒充证明。影响另算/先拆来源契约票。 |

本报告没有选择A/B；不提出“回读原档/数据重编码”作为绕过边界的第三方案。确认后直接发布实施票，不再为同一缺口建立重复SOURCE票。下文是在不改变M5/数值格式的A路径下给出的完整候选施工图。

## 3. 字段来源与可消费边界

### 角色、经验与装备

- `tools/Pal.Extract/DataTables.cs:135` 的 PLAYERROLES 是**单记录、每字段六列**。RoleId就是列号；`--roles 4,0` 代表队序[4,0]，不是把4重编号成0。equipment[部位][role]取六部位，magic[槽][role]取32槽；不转置。当前两角色上限在投影前拒绝第三个，不通过裁掉尾部适配。
- level/hp/maxHp/mp/maxMp 与五基面板 attackStrength/magicStrength/defense/dexterity/fleeRate 按角色列原样写 role；player 使用同一基值和 equipment。role 的五panel一起显式声明，使后续成长承接永久基值；不把有效装备面板写成基值。`name[role]` 是 player object id，映射 nameObjectId、scriptOnDying/scriptOnFriendDeath，不能用RoleId或相同脚本地址猜身份。缺行/错误view准确拒绝。
- 参考 `reference/sdlpal/global.c:427`、`:449`、`:455`：默认起始上下文载基表，清EXP全表，再将primary及七类hidden的 **level设为该角色level，value/count为0**。例如role4 level7时八类都是(0,7,0)，不是(0,0,0)。`reference/sdlpal/global.c:951` 清角色statuses，默认poison先由 `:450` 清空。这是明确采用默认初始化规则的**独立基表上下文**，不是剧情已推进的save，不能把缺失snapshot字段推广为“已知无毒”。
- `reference/sdlpal/global.c:953` 还执行 PAL_UpdateEquipments。非零装备不能只抄ID并填零effects：M5没有事件执行输入，缺少真实equip-script闭包就应定位拒绝；不执行脚本、不借用旧冻结author或当前live owners。Core已有装备能力不等于这个离线入口已获得该闭包。
- `coveredBy[role]`保留角色身份。snapshot的0仍是role0，不是null sentinel；player的可空实际cover是不同表示，需依据所选队伍和既有有效cover语义明确映射并留来源说明，不按party位置替换。无法证明的组合拒绝，不一律null清除。
- `DataTables.cs:246` 的 level-up-magic 每行只有roles[5]，按**原RoleId**取列并保留全行顺序/重复/零magic；`DataTables.cs:307` 的level-up-exp单记录完整100项按level索引。RoleId5没有第六升级法术列；本窄入口必须明确定位“不支持该角色的完整成长来源”，不能生成零表、借用第0/party列或声称Core不支持role5（Core实际允许其身份）。
- 角色32个已知法术与升级法术是静态数据，不等于施法执行；支持域须按#133普通物理目标及实际消费界定。原值/引用保真，不删掉非零槽假称无魔法。未能证明所选成长/钩子闭包可消费的情况拒绝；不要把“本投影入口缺少事件/法术依赖”写成“Core所有hooks/成长均未实现”。
- Level0配threshold[0]=0会使胜利结算无法前进；现有格式允许0、内部growth门只强制threshold[1..99]为正。投影需明确拒绝这个不可消费组合，而不是把表项补1或声称单靠既有门就已安全；不执行结算来试验。HP0基表值不能悄悄变1：`reference/sdlpal/battle.c:1567` 的HP0→1是入战步骤，lifecycle已有相应职责。格式允许HP0，保存原始基表值；后续能力检查必须针对正式入口的真实归一化，而非直接combat的活人限制。角色maxHP>32767、等级/EXP表等仍受当前trigger/lifecycle门限制，不放宽现有检查。

### 敌队、槽位与有符号解释

固定参考 `reference/sdlpal/battle.c:1599`、`:1604`、`:1612`、`:1716`：每次j推进，FFFF continue不推进i；0虽不加载敌记录，却仍执行i++。team数组存的是**object id**，通过enemy view的enemyId才指向enemy表；同一object/definition重复出现仍是不同战内槽，不能按敌ID合并。

- `[A,FFFF,B,FFFF,FFFF]` → compact slots0/1，原j0/j2都留来源映射；`[A,0,B,FFFF,FFFF]` 含真实空槽及跨度3，当前密集1–2敌结构不支持。即便尾部0也不能过滤成FFFF；全FFFF/全0、三活敌或任何0空槽都应定位拒绝，不造敌人或截断。战内id须非零且逐槽唯一；建议按compactSlot+1，收据另保留sourceSlot/objectId/enemyId映射，最终名称/收据布局仍待实施票冻结。
- enemy level/health/physicalResistance/exp/cash原WORD；attackStrength/defense/dexterity虽M5按WORD存，实际普通物理消费分别以SHORT解释：`reference/sdlpal/fight.c:4917`、`:4636`、`:312`。65535→-1、32768→-32768、32767→32767，不能夹为0或当正面板，也不把整张enemy表改为signed。Level附加与有效攻防计算留给Core，投影不预加公式。
- object的三个hook映射turnStart/ready/battleEnd。当前Core已能由capable host处理部分角色/战后hooks；但本入口没事件集合，不能证明这些闭包，必须在相关非零依赖处拒绝，不清零。enemy magic/dualMove/attackEquivItem等仍受当前物理能力门限制，不能只验格式。
- **enemy magic FFFF不能归一化为0**。定点读 `reference/sdlpal/fight.c:4656`、`:4663`：wMagic非零先抽RandomLong(0,9)并与magicRate比较，命中且未沉默时FFFF走goto end，即本次空动作；0则短路不抽该数并走普通物理。即使rate0，FFFF仍比0多一次RNG消费，所以也不能按rate0洗为0。当前Core普通敌门只接受MagicId0，本入口须对FFFF准确拒绝，不增加空动作/施法机制、不谎称语义等价；它与enemy-team的FFFF跳过是不同语境。原scout仅据schema注释提出表示映射，父控查真实调用后已纠正，原件和勘误保留。其他魔法/等效物品字段同样连同rate/实际消费核对，不仅看字段名。
- `reference/sdlpal/battle.c:1621` 的大量特殊怪修正在 `#ifndef PAL_CLASSIC` 内，目标经典分支不应用这些dexterity/level/自动治疗hack。图像、声音、抗性等未被现有数值格式消费的元数据不是可随意改值的字段；保留输入身份/来源映射，后续图像绑定不由本票完成。

## 4. 完整候选影响集与接口缺口（均未批准/未实现）

现有工具已有向Core的ProjectReference，不新增反向依赖。完整实施候选 **9路径**（仅A方案、不加资源/夹具文件，不更改现有M5/数值schema）：

1. `tools/Pal.Extract/Program.cs`：正式四选项解析、help、原生退出/定位诊断。
2. `tools/Pal.Extract/BattleDataProjector.cs`（新）：必要窄投影模块；包读取/安全发布与可纯输入验证的映射边界在同模块，不造通用仓储/包框架。
3. `src/Pal.Core/ModernBattleContentValidation.cs`：**如批准**，公开纯数值选中战斗能力入口，复用现有内部content/lifecycle/combat检查；不复制战斗规则/构造dummy事件，也不模拟获胜/抽RNG。
4. `tests/Pal.Core.UnitTests/ModernBattleContentTests.cs`：上述门的独立正/反例、无副作用及HP0正式归一化边界。
5. `tests/Pal.Extract.UnitTests/BattleDataProjectionTests.cs`（新）：人工现代文档在内存，独立字面映射与拒绝；不依赖文件/进程/Godot。
6. `tests/Pal.Extract.IntegrationTests/ProjectBattleDataCliTests.cs`（新）：文件安全、真正CliProcess argv/exit、输出和身份；人工源在测试内生成，不提交游戏资源。
7. `docs/design/modern-battle-data.md`：离线入口、支持域、来源/收据等级与目录契约，不改冻结author或已有Prepare严格单文档入口。
8. `scripts/GameTestBatches.ps1`：实际新增Core/CLI测试计数。
9. `tests/Pal.Game.Tests/GameBatches.Tests.ps1`：匹配的计数/元数据回归。

这不是用SOURCE票批准第3项新公共签名。现有门不可从工具直接调用是实际接口缺口；推荐只开放既有纯检查的窄包装，函数名称/拒绝语义先写入实施票，不能为复用而引入假事件/第二引擎。若需单独交付门，可先拆其5路径（3/4/7/8/9），再做投影7路径（1/2/5/6/7/8/9），每叶单独可编译验收。新文件若还需共享fixture/安全helper、来源B或修改其他调用者，重新计算，不把生成/登记路径藏到第11个。

目录建议：`output/numeric/battle-data.json` 与独立 `output/receipt.json`；Prepare仍只接收numeric目录。这仅是与已批准“数值目录外收据”相容的建议，不冻结新公开收据字段/版本。receipt应区分declared source、实际现代输入身份、ordered selections、所有字段来源/已证明映射、输出字节SHA；不可把自身hash做自递归认证。缺失/非法/未知/重复输入先完整拒绝再写文件；CreateNew，新/空目标，不覆盖、不清理他人文件，输入与输出双向不重叠且检查实际读写路径所有祖先/link。已有ExportPaths只提供其中一部分，不能直接声称完整保护已通过。

## 5. 独立验收预期（全部 NOT RUN）

| seam | 固定预期 / 反例 |
| --- | --- |
| 来源等级 | 合法10→11修改仍可通过schema，但不得产生“original chunk verified”结果；固定输入身份改变，原声明保留。若采用B，须与真正独立摘要不符而拒绝，不能用新生成自签摘要通过。 |
| 身份/数组轴 | 人工role顺序[4,0]，role4 level7/attack123/hp80/maxHp100，role0不同哨兵；八EXP=(0,7,0)，两个角色不串位；装备/32magic取正确二维列；原值不改。role5、第三角色、重复roles/teams、空/带符号/非十进制列表精确拒绝。 |
| growth/name | 100项threshold完整保留，index0允许0，reachable0拒绝；level-up两行零/重复/顺序与role4列独立字面值一致；nameObject不是RoleId，wrong view/缺行/不完整表拒绝；不能伪造slot5成长。 |
| 敌人/符号 | Aobject10→enemy2、Bobject11→enemy3；[10,FFFF,11,FFFF,FFFF]得到id1/2，原sourceSlot0/2；重复object仍两个槽。任何0/全FFFF/三活敌拒绝；WORD65535→-1，32768→-32768，不加入level基础公式。enemy.magicIdFFFF必须拒绝，包括rate0；不可清为0，否则丢空动作或多抽RNG。其他真实magic同样不能放行。 |
| 消费能力 | Parse/Validate通过只是格式；公开纯能力门覆盖HP0/lifecycle、上限、EXP、无装备闭包、hook/魔法/双动/等效物品拒绝，旧Core兼容。无RNG、无状态写、无战斗模拟；未选内容合法性与所选可消费性分别检查。 |
| 文件/真实CLI | 有效四选项正常及重排（按批准选项风格）、缺/重/未知/非法选项exit2；数据/来源拒绝exit1；成功exit0。通过既有CliProcess真实执行，核stdout/stderr、数值Parse及输出SHA；输入字节/mtime不变、两次新目录输出字节一致、非空/文件目标/双向重叠/链接/缺表/假hash语法/坏UTF8/重复未知键/records和长度不一致拒绝；原件不被覆盖。 |

实施时先冻结可编译指定RED的实际测试/首个独立断言/消息/测试SHA；不要把缺命令exit2、fixture损坏、编译失败、超时算功能RED。Core/CLI Unit走普通dotnet原生入口与runsettings；CLI Integration走正式进程，具体命令从当前测试职责文档/config核对。新增计数用实际发现/运行结果同步。SOURCE不能补签任何Unit/真实CLI/Prepare/Main。

## 本票收尾与剩余限制

父控独立读源码/参考，另有只读scout交叉核对来源边界、j/i槽映射和初始化；引用存在/行锚/hash、脚本语法及实际无参WaitForExit AST检查另见本轮收据。未运行SDK/Unit/CLI/Game/Godot/GdUnit/Prepare/import/solution、真实数据、GPU/人工/CI；没有任何新运行时通过。SOURCE反例是依据实现的静态推导，不伪称已执行测试。

#133仍OPEN；#131现金声明、#132宿主/活动battle/setParty与#129/#147等缺口均不因本报告消失。#164 SOURCE-only/477GREEN未跑、#165 cold、#168 FAILED466/474、#179历史UNKNOWN/fixture失败及broadAST14原件保持，不授予重试。17工程叶、0故事、fullM6=false不变。无自然save/隐式owner、真实资产提交上传、runtime JSON/MKF加载、cache/UID清理、工具链升级、push/PR/main/integration/canonical写入；本票只有一份报告，本地候选仍需外层独立双轴、实际集成与current发布后才关闭 #191。
