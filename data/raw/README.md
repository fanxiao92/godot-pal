# data/raw —— 原版游戏数据

把你的**《仙剑奇侠传 1998 柔情版》原版数据文件**放在这个目录里。

`pal-extract` 已支持只读检查场景所需的六个 MKF，命令见 [根目录 README](../../README.md#构建测试与运行)。后续会将原始资源转换到 `data/extracted/`；实际解压与转换尚未实现。

当前用户提供的数据目录为 `data/raw/sdlpal/`。M1 只消费场景所需的归档，输出静态场景资源，范围见 [M1 计划](../../docs/milestones/m1/plan.md)。`Word.dat` / `M.msg` 含大量英文，`sdlpal.cfg` 选择 `m_chs.slf`；简体中文和英文分别来自 `m_chs.slf`、`m_eng.slf`，文字与字库转换留到 M3，不是 M1/M2 的前置要求。原始文件保持只读。

## 需要哪些文件

98 柔情版的数据文件,通常包括(以实际版本为准,文件名大小写可能不同):

- `ABC.MKF` `MGO.MKF` `MAP.MKF` `GOP.MKF` 等 —— 精灵 / 地图
- `SSS.MKF` —— 脚本(字节码)
- `DATA.MKF` —— 游戏数据表
- `PAT.MKF` —— 调色板
- `F.MKF` `FBP.MKF` `FIRE.MKF` `RGM.MKF` —— 图像 / 战斗背景等
- `RNG.MKF` —— 动画
- 字库文件、音乐(RIX / 音频)、音效、语音、AVI 过场
- `WORD.DAT` `M.MSG` 等文本文件

> 准确的文件清单要拿到真实的 98 柔情版数据后核对 —— 柔情版和 DOS 版的文件组成、内部格式有差异。见 [决策记录与待定问题](../../docs/design/decisions.md) 的 Q3。

## 注意

- 这个目录的内容**不会进 git**(见根目录 `.gitignore`)—— 原版数据版权属于大宇 / 软星。
- 这些文件需要你自己提供。本项目无法、也不会替你下载原版游戏数据。

