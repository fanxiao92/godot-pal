# 现代战斗数值 v1（#46 有界数值准备 seam）

状态：纯 Core `ModernBattleContent.Parse(string)` → 既有 `TriggerBattleContent`，及独立 typed native Resource 编解码 `NativeBattleContent.Write/CreateCore/Load` 已实现。Core 不读文件、不抽 RNG、不创建战斗或模拟获胜；native runtime 不重读 JSON。显式离线 `PrepareBattleData.Prepare`、Editor scene 和正常 PowerShell wrapper 也已交付（见下方准备契约）；**数值准备不是完整人工 package 或 Main 资源链**。Core 测试字符串与可提交 `tests/Fixtures/ModernBattleData/battle-data.json` 均为独立人工数值，不是完整 storypackage 或原版认证。冻结的真实 `first-mandatory-battle-pre-trigger-v1`、envelope/provenance 和 owner28 `@09C3` gate 均未改动。

当前 #167 的新增装备 WORD 仅接通 Core 源码，**不在上述既有 native/Prepare 交付范围内**；运输缺口、禁止旧 writer 使用及验收状态见下方 [#167 Core 窄契约](#167-core装备-word-保真与选中战斗拒绝)。其余带票号的检查点保留各自历史范围。

## M5 基表普通物理战离线投影（#133）

正式 CLI（可重排四个选项，每项恰一次）：

```powershell
dotnet run --project tools/Pal.Extract -- project-battle-data `
  --input <已由上游核对的M5现代目录> --roles 4,0 --teams 1,0 `
  --output <新或空且与输入双向不重叠的目录>
```

`--roles` / `--teams` 是无空白/符号/十六进制的十进制逗号列表，ID 不重复；角色列表顺序就是队序，敌队选择顺序也保留。缺失/重复/未知选项或非法列表退出2；数据、能力、I/O拒绝退出1；完整写出后退出0并输出 `Battle data projected: independent base-table context (upstream-verified input required).`。不依赖 Godot、不执行脚本、不读原档、不重提取、不抽玩法随机。新增纯内存 `BattleDataProjector.Project(files, roles, teams)` 与文件边界 `Export` 供正式 CLI 调用，没有另一套战斗引擎。

### 输入信任 A 与严格包门

#133 的 OWNER 评论已批准信任A：上游提取正确性和原始来源核对是调用者前提，不是本 CLI 新认证的结果。输入必须恰含 `index.json` 与13张已注册现代JSON表（不含原档）；严格 UTF-8（可有计入身份的BOM）、递归重复键、v1/faithful、精确字段/维数/WORD/SHORT全部复用M5规则。下划线注释字段继续允许但重复键仍拒绝。每个文件限制1..64MiB、JSON深度沿用默认64，完整包先校验，不能因某列/表未被选择就跳过结构。PLAYERROLES、LEVELUPEXP、ENEMYPOS、BATTLEEFFECTINDEX必须各一行。

索引必须声明且仅声明SSS.MKF/DATA.MKF两个来源和13个唯一表：各表文件名→固定archive/chunk、recordCount、byteLength=记录数×固定宽度及实际现代records数一致，hash为64位十六进制。OBJECT按Windows14字节/记录核对，沿用M5的variant判别，DOS或12/14倍数歧义拒绝，不猜variant。散列/长度是**原归档或chunk声明**；另外记录全部14个实际现代文件的精确字节SHA-256/长度，绝不将JSON散列和chunk散列混比。合法改值仍可能通过结构门，但其现代输入/输出身份改变；这叫追溯，不是独立来源认证或防篡改。

### 投影与保守支持域

- 当前1–2角色、每队密集1–2敌位，角色身份不重编号。基表 `[field][role]`、装备 `[slot][role]`、法术 `[slot][role]` 按实际轴投影；role/player的五基面板显式同源。名字编号查OBJECT player view及两个钩子，不按party位置猜名字对象。当前无事件输入，所选角色/敌位的钩子非零均定位拒绝。
- 角色等级、HP/MP、六装备槽、32已知法术槽保真；HP0不改1（归一化属后续正式入战）。采用固定 `global.c PAL_LoadDefaultGame/PAL_InitGameData` 默认初始化：primary和七种hidden的value/count=0、level=原角色level，角色状态和毒为空。这是**独立基表起始上下文**，不是自然剧情save，也不是冻结M4 author。非零装备因缺真实装备脚本闭包拒绝，不捏造零effects；cooperativeMagic/attackAll非零拒绝。
- 完整100项阈值照level索引，有序升级法术照原RoleId取五列之一；保留零行、重复和顺序。role5没有第六列，明确拒绝本投影的成长来源，而非声称Core不支持role5。level0/threshold[0]=0不因此拒绝；其他成长/最大HP域沿用 #196。已知/升级法术仅保留ID、不承诺施法；非零ID仍须有可证明的OBJECT magic view并指向存在的MAGIC行（undetermined view不能在本入口冒充已证明magic）。
- coveredBy按原角色身份保留，包括0=role0；player仍保留该身份，不以0映射null或party索引，实际掩护由正式combat按队伍成员/HP/状态判断。不向文档未声明的自然快照推导无毒或装备闭包。
- ENEMYTEAM存的是OBJECT enemy view编号，再由enemyId查ENEMY；FFFF跳过而不递增compact slot，**0是真空槽、连尾部0也拒绝**，不压缩稀疏队。重复对象仍是两个战内槽；生成非零唯一battleId=compactSlot+1。收据逐个保留全部5个sourceSlot、FFFF skip以及已载objectId/enemyId/compactSlot/battleId映射。enemy attack/defense/dexterity按物理消费点解释为SHORT，不夹零、不预加等级公式；奖励WORD原样。
- enemy magic（含FFFF及rate0）、dualMove、等效物品/率等不可消费能力拒绝，不洗字段为0。FFFF魔法带不同RNG消费的空动作，不等于物理攻击。其余未被数值格式消费的M5元数据保留在输入身份中；不声称已接通偷窃、图像、音效、事件、语言、场景或自然存档闭包。非选中队伍只要求格式，不强求其能力已支持。

生成的数值先经公开 `ModernBattleContent.Parse/Validate`、再逐队经 `ValidatePhysicalBattle`；不是只通过格式就宣称可执行。数值格式仍v1，无额外来源键。数值UTF-8输出限制≤16MiB，与既有Prepare输入上限相容；不改任何Prepare/native/Main能力。

### 输出、来源收据与文件保护

- `output/numeric/battle-data.json`：numeric目录恰一张数值文档，可显式交给既有Prepare；本票不执行Prepare。
- `output/receipt.json`：独立版本1、kind=`m5-base-table-physical-projection`；snapshotKind=`independent-base-table-context-not-natural-save`。含输入信任级别、原sources/tables声明、全部modernInputs字节身份、有序roles/teams选择、enemySlots、逐输出叶字段（含显式空数组）的fieldSources及数值输出file/byteLength/sha256。来源数组轴/默认初始化/SHORT解释另附可读规则。收据不是数值输入或独立认证文件，不为自身作递归hash。

两侧及祖先拒绝link/reparse，所需14个输入路径逐个检查。文件边界用只读 `FileShare.Read` 持有全部输入句柄直至发布结束（Windows正常共享规则下阻止写/删），只投影实际读到的字节；不改输入bytes/mtime。输出新/空、两侧双向不重叠，发布前重查新/空条件；所有输出使用CreateNew，绝不覆盖、回滚或清理未知文件。所有数据/能力检查在创建输出前完成；真正I/O失败可能保留本次不完整文件，不发成功标记。不是多文件事务、断电安全或抵抗恶意并发路径替换的承诺；失败目录应保留诊断并用另一个新目录，不能自动删除后重试。

人工 Unit 固定角色[4,0]、多队顺序、EXP、二维轴、成长重复、HP0、SHORT、引用/能力拒绝与身份反例；Integration用既有CliProcess/正式Test-Range核真实argv/exit、bytes/mtime、确定性、单文档numeric目录、链接和输出保护。运行结果以本票实际收据为准；无真实资产/Godot/Prepare/Main/GPU/桌面/CI/fullM6验收。下文带票号的旧“单角色/暂无作者CLI”等叙述保留历史边界，不覆盖本节与已交付能力门。

## 可选 typed 投影来源与同次 checked 读取（#198，路线 A 首叶）

新增 `NativeBattleContent.LoadChecked(string uri)` / `CreateChecked(PalBattleContent native, string identity = "native battle content")`，返回普通 C# `CheckedNativeBattleContent`：只读 `Content: TriggerBattleContent` 和可空 `ProjectedSource: BattleProjectionSourceInfo`。旧 `Load/CreateCore` 经同一 checked 读取取 Content，不能绕过坏来源拒绝；不递归、不增加缓存、文件 graph、Core/工具依赖或运行时 JSON。原 `Write(content)` 等价于新重载 `Write(content, null)`；`Write(content, projectedSource)` 在创建 Resource 前核来源与数值一致性。非投影旧内容仍只走原数值格式校验，不额外施加 host-free 能力门。

来源是构造时深复制的普通 C# 值，全部公开集合使用不可改写的 read-only 包装，行 `BattleProjectionModernInput` / `BattleProjectionEnemySlot` 为不可变 record 值；不持 Godot Resource alias、不持跨 Main 活状态。来源构造即校结构，读写再对同次 Content 校选择一致性并逐队复用 `ModernBattleContent.ValidatePhysicalBattle`，不是新战斗校验引擎。

### 原生精确布局（PalBattleContent 根格式仍 1）

| 导出字段 | 未选择时的唯一默认 | 选中语义 |
| --- | --- | --- |
| `ProjectionPresent` | false | true，显式选择投影来源 |
| `ProjectionFormatVersion` | -1 | 1 |
| `ProjectionKind` / `ProjectionSnapshotKind` | 空字符串 | `m5-base-table-physical-projection` / `independent-base-table-context-not-natural-save` |
| `ProjectionReceiptSha256` / `ProjectionNumericSha256` | 空字符串 | 分别是完整原收据、原 numeric 的64位小写十六进制字节 SHA256 |
| `ProjectionReceiptByteLength` / `ProjectionNumericByteLength` | -1（long） | 正 Int64 / 1..16MiB；收据长度只运输，不据其分配内存或虚构解析上限 |
| `ProjectionInputFiles` / `ProjectionInputSha256` / `ProjectionInputByteLengths` | 空数组 | 三个对齐的 string[] / string[] / long[]，恰14项；每项 SHA 同上，长度1..64MiB |
| `ProjectionRoleIds` / `ProjectionTeamIds` | 空 int[] | 1..2个唯一角色0..4，与 players 顺序、roles/growth覆盖一致；非空唯一 WORD 队伍，与 native teams身份/顺序一致 |
| `ProjectionEnemySlots` | 空 int[] | 每队5行，每行7词：teamId、sourceSlot、objectId、enemyId、compactSlot、battleId、disposition；0=skip-FFFF，1=loaded |

`ProjectionPresent=false` 时任何非默认字段（包括 null 字符串/数组）都拒绝，不能静默忽略半声明。true 时缺失、未知版本、kind/snapshot、错误身份/形状一律 InvalidDataException；native 读取错误保留选中 URI/identity。所有来源字段缺席的旧 v1 资源依上述默认合法读为 null，无需补收据或迁移 M3/M4。

14项文件名必须恰为 ordinal 顺序：battle-effect-index.json、battle-field.json、enemy-pos.json、enemy-team.json、enemy.json、event-object.json、index.json、level-up-exp.json、level-up-magic.json、magic.json、object.json、player-roles.json、scene.json、store.json。禁止路径、大小写别名、重复、未知、遗漏或乱序；runtime不打开这些文件。

槽行按 TeamIds 顺序、sourceSlot0..4完整排列。skip-FFFF 仅 objectId65535，三个 loaded 字段平铺为 -1（C# 值为 null，绝不当业务0）。loaded 的 objectId1..65534、enemyId0..65535，compactSlot从0连续、battleId=compactSlot+1，并与对应 native enemy.Id顺序/数量一致；每队1..2个loaded，其余FFFF跳过。objectId0不是FFFF，拒绝；重复objectId/enemyId仍是不同槽，不去重。全FFFF、未知disposition、半行、越域、错队/错槽/错ID均拒绝。角色4可单独合法选择，不强迫role0；后续Story最小域不在本叶新增。

InputTrust由版本/kind固定为只读常量 `caller-verified-upstream; declarations-and-byte-identities-not-independent-authentication`，不另持可改任意文本。来源结构只证明声明内部与同次数字选择一致，**不认证 receipt→numeric→M5表或原档真实性**；两类输入字节 SHA 不与 native 文件 SHA 混比。完整 fieldSources/declaredSources/declaredTables只由 receipt 身份供后续离线原件追溯，不运进runtime解释。

这只是批准路线A的 typed 首叶；后续 #199 离线 receipt 解析见下节，**仍未接通数值Prepare可选投影入口、Story期望声明或正常Main来源闭环**，不关闭 #134，不改变上文 #133 信任A、原格式/能力门、#168装备WORD运输缺口或任何旧失败。人工native保存/重载、不可变和拒绝用例及完整native-map的实际结果以本轮冻结收据为准，不签Prepare/Main/私有资源/GPU/桌面/CI/完整M6。

## 显式投影数值 Prepare（#200，路线 A 第三叶）

新增 `PrepareBattleData.Prepare(string inputDirectory, string outputDirectory, string projectionReceiptPath)`；两参数入口保留且明确不选择投影模式，不发现 sibling receipt。三参数选中后 null/空白/不存在/目录/链接及祖先 reparse 拒绝，不退回非投影。receipt 必须位于 numeric 输入目录和 native 输出目录之外；numeric 仍恰含 `battle-data.json`。receipt 不复制到输出或成为运行时 JSON 依赖；原始离线原件按其字节身份供追溯。

同次持有 numeric+receipt 的 `FileAccess.Read/FileShare.Read` 句柄，调用 #199 `ProjectedBattleSource.Parse` 和 #198 `Write(content, source)`；在解析后、writer后、创建目录后、Save后、checked Load/readback后，逐阶段复核双源路径/原字节SHA和输出集合，保持句柄到无覆盖 `File.Move` 完成。checked readback 必须完整保留 typed source 的版本/kind/snapshot、双身份、14输入、有序角色/队伍及全部源槽。失败沿原 owned 临时文件/新空目录清理，不递归删除他者文件。numeric 保持1..16MiB；receipt格式长度仍为正Int64，当前byte-array parser文件适配器仅支持1..Int32.MaxValue字节，在分配前明确拒绝更大文件，不截断或添加较低格式上限。晚期替换/磁盘/rename/cleanup竞争无确定性故障注入证明，仅有逐阶段复核与代码审阅边界。

Editor 保留原四个 user argv，可仅在末尾追加唯一 `--projection-receipt <file>`；错序/重复/空值/缺值/未知仍 exit1，无成功marker。PowerShell wrapper新增可选唯一 `-ProjectionReceiptPath`，预检存在性/边界/reparse后追加该argv；非法参数仍exit2，原成功marker/exit0、诊断/exit1不变。例如：

```powershell
pwsh -NoProfile -File ./scripts/Prepare-BattleData.ps1 `
  -InputPath C:/offline/projected/numeric `
  -OutputPath ./src/Pal.Game/Assets/LocalProjectedNumbers `
  -ProjectionReceiptPath C:/offline/projected/receipt.json `
  -GodotPath $ApprovedGodotConsole
```

独立人工fixture为 `tests/Fixtures/ProjectedBattleData/{numeric/battle-data.json,receipt.json}`，不是正式CLI输出、真实派生资源或原档认证。字面角色[4,0]、队伍[65535,0]、同对象97重复实例/完整10槽、14输入及503字段叶覆盖，固定fixture SHA；BOM/空白装饰用独立.NET SHA核原字节，经正式Prepare→LoadChecked。新增27个Game direct与12个Prepare process覆盖身份/覆盖/角色拒绝、路径/锁定源、无覆盖、不修改源、不自动发现及必要真实argv/退出；登记不等于实测，实际结果绑定本轮收据。

**Story `battleProjection` 期望与checked native配对、正式人工M5→CLI→Prepare→正常Main的同一RoleLiveState/所属ACK/胜负/返回/再次使用仍未交付。** 本叶不改Core/工具/native v1或M3/M4非投影入口，不关闭#134，不增加工程叶18/story0/fullM6false或私有/GPU/桌面/CI证据。上下文中#198/#199的“未接Prepare”记录各原叶边界，由本节新增能力接续，不转签旧运行证据。

## 离线 receipt 与原 numeric 字节核对（#199，路线 A 第二叶）

`Pal.Game.Editor.ProjectedBattleSource.Parse(ReadOnlyMemory<byte> receiptBytes, ReadOnlyMemory<byte> numericBytes)` 返回 `(TriggerBattleContent Content, BattleProjectionSourceInfo Source)`。这是离线纯内存边界，不读文件、不创建 Godot 对象或会话、不引用 Pal.Extract，不重新投影基表。实际 numeric 严格 UTF-8（可选 BOM）解码后仅调用一次 `ModernBattleContent.Parse`；来源与同次 Content 的选择/完整槽/能力一致性直接复用 #198。numeric 沿既有 Core typed defensive snapshot 契约，Source 构造时深冻结；两者均不保留输入 buffer alias，返回后改写原数组不能污染结果。本叶不改变 Core `PanelDeclaredRoles` 等既有集合的具体存储或承诺全部 Core 集合无法向下转型修改。

- numeric 原字节必须1..16MiB；receipt 是调用者已拥有的内存，不新增任意文件/分配大小上限。两者 JSON 深度保持64，严格拒绝非法 UTF-8、注释、尾逗号和递归重复键（包括注释对象内）；属性名与全部字符串也显式完成解码，坏 surrogate 配对以带路径的 `InvalidDataException` 拒绝。不接受将小数/指数记法当整数；整数按字段 Int32/Int64 范围读取。
- receipt 根恰含 `formatVersion/kind/snapshotKind/inputTrust/declaredSources/declaredTables/modernInputs/roles/teams/enemySlots/fieldSources/output/limits`。前三项及 inputTrust 与 #198 固定值一致，limits 必须非空文字但不是认证规则。其余普通 receipt 对象不忽略未知键。仅复制自 M5 的 declaredSources/declaredTables 行保留上游 `_` 注释扩展，注释内重复键仍拒绝。
- declaredSources 恰为唯一 SSS.MKF/DATA.MKF 的 archive/sha256；原档声明 hash 可含大写十六进制，与上游兼容。declaredTables 恰13张唯一 file/archive/chunk/recordCount/byteLength/sha256，固定布局依 `data-schema` 的 Windows 宽度（OBJECT14，PLAYERROLES900 等）；recordCount1..65536、四张单记录表恰1、长度=条数×宽度，OBJECT长度兼容12/14的歧义仍拒绝。选中 team、loaded object/enemy ID 必须在对应声明 recordCount 内；这是声明内部一致性，未读取表文件认证记录数或投影值。
- modernInputs 的精确14项 ordinal 顺序/小写SHA/1..64MiB、有序角色与队伍、每队五源槽以及 loaded/skip 字段 presence、同次数值选择与物理能力均沿 #198，不能默默补齐、排序或去重。skip 行不得携带 loaded 字段（即使是 null/-1）。
- output 恰为 file=`numeric/battle-data.json`、byteLength、sha256；必须与**实际 numeric 原字节**长度/小写 SHA256 一致，不比较重序列化 JSON。BOM/空白/换行改变即不同身份；等长的语义等价空白改写仍因 SHA 不符拒绝。Source 的 receipt SHA/长度直接来自完整 receipt 原字节，不递归寻找其自身 hash。
- fieldSources 每行恰含非空 path/source，一次且仅一次覆盖实际 numeric 文档的全部叶（显式空数组也算叶）；使用 `roles[0].hp` 等无根前缀路径。缺失/额外/重复均拒绝。允许的 numeric 注释键若因点/方括号产生同一叶路径，明确拒绝 ambiguous path，不静默折叠两叶。这里只校覆盖与字符串，**不解析来源文字或复制投影算法**，不声称原档认证。

所有结构、字节、声明/选择/能力拒绝保留字段上下文的 `InvalidDataException`。本叶不改变原 `PrepareBattleData.Prepare`、wrapper/Editor argv、Story/native/Main 的运行入口或旧非投影兼容；receipt 不成为运行时 JSON。后续叶必须稳定持有 receipt+numeric 句柄、使用本 Parse 与 `Write(content, source)`，经严格 checked Load/readback 和既有无覆盖/owned cleanup 发布，再单独交付 Story 期望及正式 Main 闭环。本叶及其人工 native-map 测试不增加工程叶/故事、不关闭 #134，不替代私有资源/GPU/桌面/CI/完整 M6 验收。

## 显式角色名字对象声明与 JSON/native 运输（#158 / #159）

`ClassicBattlePlayer.NameObjectId` 是非位置构造参数的可选 `int?` 声明，默认 null 表示旧内容未声明映射；0 与 65535 都是合法对象编号，不把0当作缺省哨兵。参考 `fight.c` 的濒死/友亡检查，角色以名字编号选择全局对象，不能用 RoleId、队伍位置或相等的脚本入口反推对象身份。

公开纯格式验证 `ModernBattleContent.Validate` 要求一份内容的全部 player 同时声明映射或全部省略；已声明值必须为 WORD。相同名字对象必须声明完全相同的两个初始脚本入口（包括0），任一槽冲突即带对应 player/字段路径拒绝；不同名字对象即使两个入口相同也保持不同身份。现有防御复制、单角色兼容投影与 record-with 保留该可空值。原有未声明映射的构造和验证不变。

JSON player 可选精确字段 `nameObjectId`：缺席才表示 legacy null，字段出现时必须为非 null 的 WORD 整数，拒绝布尔、字符串、小数、`1.0`、负数、越界、对象及数组。既有 Ordinal 未知键与递归重复键规则不变，`NameObjectId` 不是此字段的别名。JSON 与 native 运输在同一 #159 交付中接通；不能把内部 Parse 已接受、native 仍 fail-closed 的检查点当成交付。旧 v1 JSON 省略字段仍兼容，但不保证旧严格 JSON reader 接受新字段。

原生 `PalBattlePlayer` 以导出 `HasNameObjectId=false` 和 `NameObjectId=0` 表达可空值。`Write` 先执行完整纯格式验证，再写出 has/value 和两个 hook WORD；原 #158 的 unsupported-transport guard 仅在读写两端同时接通后移除。`CreateCore` 在忽略未声明值之前显式拒绝 false+nonzero；true 时的 WORD 范围、全员映射及同名字两槽种子冲突复用原 Core 验证，错误仍带选中 identity 和字段信息。`CreateCore/Load` 返回独立防御复制，不保留可变 Resource alias。原生格式仍为1；两项新导出均缺席的旧 player 资源依默认 false/0 读为 null，保留既有 hook WORD，不推断或迁移真实名字编号。

**这里仅运输被动元数据，不开放运行时 owner。** 实现与用例本身不替代 #159 实际 native/Prepare 执行、整票独立复核及本地集成收据。

本声明验证不读写 world，不建立活脚本表，不改变钩子检测/完成或保存返回入口；live owner、0090、跨战斗/新执行器共享和真正 root-return 写回仍需后续前置与 #129 单独交付。人工声明不等于原版真实数据绑定，Core Unit 也不等于 native、Prepare、Main 或完整 M6 验收。

## 根与严格读取

根对象全部必填：`formatVersion: 1`、`roles`、`players`、`growth`、`teams`。不接受 profile/sourceKind/人工 ID 来跳过规则；未来 package/native 外壳另行诚实记录来源和图像/事件绑定。

- JSON 不允许注释、尾逗号、重复键（即使值相同）、非有限数字。每层仅接受下表字段；下划线开头的注释字段可忽略，但其中的重复键仍拒绝。键与七项 hidden 名称按 Ordinal 大小写精确匹配。
- `WORD` 是整数 `0..65535`，不接受字符串、布尔、null、小数或 `1.0` 记法；signed WORD 为 `-32768..32767`。bool 只接受 JSON true/false。
- 除明确标注可选的字段/section（包括 player `nameObjectId`）外，下述字段都必填；player cover **值**允许 null，但 cover 字段本身不能缺失。`nameObjectId` 出现时不允许 null。零、空列表也须显式声明，不能因缺数据就推断没有装备、钩子或成长。
- 结构/范围错误为 `InvalidDataException`，带 `battle-data.json.$` 及对象/字段路径（例如 `teams[1].teamId`）；复用的递归重复键诊断带父字段路径及重复键名，数组内该诊断不带索引。语法错误保留原 `JsonException` 为 inner exception。

`roles`、`players`、`growth` 和每队的 `enemies` 都保留数组 shape；当前到单角色 typed content 的 binding **明确仅接受恰好一项**，空项、多项（包括重复角色/战内 target ID）都拒绝，不挑第一项、不默默丢弃。`teams` 可多项且非空，teamId 必须唯一。未来 #47 可沿数组扩展 typed binding 与能力，不需要改成另一套格式；**当前未实现多角色/多敌引擎**。

## Role：复用 BattleRoleSnapshot

| 字段 | 类型/约束 |
|---|---|
| `roleId` | 整数0..5 |
| `level` | WORD（是否<=99属于当前能力预检） |
| `hp`, `maxHp`, `mp`, `maxMp` | WORD；maxHp>0，hp<=maxHp，mp<=maxMp；hp允许0供正式开场规则处理 |
| `primaryExperience` | `{value, level, count}`，三项均WORD |
| `hiddenExperience` | 恰好七个正式键：`health`, `magic`, `attack`, `magicPower`, `defense`, `dexterity`, `flee`，每项同上XP对象；可附_注释 |
| `equipment` | 恰好6项WORD |
| `magics` | 恰好32项WORD槽位（不是唯一ID注册表） |
| `statuses` | 恰好9项WORD |
| `coveredByRoleId` | 整数0..5，保留原角色编号；0仍可指角色0，不是通用的无cover sentinel。当前单角色零cover支持域只接受存储值0；实际能否掩护还取决于队伍和战斗规则，后续由 #48 扩展 |

先完成本 reader 的严格校验，再复用 `BattleModels.Role` 转换；不调用或放松真实 frozen profile validator，不增加平行 role DTO。

## Player：复用 ClassicBattlePlayer

| 字段 | 类型/约束 |
|---|---|
| `roleId`, `hp`, `maxHp` | 与role相同且三者必须一致 |
| `attack`, `defense`, `dexterity` | WORD，基表面板；声明装备闭包时有效面板由 `BattleEquipmentState` 按正式规则另行投影（本切片不接入会话执行） |
| `equipment`, `statuses` | 分别恰好6、9项WORD，显式能力输入，不从role悄悄推断 |
| `poisons` | 显式WORD数组；既有typed模型未定义固定毒槽宽，本格式不臆造宽度 |
| `dyingScript`, `friendDeathScript` | WORD |
| `nameObjectId` | 可选非 null WORD；省略表示未声明，所有 player 必须同时声明或同时省略；同名字对象的两个 hook 种子分别相等 |
| `coveredByRoleId` | null或整数0..5；combat模型以null表示没有实际cover，不能将snapshot存储的角色编号0直接当作该null。当前能力预检只接受此字段为null |
| `attackAll` | bool |

## Growth：复用 ClassicBattleGrowthData

每项 `{roleId, levelUpExperience, levelUpMagics}`：roleId为0..5且与role/player一致；`levelUpExperience` 为完整100项按level索引的WORD数组，**格式允许0**；`levelUpMagics` 为本role完整有序 `{level: WORD, magicId: WORD}` 行数组，允许空表。零行、已知法术、重复行及顺序均保留，不是ID注册表，不排序去重。包作者须提供完整表，单份数值JSON本身不能证明来源完整性。

正阈值、primary按role等级/hidden按自身等级低于阈值、没有待新增法术等，均由正式零成长能力预检证明；不能把本阶段保守能力限制永远写成数据格式规则。

## Team / Enemy：复用 ClassicBattleEnemy

每队 `{teamId: WORD, enemies: [...]}`。enemy全部必填：

| 字段 | 类型/约束 |
|---|---|
| `id` | 非零WORD，**战内target ID**，不是原始敌定义或图片ID；无需与teamId相等 |
| `level`, `hp` | WORD，hp>0 |
| `attack`, `defense`, `dexterity` | signed WORD，原表offset，非有效面板 |
| `physicalResistance` | WORD |
| `statuses`, `poisons` | 9项WORD状态、显式WORD毒数组 |
| `turnStartScript`, `readyScript`, `battleEndScript` | WORD |
| `magicId`, `dualMove`, `attackEquivItem`, `attackEquivItemRate` | WORD |
| `experience`, `cash` | WORD |

战内ID在一场内唯一；当前一场仅一个敌人，所以任何多项都明确拒绝。不同队可使用相同战内ID；未来同原enemy97出现两次时，也不暗含两者必须共享一个target ID。原始定义身份/图片绑定由未来native包决定，此纯数值格式不臆造它们。

## 无事件宿主的纯物理战能力门（#196 / #133 前置）

`ModernBattleContent.ValidatePhysicalBattle(TriggerBattleContent content, int enemyTeamId)` 是供离线投影等**无事件宿主**调用方使用的公开纯数值预检，成功返回 void，格式、选择或能力错误抛 `InvalidDataException`。现有 `Parse`、`Validate`、trigger `Start` 和 native/Prepare 的行为与 v1 格式均不改变，也不自动改用本门。

- 先调用既有 `Validate` 检查**整份内容**的数值格式；再核 teamId 是 WORD 且实际存在。未选中队伍的合法非零能力原样保留，畸形字段仍拒绝。
- 对选中队伍复用 `TriggerBattleContent.Validate` 的真实 lifecycle/combat 能力门与纯装备初始化：当前为1–2角色、密集1–2敌位；角色按 RoleId 关联，party/敌位顺序不改变。不构造事件、语言、trigger/lifecycle/combat 会话，不抽随机数、不模拟获胜，也不修改输入。临时装备寄存器仅是校验所需的局部投影，不发布活状态。
- 入战 HP0→1 只参与已有校验，原输入HP0不变（参考 `battle.c` 的 `PAL_StartBattle`）；角色 maxHP≤32767 的后续战斗限制、完整 EXP/成长域、已有装备闭包、状态/毒、attack-all、敌人 magic/dualMove/等价物品等拒绝条件继续生效。level0/threshold[0]=0、已知法术及有序重复升级法术行不因此被清零或拒绝。敌人 magicId=65535 仍拒绝，不能洗成0：固定 `fight.c` 的FFFF是带不同随机消费的空动作，不是物理攻击。
- 此门没有事件集合来证明钩子闭包，因此所选所有 player 的 `dyingScript/friendDeathScript` 与所选所有敌位的 `battleEndScript` 必须为0；错误指出 party 索引或 `teams[teamId].enemies[slot]` 字段。既有 turnStart/ready 拒绝仍复用 combat 门，其错误加选中 team 上下文。这不是撤销有能力 trigger 宿主的角色/战后钩子支持，更不能通过清零钩子来通过预检。
- 声明完整且当前可消费的装备闭包可以通过；所选角色的 sprite override 或非零装备 attackAll/cooperativeMagic 仍拒绝，非参战角色的合法值不混入所选角色。**本门不能认证装备脚本来源**；#133 的投影器没有事件输入，不能凭本门接受人工装备闭包就伪造M5装备效果。

#196交付时，M5版本/profile/结构/引用、实际普通战支持域、来源声明与现代输入字节身份、字段映射/收据、严格四选项真实CLI和路径保护仍属#133后续职责；本次实现见上文#133专节，实际整票验收仍依独立收据。信任A是已批准的上游前提，不是此门提供的来源认证。此门本身不证明图像、事件、语言、Prepare/native/Main或自然存档闭包，不能单凭#196宣布#133完成。

公开 Core 人工用例覆盖上述正反例、非首角色/敌位钩子、选中与未选中能力区别、完整格式先行、HP0/level0/signed边界、重复校验与嵌套输入不变、装备选择域；实际本轮Unit及后续独立审查收据是验收依据，不以文档描述代替运行。既有下文带票号/单角色描述保留历史交付边界，不用它们转签本门或完整M6。

## 格式合法 ≠ 选中入口可执行

所有合法非零装备、status、毒、cover、hooks、attack-all、魔法/双动/等效物品、奖励，以及不能保证零成长的表，均原样进入 typed content。`TriggerScriptSession.Start` 对选中入口的 next/loss/flee 保守闭包调用既有纯预检，在任何caller/角色/请求副作用与RNG前拒绝不支持内容；未被选择入口引用的其他队可以保留。不会通过simulate获胜来验证内容，也不因来源为人工而放行。

`TriggerBattleContent.InitialRole / Player / Growth / Teams` 为public get-only投影。构造器继续防御拷贝全部嵌套集合，输出只读；record的with只生成独立copy，不能改content中的记录。native writer读取这些同一typed值，不另造业务factory或角色schema。

公开 Core 用例从reader输出真正 `Start` → active lifecycle `SelectAttack/ResolveNextAction` → 准确自有completion ack → 原事件display/end：独立字面值43KO后HP125/MP35、attack1/health2；真实Lost HP0/MP20不恢复。另覆盖严格字段/维数/类型/范围/ID、全部现有能力字段不丢失、0阈值及重复成长行保留、选中入口无RNG拒绝和嵌套只读。它们本身不证明native准备、文件安全、Main输入/渲染、GPU、人工操作或CI。


## Typed 格式验证与 native Resource v1（#46 codec 检查点）

`ModernBattleContent.Validate(TriggerBattleContent)` 是公开、纯数值格式验证，与上述 JSON 数值/维数规则一致。Parse 在严格 JSON 读取后调用；native CreateCore 也调用。它允许合法非零能力、WORD 等级、0 阈值和重复有序成长行，不调用 frozen 真实 profile gate、selected-entry 能力闭包、RNG 或模拟战斗。既有 content 构造与 `Start` 的错误/副作用时机不改。

Game 正式 API 位于 `Pal.Game.Profiles.NativeBattleContent`：

- `Write(TriggerBattleContent)` 先格式验证，再创建独立 `PalBattleContent` 资源；不写文件、不准备完整包。
- `CreateCore(PalBattleContent, string identity = "native battle content")` 严格检查根版本、数组 shape、nested null、growth role identity、重复 team ID、canonical cover 及 canonical Name has/value，再复制字段并做 Core 格式验证。输出的所有 nested 集合独立且只读，不保留可变 Godot Resource alias。
- `Load(string uri)` 仅接受规范 `res://` URI，要求存在且类型为 `PalBattleContent`，用 `ResourceLoader.CacheMode.IgnoreDeep` 后走同一个 CreateCore。错误带选中 URI。它不读 `battle-data.json`、M1、原档或 MKF，也不解释 JSON blob。

根 `[GlobalClass] PalBattleContent` 的导出字段是显式存储的 `FormatVersion=1`（资源默认值为 invalid `-1`，正式 Write 赋1；ResourceSaver 必须写出版本，缺失版本不得继承为 v1）、`Roles: PalBattleRole[]`、`Players: PalBattlePlayer[]`、`Growth: PalBattleGrowth[]`、`Teams: PalBattleTeam[]`。前三组及每队 `Enemies: PalBattleEnemy[]` 当前必须恰好一项，team 非空且 ID 唯一；保留数组形式不代表 #47 多人能力已实现。Role/primary/hidden XP 复用 #45 的原生类型与最小共享 `NativeBattleRoleAdapter`；共享 adapter 只转换/检查必要 null，不把本格式/单角色/能力限制施加到旧 frozen envelope。

新增 player/enemy 资源逐项导出 Core 的全部字段（名字对应 PascalCase；enemy 的 attack/defense/dexterity 仍为 signed offset）。`PalBattleGrowth` 显式导出 `RoleId`、100 项 `LevelUpExperience`、完整有序 `PalBattleLearnableMagic[] LevelUpMagics`（`Level/MagicId`）；不排序去重。player 的 nullable cover 用 `HasCoveredByRoleId` 加整数 `CoveredByRoleId`：false 时值必须为0，true 时0..5；true+0 是实际 role0，不等同 null。role snapshot 的 CoveredByRoleId 继续保留原编号，不当作无 cover sentinel。player 的 nullable Name 同样用 `HasNameObjectId/NameObjectId` 表示：false+0 是未声明，true+0 是真实名字对象0；false+nonzero 必须拒绝，true 的 WORD/共享种子规则见上节。

数值资源没有 profile/source/人工开关、来源认证或 storyprofile hash。本 codec 不能证明源数据完整性、图像/语言/事件闭包或资源来源。显式离线数值生产准备入口现已追加，契约见下节；Main 从不自动 prepare，也没有新增 CLI 作者命令。

本检查点的 headless native-map 用例真实执行：内存现代 JSON Parse → 正式 Write → ResourceSaver 保存 → ResourceLoader/严格 Load → 正式 trigger/lifecycle 的真实 Won/Lost 与一次性自有 receipt ack。独立字面预期 Won HP125/MP35、Lost HP0/MP20，attack1/health2；并核所有非零字段、signed/WORD 边界、0 阈值/重复成长行、未引用非零队保留、nested 隔离、URI/版本/类型/null/shape/ID/范围及选中不支持内容无 RNG/请求副作用。本次修补精确运行新 native class 43 例（含真实保存文件删除显式版本后的拒绝，以及保留 CreateCore 输出再变更 Resource 的 nested 隔离/只读核对）；旧 frozen profile class 33 例沿用未改源码的既有精确运行证据，不冒称完整 Game/Prepare/Main、GPU、人工或 CI 验收。


## 六角色装备闭包与有效面板投影（#48 unit 1）

状态：数据/typed/Prepare 链已交付；#48 unit 2 已把执行语义接入 session/Main（DualAttack 双击、cover、有效面板、持续状态保留，见[经典战斗契约](classic-battle-session.md)），装备 battleSprite override 仍在副作用前拒绝。指定 RED 于 `6cfce2a` 真实失败（现有公开 Parse 拒绝 `roleEquipment` 未知字段），Core 15 例新增后完整 Core **831**，NativeBattleContentTests **55**（44+11，含装备 roundtrip 与 10 个 typed 边界）、公开 PrepareBattleDataTests **35**（33+2）；native-map 批次 408、库存 604、game 601，同步于 GameTestBatches/GameBatches 回归。

根对象新增两个可选 section（旧零装备文件不变合法）。

`roleEquipment`：恰 6 行 × 6 槽 WORD，即参考 `rgwEquipment[6][6]`（global.h）的六角色网格；非 party 角色行同样必须声明（参考 `PAL_UpdateEquipments` 遍历全部 `MAX_PLAYER_ROLES`）。每个 party 角色行必须与 `roles[].equipment` 逐字相等，不匹配即拒。

`equipments`：数组，每项恰 `objectId`（非零 WORD，全数组唯一）与 `effects`（恰 6 行，按 role 索引）——即该对象装备脚本（0x0017/0x001A/0x002D）对每个 role 的真实效应行。每行恰 `attack`/`defense`/`dexterity`/`battleSprite`（WORD，寄存器 SET 语义）与 `statuses`（恰 9 WORD），另可选 `magicPower`/`fleeRate`/`poisonResistance`（WORD，旧 JSON 省略时解析为0；typed 可为 null，既有 native reader 将这些旧字段的所有负数读为 null，历史行为不变；#53 依 retail stat18/21/22 装备写入加入，有效面板=基面板+寄存器和，同一 WORD 回绕规则；poisonResistance 仅入面板投影，战斗毒域仍不消费）。非零 status 仅允许 bravery/protect/haste/dualAttack（index 5..8）且值 1000..65535：>999 是参考 `PAL_RemovePlayerStatus` 判定“装备持续状态，不随普通清理清除”的边界，普通 1..999 回合值不属于装备闭包。网格非零项必须在 `equipments` 声明（缺失即拒）；已声明但未被穿戴的对象合法保留（同未引用队）。仅声明其中一个 section 亦合法（零网格+对象、或未带网格的旧内容）；无 section 时非零 `roles[].equipment` 保持格式合法但 `BattleEquipmentState.Initialize` 在任何状态前拒绝（“补闭实际装备脚本”的缺省拒绝面）。

Core 正式规则（`Pal.Core.BattleEquipmentState`，纯数据）：

- 初始化按 `PAL_UpdateEquipments`（global.c:1352）：清零全部寄存器后，按 role 外层、slot 内层逐个套用已穿戴对象效应行；寄存器 SET 不叠加，重复 Initialize 幂等，新会话从零起步不叠加旧效果。
- `AttackBonus/DefenseBonus/DexterityBonus/Effective*` 按 `PAL_GetPlayerAttackStrength/Defense/Dexterity`（global.c:1736/1800/1832，含 extra 槽位 0..6 求和；本切片 extra 恒零，留待临时效果）WORD 回绕；`EffectivePlayer` 输出战斗可消费的 `ClassicBattlePlayer` 投影（本 unit 不接入 session）。
- `BattleSprite(roleId, base)` 按 `PAL_GetPlayerBattleSprite`（global.c:1978-2008）：最后一个非零装备精灵寄存器胜出；基础精灵仍由视觉链提供，本层只暴露 override 数据。
- `StatusOverlay/ApplyStatuses` 按 `PAL_SetPlayerStatus` 好状态分支（global.c:2255）：仅取更大值、不叠加；既有 lifecycle 基础清理（<=999 清零）自然保留持续装备状态。

typed 同步：`PalBattleContent` 新增 `Equipments: PalBattleEquipment[]` 与展平 `RoleEquipment: int[]`（空=未声明，否则恰 36 WORD；Godot 导出不支持嵌套数组）；`PalBattleEquipment`/`PalBattleEquipmentEffect` 逐字段导出效应行。`Write/CreateCore` 与 JSON 同一套严格校验（形状、WORD、重复、闭包、party 行一致、status 域）；`PrepareBattleData` 无需改动即携带装备闭包，Prepare 新增两例真实走 fixture→Prepare→Load→Initialize 手算断言。选中入口对非零装备仍拒绝（本 unit 边界）：装备影响真实战斗循环留待 unit 2。


## #167 Core：装备 WORD 保真与选中战斗拒绝

状态：#166 拆分为 #167 Core 与 #168 native/Prepare 两个独立叶子。以下描述当前 #167 源码契约，不是全叶交付收据：Parse 与 selected-guard 两轮及其余四个边界 Fact 已经完整 Core **1335 GREEN**；全叶交付另以独立 Standards/Spec 双审、checkpoint 与实际集成记录为准，本页不替代这些记录。Core 反馈不证明 #168、真实私有 profile、#166/#161 或完整 M6 验收，不增加 story credit。

- **v1 严格 JSON / typed**：`formatVersion` 仍为1；`equipments[].effects[]` 新增可选、Ordinal 精确键 `attackAll` / `cooperativeMagic`，都是整数 WORD `0..65535`，不是 player 的 bool `attackAll`。缺席→null，显式整数0→0，非零值原样保留；显式 JSON null、负数、越界、非整数、布尔、字符串、对象和数组均带对应字段路径拒绝。直接 typed 值的 null 是合法未声明，非 null 值须为 WORD。此规则不改旧 `magicPower/fleeRate/poisonResistance` 的 JSON omission→0 或 native all-negative→null 行为。
- **兼容与复制**：在既有 `BattleEquipmentEffect` 上增加非位置 `int? AttackAll { get; init; }` / `int? CooperativeMagic { get; init; }`；旧八位置构造与生成的八输出 `Deconstruct` 不变，旧五参数调用仍可使用原默认值。`TriggerBattleContent` 既有 record-with 防御复制保留新标量并复制 statuses，不新增模型或改变基表/已知法术槽。
- **六角色寄存器**：`BattleEquipmentState.Initialize` 按六角色×六穿戴槽保留两套 nullable WORD 寄存器（每角色七项，index6 为 extra）；每个实际穿戴对象只使用对应 role 效应行，SET 不叠加。item0、extra 和未写寄存器均为 null，显式0不丢；已声明但未穿戴的对象不应用。重复初始化从空寄存器开始，快照不保留输入可变 alias。这些 WORD 不加入基面板或数值加成，不把 field65 当作 known magic，也不把 field4 转成已支持的全体攻击能力。
- **只读查询**：公开 `int? AttackAllWord(int roleId, int registerIndex)` 与 `int? CooperativeMagicWord(int roleId, int registerIndex)` 均为 O(1)，不暴露数组；先验证 roleId `0..5`，再验证 registerIndex `0..6`，越界抛含相应参数名和值的 `ArgumentOutOfRangeException`。
- **格式合法不代表可执行**：直接 `ClassicBattleSession`、lifecycle 与 trigger 的选中入口预检复用同一 `ClassicBattleSession.ValidatePlayer`。所选角色任一寄存器的新 WORD 非零（不限于1）即在有效面板/状态投影、公开状态/战斗请求、事件前缀副作用及 RNG 前拒绝；null/0 通过此项检查，其他未参战角色的合法非零值允许保留。既有 player 数值及能力检查顺序和原诊断不变（包括 bool `p.AttackAll`、sprite override、status）；仅在这些旧检查通过后，新增错误才指出 equipment `attackAll/cooperativeMagic` 的 role/register。原 battleSprite 与 player attack-all 拒绝边界不变，不开放全体攻击、合击或其他 M7 能力。

来源例子仅为 role2/slot3/item163 的 `attackAll=1`、`battleSprite=6`，及 role3/slot5/item260 的 `cooperativeMagic=351`；不据此宣称整个测试网格或四个新增 Fact 是 retail/natural-save。其余人工哨兵和布置只作独立保真/边界核对，不能替代真实六角色来源、前置进度或私有资源闭包。

**当前 native/Prepare 缺口**：`PalBattleEquipmentEffect` 与 `NativeBattleContent.Write/CreateCore` 尚未运输这两个新字段，`PrepareBattleData` 仍经过该旧 writer；它会丢失新值，不能把当前 Core 接受当作 native 往返保真。**在 #168 实际交付前，禁止将含这两个新字段的内容经旧 native writer 作者化或 Prepare**（包括显式0）。#168 是待 Core 叶实际验收并集成后的独立后续工作；未来计划为这两个新导出使用 -1↔null、0..65535 原值往返，其他负数或越界拒绝，但当前未实现，新的保存/加载及公开 Prepare 证据也未交付。下节既有 Prepare 契约与历史计数不为新字段补签。


## 显式离线数值准备（#46 有界 Prepare 检查点）

唯一生产转换入口：`Pal.Game.Editor.PrepareBattleData.Prepare(string inputDirectory, string outputDirectory)`。参数为物理目录路径，返回规范 `res://Assets/.../BattleContent.tres`，不执行选中事件、RNG、战斗模拟或 frozen #45 gate。只负责上文完整格式合法性；合法非零装备、hooks、reward、0 阈值及有序重复成长行原样保留，**不代表未来 selected-entry 能力闭包已满足**。

输入目录必须实际存在、位于 `src/Pal.Game` 外，且恰好一个普通顶层 `battle-data.json`；额外文件或目录（即使为空）均拒绝。本入口限制原文件 **1..16MiB**，严格 UTF-8 解码，可带 UTF-8 BOM（BOM 计入原始字节及稳定性 hash），拒绝 UTF-16/非法 UTF-8；JSON 使用同一个 `ModernBattleContent.Parse`，不补齐缺字段。两侧规范化物理路径，按平台大小写/根规则判边界，双向不得重叠。输出必须为项目 `Assets` 下新或空目录，不能是文件或已有任意 entry。两侧全部 ancestor、输入树及输出树拒绝 link/reparse point（包括悬空链接）；允许输出缺失多层 ancestor。不改变 #45 对空子目录的旧契约。

全路径/格式预检后，持续持有 `OpenStableRead` 只读 lease（Windows `FileShare.Read`），保留原始字节 hash。按阶段复核路径、精确输入/输出 entry、当前文件和 lease 的内容 hash。复用 `NativeBattleContent.Write`，`ResourceSaver` 保存唯一 owned 临时 `.tres`，由正式 `NativeBattleContent.Load` 严格实际回读，再 `File.Move(..., overwrite: false)` 原子发布固定文件。输出复核只容许本次临时文件。失败只尝试清自有临时文件和本次新建空目录，不递归删除已有目录或他者新文件；清理失败不覆盖原始异常。hash 仅用于读稳定性，不是来源认证、canonical digest 或 storyprofile receipt。

正常编辑器入口 `res://Editor/PrepareBattleData.tscn` 的 `_Ready` 只接受精确顺序 `--battle-input <dir> --battle-output <dir>`，调用同一 public Prepare，成功后输出 `PAL_PREPARE_BATTLE_DATA:BattleContent.tres` 并 exit0；失败 stderr `Battle data native import failed: <原诊断>` 并 exit1，不发成功 marker。

先按[固定 GdUnit 源码包与 bootstrap](../testing/gdunit-source-packages.md)准备仓库锁定的包；下面的 wrapper 会执行项目 build，但不负责下载或升级依赖。

```powershell
# 显式离线操作；不会由正常 Main 自动执行。OutputPath 必须新建或为空。
pwsh -NoProfile -File ./scripts/Prepare-BattleData.ps1 `
  -InputPath ./tests/Fixtures/ModernBattleData `
  -OutputPath ./src/Pal.Game/Assets/MyBattleData `
  -GodotPath $ApprovedGodotConsole
```

wrapper 负责唯一参数/路径预检、固定 Godot 版本、正常项目 build、启动上述 Editor scene、实际退出/诊断和 marker 验证，不维护第二 converter。可选 `-TimeoutSeconds` 沿既有准备工具的60秒默认、1..600范围，不修改共享预算；参数错 exit2，其余失败 exit1。实际引擎运行仍全局串行，发现已有未知 Godot 仅查 PID/Name 并暂停。

本检查点的 Game direct 新类32例调用同一 public Prepare，真正 Load 产物后经正常 lifecycle 真实KO验证 HP125/MP35；格式/UTF-8/大小/目录/覆盖/双向重叠/缺失 ancestor/Windows 大小写及 reparse 路径边界分别覆盖。Prepare process 新18例负责正常 PS/Editor argv、exit、marker 和错误传播，完整 Prepare 79例实际通过。两层共读一份可提交独立人工 numeric JSON（Core 不引入文件 I/O）；无新增业务factory或测试装配接口。晚期源替换、他者插入、磁盘/权限/rename失败及 cleanup失败没有确定性故障注入运行证明，只保留生产逐阶段复核及 owned cleanup 的代码审阅边界。它不是完整人工 storypackage、Main/SceneRunner/UI、GPU、物理操作、远端 CI 或整 #46/M4 验收。
