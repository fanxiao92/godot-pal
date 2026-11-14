# Fresh session 执行环境契约

## 开工核对

1. 使用中文反馈，命令、标识符和原始诊断保留原文。先核任务指定的 cwd、repo/branch/HEAD、index 与已有改动；保全已有工作，未知 writer 或重叠变更先暂停确认。
2. 从 host 提供的本次环境记录核对 OS、cwd、运行时和实际工具能力；普通 Pi 会话没有该记录时，用当前工具的原生查询取得所需事实。版本与可执行路径以当前配置/host 为准，不从历史会话或本文猜测。能力存在不等于本次获准执行。
3. 先读任务材料、适用 AGENTS、目标实现及直接调用方。接口不熟悉时读实际定义及官方本地文档/示例；有未批准的行为、范围或权限决定时向协调者请求决定。

## 原生执行与编码

- 优先直接调用已有入口，参数按实际白名单传递。步骤多或采证不是新增通用 wrapper 的理由；LRA 仅使用所提供的受限工具，原生命令由 host 的 `io.ts` 以 `shell:false`、独立 argv 执行，文本按 UTF-8 读取，检查双流保留原件。
- Windows PowerShell 脚本优先 `pwsh -NoProfile -NonInteractive -File <script> <args>`。含空格路径、Unicode 与每个参数保持独立，不跨 Bash/PowerShell/Python 多层拼接长脚本。普通文档直接 write，已有文件用精确 edit 保全原内容。
- 确需批量校验/结构化转换时，先写局部临时脚本再执行；不用 heredoc/stdin 喂解释器，不为展示原生日志引入 Python 或强制 Python 依赖。
- PowerShell 数组 JSON 用 `ConvertTo-Json -InputObject $rows`，确保零/一/多项均为数组。Godot 进程检查复用 LRA `engine-processes.ps1`（仅 PID/Name、显式 UTF-8）；仅在本次引擎授权与适用守卫流程中调用，不以环境采集为由探测或启动引擎。
- Unicode 输出直接保存原件；控制台显示编码问题与被测命令失败分开报告。采证只列所需字段，不转储环境变量、凭据、私人全局上下文或全机进程详情。

## 权限与停止

权限来自本次任务，环境记录和项目配置不新增 shell、引擎、委派、提交、远端写或安装升级授权。正常完整失败按[单任务有界纠正](task-execution.md)先保全原件再定位；崩溃、中止、超时、provider 错误、未知退出/副作用或输出不完整立即停止，不能用换会话、重跑或弱化断言恢复。

## 装配边界

- 普通 pi-subagents `worker` 经项目 `.pi/settings.json` 的逐字段 override 保留 `context.md` / `plan.md` 并增读本文；保留内置 fresh、模型与工具，继承项目规则，不启用私人 global context。项目配置需获 Pi project trust；重载/新会话后生效。`defaultReads` 仅把存在的文件路径写成 `[Read from: ...]` 指令，不自动注入文件内容；调用方可覆盖 reads，AGENTS 的开工必读指针仍适用。这不是 OS 或不可绕过的安全强制。
- LRA GitHub coding executor 将本文内容及 host 白名单环境记录经 SDK `agentsFilesOverride` 加入实际 system prompt，并在每次 implement / Standards / Spec 会话目录保存 `environment-contract.txt`、`environment.json`。它不替换既有 AGENTS 发现、默认 provider/model 或 fresh 语义，也不扩展旧显式计划模式。
