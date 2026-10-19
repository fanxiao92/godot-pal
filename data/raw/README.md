# data/raw —— 原版游戏数据

把你的**《仙剑奇侠传 1998 柔情版》原版数据文件**放在这个目录里。

`pal-extract` 已支持只读检查及 Windows 场景资源转换，命令见 [根目录 README](../../README.md#构建测试与运行)。`extract-scene` 只读取 SSS/DATA/MGO/PAT/MAP/GOP 六个归档，写入新的 `data/extracted/` 子目录；完整包和来源散列契约见 [M1 资源包 v1](../../docs/milestones/m1/extract-scene.md)。默认拒绝非空目录、输入内输出及链接路径。无需文字、SLF、字库或事件产物，非零脚本入口不阻塞静态转换。

当前用户提供的数据目录为 `data/raw/sdlpal/`。M1 只消费场景所需的归档，输出静态场景资源，范围见 [M1 计划](../../docs/milestones/m1/plan.md)。`Word.dat` / `M.msg` 含大量英文，`sdlpal.cfg` 选择 `m_chs.slf`；简体中文和英文分别来自 `m_chs.slf`、`m_eng.slf`，文字与字库转换留到 M3，不是 M1/M2 的前置要求。原始文件保持只读。

## 需要哪些文件

98 柔情版的数据文件,通常包括(以实际版本为准,文件名大小写可能不同):

- `ABC.MKF` `MGO.MKF` `MAP.MKF` `GOP.MKF` 等 —— 精灵 / 地图
- `SSS.MKF` —— 场景、场景对象初始记录及脚本（M1 只读取前两者）
- `DATA.MKF` —— 游戏数据表
- `PAT.MKF` —— 调色板
- `F.MKF` `FBP.MKF` `FIRE.MKF` `RGM.MKF` —— 图像 / 战斗背景等
- `RNG.MKF` —— 动画
- 字库文件、音乐(RIX / 音频)、音效、语音、AVI 过场
- `WORD.DAT` `M.MSG` 等文本文件

> 当前六归档的 Windows 静态切片已本地验证；这不证明此重打包数据与未修改的 98 柔情版逐字节一致，也不表示其他资源已转换。柔情版和 DOS 版的文件组成、内部格式有差异。见 [决策记录与待定问题](../../docs/design/decisions.md) 的 Q3。

## 注意

- 这个目录的内容**不会进 git**(见根目录 `.gitignore`)—— 原版数据版权属于大宇 / 软星。
- 这些文件需要你自己提供。本项目无法、也不会替你下载原版游戏数据。

