# Game 测试当前入口

Game 是[默认 Unit 反馈](../../../docs/testing/getting-started.md)之后的按需路径，不是第一阶段自动 CI 或测试合并要求。本页只维护当前 Game 测试入口、覆盖口径与失败判定；Configure→立即入树→输入→生产更新完成→清理的示例只在[Game 专题](../../../docs/testing/game-test-layers.md#新人最小模型仅按需进入-godot)维护，不复制第二套规则或命令。职责选择见[测试职责矩阵](../../../docs/testing/test-layers.md)，初始化、SceneRunner 生命周期、时序边界和完整门规则见[Game 测试分层](../../../docs/testing/game-test-layers.md)。旧命令、变异、性能对照、M2/M3 交付年表与历史失败见[历史运行记录](../../../docs/archive/m3/game-test-run-history.md)。

## 当前入口与覆盖

Game 测试继续使用现有 Godot 程序集、固定 GdUnit 包和 `tests/Pal.Game.Tests/game.runsettings`。真实版本、bootstrap 和包来源以[当前包消费说明](../../../docs/testing/gdunit-source-packages.md)及仓库配置为准。测试资源是本例自有、忽略的 native fixture；真实资源和私密证据不进入提交或上传。

共享完整门仍唯一使用 `scripts/Test-M0.ps1`，依赖 Core→CLI→Prepare→Game 的串行顺序和当前脚本生成的批次/身份。当前库存和批次从 `scripts/GameTestBatches.ps1` 与测试定义核对，不在本页复制案例计数；已完成运行见历史索引并保持原身份。保留完整门实现不证明当前 M4 候选的完整门运行兼容性已验证。

## 选择运行范围

- 新增/迁移场景行为：默认选 SceneRunner，遵循真实 Main 初始化、`UpdateCompleted` 完成边界、输入释放、finally 清理和资源隔离。
- argv、READY、诊断、退出码或必要进程隔离：只选对应最小真实进程契约。
- 精确业务时间：运行 Core 显式时间测试；Game 等待仅是活性保护。
- 完整候选：只调用共享 `scripts/Test-M0.ps1`，不将局部 TRX、静态 discovery 或历史结果拼成全门。

定点运行使用[显式范围入口](../../../docs/testing/range-regression.md)，而非从历史命令墙取命令。下例只核对当前参数定义，未因文档维护实际执行，不是运行证明，也不授予运行授权；两个变量须预先指向批准的 Godot console 和已验证的四文件 bundle，证据目录须不存在：

```powershell
& ./scripts/Test-Range.ps1 -Scope game -GameBatch process `
  -GodotPath $ApprovedGodotConsole -FixedPackageDirectory $VerifiedFourFileBundle `
  -OutputDirectory $NewEvidenceDirectory
```

该入口设置并恢复 `GODOT_BIN` / `PAL_TEST_GODOT`，不隐式运行未选的 Prepare；需要 Prepare 时显式加入 Scope，按固定顺序先于 Game。绕过入口直接 `dotnet` 时，必须先 bootstrap、设置这两个批准引擎路径变量并完成当前所需构建/import。实际 SDK/Godot/GdUnit/Prepare/solution 运行遵守根 AGENTS 的全局串行规则，完整参数以脚本/config 为准。

## 失败与证据

检查实际数字退出码、TRX Summary/RunInfo、Results/Definitions/Entries、发现身份和完整输出；部分 Passed、`aborted=0`、READY 或一条 PASS 日志都不表示完成。中止、崩溃、未知退出、输出不完整、缺结果、非预期 skip 或指定失败原因不符时，保存原件并停止，不自动重试、续跑、提高时限或放宽断言。按[当前异常处理规则](../../../docs/agents/test-diagnostics.md)分类；历史症状只在其明确指针触发时读取。

headless/SceneRunner、GPU、物理键盘/桌面人工和远端 CI 是互补证据，不能相互代替。M3 当前交付状态和历史限制见[M3 验收检查点](../../../docs/milestones/m3/acceptance.md#当前状态)；这不是新的运行结论。

## 维护边界

保持 `Main.tscn` 的真实入口和现有测试装配，不新增第二 Godot 项目、替代业务实现或内部测试 seam。变更时先冻结独立预期与完整身份，再同步脚本/config/本页；需要时序变异、输出捕获或完整门诊断，进入 Game 分层与当前异常规则，不在本页重写历史证据。

### #46 Story package prepare bounded checks

`PrepareStoryPackageTests` 是 native-map 中的 direct31：真实 public Prepare → typed Save → Story/Visual strict Load、完整events/hash、UTF8/schema/limits/路径/无覆盖/owned cleanup。`StoryPackagePrepareProcessTests` 增10例真实 Editor/wrapper 契约；Prepare严格总数现89，Game metadata为505（502 positives，串行74/85/343）。这些数字是库存，不表示本轮跑了全部Game；本轮只新类31和完整Prepare89。前置依赖、schema与正常命令见[生产准备文档](../../../docs/design/story-package-preparation.md)。无Main/input/动作/effect/GPU/人工/CI或完整可见人工作者包交付声明。

### #46 authored package B

`AuthoredBattlePackageTests` adds two native-map cases (Win/Loss): committed original author data → normal public numeric/story Prepare → both languages strict Story/Visual Load → literal search, indexed colors/Texture2D, nonempty glyphs, complete source hash and real public Core outcome/owned receipt/branch stop. Inventory now 507 (504 positives; serial 74/85/345). Normal wrappers also prepare these exact inputs; see [sample instructions](../../../../samples/authored-battle/README.md). This is data/preparation evidence, not Main, SceneRunner, GPU, manual or CI acceptance. Earlier A direct31/Prepare89 evidence is reused, not rerun.

### #46 normal Main roundtrip C

`StoryRoundtripSceneTests` adds nine scene-dialogue cases, including Win/Loss × both languages through real Main search/keys, separate Attack/Target confirmations, actual action/display completion observations, owned result receipt, complete stop/return, same-URI isolation and cleanup. `StoryMainProcessTests` adds seven real argv/preload/READY/exit representatives. Core adds two actual-call binding tests (final inventory782). Game inventory is now523 =520 positives +3 controls, serial81/94/345. `GameBatches.Tests.ps1` remains16 offline metadata groups, not runtime evidence. See [C contract](../../../docs/design/main-story-roundtrip.md) and [fresh preparation/launch](../../../../samples/authored-battle/README.md). B Words/font changed; old prepared hashes are intentionally not rewritten. New owned outputs are required. This is not full Game/solution/fullgate, D effects/animation fidelity, GPU/manual/CI or #46/M4 completion.

#### C independent-review repair

Four additional Core regressions (inventory786) cover completed-call replay, older/foreign/pre-search calls and failed Start identity preservation. Typed story Start now requires the actual pending interaction origin. Two additional scene-dialogue cases cover deterministic Game-layer40ms pose boundaries and active-feedback Main removal/owned overlay cleanup; actual roundtrip observers verify each pose transition and second-search Start origin. Inventory525 =522 positives +3 controls, serial81/96/345. These counts describe registration, not a full-gate run.

### #46 enemy reference presentation D1

Two new Story scene-dialogue cases cover delayed displayed enemy-impact HP/result and independent physical-action tables (magic0/1/3, attack0/2, wait0/2, hit/evade, inclusive indices, recoil/return, dying threshold and indexed shift). Existing real Main observers additionally check production impact/release ticks and cleanup. Inventory527 =524 positives +3 controls, serial81/98/345; metadata remains16 groups. No Core/native schema/Prepare/author inputs changed. D1 does not close player effects, enemy idle-speed/counter binding, floating-number fidelity, GPU/manual/CI/full gate or #46/M4; see the [bounded contract](../../../docs/design/main-story-roundtrip.md#reference-presentation-checkpoint-d1-enemy-active-physical-action).


### #46 D2-A resource binding

Fourteen NativeVisual cases and seven public Story Prepare cases add optional effect/speed format-vs-capability, no-wrap int triples, zero idle speed, exact effect hash closure, retained checked planes, strict author schema and old-input compatibility. The two authored B cases additionally require the capability and literal original effect pixels. Inventory548 =545 positives+3 controls; serial81/98/366, metadata16 groups. NativeVisual63 and direct Story Prepare38 are the matching ranges; complete Prepare remains89. These are resource/preparation checks, not Main playback, an idle counter, floating feedback, GPU/manual/CI/fullgate or #46/M4 completion. Core/Main/clock are unchanged.
