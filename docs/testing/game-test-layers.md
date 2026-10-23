# Game 测试分层与 SceneRunner

新增或改写 Game 集成测试、迁移进程用例时先读本页。选层、初始化、隔离与验收规则在此完整列出，无需查阅历史任务正文；执行前另按 [AGENTS 测试规则](../../AGENTS.md#测试)核对串行、工具链、预算和证据要求。本页不自动授权迁移全部既有矩阵。

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

1. **先核固定语义。** 版本与 bootstrap 以[包说明](gdunit-source-packages.md)、当前配置为准；读取对应固定源码的 Load、输入和 Dispose 定义，不套用 latest 文档。`Load` 会加入树，`autoFree` 必须明确。
2. **创建独立场景。** 使用本例拥有的唯一人工资源身份和新节点；加载前完成配置，观察初始化失败。真实原始/派生资源保持只读复用，不进入提交或证据上传。
3. **交付真实输入。** 历史 M2 保留失焦冻结/清键与回焦 fresh-press，其测试先通过公开 Window FocusExited → FocusEntered 信号建立确定前置；M3 已取消这些额外策略，不复制该重置前置，不因窗口焦点冻结、清键或要求 fresh-press。需要验证信号时核实际交付，不猜 OS HasFocus 或私改 Core；信号注入不等于物理焦点验收。按生产入口使用 `InputEventKey` 等实际事件；echo、逻辑/物理键位语义按用例明确验证。`InputEventAction` 不替代只接收 Key 的处理链。runner 仅跟踪自己注入的输入，自行注入的按键由本例显式释放。
4. **在实际边界断言。** 从生产 `UpdateCompleted` 等已核实的完成事件同步保存状态。第一次有效输入后的首个实际更新必须无条件检查，不先等待 walking 等“正确状态”再采样。frame-wait 返回、日志顺序或协程恢复不证明目标回调已完成；断言异常需留在测试失败通道，不误报为 Main 的业务失败。
5. **在 finally 清理。** 释放自有输入、断开订阅、Dispose runner 并核对节点已释放；部分初始化失败也释放整个 owned 场景。恢复本例改动的窗口、焦点和引擎设置，只清理本例 GUID 人工产物。
6. **核对共享状态隔离。** 新 Node 不保证新 Resource；必要时让新场景复用同 URI 验证初始状态及首次交互，并检查原资源身份未变。按所有权管理可变资源，不依靠全局清 ResourceLoader 缓存、清 InputMap 或删除其他资源掩盖污染。

现有示例：[`MainSceneTests`](../../src/Pal.Game/Tests/MainSceneTests.cs) 验证配置、失败与后续场景；[`ExplorationSceneTests`](../../src/Pal.Game/Tests/ExplorationSceneTests.cs) 验证八种真实输入路线及同 URI 后续场景隔离。具体入口、断言映射与运行命令见 [Game 测试 README](../../src/Pal.Game/Tests/README.md#eight-exploration-scenerunner-routes)。

## 时间与错误捕获

精确业务时间留在 Core 显式时间测试。固定版 `SimulateFrames` 的双参数包含真实 `Task.Delay`，不是虚拟 delta；TimeFactor/Engine 倍速也不等价于推进单调业务时钟。场景层的有界等待只防挂起：观察到更新但状态错误是功能失败，未观察到必要更新是活性问题，两者均不能计为通过。

迁移或替换时序断言前列出旧断言到新层的映射，并预先冻结最小错误反例与指定失败原因。每个临时变异单独运行、单独恢复，既核源码也核**实际运行目录**的依赖 DLL/PDB；必要时显式 Rebuild。恢复原字节而回拨旧 mtime 可能让增量构建继续使用变异 DLL，源码恢复本身不证明运行产物恢复。

关键前置检查和构建须成功后才进入下一阶段；检查实际数值退出码，前置失败立即停止，不能让复合 shell 后续命令继续启动测试。负反例只有指定功能断言失败才有效，编译失败、缺资源、崩溃或超时不算捕获目标错误。保留旧失败原件；补证必须另获明确运行授权，而不是重复跑到满意。

## 验收脚本进程边界

`Test-M0.ps1` 的 `Invoke-DotnetChecked` 让 root 执行与 stdout/stderr EOF 共用 600000ms 工作预算；失败后另有最多 5000ms 共享显式清理等待，不能为每个流重新给完整预算。root exit0 但输出未完整仍失败；未知退出保持 null，不虚构数值。启动即落命令与阶段，实际退出一旦可得立即记录；两路原始文件增量保留原字节，组合文字按各自原 StreamReader 编码/BOM规则还原，完整后再扫描诊断。取消、输出完整性、退出已知与清理完成分别记录。root 退出不证明全部后代已退出，不追杀未知进程。

持久回归是 `tests/Pal.Game.Tests/DotnetBoundary.Tests.ps1`：只用当前 SDK 的人工自有进程，实际 AST 提取唯一生产函数；不运行 Godot 或加载游戏资源。八例覆盖正常中文双流、预期/非预期退出、ERROR、执行超时、两种继承 writer 及非继承对照。每例 8 秒外护栏仅防测试挂起，不是游戏启动时限。断言实际自有 writer 仍存活时函数已返回、正确取消并拒绝不完整输出；不把整个调用（含关闭、解码、日志和系统调度）强制限制为工作预算与清理预算之和。共享截止与有界等待逻辑另由源码审查核对；不声称 OS/I/O 硬实时或统计性能。旧失败原件不按新判据改写。

其它脚本的进程封装不因本规则被宣称已修复；本次边界改动只覆盖该函数。完整门会单独执行此预检，其运行不能与定点验证混记或自动授权。

## 完整门的固定串行批次

完整入口仍为 `scripts/Test-M0.ps1`，本地和 Windows CI 共用，不建立第二门框架。保留 solution restore/build；测试改为显式串行 Core328 → CLI330＋两个原具名 skip → Prepare52 → Game 首轮，不能再用一次未拆批的 solution test 替代。Prepare 的真实 build/import 必须先于 Game adapter runner。

Game 在 `solution`、`game`、`game-repeat` **三轮各自**执行下列完整类分区，批次之间不并行：

| 批次 | 完整测试类 | 正例 |
|---|---|---:|
| process | MainProcessTests、ExplorationProcessTests | 66 |
| scene-dialogue | ExplorationSceneTests、MainSceneTests、NpcInteractionHostTests、DialogueDisplayFixtureTests、TriggerDialogueFixtureTests | 79 |
| native-map | NativeExplorationProfileTests、PalMapSceneTests、ViewportTests | 97 |

每轮共242正例，三轮共9个 Game 会话，另有245项 discovery和3个单独失败控制，最后保留原 native startup及清理检查。runsettings 的120000ms会话上限、单例/编译/连接/启动预算、固定SDK/引擎/包及CI20分钟总限时不变。拆批增加宿主启动，并重置批间共享状态；保留类内及显式后续场景隔离用例，不将三批完整覆盖称为单会话全套或性能改善，不预先保证各批/CI耗时。

`GameTestBatches.ps1` 只处理源码、文本和XML元数据：从当前C#属性独立冻结245项完整身份/显示名，验证66/79/97互斥并集242及三个具名控制；不以通过结果生成预期。当前源码语法或solution项目分类无法识别时失败，新增类/项目/案例必须同步分区，不能静默略过。CLI filter覆盖项目默认filter，因此正例必须显式限定完整类，控制使用完整FQN精确选择。

每批要求实际exit0、Summary Completed、无错误RunInfo/坏计数、全部Passed，并将完整FQN集合与该批源码预期精确比较；Results/Definitions/Entries的testId、executionId和非空名字逐项对应。通用TRX身份使用唯一ID：xUnit参数显示可能截断而同名，不能用显示名代替案例ID；Game另按完整源码FQN要求唯一。每轮单独核三批及242并集，不跨轮、跨旧partial补齐。所有15份TRX另核唯一runId和全局唯一executionId；两个CLI skip只能为原具名案例，三个控制须各exit1、恰好一个具名指定失败且无无关RunInfo错误。任一失败即停，保存原件，不自动重试、续跑、换组或提高时限。

沿用原平面 `solution/*.trx`、`game/*.trx`、`game-repeat/*.trx`：每批唯一文件名，连同Core/CLI/Prepare和三个控制共15份。`game-batch-plan.json`保存源码预期，`game-batches.json`增量保存已完整核过的批次，原日志/commands/test-results/discovery继续保留；现有CI证据通配无需扩大到资源或包缓存。

纯离线回归（不调用SDK、引擎、CIM或网络）：

```powershell
pwsh -NoProfile -File tests/Pal.Game.Tests/GameBatches.Tests.ps1
```

人工小型源码/XML/文本fixture验证漏例、同数换例、重号、未知类/项目、未识别discovery行、会话中止及跨批重叠均被拒绝；离线通过不等于真实门禁通过，实际运行结论仍按精确身份另记。

## 迁移与验收

- 先冻结原职责映射、预期集合、筛选命令、构建/缓存口径和运行次数；逐变体覆盖下沉时说明 Core、场景和进程代表各能证明什么。替代验证成功后移除被替代的旧自动入口，保留仍被使用的观察能力及必要进程契约。
- 同口径对照记录完整命令墙钟、用例耗时、额外业务 Godot 数和框架启动数，构建成本单列。没有可辨认下降则报告“迁移完成、性能收益未证实”，不自行提高超时或增加采样次数。
- 按独立预期集合核原始 TRX 的 Results/Definitions/Entries、Summary/RunInfo 和数字退出码。静态计数、局部正例、指定负例失败及完整门通过分别记录；单次性能对照不证明统计分布或 CI 已修复。
- 独立 Standards/Spec 复核覆盖职责保持、隔离清理、错误捕获和证据。验证身份绑定实际源码/产物；本地验收、提交、合 main、远端 Issue 关闭分别记录，不用其中一步冒充另一步。完整门、CI、GPU、人工桌面和后续矩阵按各自批准范围处理。
