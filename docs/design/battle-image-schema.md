# 战斗图像包契约（battle-image-schema）v1

状态：**abc 形状已冻结并交付（#92）**；`ball`/`f`/`fbp`（#93–#95）沿用同一 CLI 动词与 `index.json` 清单形状，按各自参考事实扩展 `compression`/帧布局。忠实还原以 `reference/sdlpal/` 为权威（[ADR-0001](../adr/0001-faithful-event-transcription.md)、[ADR-0004](../adr/0004-data-and-script-layers.md)）；通用 JSON 规则与 [data-schema](data-schema.md) 一致（camelCase、严格校验、只读提取）。

## 参考事实（abc.mkf）

- `battle.c` `PAL_LoadBattleSprites` 直接 `UTIL_OpenRequiredFile("abc.mkf")`，按敌人定义 `wEnemyID`（DATA chunk 1 的 `enemyId`）经 `PAL_MKFDecompressChunk` 取 chunk；读不到（空 chunk 返回 ≤0）时该敌人无精灵。`global.h`：`wEnemyID` "Also indicates the bitmap number in ABC.MKF"。
- Windows 数据（本仓库运行时基线，`PAL_IsWINVersion` 探测含 abc.mkf）中 abc 的 chunk 为 YJ2 压缩；解压结果为 PAL 精灵（WORD 偏移表 + RLE 帧），帧寻址是 `palcommon.c` `PAL_SpriteGetFrame`：帧 `f` 的字节偏移 = 表字 `f`×2，可寻址范围 `f < 首字`，比 `PAL_SpriteGetNumFrames`（首字−1，名义帧数）多一个下标。
- **末表字的两种命运（本票核对真实数据后冻结的解码规则）**：末表字＝名义帧数下标指向的「额外帧」。真实 154 chunk（153 非空）中：13 个指向真实后续帧（真实敌人的攻击段会寻址到该下标：`fight.c` 攻击循环最大帧 = idle+magic+attack−1，可等于名义帧数），134 个为 0 哨兵，6 个是指向前一帧数据内部的悬空指针。规则：先按「末表字是帧边界」整包严格解码；失败则视为悬空指针丢弃重试（只重试这一种情形，名义帧损坏仍失败）。空 chunk（真实数据 chunk 0）不产出精灵文件。
- 帧尾边界 = 后续帧起始偏移（RLE 严格消费至边界，允许 ≤1 对齐字节）；权威平面是逐像素索引与 0..255 alpha，PNG 只是可视化。

## 范围与边界

- 只读提取：`abc.mkf`（+ `pat.mkf` 供 PNG 着色）→ 完整图像包；无重编码、无 rebuild。
- 输出目录必须新建或为空、不在输入内、拒绝链接（与其他 export 一致）；清单最后发布。
- PNG 着色固定用 `pat.mkf` chunk 0 日间色，仅作可视化；原版战斗实际按当前场景调色板绘制（`battle.c` `PAL_FadeIn(wNumPalette, ...)`），消费方应以索引平面 + 运行时调色板着色。
- 资源上限：单 chunk 原始 ≤8 MiB、解压 ≤8 MiB；整包解压合计 ≤256 MiB、像素合计 ≤128M（真实 abc：解压约 1.9 MB、像素约 6.4M）。

## CLI 动词（四票冻结形状）

```
pal-extract export-battle-images --input <目录> --archive <abc> --output <新建或空目录>
```

- `--archive` 当前仅接受 `abc`（#92）；`ball`/`f`/`fbp` 由 #93–#95 按同形扩展，未实现值按参数错误退出 2。
- 必需归档按 profile 声明：abc → `abc.mkf`、`pat.mkf`（大小写不敏感，唯一匹配）。

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
| `parameters.archive` | `"abc"` |
| `sources` | 数组：`name`（abc.mkf、pat.mkf）、`byteLength`、`sha256`（整档） |
| `palette` | `archive`/`chunk`/`lighting` 与 `usage` 说明（PNG 着色专用，权威平面在精灵文档） |
| `chunks` | 每 chunk 一项，按 index 升序：`index`、`byteLength`（原始 chunk 字节）、`sha256`（原始 chunk）、`compression`（`"yj2"` 或空 chunk `"empty"`）；非空另有 `decodedByteLength`、`frameCount`（名义，`PAL_SpriteGetNumFrames`）、`decodedFrameCount`（含真实额外帧）、`sprite`（`sprites/<n>.json`） |
| `totals` | `chunks`/`decodedChunks`/`emptyChunks`/`frames`/`files`（不含 index.json 自身） |

### sprites/&lt;chunk&gt;.json 字段（v1）

| 字段 | 类型与约束 |
|---|---|
| `formatVersion` / `profile` | `1` / `"faithful"` |
| `chunk` | chunk 号 |
| `source` | `archive`、`chunk`、`encoding`（`"Windows YJ2"`）、`frameLayout`（PAL sprite WORD 偏移表 + RLE 帧） |
| `decodedByteLength` | YJ2 解压后字节数 |
| `frameCount` | 名义帧数（`PAL_SpriteGetNumFrames`） |
| `frames` | 数组，每帧 `index`（0..名义−1，可含额外下标）、`sourceOffset`、`width`、`height`、`png`（相对路径）、`indices`/`alpha`（base64，行长 width×height） |

## 与后续里程碑的衔接

- M6 战斗 Prepare 按 `enemyId` → chunk 下标消费：idle/magic/attack 分段与帧寻址以本包 `frames` 为准；额外帧下标＝`frameCount` 时必须核对 `decodedFrameCount`。
- 真实原始/派生资源不入库：本契约产物只落临时目录，仓库仅保留代码、测试与人工微型夹具。
