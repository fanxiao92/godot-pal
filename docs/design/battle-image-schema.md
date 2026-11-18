# 战斗图像包契约（battle-image-schema）v1

状态：**abc 形状已冻结并交付（#92）；ball 按同形交付（#93）；f 按同形交付（#94）；fbp 按同形交付（#95，M5 MKF 四票收官）；FIRE 导出已交付并集成（#146，本地集成 63e793e5）**。M6 的 FIRE 原生准备（#147）契约见文末末节；忠实还原以 `reference/sdlpal/` 为权威（[ADR-0001](../adr/0001-faithful-event-transcription.md)、[ADR-0004](../adr/0004-data-and-script-layers.md)）；通用 JSON 规则与 [data-schema](data-schema.md) 一致（camelCase、严格校验、只读提取）。

## 参考事实（abc.mkf）

- `battle.c` `PAL_LoadBattleSprites` 直接 `UTIL_OpenRequiredFile("abc.mkf")`，按敌人定义 `wEnemyID`（DATA chunk 1 的 `enemyId`）经 `PAL_MKFDecompressChunk` 取 chunk；读不到（空 chunk 返回 ≤0）时该敌人无精灵。`global.h`：`wEnemyID` "Also indicates the bitmap number in ABC.MKF"。
- Windows 数据（本仓库运行时基线，`PAL_IsWINVersion` 探测含 abc.mkf）中 abc 的 chunk 为 YJ2 压缩；解压结果为 PAL 精灵（WORD 偏移表 + RLE 帧），帧寻址是 `palcommon.c` `PAL_SpriteGetFrame`：帧 `f` 的字节偏移 = 表字 `f`×2，可寻址范围 `f < 首字`，比 `PAL_SpriteGetNumFrames`（首字−1，名义帧数）多一个下标。
- **末表字的两种命运（本票核对真实数据后冻结的解码规则）**：末表字＝名义帧数下标指向的「额外帧」。真实 154 chunk（153 非空）中：13 个指向真实后续帧（真实敌人的攻击段会寻址到该下标：`fight.c` 攻击循环最大帧 = idle+magic+attack−1，可等于名义帧数），134 个为 0 哨兵，6 个是指向前一帧数据内部的悬空指针。规则：先按「末表字是帧边界」整包严格解码；失败则视为悬空指针丢弃重试（只重试这一种情形，名义帧损坏仍失败）。空 chunk（真实数据 chunk 0）不产出精灵文件。
- 帧尾边界 = 后续帧起始偏移（RLE 严格消费至边界，允许 ≤1 对齐字节）；权威平面是逐像素索引与 0..255 alpha，PNG 只是可视化。

## 参考事实（ball.mkf）

- `global.c` 打开 `ball.mkf`（`fpBALL`，注释 item bitmaps）；`global.h` 物品对象 `item.wBitmap` "bitmap number in BALL.MKF"。消费点 `itemmenu.c`/`script.c`/`uigame.c`：`PAL_MKFReadChunk(bufImage, 2048, wBitmap, fpBALL)` 读出**原始 chunk 字节**后直接 `PAL_RLEBlitToSurface` 绘制。与 abc 的两点结构差异：**无 `PAL_MKFDecompressChunk`，无 YJ1/YJ2 压缩**；**无精灵 WORD 偏移表，每非空 chunk 就是整幅独立 PAL RLE 位图**（可选 `0x00000002` 文件头 + WORD 宽/高 + RLE 游程，`PAL_RLEGetWidth/GetHeight/BlitToSurface` 都按该布局）。#93 真实数据核对：252 chunk，chunk 0 空，最大 1876 字节 ≤ 参考读缓冲 2048；所有非空 chunk 均带 `0x00000002` 头，无需任何解压能力（YJ1 不涉及，不复用也不新增解压器）。
- 帧语义：无名义/额外帧之分，每非空 chunk `frameCount = decodedFrameCount = 1`；不存在 abc 的末表字双重命运与悬空指针问题，失败模式就是位图自身损坏（尺寸为 0/截断、游程越界、末尾多余数据）。
- 名称勘误：票面「法术效果」按参考事实为**物品位图**（道具栏、商店、剧情对话框展示的物品图，`item.wBitmap` 索引）；战斗内法术效果动画另经 `fight.c` 从 FIRE.MKF 解压（`fpFIRE`），不在本包范围。
- `compression`：非空 chunk 标 `"rle"`（原样存储的 RLE 位图），空 chunk `"empty"`；精灵文档 `source.encoding` = `"uncompressed PAL RLE bitmap"`，`frameLayout` = 单幅 RLE 位图布局。
- PNG 着色与 abc 同源：`pat.mkf` chunk 0 日间色，仅作可视化。

## 参考事实（f.mkf）

- **消费点与语义（战斗图/帧表）**：`global.c:179` 打开 `f.mkf`（`fpF`，注释 "player sprites during battle"）。两类消费都走 `PAL_MKFDecompressChunk`：
  - 玩家战斗精灵：`battle.c` `PAL_LoadBattleSprites` 按各队员 `PAL_GetPlayerBattleSprite(wPlayerRole)` 取 chunk（`global.c:1978`：基值 = DATA 表 PlayerRoles 的 `rgwSpriteNumInBattle`（#86/#87 已导出为 player-roles 的 `battleSprite`，真实数据 [0,1,2,3,4,8]），装备可覆盖为 `rgEquipmentEffect[kBodyPartExtra].rgwSpriteNumInBattle`（`script.c:1433`））。帧寻址用固定姿态下标：`fight.c` 把玩家 `wCurrentFrame` 置为 0（待机/傀儡）、1（睡眠/濒死）、2（倒地）、3（防御）、5（施法预备）、6（施法动作）、7（攻击前搝）、8（攻击逼近）、9（攻击出手），即帧表按姿态分段而非 abc 的 idle+magic+attack 连续段。`battle-effect-index`（DATA 11）的行下标正是该 sprite 号（#91 已核实）。
  - 召唤神精灵：`fight.c:3135-3140` 按 `wSummonEffect + 10` 取 chunk（魔法表 `rgSpecific` 的召唤视图，#88 已导出，真实值 0..8 → chunk 10..18），动画循环 `iSummonFrame < PAL_SpriteGetNumFrames - 1`（播放到倒数第二帧）。
- **压缩与帧表布局与 abc 完全同构**：Windows 数据（本仓库运行时基线；`PAL_IsWINVersion` 的六个探测档之一，`global.c:55`）中 f 的 chunk 为 YJ2 压缩（首 DWORD = 解压长度），解压结果为 PAL 精灵（WORD 偏移表 + RLE 帧，`PAL_SpriteGetFrame` 同一寻址）；DOS 形态才会是 YJ_1（探测逻辑即检查首四字节），不在本仓库基线内。#94 真实数据核对：19 chunk 全部非空（无空 chunk，与 abc/ball 的 chunk 0 空不同）、全部 YJ2，19 个末表字**全部是 0 哨兵**——0 个真实额外帧、0 个悬空指针，`decodedFrameCount == frameCount` 对全部 chunk 成立；abc 的末表字双重命运规则原样保留（结构上允许，本档真实数据未出现）。
- `compression`：非空 chunk 标 `"yj2"`，空 chunk `"empty"`（真实数据无空 chunk，夹具仍保留该路径）；精灵文档 `source.encoding` = `"Windows YJ2"`，`frameLayout` = PAL 精灵 WORD 偏移表 + RLE 帧（与 abc 相同，`archive` 字段 = `f.mkf`）。
- PNG 着色与 abc 同源：`pat.mkf` chunk 0 日间色，仅作可视化。

## 参考事实（fbp.mkf）

- **消费点与语义（战场背景 + UI/剧情画面）**：`global.c:175` 打开 `fbp.mkf`（`fpFBP`，注释 "battlefield background images"）。战斗背景：`battle.c:949` `PAL_LoadBattleBackground` 用 `PAL_MKFDecompressChunk(buf, 320*200, gpGlobals->wNumBattleField, fpFBP)` 读入——**chunk 下标 = wNumBattleField**（`script.c` opcode `0x004A` 设置；DATA chunk 5 战场表 58 行（#89 已导出 battle-field）对应 chunk 0..57）。同档还供非战斗画面：`uigame.c` 状态栏背景 chunk 0、装备菜单 chunk 1、主菜单 WIN95 chunk 2；`main.c` 启动画面 chunk 3/4（WIN95）；`ending.c` `PAL_ShowFBP`/`PAL_ScrollFBP` 结局画面 chunk 59..77（WIN95 上下半 69/70）。
- **压缩与解码结构**：消费统一走 `PAL_MKFDecompressChunk`（WIN 基线 = YJ2；fbp 是 `PAL_IsWINVersion` 六探测档之一 `global.c:55`，DOS 形态 YJ_1 拒绝，同 abc/f）；但解压结果**不是精灵也不是 RLE 位图**：`palcommon.c` `PAL_FBPBlitToSurface` 把 64000 字节线性拷贝到 320×200 8-bit 表面，即每非空 chunk 的 YJ2 载荷必须解压为**恰 64000 个调色板索引**（整幅屏幕，行主序，无 WORD 偏移表、无游程、无尺寸头），全不透明（index 0 是颜色不是透明键）。参考以固定 320×200 缓冲调用，声明长度超限在解压器层失败，不为部分/超长帧放宽。
- **帧语义**：无名义/额外帧，每非空 chunk `frameCount = decodedFrameCount = 1`，唯一帧 320×200、`sourceOffset` 0、alpha 全 255；失败模式就是位图非屏幕（解压长度 ≠ 64000）或 YJ2 自身损坏。#95 真实数据核对：78 chunk，chunk 5、58 空（`PAL_MKFDecompressChunk` 返回 ≤0，消费端不检查，导出标 `"empty"`）；其余 76 chunk 全部 YJ2 且解压恰 64000 字节。
- `compression`：非空 chunk 标 `"yj2"`（容器压缩与 abc/f 同），空 chunk `"empty"`；精灵文档 `source.encoding` = `"Windows YJ2"`，`frameLayout` = 单幅原始 320×200 索引位图、线性行主序。
- PNG 着色与 abc 同源：`pat.mkf` chunk 0 日间色，仅作可视化。

## 范围与边界

- 只读提取：`abc.mkf`（+ `pat.mkf` 供 PNG 着色）→ 完整图像包；无重编码、无 rebuild。`ball.mkf` 同理（`ball.mkf` + `pat.mkf`），`f.mkf` 同理（`f.mkf` + `pat.mkf`），`fbp.mkf` 同理（`fbp.mkf` + `pat.mkf`）；FIRE 候选同理（`fire.mkf` + `pat.mkf`）。
- 输出目录必须新建或为空、不在输入内、拒绝链接（与其他 export 一致）；清单最后发布。
- PNG 着色固定用 `pat.mkf` chunk 0 日间色，仅作可视化；原版战斗实际按当前场景调色板绘制（`battle.c` `PAL_FadeIn(wNumPalette, ...)`），消费方应以索引平面 + 运行时调色板着色。
- 资源上限：单 chunk 原始 ≤8 MiB、解压 ≤8 MiB；整包解压合计 ≤256 MiB、像素合计 ≤128M（真实 abc：解压约 1.9 MB、像素约 6.4M；真实 ball 无解压，chunk 原始字节即解压字节，合计约 0.14 MB；真实 f：解压合计 329,624 B、像素约 1.32M，最大单 chunk 解压 58,116 B；真实 fbp：解压合计 4,864,000 B = 76 屏 × 64000，像素合计同 4.87M，最大单 chunk 解压恒 64,000 B）。

## CLI 动词（四票冻结形状与 #146 FIRE 候选）

```
pal-extract export-battle-images --input <目录> --archive <abc|ball|f|fbp|fire> --output <新建或空目录>
```

- `--archive` 接受 `abc`（#92）、`ball`（#93）、`f`（#94）、`fbp`（#95）与候选新增的 `fire`（#146）；其他值按参数错误退出 2。
- 必需归档按 profile 声明：abc → `abc.mkf`、`pat.mkf`；ball → `ball.mkf`、`pat.mkf`；f → `f.mkf`、`pat.mkf`；fbp → `fbp.mkf`、`pat.mkf`；fire → `fire.mkf`、`pat.mkf`（大小写不敏感，唯一匹配）。

## 输出布局

```text
<output>/
  index.json                 清单：版本、来源、调色板、chunk 清单、总计
  sprites/<chunk>.json       每 chunk 精灵文档（权威索引/alpha 平面）
  sprites/<chunk>/<frame>.png 逐帧 PNG（pat 0 日间色着色）
```

### index.json 字段（v1）

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` | 整数 `1` |
| `profile` | `"faithful"` |
| `command` | `"export-battle-images"` |
| `parameters.archive` | `"abc"`、`"ball"`、`"f"`、`"fbp"` 或候选新增的 `"fire"` |
| `sources` | 数组：`name`（按 profile：abc.mkf/ball.mkf/f.mkf/fbp.mkf/fire.mkf 与 pat.mkf）、`byteLength`、`sha256`（整档） |
| `palette` | `archive`/`chunk`/`lighting` 与 `usage` 说明（PNG 着色专用，权威平面在精灵文档） |
| `chunks` | 每 chunk 一项，按 index 升序：`index`、`byteLength`（原始 chunk 字节）、`sha256`（原始 chunk）、`compression`（abc/f/fbp/fire：`"yj2"`；ball：`"rle"`；空 chunk `"empty"`）；非空另有 `decodedByteLength`（abc/f/fbp/fire 为 YJ2 解压后字节数，fbp 恒 64000；ball 无解压即 chunk 字节数）、`frameCount`（abc/f/fire 名义 `PAL_SpriteGetNumFrames`；ball/fbp 恒 1）、`decodedFrameCount`（含真实额外帧；ball/fbp 恒 1）、`sprite`（`sprites/<n>.json`） |
| `totals` | `chunks`/`decodedChunks`/`emptyChunks`/`frames`/`files`（不含 index.json 自身） |

### sprites/&lt;chunk&gt;.json 字段（v1）

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` / `profile` | `1` / `"faithful"` |
| `chunk` | chunk 号 |
| `source` | `archive`、`chunk`、`encoding`（abc/f/fbp/fire：`"Windows YJ2"`；ball：`"uncompressed PAL RLE bitmap"`）、`frameLayout`（abc/f/fire：PAL 精灵 WORD 偏移表 + RLE 帧；ball：单幅 RLE 位图，可选 `0x00000002` 头 + WORD 宽/高 + RLE 游程；fbp：单幅原始 320×200 索引位图，线性行主序） |
| `decodedByteLength` | abc/f/fbp/fire 为 YJ2 解压后字节数（fbp 恒 64000）；ball 无解压，即 chunk 原始字节数 |
| `frameCount` | abc/f/fire 名义帧数（`PAL_SpriteGetNumFrames`）；ball/fbp 恒 1 |
| `frames` | 数组，每帧 `index`（abc/f/fire 0..名义−1，可含额外下标；ball/fbp 恒 [0]）、`sourceOffset`、`width`、`height`（fbp 恒 320×200）、`png`（相对路径）、`indices`/`alpha`（base64，行长 width×height；fbp alpha 全 255） |

## 与后续里程碑的衔接

- M6 战斗 Prepare 按 `enemyId` → chunk 下标消费：idle/magic/attack 分段与帧寻址以本包 `frames` 为准；额外帧下标＝`frameCount` 时必须核对 `decodedFrameCount`。
- M6 物品 UI 按 #78 物品对象的 `wBitmap` → ball chunk 下标消费；每个非空 chunk 恰一帧（`frames[0]`）。
- M6 玩家战斗精灵按 player-roles `battleSprite`（含装备覆盖与 `battle-effect-index` 行下标）→ f chunk 下标消费，姿态按下标 0..9 分段；召唤神按魔法表 `summonEffect + 10` → f chunk 10..18 消费，动画播放到 `frameCount - 1`（不含末帧）。
- M6 战斗 Prepare 按 `wNumBattleField`（battle-field 表行下标，即 fbp chunk 下标）消费整幅 320×200 背景帧；UI/结局画面同档固定 chunk（0/1/2/3/4、59..77）。
- 真实原始/派生资源不入库：本契约产物只落临时目录，仓库仅保留代码、测试与人工微型夹具。

## M6 FIRE 扩展（#146，已批准契约 / 局部验证候选）

### 固定参考与复用依据

- `fight.c` 的 `PAL_BattleShowPlayerDefMagicAnim`、`PAL_BattleShowPlayerOffMagicAnim`、`PAL_BattleShowEnemyMagicAnim` 均从对象 `magic.wMagicNumber` 查 MAGIC 记录，再以 `wEffect` 选择 `fpFIRE` chunk；先 `PAL_MKFGetDecompressedSize` / `PAL_MKFDecompressChunk`，随后 `PAL_SpriteGetNumFrames` / `PAL_SpriteGetFrame` 读取 RLE 帧。这是法术效果，不是 BALL 的未压缩物品位图，也不是 FBP 的原始 320×200 索引屏幕。
- `global.c` 显式打开 `fire.mkf`，且 FIRE 是 `PAL_IsWINVersion` 探测的六个归档之一；`palcommon.c` 的统一解压与 WORD 帧寻址规则适用。因此 Windows 基线复用现有 `BattleImageDecoder` → `Yj2Decoder` / `SpriteDecoder`，不新增 FIRE 专用解码器。DOS YJ1 仍严格拒绝。
- 参考只读，未编译或运行。此票没有读取真实 FIRE/PAT 资源；不声明真实 chunk 数、帧数或像素统计。上述参考的原字节 SHA-256 为：`fight.c` = `6d71434242de081d3cd2d5a0578d05485948e7c6cd507407805d0eafa3e761f5`，`global.c` = `6e7162374dca7af62e3e67b5ab1359f1dc0e040ac3a94612ae32f3ca37d82ddc`，`palcommon.c` = `919f428112e70c11b8db208e9bba486c8ab056aa4a893a8d4d6bae9b21b9aae6`。

### 新 archive 值与 v1 字段

批准后的正式形状为：

```text
pal-extract export-battle-images --input <directory> --archive <abc|ball|f|fbp|fire> --output <new-or-empty-directory>
```

- 唯一新增值是大小写精确的 `fire`；只需 `fire.mkf` 与 `pat.mkf`，归档文件名仍大小写不敏感且必须唯一匹配。argv 的固定顺序、缺失/重复/空值/以 `--` 开头值拒绝及旧四档行为保持不变；参数错误退出 2，资源/路径错误退出 1，成功退出 0 且无 stdout/stderr。
- 延续 v1，不新增文件种类：`parameters.archive="fire"`，整档 `sources` 记录 `fire.mkf`、`pat.mkf` 各自 SHA-256/byteLength；每个原始 chunk 保留其 index、原字节 length/hash。非空 chunk 的 `compression="yj2"`，`decodedByteLength` 是解压后长度；`frameCount` 是首 WORD−1 的名义数量，`decodedFrameCount` 是实际导出数量。
- `sprites/<chunk>.json` 的 `source.archive="fire.mkf"`、`source.chunk` 保留原下标、`source.encoding="Windows YJ2"`、`source.frameLayout="PAL sprite WORD offset table + RLE frames"`。每帧保留 index、解压字节中的 sourceOffset、width/height、权威 base64 indices/alpha 和根相对 PNG 路径。真实可解码的末表字额外帧、零/末尾哨兵、悬空可选指针都沿用 abc/f 的严格规则；名义帧损坏不能被丢弃。
- 空 chunk 只写包含 index/byteLength/sha256/compression 的 bare entry（compression 为 `empty`），不写精灵/PNG，不重新编号后续 chunk。`totals.files` 不含 index.json。
- PNG 固定用 PAT chunk 0 / day（六位分量 `<<2`）预览，不取代 indexed/alpha 权威平面，也不证明 GPU。法术定位、层偏移、重复、时序和运行时调色/播放不进入此数值包。

### 人工预期、安全和验证边界

- 主 CLI 夹具为三个 MKF chunks：空、YJ2(A)、YJ2(B)。A 的字面帧表 `[3,7,10]`：26 解压字节、名义 2/实际 3；帧 `(index,offset,width,height,indices,alpha)` 分别为 `(0,6,4,1,[7,0,0,0],[255,0,0,0])`、`(1,14,1,1,[9],[255])`、`(2,20,1,1,[4],[255])`。B 的帧表 `[2,6]` 末字指向 buffer 末尾：12 解压字节、名义/实际均 1，帧 `(0,4,2,1,[11,0],[255,0])`。合计 chunks=3、decodedChunks=2、emptyChunks=1、frames=4、files=6；文件集恰为 index.json、两个 sprite 文档与四张 PNG。
- 预期由人工帧表/RLE 和测试自有 MKF/PAT 字节给出，不调用被测 decoder 生成。PAT7=(63,0,30) 的首像素 RGBA 为 (252,0,120,255)，透明像素保留 PAT0=(12,16,20) 的 RGB、alpha 为 0；CLI 预览经 BCL zlib 读取 scanline 与该字面值比较。
- 保留单 chunk 原始/解压各 8 MiB、整包解压 256 MiB、像素 128M 及帧级 `IndexedImage.MaxPixels` 安全边界，不放宽解码失败。人工拒绝例覆盖缺 FIRE/PAT、YJ1、截断 YJ2、坏 table/名义 RLE、声明解压超限与超像素尺寸，诊断精确定位 fire.mkf/chunk，不泄漏绝对路径/堆栈；先前 chunk 成功不允许发布部分包或完整 index。
- 新/空输出可用；输入内输出、非空目录、文件、链接或链接祖先仍拒绝。确定性例比较两次导出的完整文件集和逐文件原字节，并核输入 bytes/mtime/readonly attributes 保持；覆盖拒绝后原输出也保持不变。全部解码暂存完成、再发布文件且 index 最后发布的既有策略不变，不引入失败回滚。
- 本轮原生验证确认新增 **10 个 Unit 案例**（8 个方法，含 theory 展开）和 **22 个 Integration 案例**（17 个 FIRE 正反例 + 现有参数 theory 新增 5 例）。完整 CLI Unit **475 passed / 0 failed / 0 skipped**；完整 CLI Integration **382 passed / 0 failed / 2 skipped**（共 384，只有既有两个 case-sensitive-filesystem skips），旧四档回归保留。与合同基数 465 / 360+2 及实际新增 +10 / +22 一致，不采用调查建议的 8/16。
- 指定 RED 已通过真实 CLI 获得：`Export_fire_decodes_complete_indexed_package` 的首个 `Assert.Equal(0, result.ExitCode)` 实际为 2；TRX 仅此 1 个失败，test root exit=1，无编译/夹具/超时/中止替代。确认后才最小注册 fire strategy 与生产 help；随后两个完整工程 test root 均 exit=0，未重试、未改测试断言。原始命令、数字退出、stdout/stderr、UTC 和三份 TRX 保存在外部 `C:/evidence/m6/herdr-resume-20261109-1233/146/R3/`；文档更新不等于独立审查或集成。
- 按工程规范的显式未同步分支，**full-gate 的非 Game 计数 pins 尚未同步，完整门被阻断**。#161 预约 `scripts/GameTestBatches.ps1` / `tests/Pal.Game.Tests/GameBatches.Tests.ps1`，本票五路径不触及它们；Main 须另授无冲突的 ledger 同步并消费真实执行计数，不能以局部候选关闭 #146 或解锁 #147。（历史状态：#146 已于 63e793e5 集成关闭；后续 #147 已按当前源码事实同步两份 ledger。）
- FIRE Prepare/原生加载、法术播放、Game/headless、真实私有资源、GPU/人工、远端 CI 和完整 M6 均未由本源码准备证明。运行时不读取 MKF，不添加 Godot 依赖；只读输入不是新的可变玩法状态，也不提前冻结存档契约。

## M6-A T19：FIRE 的显式原生准备与加载（#147）

### 入口与产物

- 公开静态 `Pal.Game.Editor.PrepareFireEffects.Prepare(string inputDirectory, string outputDirectory) -> IReadOnlyDictionary<int, string>`；headless Editor 场景 `res://Editor/PrepareFireEffects.tscn`（`--fire-input <目录> --fire-output <新建或空目录>`，成功标记 `PAL_PREPARE_FIRE_EFFECTS:<映射条数>`）与 wrapper `scripts/Prepare-FireEffects.ps1`（参数/链接/进程守卫与既有 Prepare wrapper 同形）。运行时不调用该节点，正常 Main 只消费已准备资源。
- 消费一个完整的 export-battle-images fire 包（上节契约）：输出 `Sprites/<chunk>.tres`（既有 `PalSprite`/`PalFrame`，`SpriteId` = 原 FIRE chunk 号，保留名义 `FrameCount` 与含别名的实际帧集，`Source` = `{archive: fire.mkf, chunk, encoding: Windows YJ2, frameLayout: PAL sprite WORD offset table + RLE frames}`）与 `Textures/<chunk>/<frame>.tres`（既有原生 `ImageTexture`，取自包内对应 PNG）；返回 chunk → sprite URI 映射。不新增纹理格式、UID/模型或平行 loader；空 chunk 不发布精灵；chunk 号是法术效果身份，不重编号为连续物品号。

### 校验与安全

- 发布前严格校验完整包：index.json 逐字段（版本/来源 fire.mkf+pat.mkf/调色板声明/每 chunk 身份与压缩/totals 与实际一致）、精灵文档与清单交叉核对（chunk/decodedByteLength/frameCount/帧数）、帧平面 base64 长度 = width×height、PNG 可解码且尺寸与帧声明一致、实际文件集与声明集精确相等（多余/缺失均拒绝）；非法输入以精确 `InvalidDataException` 定位，不防不夹值。
- 路径安全沿用既有 Prepare 边界：输出必须位于项目 Assets 内且新建或空、输入/输出不得重叠、输入在 src/Pal.Game 外、拒绝链接/reparse；输入只读且前后重核哈希；失败只清理本次自建文件与目录，不删除不明产物、不假成功。
- 发布后自检：用取自包数据（非加载目标）构造的 expected 与已发布文件的精确 SHA-256 清单，走既有生产校验链 `NativeBattleVisualProfile.LoadSprite(uri, expected, resourceSha256)` 逐字段读回（含别名下标、offset、尺寸、indices/alpha、Source 身份、纹理 URI/类型/尺寸），兼容一参 `LoadSprite(uri)` 行为不变。

### 验证与登记（本票实际执行）

- 两层独立验证：`tests/Pal.Prepare.Tests/FireEffectsPrepareProcessTests.cs` 真实 Godot 进程（argv/标记/退出/重复运行拒绝、输入只读、输出文件集）；`src/Pal.Game/Tests/PrepareFireEffectsTests.cs` GdUnit 公开会话（字面往返 + checked 加载 + 13 个发布前拒绝例，预期全部来自人工字节夹具）。
- Ledger 同步（按当前源码事实）：`Get-PrepareTestCount` 102→115（editor publish 例 + repair 新增 12 个 wrapper/argv 真实进程用例，合计 13 个 fire 进程用例）；native-map 728→742（新增 `PrepareFireEffectsTests`）；库存 1018→1032；game 正例 1015→1029（committable 1014→1028）。同步同时修正 `GameBatches.Tests.ps1` 的存量滞后基线（core 1465→1473、库存 1016→1018→本轮 1032、scene-realfull 42→44/41→43、committable 1012→1014→本轮 1028、补齐两个 `NormalBattleFlee*` 用例身份）。
- 仍不包含：FIRE 动画时序/法术播放、通用图像导入框架、音视频或场景调色板循环（M7）；真实 FIRE 资源仅本地显式准备，不入库不上传。

## M6-B T03b：BALL 的显式原生准备与物品页消费（#218）

### 入口与产物

- 公开静态 `Pal.Game.Editor.PrepareItemBitmaps.Prepare(string inputDirectory, string outputDirectory) -> IReadOnlyDictionary<int, string>`；headless Editor 场景 `res://Editor/PrepareItemBitmaps.tscn`（`--ball-input <目录> --ball-output <新建或空目录>`，成功标记 `PAL_PREPARE_ITEM_BITMAPS:<映射条数>`）与 wrapper `scripts/Prepare-ItemBitmaps.ps1`（参数/链接/进程守卫与 FIRE wrapper 同形）。运行时不调用该节点；正常 Main 只消费已准备资源。
- 消费一个完整的 export-battle-images **ball** 包（#93 交付的 CLI 解码链，v1 形状不变）：输出 `Sprites/<chunk>.tres`（既有 `PalSprite`/`PalFrame`，`SpriteId` = 原 BALL chunk 号 = item.wBitmap 身份，`FrameCount` 恒 1，`Source` = `{archive: ball.mkf, chunk, encoding: uncompressed PAL RLE bitmap, frameLayout: single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs}`）与 `Textures/<chunk>/0.tres`（既有原生 `ImageTexture`，取自包内 PNG）；返回 chunk → sprite URI 映射。不新增纹理格式、UID/模型或平行 loader；空 chunk（真实数据 chunk 0）不发布；chunk 号是物品位图身份，不重编号。

### 菜单绑定与消费

- authored `battle-data.json` 的 `$.useMenu` 每行新增必填 `bitmap`（WORD 0..65535）：rgObject item.wBitmap。0 = 参考 `PAL_MKFReadChunk(...) > 0` 的空 chunk 分支，物品页不画图；Core `BattleUseMenuBinding.Bitmap` 贯穿 typed 传输（`PalBattleContent.UseMenuBitmaps` 平行数组，失配拒绝）并投影到 `ExplorationMenuUseRow.Bitmap` / `ExplorationMenuItemTargetView.Bitmap`。
- `PalStoryProfile.ItemBitmaps`（object WORD → sprite URI，story-package.json 可选 `itemBitmaps: [{objectId, uri}]`）在 `NativeStoryProfile.Load` 严格核对：覆盖恰为 `bitmap != 0` 的 use 绑定集合，每个 sprite 经 `NativeBattleVisualProfile.LoadSprite` 读回且 `SpriteId`/`Source.archive=ball.mkf`/`Source.chunk` 必须等于绑定位图字；缺失/多余/坏身份在故事加载即拒绝（fail-closed，无占位图）。未准备位图的条目不要求预准备（未列出的 use 对象不受影响）。
- `PalMenuView` 在物品页/物品目标页（含脚本挂起态）绘制选中行的位图（authored 布局，活动场景调色板逐像素，透明像素跳过，等同 `PAL_RLEBlitToSurface`）；内部探针 `PresentedItemBitmap` 暴露 (object, bitmap, 尺寸) 供测试观察。两种已支持语言下位图身份一致，名称/数量仍随语言包。

### 校验、安全与登记

- 发布前严格校验完整 ball 包（index.json 逐字段、compression 恒 rle/empty、frameCount/decodedFrameCount 恒 1、帧平面长度、PNG 可解码且尺寸一致、实际文件集与声明集精确相等）；路径/链接/只读/失败清理边界与 FIRE prepare 同形；发布后用包数据构造 expected 走 `LoadSprite(uri, expected, resourceSha256)` 逐字段读回。
- 两层独立验证：`tests/Pal.Prepare.Tests/ItemBitmapsPrepareProcessTests.cs` 真实 Godot 进程（editor/wrapper argv/标记/退出/预检，13 例）；`src/Pal.Game/Tests/PrepareItemBitmapsTests.cs` GdUnit 公开会话（字面往返 + checked 加载 + 12 个发布前拒绝例）。菜单消费由 `MenuSceneTests` 新用例（两语言理论 + 缺绑定 fail-closed 拒绝）经正式 Main/SceneRunner 覆盖；Core 投影由 `ExplorationMenuUseTests` 新用例覆盖。
- Ledger 同步（按当前源码事实）：`Get-PrepareTestCount` 115→128；native-map 754→772（新增 `PrepareItemBitmapsTests` 13 例与 `NativeStoryProfileTests.ItemBitmapsCarryTheBoundBallChunkIdentityOrRejectAtTheStoryBoundary` 的 5 个 story 边界身份/精确覆盖反例）；scene-dialogue 115→118；库存 1051→1072；game 正例 1048→1069（committable 1047→1068）；core 1489→1490。`GameBatches.Tests.ps1` 对应字面同步（full 批次串 85/118/…/772/44、committable 变体 …772/43、两份 PASS 摘要 1069/1068 与 ball 进程类 13 例钉）。
- 仍不包含：商店侧图像消费（#217 消费同一链）、真实 BALL 资源准备入库（仅本地显式准备）、GPU/人工桌面像素验收与远端 CI。
