# 测试职责矩阵

本页是按**契约与依赖**选择测试层的短入口；它不按耗时分类，也不替代各层的完整操作规则。先确认当前 Issue、工作树和工具链，再按本表进入唯一适用的细则。

| 层 | 适用契约 | 依赖与运行时机 | 能证明 | 不能证明 | 细则 |
| --- | --- | --- | --- | --- | --- |
| Core / CLI unit | 公开业务规则、精确时间；内存算法和纯数据变换 | 普通 .NET 项目；可用 `scripts/Test-Unit.ps1` 做显式 unit 选择 | 独立预期下的状态、边界、回绕、释放与重调度 | 文件/进程、Godot 接线、资源导入、桌面显示 | [unit 入口](unit-entry.md)；调整 CLI 项目边界再读[迁移说明](cli-test-migration.md) |
| CLI integration | argv、真实退出/诊断、文件系统和已构建 CLI 产物 | `Pal.Extract.IntegrationTests`；先构建，再显式范围运行 | CLI 文件/进程契约、退出码、输出和清理 | Godot 场景、GPU、完整门覆盖 | [范围回归](range-regression.md)；调整项目边界再读[CLI 迁移](cli-test-migration.md) |
| Prepare | 原生资源准备、导入和拒绝诊断 | 共享完整门中先于 Game；使用批准工具链和人工资源 | 准备产物、解析/导入失败传播及输入安全 | 场景树业务、GPU、物理桌面 | [范围回归](range-regression.md)、[Game 分层](game-test-layers.md) |
| Game SceneRunner | 真实 Resource/PackedScene、输入、信号、更新完成边界 | 固定 GdUnit；默认嵌入真实 Main，逐例隔离并在 finally 清理 | 引擎接线、场景业务状态、真实更新完成后的快照 | 精确业务时钟、GPU 像素、OS 物理输入和完整进程启动 | [Game 分层](game-test-layers.md) |
| 真实进程 / 完整门 | argv、READY、启动诊断、退出、必要进程隔离；最终候选覆盖 | 最小进程契约按需运行；唯一完整门为 `scripts/Test-M0.ps1`，Core→CLI→Prepare→Game 批次串行 | 原始退出、TRX/RunInfo/身份完整性、指定负控制和启动链 | 单次局部通过不能替代全门；headless 不能替代 GPU/人工桌面/远端 CI | [Game 分层](game-test-layers.md)、[CI 分流](ci-dispatch.md) |

## 选择顺序

1. 先写出要证明的契约和独立预期；在能直接观察该契约、依赖最少的适用层断言。独立业务规则与跨层接线分别覆盖，不用一层替另一层签字。
2. 业务时间留在 Core 的显式时间接口；真实输入、信号和资源装配进入 SceneRunner；argv、退出和诊断保留真实进程。
3. 需要迁移或改写 Game 集成测试时，先读 [Game 分层与 SceneRunner](game-test-layers.md)。需要 CLI 项目/文件集成时，读 [CLI 迁移](cli-test-migration.md)。固定 GdUnit 包只在还原、排查或获准运行 Game 时读取[包说明](gdunit-source-packages.md)。
4. 完整验收只调用共享入口，不从局部命令拼出成功；失败、取消、中止、未知退出或结果不完整时保留原件并停止。

### 共同证据边界

预期来自规格、迁移前冻结清单或独立字面值，不从被测结果反推。结果须绑定实际源码/候选身份、退出码、TRX Summary/RunInfo 和完整结果图；显示名、静态计数或一条 PASS 日志不能替代案例身份。当前状态、操作规则和历史证据分别维护：历史链接只在排障、追溯或明确反例触发时读取。

仓库架构、安全、隐私、全局串行、未知进程与失败停止规则仍以根目录 [AGENTS.md](../../AGENTS.md) 为不可绕过的权威来源；本页只负责选择入口，不缩减这些规则。

涉及业务时间、输入更新边界、相关断言、Game 迁移或调度偶发诊断时，必须先读[时序验证细则](time-boundaries.md)。普通选层不需要加载其长细则或历史时间线。
