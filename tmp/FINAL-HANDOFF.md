# M6 收敛最终交接（2026-11-23，含远端推送）

操作者将迁移到新机器继续。本文档是新会话/新机器的**唯一入口**。

## 一、远端状态（本次推送）

- 分支 `integration/m6-long-running` 已 push 至 `fanxiao92/godot-pal`（origin），HEAD = `52ad2c9`（Implement #234）。
- **未合 main、未开 PR**（操作者未授权；M6 收敛后由操作者决定）。
- 新机器起步：`git clone -b integration/m6-long-running https://github.com/fanxiao92/godot-pal.git`（或 fetch 后 checkout）。

## 二、必须随迁的本地资产（git 里没有）

| 资产 | 本机路径 | 说明 |
|---|---|---|
| **LRA state**（无需迁移，2026-11-23 更新） | 旧机 `C:/Users/xiaofan/.pi/agent/long-running/8abd28935ce05eefeb27/124/`（3.5GB） | GitHub 是唯一事实源：新机 `cli.ts init` 生成新 state，83 张已关票自动 accounted，#238 作 ready 新叶重做（候选 patch 在 `tmp/238-candidate-uncommitted.patch`）；旧 state 仅审计价值，留旧机或另行打包（非 git） |
| #238 候选 patch | `C:/evidence/m6/207-repair-20261114/m6c-tickets/238-candidate-uncommitted.patch` | 584 行 12 文件（契约/捕获/恢复/JSON/矩阵/测试全改完，**未验证**）；新机器可 apply 参考或让 LRA 重做 |
| 证据根（建议） | `C:/evidence/m6/` | 全部历史证据（收尾/基线/RED） |
| 固定 GdUnit 包 | `worktrees/godot-pal-m6-long-running/.packages/gdunit` | 固定版本测试包（SHA256SUMS 校验）；或按 docs/testing/gdunit-source-packages.md 重新准备 |
| Godot 引擎 | 4.7.2 mono（`godot-mono.console.exe`） | 版本钉定 |
| 工具脚本 | `C:/evidence/m6/207-repair-20261114/reconciliation/`（resume-precheck.sh / watch-lra.sh / recover-lock.mjs） | 恢复链工具 |
| 引擎清理脚本 | `C:/evidence/m6/closeout-full/clean-engines2.ps1` | OutputDrainTimeout 后必用 |

## 三、当前进度（83/84）

**已完成并推送**：M6-A 全链（T01–T19+FIRE）、M6-B 七叶（#212–#218）、M6-C 八叶（#221–#228）、T08 补齐（#235/#236）、T03 集成（#237 `d193a01`）、T03 Main 链（#234 `52ad2c9`）、#161；工程修复两刀（#219/#220 + 本会话 `8a34dab`/`15a99c6` 已入树）；完整门 committable PASS（`closeout/m0-committable-run3`，注意当时 HEAD=a4a15be，**收口后需重跑绑定新 HEAD**）。

**唯一未完：#238**（v1 存档补 trigger 计数）：
- 实现在工作树（见 patch），因**引擎缓存退化** 4 次中断（#161/#237/#238×2 同型：多次检查后 `.godot` 退化 → 引擎批 300s 挂起 → 留 GdUnit 孤儿）
- 新机器处置：清缓存（clean-engines2.ps1 + 删 `src/Pal.Game/.godot/{shader_cache,mono/temp}` + `%APPDATA%/Godot/app_userdata/godot-pal/{load-tests-*,logs}`）→ LRA resume → #238 按票面+评论完成验证
- 票面：`https://github.com/fanxiao92/godot-pal/issues/238`（含协调者评论：环境诊断 + 裁定依据）

## 四、#238 完成后的收尾路径（新机器执行）

1. **验收关闭 #129/#134**（模式= #166/#168：核对子票交付+实质承载，发验收评论后 close）：
   - #134：#235/#236 已交付其 route-A 契约第 5/6 项
   - #129：#237/#234 已交付 T03 全部剩余
2. 全部 accounted 后 **父规格审查自动触发**（LRA spec 会话通读 #124 全契约 vs 交付）
3. 审查 passed → **重跑完整门**（`scripts/Test-M0.ps1 -GodotPath <path> -Scope committable -EvidenceDirectory <新目录>`）绑定最终 HEAD → **M6 执行面收敛**
4. 若审查 blocked → 按缺口清单逐条处置（参考本会话处置过的 7 类缺口）

## 五、LRA 运行（新机器）

标准命令（模型 glm-5.3，已实测恢复；若 503 按"卡点速查"换 deepseek-v4-pro）：

```
cd <业务树> && node tools/long-running-agent/cli.ts run --issue 124 --commit --auto-receipt \
  --provider xdt --model glm-5.3 --max-turns 250 --session-timeout-ms 3600000 \
  --engine-checks --godot-path <godot-mono.console.exe> \
  --fixed-package-directory <业务树>/.packages/gdunit --json --resume --confirm-quiescent
```

- state 目录必须迁到相同相对位置或修改 cli 的 --state 指向（`github-state.json` 内含 cwd 绝对路径，**迁移后需把 state 中 cwd/路径字段改为新机器路径**——json 里搜旧路径 `C:\\Users\\xiaofan\\Repos\\worktrees\\godot-pal-m6-long-running` 全部替换）
- 每次启动后挂看门狗子代理（watch-lra.sh + 单命令任务）；**注意看门狗可能静默死亡**（本会话实测 provider 503 致 worker 死且通知丢失 → 1h42m 无人值守）：建议叠加时间戳自检（日志 mtime 超 30 分钟未变即人工检查）
- 卡点速查与纪律红线：见 `%TEMP%` 旧文档被本档覆盖前的版本，或直接参考 `C:/evidence/m6/207-repair-20261114/M6C-HANDOFF-PROMPT.md` + 本会话各票评论链

## 六、已定决策（不可重开）

1. #231 GPU 自动化 = 治理单挂 #232（M7：agent 适配与验证基建；原音视频顺延 M8+）；执行时机 M6 收敛后
2. full 范围真实资产重建：数据核对完成（`C:/evidence/m6/closeout-full/prep/data/`），实现未做——M6 收敛后问操作者
3. 人工桌面验收：`C:/evidence/m6/207-repair-20261114/closeout/MANUAL-ACCEPTANCE.md`（5 步）；轻量化等 #231 GPU 自动化
4. 远端 CI：操作者自建 PR（本次 push 为分支推送，非 PR）
5. 模型 glm-5.3 优先

## 七、本会话关键票务记录（备查）

- #233 关闭（前提错误，not_planned）→ #237 取代；**教训：关票必须同步清 GitHub 依赖**（REST DELETE）
- #234 三轮裁定：静态预检 5 点要求（comment-7042118349）、FFFF Main 覆盖=已知限制（comment-7043138980）、world 回写修复 A（comment-7089953077）
- #237 裁定：存档缺口拆 #238（comment-7036050727）
- #131/#132/#166/#168/#161 已验收关闭（协调者评论链）
