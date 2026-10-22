# M3 对话显示资源与调用

本页只描述 Issue #23 的语言包/显示调用边界；不包含事件执行、独立 Main 启动、静态场景准备或自由探索接入。

## 离线来源转换

从授权的原始 SDLPal 数据目录执行：

```text
pal-extract extract-dialogue --input <raw-sdlpal-directory> --reference <reference/sdlpal> --output <new-or-empty-directory>
```

命令要求简中和英文 SLF、`RGM.MKF`、`DATA.MKF` 及固定参考目录，读取两种语言的消息章节、RGM chunk 59、DATA chunk 12 和参考 `ascii.h` / `fontglyph.h`。头像是直接 RLE；等待提示是帧表图像。输出的 `.pimg` 保留索引像素和 alpha，并声明使用运行时当前场景调色板。它不得覆盖已有目标或写入输入目录；应将真实输出保存在唯一的仓库外临时目录，不能提交、上传或放进 CI。事件仍仅通过原始消息 ID/跨度解析正文。

输出的现代语言契约见 [events-schema.md](events-schema.md#语言包现代格式-v1)。图像文件 `.pimg` 为 `PALIMG1\\0` 8字节标记、little-endian Int32 宽高，随后逐像素交错的 palette index / alpha；它保持当前场景调色板语义，而非固定 RGB。`font.json` 只含实际语言文字使用的字形，ISO 8×15 glyph 按参考位序归一，Unifont 使用 16×16、每行1或2字节位图及原始宽度元数据。两种来源散列留在封套中。

导出目录随后必须由明确的 Godot 离线准备阶段转成原生 Resource；不可直接作为运行时包读取。先为两步分别选择尚不存在的唯一输出路径，再执行：

```powershell
dotnet run --project tools/Pal.Extract -- extract-dialogue `
  --input C:/Users/<user>/Repos/godot-pal/data/raw/sdlpal `
  --reference reference/sdlpal `
  --output C:/Users/<user>/AppData/Local/Temp/<new-dialogue-package>
./scripts/Prepare-DialoguePackage.ps1 `
  -InputPath C:/Users/<user>/AppData/Local/Temp/<new-dialogue-package> `
  -OutputPath src/Pal.Game/Assets/<new-dialogue-native-package> `
  -GodotPath C:/Users/<user>/scoop/apps/godot-mono/4.7.2/godot-mono.console.exe
```

`-InputPath` 必须是项目外现成、完整的 extractor 输出；`-OutputPath` 必须是新建或空的 `src/Pal.Game/Assets` 子目录。准备器只产出并重新加载验证 `Dialogue/Language.zh-Hans.tres`、`Language.en.tres`、`Font.tres`、`Portrait-059.tres` 和 `Prompt-00.tres` 至 `Prompt-02.tres`。它核对精确输入文件集合、v1 字段/来源身份/散列格式、原消息范围、字形位图形状、PIMG 长宽与文件长度及图像帧/来源引用；拒绝未知/重复字段、意外文件及覆盖。失败产物保留，不清理、不回滚。真实派生包与 Assets 资源都位于仓库外或 ignored Assets，不提交上传。运行时仅 `ResourceLoader` 加载这些 `.tres`，不读取语言 JSON、font JSON 或 `.pimg`，不自动抽取/同步。

默认语言为简中；英语必须显式选择。选择的语言缺少 `(startMessageId, span)`、存在重复范围、非法 UTF-8/范围或资源不完整时必须明确失败，不混用另一语言，不从另一语言补缺。空白行、空文本条目、CLEAR 及文本显示控制符需按来源顺序保留。

真实源文件身份：`m_chs.slf`、`m_eng.slf`、`RGM.MKF`、`DATA.MKF`。此文档不复制原始/派生对话内容或字形数据。完整字体覆盖来自固定参考的 Unifont bitmap 与 ISO 字形资源；运行时不得用 Godot 默认 UI 字体或近似系统字体冒充。

## Core 显示会话

`DialogueLanguagePackage.Resolve(startMessageId, span)` 仅做原始引用解析；它不驱动事件命令。`DialogueDisplaySession` 在 Core 内以显式毫秒时间和显式输入参数推进一个已解析块，24ms 默认字间隔、逐条显示、加速、CLEAR 等待、结束输入和 100ms 调色板轮转均可确定性验证。`-`、`'`、`@` 与 `"` 分别切换 cyan `0x8D`、red `0x1A`、alternate red `0x17` 与 yellow `0x2D`，关闭相同控制符时回到参考默认色 `0x4F`；dialog 命令颜色值0表示继承传入的当前调色板索引（索引0本身有效，不映射为默认色）。色彩状态及 `$NN` 延时跨行持续；源码的默认延时3单位为 `3*8=24ms`，`$NN` 将延时设为 `(NN*10/7)*8ms`（整数除法顺序与 C 一致）。反斜线转义下一控制字符；`)`、`(` 选定提示帧1、2，默认帧0。`PAL_ShowDialogText` 每行重置提示帧，不自动动画帧1/2。

首条正文出现前，以 `:`、`U+FF1A` 或 `U+2236` 结尾的消息按 `PAL_ShowDialogText` 识别为说话人名称：立即显示、不消耗正文行数或逐字时间，也不改变继承的正文颜色和速度。名称按 `PAL_UnescapeText` 处理：不含反斜线时原样保留；含反斜线时保留被转义的字符，同时移除未转义的 `- ' @ " $ ~ ) (`（只移除控制符本身，不消费其后数字）。这不是执行正文颜色/速度控制。有画像时名称锚点为(80,8)、固定颜色`0x8C`，正文起点仍为(96,26)；无画像时名称/正文起点分别为(12,8)/(44,26)。原始消息引用及条目数不变，不能把目标的“名称＋正文”当成两条正文。公开快照保留既有`VisibleText`/`VisibleRuns`当前文字语义，另以`TitleText`和不可变`VisibleLines`表达应持续呈现的名称与页面正文；等待提示使用末条可见正文的位置，而不是已递增的下一行计数。CLEAR/翻页恢复时清除正文页面并保留名称，完成时清除名称、正文和画像覆盖层。

SDLPal `~NN` 在普通消息中先延时 `NN*80/7ms`，然后提前返回并重置当前行；这条带时序/返回状态的控制在本窄切片中明确拒绝，不声称支持。#23 目标 start61/span1 两语言消息中均未出现 `~` 控制（离线核对只读取控制计数，未输出消息内容）。Core 不访问文件、Godot 输入、纹理或节点。

SDLPal 依据：`text.c` 的 `PAL_ReadMessageFile` / `PAL_GetMsgNum` 保留消息索引跨度和 CLEAR；`script.c` 的 `MESSAGE_GetSpan` 把相邻连续 `0xFFFF` 命令归入块；`PAL_ShowDialogText` 调用 `TEXT_DisplayText(..., FALSE)`，后者决定逐字、颜色控制、默认延迟、按键加速和翻页；等待状态每100ms轮转调色板索引 `0xF9..0xFE`，任何输入后恢复原调色板。`font.c` 的字形取位、字宽/行高、阴影和 `PAL_StartDialogWithOffset` 的头像/文字锚点是显示层来源；`input.c` 将 Return/Space/keypad Enter/Left Ctrl 映射为搜索，Escape/Insert/Alt/keypad 0 映射为菜单。不能用 `ui_accept` 替代此输入集合。

Godot `PalDialogueDisplay.ActivePaletteChanged` 提供一个显式 consumer seam：参数是已经轮转或恢复后的完整256色调色板副本，宿主把它送给场景调色板使用者。Fixture 的 `PalDialoguePaletteSurface` 用合成原生索引图像实际建立 `ImageTexture` 并在 `_Draw` 中绘制；测试读取该 target 的调色板像素观察等待轮转与输入恢复。此 seam 不自动修改真实地图/主场景 palette，也不声称已接入 map10；这种连接属于下游宿主集成。

## 验收边界

自动化合成资源只验证格式、范围、语言隔离、缺失错误、原生资源导入加载、索引像素解码与会话时序；不得包含真实游戏文字或真实派生图片。真实目标 61/span1 的来源核对使用仓库外临时转换目录，仅记录语言范围、来源身份/散列与计数，不打印正文。Godot headless 的模拟背景像素与真实桌面/GPU输出分别报告；仍需人工核对真实素材布局、完整字体阴影/字宽与场景 palette 宿主集成。

此资源/显示 slice 不执行 `events.json`，不改 `Main.cs`、`NativeSceneLoader`、静态 profile、场景启动/焦点行为，不增加正常场景调度，也不代替 #24/#27 的所有权。真实双语显示 fixture 属于 #23 验收要求；暂未覆盖的布局/字体/调色板与真实显示能力应明确列为未验收，不写作通过。

### 本机真实双语 GPU fixture

`Tests/RealDialogueFixture.tscn` 是独立、显式的本机验收入口，不被 Main 或 headless GdUnit 自动选择。它仅加载已经准备的原生 `.tres`，不提取、不生成配置、不覆盖资源。通过公开 `PalDialogueDisplay.Start` 加载目标61/span1、画像59及默认颜色0x4F；使用真实 `Input.ParseInputEvent` 和 `RenderingServer.FramePostDraw` 观察显示更新后的逻辑GPU画面。固定选择已准备的 palette0/day，背景为人工索引图像（底部包含F9–FE色带），不是map10接入。

独立画面预期使用 `text.c` 的画像锚点(9,4)、名称锚点(80,8)与颜色`0x8C`、正文起点(96,26)、18像素行距及三重阴影，再直接解包原生字形位图，不调用生产 `IsSet` 或绘制函数。目标首消息按名称保留，第二消息从正文第0行逐字显示；加速后应进入正文完成的等待，而非断言“第一条消息完成”。名称和完整正文预期来自原生输入，不由当前快照生成。已完成的前行应保留，等待提示位于末行末尾，结束应还原整个人工背景。fixture按已核实的处理优先级在显示节点实际`_Process`完成后保存快照及引擎帧身份，仅为不同的已完成更新取GPU画面；信号清理幂等，失败也保存阶段结果。Core tokenizer只用于解释正文输入控制符；其语义由独立Core测试负责。此检查证明给定原生素材的GPU显示，不独立证明离线字库/文字与源档完全一致，更不代替物理键盘、操作者或OS屏幕验收。

先按仓库规则核对进程并构建，随后每种语言单独调用一次；任何失败、超时或未知退出保留原件并停止，不自动运行下一语言或重试：

```powershell
dotnet build src/Pal.Game --no-restore
./scripts/Test-RealDialogueGpu.ps1 `
  -NativeDialogueDirectory 'C:/local/prepared/Dialogue' `
  -PalettePath 'C:/local/prepared/Palettes/Palette.tres' `
  -Language zh-Hans `
  -EvidenceDirectory 'C:/external/new-unique-zh-evidence' `
  -GodotPath 'C:/path/to/godot-mono.console.exe'
# 上一轮成功后，用 -Language en 与另一新EvidenceDirectory 显式启动英语。
```

脚本不build/prepare，拒绝已有证据目录；运行前后记录七个对话资源及palette的SHA-256、长度、mtime。每次只有一个Godot进程，fixture有30秒活性界限、包装器有60秒自有进程watchdog，退出码/日志、六阶段实际及预期PNG和机器可读比较结果保留在仓库外。缺结果、阶段不完整、像素不符、引擎错误或资源变化均不通过。真实画面、预期PNG及任何真实素材不得提交或上传CI。

### 已保存的本机验证检查点（2026-10-23）

本机候选为 `8765445` 加本页所述的准备、控制符、名称/页面及 GPU fixture 增量；后续仅固化这些已验证文件，不重新实现或重跑。保存的 Core `unescape-core.trx` 为155/155、合成 GdUnit `unescape-gdunit.trx` 为3/3，均为 Completed、无 RunInfo；最后一次 Game build 为零警告/错误。Extract `extract-dialogue-checked.trx` 为317通过、2个既有大小写文件系统能力跳过（共319）。这些是对应候选的分层结果，不是完整 M3 共享门禁或远端 CI。

本机 `%TEMP%/pal-dialogue-gpu-zh-titlefix-20261023-a5fe56a4` 与 `pal-dialogue-gpu-en-titlefix-20261023-a5fe56a4` 各保存一次完整运行：进程退出0、无超时/引擎错误，六阶段差异像素均为0；七个原生对话资源及所选 palette 的 SHA-256、长度和 mtime 前后一致。收尾只读核验时，资源仍与记录一致，现存 Core/Game/Extract PDB 的手写源码校验和匹配当前源码。原运行日志和 TRX 保留在本机，图片及真实派生资源不随代码提交。此结果仅证明真实原生对话素材、人工索引背景、程序化输入与逻辑 GPU 输出；map10 宿主接入、物理键盘、Alt-Tab、人工桌面及完整闭环仍由后续任务验收。
