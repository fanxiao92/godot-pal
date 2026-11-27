---
name: 验证工单（Verification）
about: 指定 PR/分支与验证档位，由 agent 或人工 QA 使用 pal.ps1 执行并回填标准证据评论
labels: verification
---

## 验证目标
- **目标 PR / 分支**：#___ / `branch-name`
- **基线提交（HEAD SHA）**：`_______`
- **要求档位**：
  - [ ] T1（本地全量复现）
  - [ ] T3（真实资源 + GPU 截图与金图比对）
- **重点关注场景 / 回归项**：（留空表示按档位默认集；T3 默认 5 场景：`boot-and-explore`、`real-dialogue-bilingual`、`battle-closure`、`save-load-restore`、`dynamic-scene`）

## 执行命令（agent 与人工 QA 共用）
1. 检出目标提交并确认工作树干净、无冲突 Godot 进程。
2. 若跑 T3：设置 `PAL_RAW_DATA_DIR` 后执行：
   ```pwsh
   pwsh -NoProfile -NonInteractive -File ./pal.ps1 assets prepare
   ```
3. 执行验证：
   - T1：`pwsh -NoProfile -NonInteractive -File ./pal.ps1 test full --out .artifacts/pal/verify-t1`
   - T3：`pwsh -NoProfile -NonInteractive -File ./pal.ps1 test e2e --gpu --out .artifacts/pal/verify-t3`
   - 单场景调试：`pwsh -NoProfile -NonInteractive -File ./pal.ps1 test e2e --gpu --scenario <name|path.yaml>`
4. 生成标准 PR 证据评论：
   ```pwsh
   pwsh -NoProfile -NonInteractive -File ./pal.ps1 report --from <out-dir> --out <out-dir>/pr-comment.md
   ```
5. 将 `pr-comment.md` 内容贴到目标 PR 评论区（**禁止上传真实资产、版权截图或私密本机路径**）。
6. 人工 5 分钟审阅（T3）：
   - [ ] 打开本地 `<out-dir>/contact-sheet.png` 核对画面无花屏/错位/字体缺字；
   - [ ] 核对探针断言与金图 diff % 均在容差内。
