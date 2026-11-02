# Game 测试分层与 SceneRunner

本页是 Game 的**当前操作规则**。新增或改写 Game 集成测试、迁移进程用例时进入；历史失败、变异、性能对照和交付年表见[历史记录](../archive/m3/game-test-history.md)。两者不是两套规则：本页保留当前可执行的契约，历史页只保存原身份和结论。

## 新人最小模型（仅按需进入 Godot）

先完成[短指南](getting-started.md)的 Unit 反馈；只有目标契约需要真实引擎接线时才进入本页。Game `[RequireGodotRuntime]` 由 VSTest → 固定 GdUnit adapter 启动 Godot .NET runtime，不是普通纯内存 Unit；引擎路径、bootstrap 与实际命令只由 [Game README](../../src/Pal.Game/Tests/README.md)维护。

```text
本例 GUID Resource/PackedScene → new Main → Configure + 订阅（入树前）
  → Load(main, autoFree:true) → 立即 AddChild / _Ready → 真实资源加载/装配
  → InputEventKey → _Input → 实际 _Process → UpdateCompleted 内采样
  → release 自有输入 → unsubscribe → Dispose → 验证节点释放 → 恢复全局 → 删除自有 fixture
```

以下节选现有 [`ExplorationSceneTests.ExplorationScene`](../../src/Pal.Game/Tests/ExplorationSceneTests.cs) 构造器，不是独立可粘贴程序；`fixture`、字段和回调的定义/失败清理同在该文件。本次只核对定义与固定源码，未执行 Game 示例，不据此声明当前候选的 Godot 测试通过。

```csharp
Main.Configure(GameSceneConfiguration.FromArguments("--m2-resources", fixture.ResourceRoot,
    "--m2-profile", fixture.Uri));
Main.Failed += OnFailed;
Main.UpdateCompleted += OnUpdated;
runner = ISceneRunner.Load(Main, autoFree: true);
```

`Load` 立即入树而非延迟启动，所以配置和订阅都在前。这里嵌入 `new Main()` 是为了使用公开的入树前配置；fixture 的地图 PackedScene 仍经生产 loader 加载，不是另一套装配。正常 `Main.tscn` / `GameStartup` 的 argv、READY、退出契约继续由最小真实进程覆盖。

## 按验证职责选层

| 职责 | 首选入口 | 必须证明 |
|---|---|---|
| 纯业务规则、精确时间 | Core 公开构造、输入和显式时间更新接口 | 截止前/截止时、回绕、晚到不补步、释放与重新调度；预期来自独立规格或已核对字面值 |
| 原生资源装配、真实输入/信号/更新连接、场景交互 | 固定版 GdUnit4Net SceneRunner，承载真实生产场景 | 实际输入到达、对应生产回调完成时的业务状态及清理后下一场景可用 |
| argv、真实启动诊断、退出码、READY 顺序和必要进程隔离 | 正常游戏入口的最小代表性真实进程测试 | 原诊断上下文、实际退出及不应出现的 READY；不能以解析器抛异常或场景初始化代替 |

新场景行为测试默认采用 SceneRunner，而非每个变体另启 Godot 子进程。确需进程测试时，写明其独有的进程契约。CLI、Prepare/import 的真实进程职责保持；SceneRunner 的框架 editor/runtime host 仍有启动成本，“无额外业务子进程”不等于零进程。

## 共用真实初始化

- 游戏场景与测试共用资源加载、校验、装配、输入、更新及失败传播；测试宿主只配置、交付输入、观察和清理，不维护另一套装配或状态推进实现。
- 嵌入 Main 时依次 `new Main()` → `Configure(GameSceneConfiguration.FromArguments(...))` → `ISceneRunner.Load(main, autoFree: true)`。配置只能在入树前调用一次；普通 `Main.tscn` 上的 `GameStartup` 负责真实 argv、窗口策略和失败后的诊断/exit1。
- Main 的首个 `Failure` / `Failed` 统一报告初始化和运行失败，并停止相关更新；场景不退出宿主。测试必须把意外失败判为本例失败，不能只等待 READY。宿主拥有成功或部分初始化的整个场景，失败/移除后的节点不复用。
- 复用公开行为接口；测试模式开关、全局 argv 替换、私有状态反射、手调生产 `_Process` 或绕过校验不能作为真实引擎接线的证据。

## 每例生命周期

1. **先核固定语义。** 版本与 bootstrap 以[包说明](gdunit-source-packages.md)、当前配置为准；使用前读取对应固定源码的 Load、输入和 Dispose 定义，不套用 latest 文档。`Load` 会加入树，`autoFree` 必须明确。
2. **创建独立场景。** 使用本例拥有的唯一人工资源身份和新节点；加载前完成配置，观察初始化失败。真实原始/派生资源保持只读复用，不进入提交或证据上传。
3. **交付真实输入。** 历史 M2 保留失焦冻结/清键与回焦 fresh-press；M3 当前共享探索不复制该额外重置。需要验证信号时核实际交付，不猜 OS HasFocus 或私改 Core；信号注入不等于物理焦点验收。按生产入口使用 `InputEventKey` 等实际事件；echo、逻辑/物理键位语义按用例明确验证。`InputEventAction` 不替代只接收 Key 的处理链。runner 仅跟踪自己注入的输入，自行注入的按键由本例显式释放。
4. **在实际边界断言。** 从生产 `UpdateCompleted` 等已核实的完成事件同步保存状态。第一次有效输入后的首个实际更新必须无条件检查，不先等待 walking 等“正确状态”再采样；frame-wait 返回、日志顺序或协程恢复不证明目标回调已完成。断言异常需留在测试失败通道，不误报为 Main 的业务失败。
5. **在 finally 清理。** 释放自有输入、断开订阅、Dispose runner 并核对节点已释放；部分初始化失败也释放整个 owned 场景。恢复本例改动的窗口、焦点和引擎设置，只清理本例 GUID 人工产物。
6. **核对共享状态隔离。** 新 Node 不保证新 Resource；必要时让新场景复用同 URI 验证初始状态及首次交互，并检查原资源身份未变。按所有权管理可变资源，不依靠全局清 ResourceLoader 缓存、清 InputMap 或删除其他资源掩盖污染。

现有入口：[`MainSceneTests`](../../src/Pal.Game/Tests/MainSceneTests.cs)、[`ExplorationSceneTests`](../../src/Pal.Game/Tests/ExplorationSceneTests.cs)。具体 Game 命令、当前覆盖和失败控制见 [Game 测试说明](../../src/Pal.Game/Tests/README.md)。

## 输入、等待与资源所有权

固定源码链接由[包说明](gdunit-source-packages.md#固定-scenerunner-源码语义)维护。runner 的模拟按压 API 才进入其输入跟踪集合；本仓上述示例直接 `Input.ParseInputEvent` + `FlushBufferedEvents`，因此显式 `SendKey(false)` 由本例负责，不能指望 runner reset 代放。`OnUpdated` 首个完成无条件断言，再推进已有测试状态机；其断言异常先捕获，回测试协程重新抛出，避免穿过 `Main._Process` 被包装成 production failure。

| 边界 | 固定版定义／当前用法 | 不可替代 |
| --- | --- | --- |
| `AwaitIdleFrame()` | 等待下一 process frame；示例仅用于 yield | 目标业务回调已完成 |
| `AwaitInputProcessed()` | 可处理场景先 flush，再等 process 和 physics frame | 应用级更新完成或精确业务时间 |
| `Main.UpdateCompleted` | 生产 `_Process` 在业务更新/渲染计划提交后发出的完成事件；回调内同步采样 | GPU 像素已呈现；display 自有完成边界 |

新 Node 不等于新 Resource：默认同 URI 加载可能复用缓存。示例用 GUID fixture 隔离文件，并刻意让第二个新 Main 复用同 URI，检查首步/初态、资源字段与文件 hash 未污染；可变资源按所有权管理，不全局清缓存或 InputMap。runner 默认 `autoFree=false`，本例明确 true；Dispose 后核对 `IsInstanceValid==false`，部分初始化也兜底释放整个 owned 场景。窗口、时间倍率、physics ticks 和 focus 等本例改动的全局值仍由宿主恢复；runner reset 不替代该责任。

## 时间与错误捕获

通用时序契约先读[时序验证细则](time-boundaries.md)。精确业务时间留在 Core 显式时间测试。固定版 `SimulateFrames` 双参数包含真实 `Task.Delay`，不是虚拟 delta；TimeFactor/Engine 倍速也不等价于推进单调业务时钟。SceneRunner 的有界等待只防挂起：观察到更新但状态错误是功能失败，期限内未观察到必要更新是活性问题，两者均不能计为通过。引擎调度延迟不能替代 Core 的 deadline 前/时/晚到边界；集成层只证明真实输入到达及对应更新完成边界。

遇到时序变更、调度偶发、输出捕获、断言变异或 Game 迁移时，先对照根 [AGENTS 测试规则](../../AGENTS.md#测试)和[测试职责矩阵](test-layers.md)，再进入[历史诊断与复现记录](../archive/m3/test-diagnostics-history.md)中明确触发的症状。迁移或替换时序断言前列出旧断言到新层的映射，预先冻结最小错误反例与指定失败原因。变异必须逐例运行、逐例恢复，既核源码也核实际运行目录 DLL/PDB；必要时显式 Rebuild，源码字节/mtime 恢复不证明运行产物恢复。指定失败原因、退出码和原日志必须保留。不要以提高超时、重复运行、崩溃或编译失败替代目标错误捕获。

### 失败分类（不能互相代签）

| 类别 | 识别边界 | 记录与处理 |
| --- | --- | --- |
| assertion | 已观察到目标完成，状态与独立预期不符 | 功能失败；从回调交回测试失败通道，不伪装成 `Main.Failure` |
| production failure | `Failure` / `Failed` 出现在初始化或运行中 | 保留精确生产诊断、停止状态并释放 owned 场景；SceneRunner 不要求宿主退出 |
| liveness / 未完成 | 有界等待内未观察到必要事件/更新 | 记录缺失边界；不是业务冷却失败的自动结论，更不能通过 |
| session / adapter | 崩溃、中止、connect/compile timeout、未知退出、TRX/输出不完整 | 保存原始退出/双流/TRX/RunInfo 并停止；不以任意非零代替指定负控制 |
| Prepare / resource import | 准备、导入或资源身份/格式不符 | 保存输入身份和工具诊断，与场景 assertion 分开；获准完整门仍先 Prepare 后 Game，不自动重建真实资源 |

通用停止顺序以[当前诊断规则](../agents/test-diagnostics.md)为准。headless 引擎接线、GPU 回读、程序输入和物理桌面/人工验收分别留证；一次成功不互相覆盖。

## 完整门边界

Unit-only 第一阶段不默认运行完整门，也不以完整门作为当前测试合并要求。仅显式选择完整验收时，唯一入口是 `scripts/Test-M0.ps1`，按当前脚本与 runsettings 选择 Core、CLI、Prepare、Game 批次并全局串行。Prepare 必须先于 Game；Game 的分批、发现身份、TRX Results/Definitions/Entries、RunInfo、控制失败和 startup 阶段必须按实际源码/脚本核对。任一中止、崩溃、未知退出、输出不完整、缺结果或非预期失败均保留原件并停止，不拼接旧轮、不自动重试或续跑。

当前 dotnet 进程边界见[生产定义](../../scripts/DotnetCheckedDefinition.ps1)及[定点回归](../../tests/Pal.Game.Tests/DotnetBoundary.Tests.ps1)：root 与双流 EOF 共用工作预算，失败后只给共享清理预算；root exit0 不代表输出完整或后代退出，未知退出保持 null。增量保留两路原始字节，完整后才扫描诊断；取消、退出已知、输出完整与清理完成分别记录，只处置自有进程。持久回归的外护栏不证明 OS/I/O 硬实时，也不授权放宽生产预算。

其余完整批次、历史预算值、TRX 身份图、成本/性能口径和历史交付记录保留在[历史记录](../archive/m3/game-test-history.md)；当前命令与覆盖以 Game README 和脚本配置为准，不复制会过期的案例清单。
