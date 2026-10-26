# Issue tracker: GitHub

规格与任务存放在 `fanxiao92/godot-pal` 的 GitHub Issues。
使用 `gh` CLI 操作，显式指定仓库，避免依赖本地 remote 配置。

## 常用操作

- 创建：`gh issue create --repo fanxiao92/godot-pal --title "..." --body "..."`
- 读取：`gh issue view <number> --repo fanxiao92/godot-pal --json number,title,body,labels,comments`
- 列表：`gh issue list --repo fanxiao92/godot-pal --state open`
- 评论：`gh issue comment <number> --repo fanxiao92/godot-pal --body "..."`
- 标签：`gh issue edit <number> --repo fanxiao92/godot-pal --add-label "..."`
- 关闭：`gh issue close <number> --repo fanxiao92/godot-pal --comment "..."`

多行正文优先通过 `--body-file` 传入。

## 技能操作约定

- “发布规格或任务”：创建 GitHub Issue。
- “读取任务”：读取 Issue 正文及评论。
- 规格与已批准的实施任务使用 `ready-for-agent` 标签。
- 子任务优先使用 GitHub 原生 sub-issue 关系；不可用时，在子任务正文中记录 `Part of #<parent>`，并在父任务中维护任务列表。
- 阻塞关系优先使用 GitHub 原生 issue dependencies。API 需要数据库 ID，而不是 Issue 编号：查询 `repos/fanxiao92/godot-pal/issues/<blocker>` 的 `.id`，再向 `repos/fanxiao92/godot-pal/issues/<child>/dependencies/blocked_by` 提交 `issue_id`。
- 原生依赖不可用时，在任务正文中记录 `Blocked by: #<number>`。
- 只有所有阻塞任务均已完成的任务才可开始。
- API 不支持、无权限或调用失败时，说明实际情况，不声称操作成功。

规划多票规格、长会话或多代理交接时，参考[多票长任务的信息分工](large-task-handoffs.md)：协调者工作集、收据交接、等待与恢复建议；不改变此处的任务系统、授权或项目验收要求。

## Wayfinding

- 决策地图使用 `wayfinder:map` 标签。
- 决策子任务使用 `wayfinder:<type>` 标签，类型为 research、prototype、grilling 或 task。
- 子任务、阻塞关系遵循上述约定。
- 开始处理时认领任务；解决后记录结论，关闭任务，并将结论摘要与链接回填至地图。
- 选择没有未完成阻塞项、尚未被认领的任务推进。

## Pull requests as a triage surface

PRs as a request surface: no.

## 操作前提

需要可用的 `gh` CLI、GitHub 授权及目标仓库权限。
缺少前提时报告阻塞，不擅自改用其他任务系统。
配置 Git remote 或 SSH 推送成功不能替代 GitHub API 授权。
