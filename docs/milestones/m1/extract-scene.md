# 静态场景资源包 v1

独立 .NET CLI 的最终 M1 入口（#10）：

```powershell
dotnet run --project tools/Pal.Extract --no-build -- extract-scene `
  --input data/raw/sdlpal --output data/extracted/m1-scene-2 `
  --scene 2 --palette 0 --lighting day
```

五个选项必须显式提供，可换顺序。scene 是正整数，palette 是非负整数，lighting 只接受 `day` / `night`。**不接受 `--map`**：地图来自选中 SSS SCENE 的原始 mapId。调色板不是按场景猜测；0/day 是本地验证时显式选用的参数，不表示执行进入脚本后的状态。成功静默返回 0，参数错误 2，输入/数据/输出错误 1；错误写 stderr，不输出部分成功 JSON、堆栈或本机绝对路径。

## 输入及边界

只消费 `SSS.MKF`、`DATA.MKF`、`MGO.MKF`、`PAT.MKF`、`MAP.MKF`、`GOP.MKF`，文件名大小写不敏感，存在多个大小写候选则失败。未使用的其他文件忽略。归档有完整偏移表验证与资源上限；支持已验证的 Windows 切片，YJ1 明确不支持。无需文字、消息、SLF、字库或事件产物，也不解读脚本 chunk。

SSS chunk0 是原始 32 字节 EVENTOBJECT，chunk1 是 8 字节 SCENE；不是用 SSS chunk2 的物品/魔法 OBJECT 代替场景对象。DATA chunk3 的默认 role0 精灵来自原始 PLAYERROLES；不把其他五个角色当作当前队伍，不硬编码主角精灵号。所有非零初始对象/默认角色精灵（含隐藏对象）均导出，覆盖所有名义帧及原始对象选中的合法额外帧。脚本可能更改的动态依赖不在 M1 的静态闭包内。

保留原始 ID、所有原始记录字段、signed SHORT / unsigned WORD、坐标、方向、帧数、状态、索引、独立 alpha、调色板和定位约定。索引 0 可以不透明。对象初始帧、显隐与定位沿用 #9；不修改隐藏状态来制造展示。非零进入/trigger/auto 入口只是数值来源数据，**不是指向事件文件的执行引用**。不运行脚本、动画、遮挡或碰撞，不给默认角色虚构场景位置。

## 一个输出根

| 路径 | 内容 |
|---|---|
| `manifest.json` | 本文定义的来源、参数、资源入口与产物身份 |
| `scene.json` | #9 v1：原始场景/对象、默认 role0、静态 spriteIds、定位元数据 |
| `map.json` | #8 v1：原始 DWORD、两层瓦片引用、高度/碰撞、坐标和预览约定 |
| `tiles.json`、`tiles/<index>.png` | #8 v1：原始 GOP 帧索引/偏移/尺寸、base64 索引及 alpha、PNG |
| `sprites/<id>.json`、`sprites/<id>/<index>.png` | #9 v1：原始 MGO ID、帧元数据、索引及 alpha、PNG |
| `palette.json` | 共享 #8/#9 v1 调色板，含请求 lighting 与实际 selectedLighting（只有日间颜色时允许夜间回退） |
| `preview.png` | #8 的 2048×2048 **地图瓦片**预览；不合成人物或剧情状态 |

全部 PNG / sprite 路径相对**整个资源包根目录**，用 `/` 分隔，不相对各个 JSON 的所在子目录。无需原始归档即可读取本包的静态数据。每个 JSON 的 `formatVersion` 为 1；原始字段和坐标说明见交付代码及 [地图契约](export-map.md)。单独的 `export-map` / `export-scene` 命令仍可用于检查各自切片，但不输出完整包清单。

## manifest.json

| 字段 | v1 契约 |
|---|---|
| `formatVersion` | 整数 1 |
| `command` | `extract-scene` |
| `parameters` | `sceneId`、从真实 SCENE 读取的 `mapId`、`paletteId`、请求的 `lighting` |
| `sources` | 六个实际消费归档的 `{name, byteLength, sha256}`，按规范小写文件名 ordinal 排序；散列覆盖**整个归档**，不是只覆盖选中 chunk；不作为只允许一份原版的白名单 |
| `resources` | `scene: "scene.json"`、`map: "map.json"`、`tiles: "tiles.json"`、`palette: "palette.json"`、`preview: "preview.png"`、`sprites: [{id, path}]`（按原始 ID 递增，无精灵时为空） |
| `artifacts` | 所有输出文件的 `{path, byteLength, sha256}`，根相对路径 ordinal 排序，**排除 manifest.json 本身** |
| `bundleSha256` | 对 artifacts 的规范串联 UTF-8 字节做 SHA-256：每条为 `path` + TAB + 十进制 byteLength + TAB + sha256 + LF；不含 BOM，不依赖当前区域格式 |

所有 SHA-256 都是 64 个小写十六进制字符。sources 表示原始输入身份；artifacts 与 bundleSha256 表示**实际产物字节集合**，不表示来源或整个清单自身的散列。未选中 chunk 变化可以只改变 sources 而不改变 bundleSha256。避免自递归：如需校验清单本身，由调用者在包外另行计算其散列。输出没有时间戳或本机绝对路径；相同输入、参数及工具/.NET 版本的两次本地输出字节一致，不保证跨压缩库/运行时版本的 PNG 字节一致。

## 引用验证和写入安全

命令复用现有地图/场景导出器，在临时新目录中组合、比较两个切片的调色板并只保留一份；不重新实现图像解码或改变对象/地图语义。发布前检查 scene/map/tiles ID 一致、palette 完整、底/上层帧、所有精灵依赖/选中帧/名义帧、帧尺寸及索引/alpha 平面、PNG 存在与头部尺寸、预览引用，再散列实际文件。现有解码器负责原始图像/RLE/PNG 编码的正确性；清单验证不是第二套图像解码器。

输出必须新建或为空，拒绝已有文件/非空目录，不清空旧数据。禁止输出在输入目录内，保守拒绝输入/输出及其祖先和所需归档上的 link/reparse point，防止经链接写回输入；组合结束后再次检查路径。输入只读打开，记录/复核源身份。源解析或引用错误不发布部分包，临时目录清理。发布使用不覆盖的文件复制，清单最后复制；磁盘等 I/O 故障仍可能留下不完整的新输出目录，不承诺事务回滚。路径组件不得被其他进程并发恶意替换。

## 已执行与未执行的验证

自动化用人工构造的小输入，走真实编译 CLI 子进程；覆盖完整包、无文字/字库/事件且入口非零、隐藏/无精灵、缺归档、坏地图/精灵/帧/调色板、显式参数、非空/重叠/链接安全、只读输入、重复确定性及来源/产物散列。常规 CI 不需要或分发原版数据。

本地实际使用场景 2 / 地图 12 / palette0/day：两次新目录导出 **416 文件（415 个受清单散列的产物 + 清单）** 字节一致；地图 323 帧、精灵 12 个 ID / 75 帧、对象 1～32 的原始字段和入口保留。完整包内的地图与精灵产物分别与 #8/#9 已核对输出逐字节一致；对完整包重跑独立 GOP/RLE/PAT/PNG 像素、地图预览重合成和原始 SSS/DATA 字段核对。MGO 原始解压字节来自交付 Yj2Decoder 的独立调用，**不是第二套独立 YJ2 实现**。全部 27 个原始文件的散列、长度、mtimeNs 与开工基线一致。

当前本地 Debug/Release solution build/test 和真实 Godot console 的 headless version/import/startup 均通过。现有 solution 将 Pal.Game 的 Release 配置映射到 Debug（未修改）；提取 CLI/Core/测试实际按各自 Debug/Release 构建，不声称游戏的独立 Release 配置已验收。此轮**未重跑**全新 NuGet 缓存验收、桌面窗口人工检查、远程 CI 或其他平台检查；M0 历史验收不能当作本轮新执行。最终独立审查与 Issue 关闭由父任务统一处理。离线预览不等于 M2 的 Godot 显示、行走、碰撞或 M3 事件行为验收。
