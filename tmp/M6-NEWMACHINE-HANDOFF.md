# 新机器续做交接（M6 最后收口）

撰写：2026-11-23，前一协调会话（本机）。焦点：**在新机器上继续并完成 M6 的剩余收口工作**。

## 任务一句话

远端 `fanxiao92/godot-pal` 分支 `integration/m6-long-running`（HEAD `52ad2c9`）已推送；在新机器上完成**唯一剩余实施叶 #238**，随后走「关闭 #129/#134 → 父规格审查 → 重跑完整门」三步，M6 执行面即收敛。

## 必读文档（按序，不重复本文内容）

1. **`C:/evidence/m6/207-repair-20261114/FINAL-HANDOFF.md`**（或随迁副本）——主交接：迁移资产清单、LRA 运行命令、state 路径替换说明、卡点速查、已定决策（不可重开）、票务记录。**先读它**。
2. `C:/evidence/m6/207-repair-20261114/M6C-HANDOFF-PROMPT.md`——原始纪律红线（单 writer、双审、不 push/PR 未经授权等；注意其中 LRA state 路径与模型信息已被 FINAL-HANDOFF 取代）。
3. Issue #238 正文 + 其协调者评论（环境诊断与裁定依据）。
4. 项目规则：新会话开工必读 `docs/agents/execution-environment.md`；执行细节 `docs/agents/task-execution.md`。

## 新机器环境准备（缺一不可）

- [ ] `git clone -b integration/m6-long-running`（远端已验证 `52ad2c9`）
- [ ] 迁移 LRA state：`<旧机>C:/Users/xiaofan/.pi/agent/long-running/8abd28935ce05eefeb27/124/` 整目录 → 新机任意位置；**把 `github-state.json` 内所有旧绝对路径（含 `C:\\Users\\xiaofan\\Repos\\worktrees\\godot-pal-m6-long-running` 与 attempts 路径）替换为新机路径**
- [ ] Godot 4.7.2 mono（`godot-mono.console.exe`）+ `.packages/gdunit` 固定测试包（随迁或按 `docs/testing/gdunit-source-packages.md` 重备）
- [ ] 随迁 `C:/evidence/m6/207-repair-20261114/`（含 #238 候选 patch：`m6c-tickets/238-candidate-uncommitted.patch`，584 行/12 文件，实现完整未验证）
- [ ] gh CLI 已登录（票务操作需要）
- [ ] 模型：`glm-5.3`（provider `xdt`；不可用时实测后换 `deepseek-v4-pro`——不要凭记忆判断，用 curl 打 `/v1/chat/completions` 验证）

## 执行顺序（#238 之后）

1. **完成 #238**：LRA resume（命令在 FINAL-HANDOFF 第五节）。可让 LRA 重做（票面自足），或参考候选 patch。验证面：unit 全工程 + 触碰矩阵文档（guarded）时先完整 native-map 无 filter 重建基线。
2. **验收关闭 #129/#134**（模式 = #166/#168 先例：核对子票交付 + 实质承载，发验收评论后 close）。#134 的第 5/6 项由 #235/#236 承载；#129 的剩余由 #237/#234 承载。
3. **父规格审查自动触发**（全部 accounted 后 LRA 自动跑 spec 通读）——passed 则进入下一步；blocked 则按缺口处置（此前 7 类缺口已有处置先例，见 FINAL-HANDOFF 票务记录）。
4. **重跑完整门**：`scripts/Test-M0.ps1 -GodotPath <path> -Scope committable -EvidenceDirectory <新目录>`，绑定最终 HEAD。
5. **收敛报告** + 收尾缺口如实登记（GPU/人工/真实资产/远端 CI 均有既定归属，见 FINAL-HANDOFF 已定决策）。

## 高频坑（详情见 FINAL-HANDOFF「卡点速查」）

- 引擎批 300s 挂起（OutputDrainTimeout，本机 4 次）：清 `.godot` 缓存 + `app_userdata` 可再生产物 → 复现验证 → 票评论留证 → resume。新机器干净环境大概率不复发。
- 看门狗子代理会静默死亡（provider 503 时通知丢失）：叠加日志 mtime 自检（>30 分钟未变即人工检查）。
- 关闭未执行票：必须同步清 GitHub 依赖（REST `DELETE .../issues/<N>/dependencies/blocked_by/<db-id>`），否则 LRA 假依赖死锁。
- review verdict 丢失（模型输出无 JSON / evidenceKey 格式错）：读会话 jsonl 提实质结论，有效 finding 由协调者转正式评论再 resume。

## 权限边界（新会话开始时向操作者确认的默认值）

push 已授权过一次（分支推送已完成）；**PR/合 main/真实资产准备/人工验收代行**仍需操作者明确授权。full 范围真实资产重建未立项——M6 收敛后询问操作者。

## Suggested skills

- `code-review`：#238 候选 patch 若人工采纳，合并前按 Standards/Spec 双轴审查。
- `diagnosing-bugs`：若新机器复现引擎批挂起等环境问题，按其诊断循环定位。
- `herdr`：若操作者在新机器使用 Herdr 多面板编排（需 `HERDR_ENV=1`）。
- 一般不需要 `research`/`prototype`——剩余工作是既定路径执行。

（敏感信息已省略：provider API key 不入档，需要时从操作者处获取或读本机 models.json。）
