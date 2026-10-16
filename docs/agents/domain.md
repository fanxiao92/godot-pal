# Domain docs

采用 single-context 布局：
- 根目录 `GLOSSARY.md`：项目术语。
- `docs/adr/`：架构决策记录。

## 读取规则

探索相关代码前，读取术语表及与任务相关的 ADR。
如果这些文件尚不存在，直接继续，不因缺失而要求先创建。
术语与决策在实际澄清后，由 domain-modeling 工作流按需记录。

现有 `docs/01-feasibility.md` 至 `docs/04-decisions.md` 继续作为项目设计背景；M0 工作还应读取 `docs/05-m0-implementation-plan.md`。
本次配置不迁移或重写已有设计文档。

## 用词与冲突

- 使用术语表已经定义的领域名称，避免随意替换为同义词。
- 遇到真实术语缺口时，交由 domain-modeling 澄清。
- 如果拟议方案与已有 ADR 冲突，明确指出冲突，不静默覆盖既有决策。
