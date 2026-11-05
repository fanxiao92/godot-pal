# M6 执行计划：完整战斗 → 菜单 → 存读档与失败恢复

## 权威、基线与范围

- 导航：[#124](https://github.com/fanxiao92/godot-pal/issues/124)。正式规格为 [#126](https://github.com/fanxiao92/godot-pal/issues/126)（A/B，98 条故事）与 [#148](https://github.com/fanxiao92/godot-pal/issues/148)（C，66 条故事）；正文、评论、原生关系及实际候选证据为权威。
- 执行基线：`main@6ada6dd1fec9f30c7dd73c99fe02c53ea8399233`。M4 PR #74 与 M5 PR #117 的 merge commit 均为该基线祖先。历史验收不转签 M6。
- 本计划在隔离 integration 工作区依据实时批准规格新建，不复制或覆盖主工作树未提交的计划/导航改动。
- D1=A、D2=A、D3=A、D4=A 已批准；#148 覆盖旧文档中 C 尚待决定的历史措辞。A/B 仍不依赖 C。
- 排除 #125 全场景后继、M7 音视频及 M8 通关。固定 SDLPal 经典/Windows 参考只读、不编译运行；真实原始/派生资源与玩家存档不提交上传。

## 阶段与完成条件

| 阶段 | 范围 | 实际完成条件 |
| --- | --- | --- |
| A | 完整经典战斗、阵容、动作、法术/FIRE、道具、状态/毒、敌方能力、结果交接 | 全部能力矩阵、正式 Main、公开 Core、CLI/Prepare、真实资源、双语 GPU、Windows 人工与独立 Standards/Spec 评审；A 可保留明确的失败重载限制 |
| B | 状态/装备、背包/探索法术、真实商店，与战斗共用状态 | 正反例及菜单→战斗连续流程；独立阶段候选验收，不等待 C |
| C | 五手动槽、稳定探索保存、现代版本化 JSON、Godot FileAccess、真实重建与战败重载 | A/B 实际所有者逐字段保留/清除/重建矩阵先冻结；保存→关闭→读取→菜单/事件→战斗→战败→真实重载；不以 codec 或 TODO 签收 |

当前恢复槽只在成功保存/读取后更新，独立启动无默认槽。requestReload 下一生产更新处理；先完整验证/准备候选，再切换。失败保留旧槽/会话的适用边界，坏档严格拒绝，不伪造新游戏或自动重试。这些现代安全策略不冒称参考原样行为。

## 小票与协调

- 首批 #127–#147 共 21 票，不代表完整 A/B；后续 A/B 71 个规划单元尚待核实实际 Interface、完整文件预算和验收后发布。C 子票待 A/B 实际状态矩阵冻结，空子票列表不代表完成。
- 每票单一主要行为 seam，完整影响优先 ≤5、最多 10 文件；超限先拆，不按 commit 重新计数。三个批准 seam：公开 Core、正式 Main/SceneRunner、正式 CLI/Prepare/原生加载。
- 连续模式：每票 fresh goal/session，固定最新已验证 integration tip，串行源码 writer；只读 discovery 可并行。integration 工作区仅协调者写入。每票 TDD 与完整 impact set 后核对实际 HEAD/diff/收据，再本地集成并推进 frontier。
- Godot、Prepare、GdUnit、包含它们的 solution 全局串行；未知 Godot PID/Name 需确认归属。崩溃、中止、timeout、未知退出或报告不完整时保留现场并停止，不自动重跑、续跑、加时、放宽断言或升级工具链。
- 阶段与完整 M6 candidate 各留对应独立评审/必要修复证据，普通 checkpoint 不重复 high 双审。Core、平台 I/O、Main、真实资源、GPU、人工及远端 CI 证据分列。

## 授权与持久索引

本轮用户通过 `C:/evidence/m6/planning/spec-148/start-m6.txt` 明确授权连续协调、实施/独立评审子代理、本地隔离分支/worktree/checkpoint/集成、批准范围内后续拆票发布及实施叶子维护、适用测试/阶段完整门/自动 GPU 和已知来源资源的显式准备。

持久执行索引：`C:/evidence/m6/execution/current.json`；实时 Issue 快照与评论/原生关系：同目录 `live/`；规划交接仅为派生索引，不替代 GitHub。每票记录固定 base、候选、差异、验证命令/退出/原件、唯一 writer 和未执行项。

尚未授权 push、创建/更新 PR、合入 main、关闭父规格、删除分支/worktree或修改合并保护。Windows 人工验收由用户实际执行。到闸门保存 checkpoint 并请求对应授权，不将本地候选冒称主线交付或整个 M6 完成。
