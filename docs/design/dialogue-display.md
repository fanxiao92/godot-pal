# M3 对话显示资源与调用

本页只描述 Issue #23 的语言包/显示调用边界；不包含事件执行、独立 Main 启动、静态场景准备或自由探索接入。

## 离线来源转换

从授权的原始 SDLPal 数据目录执行：

```text
pal-extract extract-dialogue --input <raw-sdlpal-directory> --reference <reference/sdlpal> --output <new-or-empty-directory>
```

命令要求简中和英文 SLF、`RGM.MKF`、`DATA.MKF` 及固定参考目录，读取两种语言的消息章节、RGM chunk 59、DATA chunk 12 和参考 `ascii.h` / `fontglyph.h`。头像是直接 RLE；等待提示是帧表图像。输出的 `.pimg` 保留索引像素和 alpha，并声明使用运行时当前场景调色板。它不得覆盖已有目标或写入输入目录；应将真实输出保存在唯一的仓库外临时目录，不能提交、上传或放进 CI。事件仍仅通过原始消息 ID/跨度解析正文。

输出的现代语言契约见 [events-schema.md](events-schema.md#语言包现代格式-v1)。图像文件 `.pimg` 为 `PALIMG1\\0` 8字节标记、little-endian Int32 宽高，随后逐像素交错的 palette index / alpha；它保持当前场景调色板语义，而非固定 RGB。`font.json` 只含实际语言文字使用的字形，ISO 8×15 glyph 按参考位序归一，Unifont 使用 16×16、每行1或2字节位图及原始宽度元数据。两种来源散列留在封套中。

默认语言为简中；英语必须显式选择。选择的语言缺少 `(startMessageId, span)`、存在重复范围、非法 UTF-8/范围或资源不完整时必须明确失败，不混用另一语言，不从另一语言补缺。空白行、空文本条目、CLEAR 及文本显示控制符需按来源顺序保留。

真实源文件身份：`m_chs.slf`、`m_eng.slf`、`RGM.MKF`、`DATA.MKF`。此文档不复制原始/派生对话内容或字形数据。完整字体覆盖来自固定参考的 Unifont bitmap 与 ISO 字形资源；运行时不得用 Godot 默认 UI 字体或近似系统字体冒充。

## Core 显示会话

`DialogueLanguagePackage.Resolve(startMessageId, span)` 仅做原始引用解析；它不驱动事件命令。`DialogueDisplaySession` 在 Core 内以显式毫秒时间和显式输入参数推进一个已解析块，24ms 默认字间隔、逐条显示、加速、CLEAR 等待、结束输入和 100ms 调色板轮转均可确定性验证。颜色参数为 0 时继承既有颜色，不把 0 解释为黑色。Core 不访问文件、Godot 输入、纹理或节点。

SDLPal 依据：`text.c` 的 `PAL_ReadMessageFile` / `PAL_GetMsgNum` 保留消息索引跨度和 CLEAR；`script.c` 的 `MESSAGE_GetSpan` 把相邻连续 `0xFFFF` 命令归入块；`PAL_ShowDialogText` 与 `TEXT_DisplayText` 决定逐字、默认延迟、按键加速和翻页；等待状态每100ms轮转调色板索引 `0xF9..0xFE`，返回输入后恢复原调色板。`font.c` 的字形取位、字宽/行高、阴影和 `PAL_StartDialogWithOffset` 的头像/文字锚点是显示层来源；`input.c` 将 Return/Space/keypad Enter/Left Ctrl 映射为搜索，Escape/Insert/Alt/keypad 0 映射为菜单。不能用 `ui_accept` 替代此输入集合。

## 验收边界

自动化人工资源只验证格式、范围、语言隔离、缺失错误、像素解码与会话时序；不得包含真实游戏文字或真实派生图片。真实目标 61/span1 的来源核对使用仓库外临时转换目录，仅记录语言范围、来源身份/散列与计数，不打印正文。Godot 图形与 headless 状态分别报告；本地独立准备不自动执行。

此资源/显示 slice 不执行 `events.json`，不改 `Main.cs`、`NativeSceneLoader`、静态 profile、场景启动/焦点行为，不增加正常场景调度，也不代替 #24/#27 的所有权。暂未覆盖的布局/字体/调色板与真实显示能力应明确列为未验收，不写作通过。
