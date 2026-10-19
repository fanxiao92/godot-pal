# Domain docs

采用 single-context 布局：
- 根目录 `GLOSSARY.md`：项目术语。
- `docs/adr/`：架构决策记录。

## 读取规则

探索相关代码前，读取术语表及与任务相关的 ADR。
如果这些文件尚不存在，直接继续，不因缺失而要求先创建。
术语与决策在实际澄清后，由 domain-modeling 工作流按需记录。

项目设计背景位于 `docs/design/`，阅读入口见 `docs/README.md`。实现任务前读取对应 GitHub Issue 正文、评论及 `docs/milestones/<里程碑>/plan.md`；如果计划尚不存在，先编写计划再编码。
`docs/archive/` 仅在排障、环境升级或追溯历史结论时读取，不作为默认必读文档。当前技术版本与常用命令见根目录 `README.md`；检查由测试及 CI 执行，任务结果记录在 Issue 中，仅对值得长期保留的特殊调查另写记录。

## 用词与冲突

- 使用术语表已经定义的领域名称，避免随意替换为同义词。
- 遇到真实术语缺口时，交由 domain-modeling 澄清。
- 如果拟议方案与已有 ADR 冲突，明确指出冲突，不静默覆盖既有决策。
