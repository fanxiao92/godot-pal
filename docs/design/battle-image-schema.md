# 战斗图像包契约（battle-image-schema）v1

状态：**abc 形状已冻结并交付（#92）；ball 按同形交付（#93）；f 按同形交付（#94）**；`fbp`（#95）沿用同一 CLI 动词与 `index.json` 清单形状，按各自参考事实扩展 `compression`/帧布局。忠实还原以 `reference/sdlpal/` 为权威（[ADR-0001](../adr/0001-faithful-event-transcription.md)、[ADR-0004](../adr/0004-data-and-script-layers.md)）；通用 JSON 规则与 [data-schema](data-schema.md) 一致（camelCase、严格校验、只读提取）。

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

## 范围与边界

- 只读提取：`abc.mkf`（+ `pat.mkf` 供 PNG 着色）→ 完整图像包；无重编码、无 rebuild。`ball.mkf` 同理（`ball.mkf` + `pat.mkf`），`f.mkf` 同理（`f.mkf` + `pat.mkf`）。
- 输出目录必须新建或为空、不在输入内、拒绝链接（与其他 export 一致）；清单最后发布。
- PNG 着色固定用 `pat.mkf` chunk 0 日间色，仅作可视化；原版战斗实际按当前场景调色板绘制（`battle.c` `PAL_FadeIn(wNumPalette, ...)`），消费方应以索引平面 + 运行时调色板着色。
- 资源上限：单 chunk 原始 ≤8 MiB、解压 ≤8 MiB；整包解压合计 ≤256 MiB、像素合计 ≤128M（真实 abc：解压约 1.9 MB、像素约 6.4M；真实 ball 无解压，chunk 原始字节即解压字节，合计约 0.14 MB；真实 f：解压合计 329,624 B、像素约 1.32M，最大单 chunk 解压 58,116 B）。

## CLI 动词（四票冻结形状）

```
pal-extract export-battle-images --input <目录> --archive <abc|ball|f> --output <新建或空目录>
```

- `--archive` 接受 `abc`（#92）、`ball`（#93）与 `f`（#94）；未实现值（`fbp` 等）按参数错误退出 2。
- 必需归档按 profile 声明：abc → `abc.mkf`、`pat.mkf`；ball → `ball.mkf`、`pat.mkf`；f → `f.mkf`、`pat.mkf`（大小写不敏感，唯一匹配）。

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
| `parameters.archive` | `"abc"`、`"ball"` 或 `"f"` |
| `sources` | 数组：`name`（按 profile：abc.mkf/ball.mkf/f.mkf 与 pat.mkf）、`byteLength`、`sha256`（整档） |
| `palette` | `archive`/`chunk`/`lighting` 与 `usage` 说明（PNG 着色专用，权威平面在精灵文档） |
| `chunks` | 每 chunk 一项，按 index 升序：`index`、`byteLength`（原始 chunk 字节）、`sha256`（原始 chunk）、`compression`（abc/f：`"yj2"`；ball：`"rle"`；空 chunk `"empty"`）；非空另有 `decodedByteLength`（abc/f 为 YJ2 解压后字节数，ball 无解压即 chunk 字节数）、`frameCount`（abc/f 名义 `PAL_SpriteGetNumFrames`；ball 恒 1）、`decodedFrameCount`（含真实额外帧；ball 恒 1）、`sprite`（`sprites/<n>.json`） |
| `totals` | `chunks`/`decodedChunks`/`emptyChunks`/`frames`/`files`（不含 index.json 自身） |

### sprites/&lt;chunk&gt;.json 字段（v1）

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` / `profile` | `1` / `"faithful"` |
| `chunk` | chunk 号 |
| `source` | `archive`、`chunk`、`encoding`（abc/f：`"Windows YJ2"`；ball：`"uncompressed PAL RLE bitmap"`）、`frameLayout`（abc/f：PAL 精灵 WORD 偏移表 + RLE 帧；ball：单幅 RLE 位图，可选 `0x00000002` 头 + WORD 宽/高 + RLE 游程） |
| `decodedByteLength` | abc/f 为 YJ2 解压后字节数；ball 无解压，即 chunk 原始字节数 |
| `frameCount` | abc/f 名义帧数（`PAL_SpriteGetNumFrames`）；ball 恒 1 |
| `frames` | 数组，每帧 `index`（abc/f 0..名义−1，可含额外下标；ball 恒 [0]）、`sourceOffset`、`width`、`height`、`png`（相对路径）、`indices`/`alpha`（base64，行长 width×height） |

## 与后续里程碑的衔接

- M6 战斗 Prepare 按 `enemyId` → chunk 下标消费：idle/magic/attack 分段与帧寻址以本包 `frames` 为准；额外帧下标＝`frameCount` 时必须核对 `decodedFrameCount`。
- M6 物品 UI 按 #78 物品对象的 `wBitmap` → ball chunk 下标消费；每个非空 chunk 恰一帧（`frames[0]`）。
- M6 玩家战斗精灵按 player-roles `battleSprite`（含装备覆盖与 `battle-effect-index` 行下标）→ f chunk 下标消费，姿态按下标 0..9 分段；召唤神按魔法表 `summonEffect + 10` → f chunk 10..18 消费，动画播放到 `frameCount - 1`（不含末帧）。
- 真实原始/派生资源不入库：本契约产物只落临时目录，仓库仅保留代码、测试与人工微型夹具。
