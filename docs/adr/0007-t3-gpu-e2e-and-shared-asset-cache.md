# ADR-0007：T3 真实资源 GPU 端到端回归与仓库外共享缓存

> Status: Accepted（2026-11-27）
> 关联规格：[M7-5 #254](https://github.com/fanxiao92/godot-pal/issues/254)、[T3 场景集决定 #247](https://github.com/fanxiao92/godot-pal/issues/247)

## 背景

Linux CI（T1/T2）仅运行无版权、无真实原版数据的 headless 测试，无法覆盖真实 GPU 渲染、字体/纹理显示、真实原版场景/对话/战斗/存档闭环与视觉回归。此前 M3/M4 各自维护零散的专用 GPU 回归脚本与手工 fixture，存在重复准备真实资源耗时高、多 worktree 无法共享缓存、场景扩展成本高及生产工程易被塞入测试钩子等问题。

## 决策

1. **GdUnit4 GPU 模式 + YAML 声明式场景驱动**：
   - T3 端到端回归位于 `tests/Pal.Game.Tests/E2E/`，使用 `tests/Pal.Game.Tests/e2e-gpu.runsettings` 以真实 GPU 模式运行，通过 `./pal.ps1 test e2e --gpu` 触发（强制要求 `--gpu`，绝不降级为 headless）。
   - 场景由 YAML 声明驱动（`tests/Pal.Game.Tests/E2E/Scenarios/`），首批固化 5 个核心场景：`boot-and-explore`、`real-dialogue-bilingual`、`battle-closure`、`save-load-restore`、`dynamic-scene`。
2. **`Pal.Game` 零测试钩子**：端到端探针（`StateProbe`）与输入注入完全留在 `tests/Pal.Game.Tests/E2E/`，仅通过生产公开接口与真实输入事件驱动，不在 `src/Pal.Game` 中添加测试专用分支、环境变量钩子或隐藏状态接口。
3. **0/1 严格本地金图比对**：截图与本地金图按像素容差严格二态判定（无软警告、无自动重试）；金图仅通过显式 `./pal.ps1 test e2e --gpu --update-golden` 创建或刷新，不自动覆写。
4. **环境变量驱动的仓库外共享缓存**：
   - 原始数据目录由 `PAL_RAW_DATA_DIR` 指定，共享缓存根目录由 `PAL_CACHE_DIR` 指定（默认 Windows `%LOCALAPPDATA%/godot-pal-cache`、Linux `~/.cache/godot-pal-cache`）。
   - `./pal.ps1 assets prepare` 按「原始归档 SHA-256 摘要 + `PrepareVersion`」构建或命中 `assets/<rawHash>-<ver>/`，并在当前工作树 `src/Pal.Game/Assets/T3Real` 建立链接/映射，支持多 worktree 零重复准备；金图独立存放在 `goldens/<rawHash>/`。
   - `./pal.ps1 assets status` 与 `./pal.ps1 report` 只输出抽象缓存键与相对路径，绝不将真实原始数据、派生资源、版权截图或本机私密绝对路径提交进仓库或上传至公开 PR/CI。
5. **标准验证工单闭环**：通过 `.github/ISSUE_TEMPLATE/verification.md` 与 `./pal.ps1 report --from <dir> --out <dir>/pr-comment.md` 统一代理与人工 QA 的取证格式，并保留人工 5 分钟 `contact-sheet.png` 观感审阅清单。

## 代价与边界

- T3 强依赖本机真实原版数据与可用 GPU，无法在公开 GitHub Linux CI 自动执行，需按[验证矩阵](../testing/verification-matrix.md)中的显式触发规则在本地执行并回填 PR 证据评论。
- 退役旧里程碑专用 GPU 脚本与孤立 fixture，后续新里程碑只需在 `tests/Pal.Game.Tests/E2E/Scenarios/` 增量添加 YAML 场景。
