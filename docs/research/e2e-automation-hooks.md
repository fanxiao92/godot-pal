# 研究：Godot 端到端自动化钩子与输入驱动实践（#241）

- 关联：地图 [#240](https://github.com/fanxiao92/godot-pal/issues/240)（约束见 Notes 第 4、5 条），研究票 [#241](https://github.com/fanxiao92/godot-pal/issues/241)。
- 基线：引擎事实以 `godotengine/godot` 的 `4.7.2-stable` tag 源码与 class reference 为准（与仓库钉住的 `4.7.2.stable.mono.official.ed1daf0bf` 一致，见 [R6]）；教程类事实以 `godotengine/godot-docs` master 为准。仓库事实以 `main@7f0567b` 为准。
- 方法：只读。未运行 Godot、`dotnet test`、Prepare 或任何引擎进程；结论中凡是"需实测"的点都列入第 10、11 节，不当作已验证。
- 引用：正文 `[Gx]` = Godot 引擎/文档，`[Fx]` = 第三方框架，`[Rx]` = 本仓库。完整链接见文末"来源"。

## 1. 结论速览

1. **钩子形态**：业界 C# Godot 项目的常规做法是"生产主场景读命令行用户参数决定是否进入测试模式 + 测试代码用编译配置排除出发布构建"（Chickensoft GoDotTest 即此模式 [F1]）。Godot 原生提供 `--` 之后的用户参数通道 [G1][G2]，以及 `ExportRelease` 配置、`DEBUG`/`TOOLS` 编译常量 [G9][G10]；自定义 feature tag 只在导出构建或编辑器"Customize Run Instances"里生效，**不能**通过命令行给编辑器二进制打开 [G8]，因此不适合作为本项目（用编辑器二进制 `--path` 直接跑工程）的主开关。
2. **输入驱动**：要触发 `_Input`，必须用 `Input.ParseInputEvent`；`Input.ActionPress` 明确不会产生 `_input` 调用 [G3]。本仓库 `Main._Input` 只接受 `InputEventKey` 且同时检查 `Keycode` 与 `IsAction` [R1]，所以驱动器必须注入 `InputEventKey`（不能用 `InputEventAction`），并在每次注入后 `FlushBufferedEvents`（现有 GPU fixture 已这样做 [R3]）。Godot 没有内置的输入录制/回放 API（`Input` class reference 无此类方法 [G3]）。
3. **确定性**：`--fixed-fps` / Movie Maker 只把**上报给 `_process` 的 `delta`** 固定 [G4][G5][G6]；`Time.GetTicksMsec()` 直接取 OS 时钟 [G7]，不受影响。本仓库 `Main` 的业务时间全部取自 `Time.GetTicksMsec()` [R2]，所以 `--fixed-fps` **不能**让本项目的端到端运行确定；可靠做法是"按状态条件等待 + 存活超时"，而不是按帧数/时间戳回放。
4. **断言**：首选结构化状态探针（JSON 快照）；日志标记只用于存活/诊断；截图依赖真实渲染——`--headless` 走 dummy 渲染器，不绘制视口 [G1][G11]，所以截图只能在 GPU 档做（与地图约束 5 一致）。
5. **现有框架**：GdUnit4 SceneRunner [F2]、GUT InputSender [F4]、GodotTestDriver [F3] 都是"测试进程内装载场景 + 合成输入"，不是"从生产主场景启动的真实游戏进程"；PlayGodot 是真正的外部驱动，但需要自定义 Godot 引擎 fork [F5]，与本项目钉住官方构建、退役 fork 的方向（地图约束 13）冲突。
6. **与现有 GPU fixture 的关系**：`M4FirstBattleGpuFixture` 已经具备"真实渲染进程 + `ParseInputEvent` 自驱动 + `FramePostDraw` 截图 + marker/JSON 断言"的全部零件 [R3]，但它是**独立 fixture 场景自己 `new Main()`**，绕过了生产主场景 `GameStartup` 的 argv 路径 [R3][R4][R5]，且每个场景一个手写状态机 + 一个专用 ps1 [R6][R7]。零件可推广，形态需要改成"生产入口挂钩子 + 通用场景脚本 + 单一启动器"。

**推荐**：方案 A——生产入口内的、编译期可剔除的 argv 激活钩子 + 进程内 C# 驱动器 + 声明式场景脚本（状态等待 + `InputEventKey` 注入）+ JSON 状态探针为主断言 + 仅本地 T3 的 GPU 截图容差比对。详见第 8 节。

## 2. 自动化钩子形态

| 形态 | Godot 原生支持 | 适配本项目 | 依据 |
|---|---|---|---|
| 命令行用户参数（`-- --pal-e2e <scenario>`） | `--`/`++` 之后的参数引擎不解析，由 `OS.GetCmdlineUserArgs()` 读取 | 已在用：`GameStartup` 构造时读 `OS.GetCmdlineUserArgs()` 并交给 `GameSceneConfiguration.FromArguments` [R4][R5]；`Test-RealDialogueGpu.ps1` 也已通过 `--` 传参 [R7] | [G1] L67-68，[G2] |
| autoload 测试驱动 | 可行，但 autoload 在 `project.godot` 注册，会随工程进入所有构建 | 需配合编译剔除，否则发布构建也会加载；本项目没有现成 autoload | [R8]（`project.godot` 无 `[autoload]`） |
| 自定义 feature tag | `OS.HasFeature()` 可查；自定义 tag 只在导出构建或编辑器 Run Instances 对话框生效，标记为 Runnable 的导出预设在编辑器运行时也不生效 | 本项目 T0/T3 用编辑器二进制 `--path` 直接运行 [R6]，且仓库无 `export_presets.cfg`，无法用命令行打开自定义 tag | [G8] L153-176，[G2] `has_feature` |
| 内置 tag `debug`/`release`/`template` | `debug` 包含编辑器；`template` 表示导出模板；`OS.IsDebugBuild()` 在 release 模板返回 false | 可作运行时第二道闸（release 模板拒绝激活） | [G8] L59-69，[G2] `is_debug_build` |
| C# 条件编译 | Godot.NET.Sdk 只有 `Debug`/`ExportDebug`/`ExportRelease` 三种配置；`Debug` 定义 `TOOLS`，`ExportDebug` 定义 `DEBUG`；导出插件按 debug/release 选 `ExportDebug`/`ExportRelease` | 可在 `ExportRelease` 排除钩子源文件（GoDotTest 用 `DefaultItemExcludes` 正是这样排除测试代码）；可自定义 `DefineConstants` | [G9] L12、L18-21，[G10] L191，[G12] L154-190，[F1] L211-217 |

**保证发布构建不含钩子的常规做法**（GoDotTest 模式 [F1] L201-260）：

1. 生产主场景在 `#if DEBUG`（或自定义常量）内检查命令行，决定进入测试模式还是正常游戏；
2. `.csproj` 在 `ExportRelease` 下用 `DefaultItemExcludes` 剔除测试目录，使发布程序集里根本没有钩子类型。

本项目可在此基础上加一条**可自动验证**的证据：Unit 档对 `-c ExportRelease` 构建出的 `Pal.Game.dll` 做元数据检查（不需启动 Godot），断言不存在钩子命名空间/类型。此项能否在无编辑器环境下构建 `ExportRelease` 需在 prototype 中实测（见第 10 节）。

## 3. 输入驱动

### 3.1 脚本化注入

- `Input.ParseInputEvent(event)`："Feeds an InputEvent to the game … Also generates `Node._input` calls" [G3] L541。
- `Input.ActionPress`："will not cause any `Node._input` calls … If you want to simulate `_input`, use `parse_input_event` instead" [G3] L16。
- `Input.FlushBufferedEvents()`：把因 accumulated input 或 agile flushing 缓冲的事件立即送入游戏循环；引擎本身每帧至少 flush 一次 [G3] L50。`use_accumulated_input` 默认开启 [G3] L845；`input_devices/buffering/agile_event_flushing` 默认 false，即每个 process 帧只 flush 一次 [G13] L1660-1662。
- **本项目约束**：`Main._Input` 首行即 `@event is not InputEventKey key` 则返回，并对方向键同时检查 `key.Keycode` 与 `key.IsAction(...)`；确认键检查 `Pressed && !Echo` 与具体 `Keycode` [R1]。因此：
  - 只能注入 `InputEventKey`（同时设 `Keycode` 与 `PhysicalKeycode`，现有 fixture 即如此 [R3] `InputKey`）；`InputEventAction` 或 `ActionPress` 对 `Main` 无效。
  - "按住"语义（如 100ms 离散步需要跨 tick 按住）必须显式 press → 等待 → release，现有 fixture 的 Down 键按住 40 帧就是例子 [R3] `case 3`。
- 社区框架做法一致：GdUnit4Net SceneRunner 的按键模拟内部即 `Input.ParseInputEvent` + `Input.FlushBufferedEvents` [F2b] L671-674；GUT InputSender 提供 `hold_for`、`wait_frames`、`wait_secs`，并警告以 `Input` 为接收者时未收到 up 事件则不响应后续 down [F4] L91、L137、L172。

### 3.2 录制/回放

- Godot `Input` 没有录制/回放 API [G3]（全文无 record/replay）；任何回放都要自建。
- 回放的前提是"同样输入在同样时刻到达同样状态"。由于本项目业务时间来自墙钟（第 4 节），按时间戳或帧号回放会随帧速漂移；可行的录制形态是**把录制结果落成与手写脚本相同的"等待状态 → 注入按键"步骤**，而不是时间轴。

### 3.3 OS 级输入（物理键盘）

`Test-M4PhysicalDesktop.ps1` 用 Win32 `SendInput` 注入硬件级事件，并需要真实点击标题栏获得焦点 [R9] L27-63、L101-107。它验证的是"OS → 窗口 → 引擎"链路，`ParseInputEvent` 不经过 OS 焦点，二者证据不可互换；`SendInput` 只在 Windows 存在，不可能进入 Linux-only CI（地图约束 2）。

## 4. 帧/时间推进的确定性

| 手段 | 实际效果 | 依据 |
|---|---|---|
| `--fixed-fps <fps>` | "Force a fixed number of frames per second. This setting disables real-time synchronization."；实现上 `process_step = 1.0 / fixed_fps`，且启用时跳过帧延迟/睡眠 | [G1] L229，[G6] main_timer_sync.cpp L433-435，main.cpp L5170-5172 |
| `--write-movie <file>` | 未指定时强制 `fixed_fps = 60`；音频强制 Dummy 且非线程；MovieWriter "forces the reported delta in `_process` to be identical across frames" | [G6] main.cpp L1955-1966、L2829-2833，[G5] L6 |
| `--time-scale` / `Engine.TimeScale` | 影响 `Timer`、`SceneTreeTimer` 和所有使用 `delta` 的模拟 | [G1] L220，[G14] L348 |
| `Time.GetTicksMsec()` | 直接返回 `OS::get_ticks_msec()`（自引擎启动以来的时间） | [G7] time.cpp L384-386，class ref L108 |
| `--quit-after <n>` | 迭代 n 次后退出 | [G1] L85 |

**对本项目的含义**：

- `Main._Process` 的探索/交互/战斗/红渐变/世界节拍都以 `Time.GetTicksMsec()` 作为 `now`，例如 `battle?.Update(...)`、`Interaction?.Update(...)`、`input!.Update(...)` 与 100ms 世界步进 [R2] L512、L528-530、L539-546。`Pal.Game/Scripts` 下共 11 处调用（`Main.cs` 8、`Main.Dynamic.cs` 2、`Main.Story.cs` 1，本次 grep 统计）。
- 因此 `--fixed-fps`、`--write-movie`、GdUnit4 `SetTimeFactor`（实现为改 `Engine.TimeScale` 与 `PhysicsTicksPerSecond` [F2b] L771-773）都**不改变**本项目的业务节拍；它们只改 `delta`。
- 现有 fixture 已经采用"状态驱动"：等待 `UpdateCompleted` 计数、`PendingDisplay` 稳定 `WaitingForAdvance` 若干帧、`StoryBattleMode` 等，再注入输入，并设总存活上限 [R3] `_Process`。这是在墙钟时间下唯一稳健的推进方式。
- 本仓库时序规则要求确定性时间边界由 Core 公开接口显式传入时间值来证明，集成层通过不能替代 [R10] L6-8；端到端层同样不应承担时序边界证明，只验证链路与可见结果。
- 若将来需要帧级确定（逐帧回放、动画中间帧金图），唯一路径是给 `Main` 引入可替换时钟（钩子模式下由驱动器按固定步长推进），再配 `--fixed-fps`。这是生产代码改动与新增扩展，需要用户单独决定（第 11 节 Q2）。

## 5. 断言

### 5.1 状态探针（推荐为主断言）

- `Main` 已公开大量只读状态：`Failure`/`Failed`、`UpdateCompleted`、`Dialogue`、`Display`、`Interaction`、`StoryLostTerminal` 等 [R2] L51-105；GPU fixture 断言实际读的是 `StoryBattle.Current.Combat.Enemies`、`StoryBattleMode`、`StoryDrawSnapshot`、`StorySettlementLine`、`StoryRedFadeStep` 等 [R3]。
- 常规做法是把这些汇总成稳定的**探针快照 schema**（JSON），场景脚本只依赖探针字段，不依赖 `Main` 内部结构；结果写一个机器可读文件（现有 `fixture-result.json` 即雏形 [R3] `SaveResult`），由启动器转换为 JUnit/TRX 以符合地图约束 8。

### 5.2 日志标记

- 现有 fixture 输出 `PAL_M4_GPU_BEGIN/CHECK/ASSERT/END` 标记，启动器再比对 END 行、扫描 `ERROR:|SCRIPT ERROR:|Unhandled` [R3] `Add`/`Finish`，[R6] L70、L89-91。
- Godot 自身 CI 也用"运行工程 + 扫描日志"做冒烟：`regression-test-project` 在 `xvfb-run` 下分别以 headless、Vulkan、GLES3 导入并运行，再用 `check_ci_log.py` 检查日志 [G15]。
- 建议：标记只做存活/阶段诊断和"引擎错误即失败"闸门，不承担业务断言（业务断言放探针 JSON，避免解析自由文本）。

### 5.3 截图 + 容差比对

- `--headless` = `--display-driver headless --audio-driver Dummy` [G1] L120；dummy 渲染器的纹理存储只保存初始化时给的图像、不执行绘制 [G11] L42-44、L83-87、L108-112，所以 headless 下视口读回不是渲染结果。现有 fixture 在 `_Ready` 直接拒绝 headless [R3] L74。
- 正确的取图时机是等 `RenderingServer.frame_post_draw`，否则纹理可能全黑或过时 [G16] L122；现有 fixture 订阅 `FramePostDraw` 后 `GetViewport().GetTexture().GetImage()`，并要求 320×200 [R3] `ObserveFrame`/`CaptureNow`，与工程视口 320×200 一致 [R8]。
- 现有 fixture 的像素断言不是整图金图，而是**独立推导的期望 + 区域采样 + 容差**（调色板颜色计数、背景合成逐点比对 `≤1.1/255`、红渐变公式 `delta ≤ 2`），并记录每张截图 SHA-256 [R3] `CheckBattleIdle`/`CheckFade`/`CaptureNow`。这比整图金图更抗渲染差异，适合作为 T3 截图断言的默认风格；整图金图只用于"人眼复核包"。
- Linux 无 GPU 也能通过 `xvfb-run` + 软件渲染跑图形驱动（Godot 自身 CI 即如此 [G15]），但地图约束 5 已决定截图不进 GitHub CI；该事实只作为将来扩展的备注。

## 6. 现有框架/项目及局限

| 项目 | 形态 | 局限（相对地图约束 4） | 依据 |
|---|---|---|---|
| GdUnit4Net SceneRunner | 测试进程内 `Load` 场景，`SimulateKey*`、`SimulateFrames`、`SetTimeFactor`、`AwaitInputProcessed`；headless 下窗口相关方法无效 | 场景由测试运行器装载，不经生产主场景/argv；`SetTimeFactor` 只改 `TimeScale`。地图已定 SceneRunner 只做接线验证 | [F2] L84，[F2a] L100-140、L317-345、L437、L458-472，[F2b] L771-773 |
| GUT InputSender | GDScript 测试框架内的输入发送器（`hold_for`、`wait_frames`） | 同属测试进程内；GDScript 生态，本项目为 C# | [F4] |
| Chickensoft GodotTestDriver | C# 节点驱动/Fixture，模拟鼠标、按键、动作；"not a test executor" | 只是驱动库，需宿主执行器；偏 UI 控件 | [F3] L8-10、L24 |
| Chickensoft GoDotTest | 游戏进程内 C# 测试执行器，`--run-tests`、`--quit-on-finish`；主场景按命令行决定跑测试或切到游戏；`ExportRelease` 排除测试代码；可配 coverlet | 引入第三套测试框架；但其"主场景 argv 闸门 + ExportRelease 剔除"模式正是本项目钩子所需 | [F1] L5、L141-156、L201-260、L298-300 |
| PlayGodot | 外部 Python 客户端经 Godot 调试协议驱动游戏（取节点、改属性、截图、输入），类 Playwright | **需要自定义 Godot 构建（automation 分支 fork）**；与钉住官方 4.7.2、退役 fork 方向冲突 | [F5] L7-23、L63-81 |
| Godot 官方 CI | `--headless --test`（引擎 C++ 单测）+ 在 xvfb 下运行回归测试工程并扫日志 | 冒烟级"进程不崩、日志无错"，无输入驱动和状态断言 | [G15] |

## 7. 与本仓库现有 GPU fixture 的关系

现有三套入口（`M4FirstBattleGpuFixture` + `Test-M4FirstBattleGpu.ps1`、`RealDialogueFixture` + `Test-RealDialogueGpu.ps1`、`M4PhysicalDesktopFixture` + `Test-M4PhysicalDesktop.ps1`）的可推广点与不可推广点：

**可直接推广的零件**

- 注入：`InputKey`/`InputTap` = `InputEventKey` + `ParseInputEvent` + `FlushBufferedEvents` [R3]。
- 取图：`FramePostDraw` 订阅、同帧去重、`GetImage` 尺寸校验、PNG + SHA-256 [R3]。
- 结果：增量写 `fixture-result.json`（失败时也写）、`Finish/Fail` 统一以退出码结束 [R3]。
- 启动器护栏：进程归属冲突检查、钉版本、证据目录必须在仓库外且新建、受保护资源前后快照比对、超时 kill、引擎错误扫描、必需检查名单 [R6] L16-19、L41-47、L62-72、L74-91；`ProcessStartInfo.Environment` 只作用于子进程（`Test-RealDialogueGpu.ps1` 写法）优于修改当前进程环境变量（`Test-M4FirstBattleGpu.ps1` 写法）[R7] L43-54 对比 [R6] L55-59。

**需要改造的形态**

| 现状 | 问题 | 推广后的形态 |
|---|---|---|
| 运行 `res://Tests/M4FirstBattleGpuFixture.tscn`，fixture 自己 `new Main()` 并 `Configure(FromArguments(...))` [R3] L96-102、[R6] L50 | 不经 `run/main_scene` → `GameStartup` 的 argv/失败退出路径 [R4][R8]，不满足"真实游戏进程从 Main 启动" | 正常启动主场景；`GameStartup` 在钩子编译进来且 argv 含 `--pal-e2e` 时挂驱动节点 |
| 配置走 `PAL_M4_GPU_*` 环境变量 [R3] L75-84 | 每套 fixture 一套变量名 | 统一走 `--` 之后的用户参数（与游戏参数同一通道 [G2]） |
| 每个场景一个手写 `phase` 状态机 [R3] `_Process` | 新场景 = 新 C# 类 + 新 ps1 | 声明式场景脚本（等待条件/按键/截图/断言步骤）+ 一个通用驱动器 |
| 每个 fixture 一个 ps1，必需检查名单写死在 ps1 [R6] L74-86、[R7] L89 | 入口分散，与地图约束 7 的统一前门冲突 | `pal.ps1 e2e <scenario>` 一个入口；必需检查来自场景文件 |
| fixture 拒绝 headless [R3] L74 | 同一场景无法在 CI headless 跑链路部分 | 截图步骤在 headless 下记为 `skipped`（显式，不算通过），其余步骤照跑 |
| 物理桌面 `SendInput` [R9] | Windows-only、需焦点 | 不进通用面；保留为人工/桌面验收专用（待用户定去留，Q5） |

结论：**零件能推广为通用面，入口形态需要改**。像素 oracle（调色板/背景合成/红渐变公式）是场景特定的独立推导，应作为可复用的"检查函数库"由场景引用，而不是塞进驱动器。

## 8. 推荐方案（方案 A）

```text
pwsh pal.ps1 e2e <scenario>            # 统一前门（地图约束 7）
  └─ godot --path src/Pal.Game --audio-driver Dummy [--headless] \
        -- <原有游戏选择参数> --pal-e2e <scenario.json> --pal-e2e-out <证据目录>
       └─ run/main_scene = Main.tscn → GameStartup（生产入口，读 argv）
            └─ [#if PAL_E2E] 发现 --pal-e2e → 挂 E2EDriver 节点
                 ├─ 每次 UpdateCompleted / FramePostDraw 推进场景步骤
                 ├─ 输入：InputEventKey + ParseInputEvent + FlushBufferedEvents
                 ├─ 等待：探针条件成立（带每步与全局存活超时），不按帧数/时间戳
                 ├─ 断言：探针 JSON 快照字段比较；引擎错误即失败
                 ├─ 截图（仅 GPU）：FramePostDraw 读回 + 区域/容差检查；headless 记 skipped
                 └─ 输出 e2e-result.json → 启动器转 JUnit + 退出码
```

要点：

1. **激活**：生产入口 `GameStartup` 读 `OS.GetCmdlineUserArgs()`（已有路径 [R4]），新增 `--pal-e2e*` 参数族；与现有 `GameSceneConfiguration` 的"未知参数报错"规则需协调——钩子参数应在交给 `FromArguments` 之前剥离 [R5]。
2. **发布剔除（双闸）**：编译期——自定义常量（如 `PAL_E2E`）只在 `Debug`/`ExportDebug` 定义，`ExportRelease` 用 `DefaultItemExcludes` 排除钩子目录（GoDotTest 模式 [F1][G9]）；运行期——即使存在钩子代码，`OS.HasFeature("template") && !OS.IsDebugBuild()` 时拒绝激活 [G2][G8]。证据——Unit 档对 `ExportRelease` 产物做元数据断言（无 Godot 进程）。
3. **场景脚本**：数据文件（JSON）描述步骤；步骤类型最少集合：`wait(probe 条件)`、`key(tap|down|up)`、`hold(key, 直到条件|N 次更新)`、`snapshot(name)`、`screenshot(name, checks)`、`assert(probe 路径, 比较)`。录制工具（可选）输出同一格式。
4. **两种运行模式、同一场景**：headless（CI T1/T2，可公开的合成/样例资源，无截图）与 GPU（本地 T3，真实资源缓存 + 截图），对应地图约束 3、5、16。
5. **复用**：从现有 fixture 抽出注入、取图、结果写出、像素检查函数；从现有 ps1 抽出进程护栏进前门。现有 M4/M3 GPU 场景改写为场景文件后，原 fixture 与 ps1 按地图约束 12 删除。

## 9. 备选方案

| 方案 | 做法 | 何时选 | 代价 |
|---|---|---|---|
| B. 独立钩子程序集 | 钩子放在单独 C# 项目，仅 e2e 构建引用，`Pal.Game` 零测试代码（更贴地图约束 9） | 用户要求生产程序集完全不含测试代码 | 需实测 Godot C# 能否在主程序集之外承载 `Node` 子类/脚本并被实例化（本研究未找到可引用的一手结论，列为 prototype 项） |
| C. 虚拟时钟 + `--fixed-fps` | 给 `Main` 注入可替换时钟，钩子模式按固定步长推进 | 需要逐帧回放、动画中间帧金图或可复现性能曲线 | 改 11 处生产时间读取 [R2]；属新增扩展，需用户决定并写 ADR |
| D. 导出 debug 构建 + 自定义 feature tag | `export_presets.cfg` 增加带 `pal_e2e` tag 的 debug 预设，e2e 跑导出产物 | 需要验证"导出产物"而非编辑器运行 | 每次 e2e 多一次导出；仓库目前无导出预设 [G8][G10] |
| E. 外部驱动（PlayGodot 类） | 外部进程经调试协议驱动 | 需要黑盒、跨语言驱动 | 需自定义引擎 fork [F5]，与地图约束 13 方向冲突，不推荐 |
| F. 引入 GoDotTest 作为执行器 | 直接用其 `--run-tests` 进程内执行 | 想要现成的进程内 C# 测试执行器 | 第三套测试框架（已有 xUnit/GdUnit4），只借模式不引依赖更 KISS |
| G. 录制/回放为主 | 录制真人输入时间轴回放 | 时钟确定后（依赖方案 C） | 墙钟下回放漂移；Godot 无内置录制 [G3] |

## 10. 风险

1. **墙钟时序偶发**：业务时间取墙钟 [R2]，GPU 帧速/CI 负载变化可能导致等待超时或越过短暂状态。缓解：只按状态条件等待；对"短暂态"（如稳定等待页）使用"连续 N 次更新满足"判据（现有 fixture 用 6 帧稳定 [R3]）；偶发按地图约束 6 进隔离清单。
2. **headless 无像素**：dummy 渲染器不绘制 [G11]；截图步骤必须显式 `skipped`，不得算通过，结果文件里与 GPU 运行区分。
3. **钩子泄漏到发布构建**：双闸 + 元数据断言；`ExportRelease` 能否在无编辑器的 CI 中用 `dotnet build -c ExportRelease` 构建需 prototype 实测（Godot.NET.Sdk 定义了该配置 [G9]，但未实跑）。
4. **探针耦合**：探针 schema 若直接映射 `Main` 内部类型，重构即破坏场景；需定义稳定的只读快照 DTO 与版本号。
5. **参数通道冲突**：`GameSceneConfiguration.FromArguments` 对未知/重复参数抛错 [R5]；钩子参数剥离点设计错误会让生产参数校验失效或被绕过。
6. **输入语义差异**：`ParseInputEvent` 不经 OS 焦点与键盘重复，`Echo` 由驱动器决定；与物理键盘证据不可互换 [R9]。`Main` 只认 `InputEventKey` [R1]，若将来改用 Action 事件需同步驱动器。
7. **金图存储**：截图来自真实游戏资源，属派生资源，按项目边界不应提交/上传；金图/期望应放本机资源缓存（键同地图约束 16），这意味着 T3 金图不可跨机共享，除非用"独立推导期望"代替整图金图。
8. **全局串行**：e2e 仍是 Godot 进程，受"实际 Godot 全局串行"约束（AGENTS.md 测试章节），前门需保留进程归属检查 [R6] L42-43。

## 11. 待用户决定的问题

- **Q1 钩子代码放哪里**：`Pal.Game` 内 `#if PAL_E2E` + `ExportRelease` 剔除（推荐，GoDotTest 先例 [F1]），还是独立程序集（方案 B，更贴约束 9 但需 prototype 证明可行）？
- **Q2 是否引入虚拟时钟**（方案 C）：M7 只做状态驱动（推荐），还是同时给 `Main` 加可替换时钟以支持逐帧回放？
- **Q3 场景脚本格式**：JSON 数据文件（推荐，录制工具可产出、agent/QA 可编辑），还是 C# 场景类（类型安全但每个场景需编译）？
- **Q4 发布剔除的验收证据**：只做 `ExportRelease` 元数据断言，还是同时新增导出预设并验证导出产物（方案 D）？
- **Q5 物理桌面 `SendInput` 验收**（`Test-M4PhysicalDesktop.ps1`）：保留为人工桌面验收专用、并入 T3 本地 Windows 可选步骤，还是退役？
- **Q6 CI headless 运行同一场景的分层命名**：算"端到端（headless 模式）"还是归入"集成"？影响验证矩阵口径（地图约束 10）。
- **Q7 截图断言风格**：默认"独立推导期望 + 区域容差"（推荐，沿用现有 oracle），整图金图只作人工复核包；还是以整图金图 + 容差为主？

## 来源

**Godot 引擎与文档**

- [G1] Command line tutorial（`--`/`++` L67-68、`--quit-after` L85、`--headless` L120、`--write-movie` L125-128、`--time-scale` L220、`--fixed-fps` L229）：https://github.com/godotengine/godot-docs/blob/master/tutorials/editor/command_line_tutorial.rst ；页面 https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html
- [G2] `OS` class reference（`get_cmdline_user_args` L233、`has_feature` L648、`is_debug_build` L657）：https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/OS.xml
- [G3] `Input` class reference（`action_press` L16、`flush_buffered_events` L50、`parse_input_event` L541、`use_accumulated_input` L845）：https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/Input.xml
- [G4] Creating movies（non-real-time 录制、`--write-movie --fixed-fps` 用法、退出方式 L288-294）：https://github.com/godotengine/godot-docs/blob/master/tutorials/animation/creating_movies.rst
- [G5] `MovieWriter` class reference（固定上报 delta，L6）：https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/MovieWriter.xml
- [G6] 引擎主循环：`main/main.cpp`（L1955-1966 `--fixed-fps`/`--write-movie` 解析，L2829-2833 movie 模式音频，L5170-5172 fixed-fps 跳过帧延迟）https://github.com/godotengine/godot/blob/4.7.2-stable/main/main.cpp ；`main/main_timer_sync.cpp` L433-435 https://github.com/godotengine/godot/blob/4.7.2-stable/main/main_timer_sync.cpp
- [G7] `Time::get_ticks_msec` 实现 `core/os/time.cpp` L384-386：https://github.com/godotengine/godot/blob/4.7.2-stable/core/os/time.cpp ；class reference L108：https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/Time.xml
- [G8] Feature tags（默认 tag 表 L59-69、Custom features L153-176）：https://github.com/godotengine/godot-docs/blob/master/tutorials/export/feature_tags.rst
- [G9] Godot.NET.Sdk：`Sdk.props` L12（配置列表）、L87-103（define 常量）https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/Sdk.props ；`Sdk.targets` L18-21（`ExportDebug`→`DEBUG`、`Debug`→`TOOLS`）https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/Sdk.targets
- [G10] C# 导出插件 `ExportPlugin.cs` L191（`isDebug ? "ExportDebug" : "ExportRelease"`）：https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs
- [G11] Dummy 渲染器纹理存储 `servers/rendering/dummy/storage/texture_storage.h` L42-44、L83-87、L108-112：https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/dummy/storage/texture_storage.h
- [G12] C# language features — Preprocessor defines（L99-190）：https://github.com/godotengine/godot-docs/blob/master/tutorials/scripting/c_sharp/c_sharp_features.rst
- [G13] `ProjectSettings` `input_devices/buffering/agile_event_flushing` L1660-1662：https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/ProjectSettings.xml
- [G14] `Engine.time_scale` L348：https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/Engine.xml
- [G15] Godot CI：`.github/workflows/linux_builds.yml`（L241 `--headless --test`、L270-275 运行测试工程）https://github.com/godotengine/godot/blob/4.7.2-stable/.github/workflows/linux_builds.yml ；`.github/actions/godot-project-test/action.yml`（xvfb-run headless/Vulkan/GLES3 + `check_ci_log.py`）https://github.com/godotengine/godot/blob/4.7.2-stable/.github/actions/godot-project-test/action.yml
- [G16] `Viewport.get_texture` L122（需等待 `frame_post_draw`）：https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/Viewport.xml

**第三方框架**

- [F1] Chickensoft GoDotTest README（L5、L141-156 `--run-tests`/`--quit-on-finish`、L201-260 主场景 argv 闸门与 `ExportRelease` 剔除、L298-300 覆盖率）：https://github.com/chickensoft-games/GoDotTest/blob/main/README.md
- [F2] gdUnit4Net README（L84 Scene runner）：https://github.com/MikeSchulze/gdUnit4Net/blob/master/README.md ；[F2a] `Api/src/ISceneRunner.cs`（L100-140 按键/动作模拟、L317-345 `SetTimeFactor`/`SimulateFrames`、L437 `AwaitInputProcessed`、L458-472 headless 下窗口方法无效）https://github.com/MikeSchulze/gdUnit4Net/blob/master/Api/src/ISceneRunner.cs ；[F2b] `Api/src/core/SceneRunner.cs`（L671-674 `ParseInputEvent`+`FlushBufferedEvents`、L771-773 `TimeScale`/`PhysicsTicksPerSecond`）https://github.com/MikeSchulze/gdUnit4Net/blob/master/Api/src/core/SceneRunner.cs
- [F3] Chickensoft GodotTestDriver README（L8-10 能力、L24 非执行器）：https://github.com/chickensoft-games/GodotTestDriver/blob/main/README.md
- [F4] GUT `GutInputSender` class reference（L91 `hold_for`、L137 `wait_frames`、L172 `INPUT_WARN`）：https://github.com/bitwes/Gut/blob/main/documentation/docs/class_ref/class_gutinputsender.rst
- [F5] PlayGodot README（L7-23 架构与"requires custom Godot build"、L63-81 需 automation 分支 fork 与 `--remote-debug`）：https://github.com/Randroids-Dojo/PlayGodot/blob/main/README.md

**本仓库（`main@7f0567b`）**

- [R1] `Main._Input`（L305-498，L307 仅 `InputEventKey`，L312 `Keycode`+`IsAction`，L411-412 确认键）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/src/Pal.Game/Scripts/Main.cs#L305-L498
- [R2] `Main` 公开状态（L51-105）与 `_Process` 墙钟时间（L500-560）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/src/Pal.Game/Scripts/Main.cs#L500-L560
- [R3] `M4FirstBattleGpuFixture.cs`（L74 拒绝 headless、L96-104 `new Main()`+`FramePostDraw`、`_Process` 状态机、L333-344 注入、L606-622 取图、L670-713 断言/结果）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/src/Pal.Game/Tests/M4FirstBattleGpuFixture.cs
- [R4] `GameStartup.cs`（L15 读 `OS.GetCmdlineUserArgs()`，失败退出策略）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/src/Pal.Game/Scripts/GameStartup.cs
- [R5] `GameSceneConfiguration.FromArguments`（未知/重复参数抛错）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/src/Pal.Game/Scripts/GameSceneConfiguration.cs
- [R6] `scripts/Test-M4FirstBattleGpu.ps1`（L42-45 进程冲突与钉版本、L50 fixture 场景参数、L55-59 环境变量、L70 引擎错误扫描、L74-91 必需检查与通过判据）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/scripts/Test-M4FirstBattleGpu.ps1
- [R7] `scripts/Test-RealDialogueGpu.ps1`（L43-54 子进程环境与 `--` 用户参数、L87-91 判据）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/scripts/Test-RealDialogueGpu.ps1
- [R8] `src/Pal.Game/project.godot`（`run/main_scene`、320×200 视口、无 autoload）与 `Scenes/Main.tscn`（脚本 = `GameStartup.cs`）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/src/Pal.Game/project.godot
- [R9] `scripts/Test-M4PhysicalDesktop.ps1`（L27-63 Win32 `SendInput`、L101-107 标题栏点击取焦点）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/scripts/Test-M4PhysicalDesktop.ps1
- [R10] `docs/testing/time-boundaries.md`（L6-8 确定性时间与证据互补）：https://github.com/fanxiao92/godot-pal/blob/7f0567bf2ac68b726217aa8f01da69263c6359cc/docs/testing/time-boundaries.md
