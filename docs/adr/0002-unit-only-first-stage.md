# 第一阶段采用 Unit-only 默认反馈与测试合并门槛

为降低 work agent 的依赖和反馈编排成本，按[规格 #69](https://github.com/fanxiao92/godot-pal/issues/69) 采用直接项目级、默认全量的 Core/CLI Unit 反馈，并以独立 `unit-check` 满足第一阶段的测试合并要求，而非默认执行 solution 或重型完整门。选择依据是纯内存规则可以独立反馈，不是承诺固定耗时；具体协议由[短指南](../testing/getting-started.md)及其专题维护。

后续用户在 #69 复盘中批准[原生反馈方案 B（ADR-0003）](0003-native-unit-feedback.md)：Unit-only 覆盖决定不变，默认证据/成本改为原生基线，强审计转为显式维护。该后续调整不改写 #69 的历史交付或远端结果。

## 代价与保留

Unit 不能证明文件/进程、Prepare/import、Godot 场景/资源/输入接线、GPU 或物理桌面契约；暂停自动 Source/Offline、Integration、Prepare、Godot 和完整验收，意味着合并时主动接受这些覆盖缺口。保留 `Test-M0.ps1`、显式维护工具和原始历史证据，供另行授权的分层调查与完整验收使用；保留实现不证明更名后完整门的运行兼容性已验证，也不以历史通过补签新候选。

## 决定与生效分开

[#67](https://github.com/fanxiao92/godot-pal/issues/67) 已分别取得本地契约、真实远端事件、保护回读及普通拒绝/允许证据，并由 [PR #70](https://github.com/fanxiao92/godot-pal/pull/70) 合入 main；这只是该交付检查点，后续实时状态以[保护说明](../testing/merge-protection.md)及服务端为准，不保证所有主体或权限例外都不可绕过。

恢复重型自动验收或完整门合并要求须另立任务：验证工具链、候选绑定、重复运行、指定失败 controls 和完整证据，再分别批准 CI 调度与保护切换。局部通过、保留脚本或独立 Godot 程序集 prototype 均不自动触发恢复。
