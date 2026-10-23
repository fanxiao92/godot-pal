# M3 分层验收检查点（固定串行拆批完整门通过，CI与发布分列）

## 当前统一收尾候选

隔离分支 `task/m3-finalize` 从本地 main52afa6c 与 committed M3 794b078统一，并移入已审共享初始化/SceneRunner增量；不是把全部M3合入主工作树。保留M3已批准焦点策略、main独有normal-deadline Fact和profile-cleanup合同，及M3独有NPC/display失败合同。本地合并候选 `00f3c64` 的P2前独立源码库存为Core328、Game240 positives/243含controls；已完成Core87及获准拆批的Game66+77定点验证。用户随后要求补修三项P2，P2补丁的静态要求为Core328、CLI330+2既有skip、Game242 positives/245含controls；新旧结果与限制分列如下。静态库存不是全套通过结果。P2修复已在本地提交 `0ee8204`；随后第一次拆批完整入口在新增CLI身份核验处误报停止；用户批准持续修复到主线交付后，修复并完成了新的完整入口，15份TRX全部核验通过。CI、主线和Issue交付另列，失败原件不改判。

历史 [CI39884910037](https://github.com/fanxiao92/godot-pal/actions/runs/39884910037) 在794b078、attempt1上为completed/cancelled：Game120s会话中止，仅210/239结果，平台20分钟取消，solution实际退出未知。该失败不因后来局部回归而改判；继承pipe writer的合成反例也不是它的已证实根因。

Test-M0仅修复dotnet函数的root+双流EOF共享600s预算和额外最多5s失败显式清理等待，保留真实退出/不完整输出失败/早期持久化和原始双流。合成回归的8s每例防挂不限制游戏资源加载；不承诺整个调用含OS/I/O硬实时。两次旧回归失败原件保留：首次UTF8解码回归已修正；后一次stderr-held全调用2.93s触发不属于原规格的2.5s硬断言，未将它当M3业务缺陷。用户明确选方案1校准该判据；修订后8例已单次通过，不等于完整门、稳定性或旧CI修复。

### 2026-10-23 固定串行拆批：修复后完整入口通过

用户明确授权继续处理后续故障，完成M3主线合入及相关Issue关闭。通用TRX图改按testId/executionId识别案例，保留非空名字和逐项对应；xUnit允许不同案例显示同名，Game仍按独立源码FQN强制唯一。新增合法同显示名/不同ID回归先RED（准确duplicate identity），修后12组纯离线回归GREEN；旧原CLI332结果只读重放通过，不重签旧门结果。

实际身份为 `task/m3-finalize@0ee82045320a9793ec64ca63ca7bf3cf5fda2baf` 加未提交的门禁/文档补丁；新完整入口UTC **2026-10-23T16:16:31.518922Z–16:30:11.282978Z**，实际exit0、**819.764秒**，无outer timeout。独立空NuGet包/HTTP缓存，保留bin/.godot；固定SDK10.0.402、Godot4.7.2及原API/adapter不变。不是clean checkout，不证明零概率故障消除或历史CI根因。

| 层 | 本次实际完整结果 |
|---|---|
| 元数据及真实边界预检 | 12组离线metadata、8个真实自有进程boundary均完整通过；owned退出已知、无outer guard |
| Core / CLI / Prepare | 328P；330P＋原2具名skip；52P，各exit0/Completed。CLI仅两个对应skip的Warning RunInfo，其余正例无RunInfo |
| Game三轮 | solution、standalone、repeat各66＋79＋97＝242P，九会话全部exit0/Completed、0skip，无错误RunInfo；每批完整FQN与独立源码预期精确一致，每轮互斥并集242，不跨轮补齐 |
| discovery | 独立源码245项显示身份与实际245唯一发现精确一致，含原三个控制 |
| 三个控制 | 分别exit1、1个具名指定失败；断言差异、500ms单例超时和READY异常诊断准确，无无关会话错误 |
| native startup | version/import/fixture-build/prepare/profile/startup六阶段全部exit0、未超时；实际Main加载选定合成原生map并输出Core ready |
| 整体完整性 | 主dotnet19条，前16exit0、后3指定exit1；15个完整TRX、15个唯一runId、1441个唯一executionId，1436P＋2原skip＋3指定F；Results/Definitions/Entries逐条对应，Summary/Counters/RunInfo/Times核过 |

源码1064文件在运行区间SHA/长度/mtime全不变，后续只同步证据文档；原main958/integration1058源/Git状态及5278资源精确保护。最终受限进程快照为空；不恢复Observer、不采未知详情、不推断所有历史后代生命周期。本次headless不新增GPU/物理键盘/人工结果，继续按原身份复用适用的双语GPU及用户Windows反馈。原143中止、前次CLI核验误报和CI39884910037取消均保留。

原件在本机 `C:/Users/xiaofan/.pi/m3-closeout-02/gate-01/`、`gate-01-run/`、`gate-01-audit.json`和源码/保护快照；不是清洁检出依赖或应上传的真实资源。本次完整门通过不自动等于CI、合main或Issue关闭；后续按用户已给交付授权分别记录。

### 历史：2026-10-23 固定串行拆批第一次完整入口未通过

用户确认保留120秒、工具链、全部案例与三轮，按完整类固定拆为66/79/97；[固定规则](../../testing/game-test-layers.md#完整门的固定串行批次)已实现于共享入口及小型元数据辅助，保留solution restore/build，显式串行Core/CLI/Prepare，避免再次未拆批solution test。纯离线11组回归、语法、245项独立源码库存及历史格式核对通过，不作为运行通过证据。

实际身份为 `task/m3-finalize@0ee82045320a9793ec64ca63ca7bf3cf5fda2baf` 加本轮未提交门禁/说明补丁；唯一入口UTC **2026-10-23T15:51:15.872817Z–15:52:44.536975Z**，实际exit1、88.664秒，无outer timeout。首五条主dotnet命令全部exit0：SDK info（10.0.402）、独立空NuGet包/HTTP缓存restore、solution build、Core及CLI。保留已有bin/.godot，不称clean build/clean checkout。

| 实际阶段 | 原始证据与结论 |
|---|---|
| 边界预检 | 8/8真实自有进程案例完整通过，所有owned退出已知、无outer guard；本轮新执行，非复用P2结果 |
| Core | 328P、exit0、Summary Completed，无RunInfo，328份Results/Definitions/Entries及ID对应 |
| CLI | 330P＋2个原具名skip、exit0、Summary Completed，332份Results/Definitions/Entries；两个Warning RunInfo仅为相应skip。testId/executionId各332唯一、逐项关联正确 |
| 门禁失败 | 新增 `Read-TestRunIdentity` 对所有框架错误要求显示名唯一；xUnit byte-array MemberData截断显示产生2组同名（分别6/2例），但案例ID不同且关联正确。当前helper因此误报duplicate identity。这是本轮新增脚本误判，不是CLI功能失败、历史问题归因或120秒会话中止 |
| 未运行 | Prepare、9个Game批次、discovery、3个控制及startup；未产生PASS.txt，不能以两个完整TRX替代15份完整门，也未验证拆批运行效果 |

完整入口额度已使用一次，失败后只读核验并停链，未修helper、自动重试、从Prepare续跑或修改时限。完整源在运行区间SHA/长度/mtime均不变；原main/integration源/Git状态和5278资源精确保护，退出受限进程快照为空。未启动实际Godot、CI、GPU/人工、提交/合main/推送/Issue写。原件保存在本机 `C:/Users/xiaofan/.pi/m3-batched-gate-01/` 的 `full-gate/`、`gate/`、`gate-audit.json`、`source-before.json`、`source-after-run.json`、`runtime-manifest.json`及保护记录。

下一步建议而非已执行：按testId/executionId核通用TRX图，保留非空显示名和逐项对应，仅对Game源码完整FQN要求唯一；补xUnit合法同名反例与不合法重号/错连回归，再另获一次全新证据目录的完整入口许可，不拼接本次partial阶段。该历史核验缺陷及后续修复、完整门结果以上一节为准，不用成功结果覆盖本次原失败。

### 2026-10-23 P2补修及定点回归

本轮仅在 `task/m3-finalize`、`00f3c64` 之上修复三项有效P2，不修改原main/integration、真实资源、Godot/GdUnit版本或时限；本节记录局部行为验证，不作为main发布证据。

| P2 | 修复及本轮证据 |
|---|---|
| 显示fixture异常时残留自有Space/Escape | 小型fixture私有按键作用域把真实press、等待及断言放入 `try/finally`，正常路径顺序不变。无finally的指定反例两例均正常Failed（实际仍按下）；加入finally后完整显示类10P，exit0/Completed，无skip或异常RunInfo。反例自身另有兜底释放，不污染下例 |
| held-writer持久判据未核取消/清理 | 保留所有旧退出/早期记录/存活writer/输出不完整判据，新增deadline、cancellationRequested、cleanupComplete、相关流Canceled及另一路RanToCompletion硬断言；原八个真实自有进程案例全部通过。逐字提取新旧实际验收表达式的纯谓词核对另检2个真实状态和14个单字段反例：旧表达式均误收、新表达式均拒绝；这不是生产函数取消失败的运行证据 |
| 事件JSON容错UTF8替换 | index/shared统一严格UTF8解码，非法字节报明确文件名及编码错误，保留可选UTF8 BOM，不自动接受UTF16。先以非法字节、截断序列和UTF16 BOM共6例证实旧CLI错误exit0；修复后事件CLI全类27P，包括合法中文/字面U+FFFD及有/无UTF8 BOM往返、失败无输出及输入不变 |

上述GREEN均完整exit0；两个RED均是指定断言失败，不以会话中止替代，TRX中的预期Error RunInfo逐项对应指定断言日志。共执行CLI restore一次、CLI显式build两次、Game显式build两次、boundary fixture build一次、纯谓词核对小项目build一次（其本地无包restore包含在build中）；未重新跑Core、旧Game143/66/77、Prepare、import、完整门、CI或桌面/GPU。Game前后有效全局Godot PID/Name快照为空，未检查未知进程详情或清理进程。CLI两次build均有1条 `xUnit2031` warning（`DialogueLanguageCliTests.cs:53`），build exit0/0 errors；本轮未处理该warning，未把代码位置未改当作其归因证据。

独立源码计数保留全部旧案例，再增加CLI8例与Game2例；门禁要求和使用说明同步为Core328、CLI330+2既有skip、Prepare52、Game242/245。这些是源码库存，不是全套通过。原 `allowFrameHeader` 恒真参数仅为可选整理，非有效契约缺陷，本轮不作无关重构。本轮定点证据独立保存在本机 `C:/Users/xiaofan/.pi/m3-p2-fixes-01/`；旧合并候选及其审查/失败记录不改判；上述修复验证阶段未做独立双轴审查、提交、合入main、推送或Issue写入，后续复核及提交单独记录，不回填为新增测试。

### 2026-10-23 P2前统一候选的局部验证

运行身份为上述两父提交的未提交统一候选，测试时tree `626b9005fe3819e1324586475d7f37f0d94004f4`；后续仅同步本页等文档，不将记录冒称为新提交上的完整门。

| 检查 | 实际结果与边界 |
|---|---|
| 固定包准备、Core/Game restore与显式build、纯脚本editor import | 各批准次数一次，全部exit0；两次build均0 warnings/errors。复用已核锁包及已有全局NuGet cache，不是empty-cache完整门 |
| Core定点 | 一次exit0，87P/0skip，TRX Completed、无异常RunInfo，独立source方法/参数case数量匹配 |
| 最初Game143整组 | 一次exit1，120000ms会话中止，仅141P，TRX Summary Failed+RunInfo Error；原失败保留，不能因partial计数aborted=0或控制台Passed!改判 |
| 追加Game66 | 明确获准后完整运行一次，exit0/46.656s，66P/0skip，TRX Completed、无异常RunInfo；MainProcess48+ExplorationProcess18 |
| 追加Game77 | 其后串行完整运行一次，exit0/36.573s，77P/0skip，TRX Completed、无异常RunInfo；其余5类，含原未收齐的两个trigger案例 |

新两批各自核对完整Results/Definitions/Entries和unique IDs，并与独立source集合逐项一致；66与77互斥、并集恰为143，不是旧partial141与补2拼接。保持120秒/runsettings/产品源码/工具链不变，未增加显式build/import或重跑Core/PS八例。两批结束时受限全局Godot快照为空，测试前后产物SHA/len/mtime无变化；不能由快照推断所有历史后代生命周期。最初Game运行中adapter生成runner并内部build，6个Game DLL/PDB/ref改变的前后身份独立保留，不把首次--no-build宣称为完全没有编译。

原中止后的两Godot实例经本次日志的宿主PID、服务端出生窗口及直接父子关系确认，再用对应进程句柄/创建时间复核，仅清理确证本轮实例；没有全局命令行或外部项目父链采集，不是恢复已停用Observer或补填其历史未知值。清理和追加测试均另获明确批准。

Core、Game66、Game77的TRX SHA256分别为 `d662ccd54af51be772c5270579d3e25d81f24ccc78b5dd5cb69c0a64f2b964c7`、`2266afde7bd6ddc1a280a285ddda26ac17281350df336fbb0d1f4cb4ed6943f9`、`75481292a4b6058f47f8c5111f30448a59cec83c054b2402e2e1e0224009f8bc`。这些仅证明局部行为，既不保证稳定性，也不证明完整Game240或共享门能在现有120秒内完成。完整门的成本与会话风险尚待单独处理和授权；本次未修改完整门分组、增加时限、启动CI/Prepare/GPU或重做Windows人工验收。

下文实测表均按各自旧提交身份保留；已有双语GPU/用户人工反馈按未改行为适用，不为提交哈希变化重复全套桌面验收。原main用户文档和5278资源受保护，不覆盖、重提取或上传。完整门、CI、推送、主main合入与Issue发布仍需另行批准。


## 状态与范围

#26 以本地 integration `6c2e036f984dac1479b22697f73488fa2499f37d` 为起点，阶段1修正两项字形/标识符边界，阶段2修正四项reference显示输入/时序偏差及直接调用方/fixtures。固定全M3候选经过 Standards/Spec 两轴初审、修复后定点复核，原六项source finding均关闭，无新有效源码问题；最终候选 `5bf481fba44f7f278f555daad046e11ed1eb4eea` 已完成匹配双语GPU，并机械no-ff合入实际 integration `dab79ad85a4a352826a31eb0569a58e1f17eca88`，两者tree均为 `b4fb69689d6820727b31ce6909117b1904a5b540`。本地合入不等于整体验收或已推送。

**原检查点结论：精确dab79ad8完整自动门PASS，经独立evidence review支持；用户确认Windows人工清单通过。这些历史证据不签当前统一候选；当前CI失败及收尾边界以上节为准。** Observer 是 agent 自行追加、规格外的辅助采证工具，现已停用，其缺陷不作为 M3 功能验收门；历史 verifier criterion-1 仍为 `not-satisfied`，不改写历史报告、不宣称工具修复或隐私风险消除。2026-10-23重新只读核对Issue正文、评论及直接依赖，与规划快照一致：#20/#21/#23/#24/#25/#26/#27 OPEN，仅 #22 CLOSED；未关票或推送。

批准范围见 [计划](plan.md)；普通运行与资源身份见 [NPC 宿主契约](../../design/npc-interaction-host.md)。这是 scene4/map10/owner60/0188 的显式独立上下文，不是原版出生、正常自由探索或全量 VM/auto。owner57 热点未改 state，只是不在此上下文调度；32原始对象保留显示/碰撞。默认简中，英文只在启动时显式选择。不带 M3 参数仍走 M2 Main，独立静态模式保留。运行时只读准备好的原生资源和现代事件 JSON，不读原始 MKF/SLF/M.MSG/M1 JSON/参考头文件，不自动提取或补配置。

## 实际 integration 共享门结果

共享入口仍为 `scripts/Test-M0.ps1`，CI 调用同一个入口，没有第二门框架。固定 SDK/引擎/GdUnit 包、串行 runsettings、120000ms 会话上限及零测试失败保持。

实测HEAD为上述精确 `dab79ad8`，并非后续文档提交。唯一原入口在 **2026-10-22T21:30:31.1888892Z–21:38:30.5425964Z** 正常约479s完成，outer PID34196、exit0、timedOut=false；10条主dotnet命令前7条exit0、后三个controls各exit1，9份完整TRX，原生startup六个stage均exit0/未超时。当时完整门额度实际已用 **1/2**；旧lifecycle修后1/2独立记账，未运行第二轮。此为历史账目，不是当前可沿用的运行授权。

| 层 | 实际结果 | 适用范围/完整性 |
|---|---:|---|
| Core | 327 pass，0 skip | Completed；含11个显示参考时序案例 |
| CLI | 322 pass＋2原skip，total324 | Completed；仅下列两个既有Windows/filesystem大小写冲突skip |
| Prepare | 52 pass，0 skip | Completed；**仅M2人工静态准备链，不覆盖新增dialogue importer** |
| Game positives | 三轮各239 pass，0 skip | solution、standalone、repeat各Completed；与独立source构建的239完整FQN集合逐轮一致 |
| Game discovery | 242 unique | 239 positives＋原3 controls；不是只比计数或由positive TRX反推预期集合 |
| controls | 各1 failed、exit1 | 三个指定原因正常Failed收尾，非abort/crash/session timeout替代 |

CLI两个精确历史skip：
- `Pal.Extract.Tests.ExportMapCliTests.Export_map_rejects_case_ambiguous_archives`
- `Pal.Extract.Tests.InspectCliTests.Inspect_rejects_ambiguous_case_insensitive_names`

三个control分别为320/321断言不等、故意500ms **case** timeout、实际加载scene/script后 `ExpectedReadyException._Ready()` 抛出的 `ISSUE13_EXPECTED_READY_EXCEPTION`。六个startup stage为version/import/fixture-build/prepare/profile/startup，最后Main打印所选synthetic scene及 `PAL_GAME_READY:Core ready`；这仍是M2 synthetic链，不是真实NPC桌面验收。

离线审计核9份TRX的Summary/Counters/RunInfo/Times、definitions/entries/results与unique IDs；9个run ID、1423个execution ID唯一，正常层无坏会话计数，controls各仅指定失败。独立evidence reviewer另核原命令/日志/TRX及FQN集合逻辑，支持原自动门PASS，不据PASS.txt或aborted=0代替完整性。精确原件见本机 `C:/Users/xiaofan/.pi/m3-execution/final-shared-gate.md`、`final-evidence-review.md` 及 `final-gate-evidence-20261022T212917Z-3fd7bfd3`；这些工程外路径不是清洁检出依赖，也不上传真实证据。

## 已有证据及源码适用性

以下为先前组件运行，本阶段不重复全量。详细原件和数字退出在本机任务交接记录 `C:/Users/xiaofan/.pi/m3-execution/26-pre-gate.md`，后续边界修复及输入/时序记录分别为同目录26-boundary-fixes.md与26-dialogue-fidelity-fix.md；该工程外记录不是清洁检出依赖，真实内容从不上传。

| 组件 | 已实现/已核证据 | 不能外推为 |
|---|---|---|
| #21 焦点 | `5806167`取消共享额外暂停；`b87bf0b`修有效信号刺激，RED2→GREEN2，冻结/清键反例均捕获，恢复2＋受影响28 | 物理焦点、OS实际释放/回焦交付 |
| #22 事件 | `30e864c`，merge `73db97d`；完整真实chunk4往返逐字节一致，CLI314＋2原skip，严格字段/跨文件/重编码反例 | 全量 opcode、完整union根分类、全游戏可达性 |
| #23 资源/显示 | `562fd61`，merge `39aada3`；CLI317＋2、Core155、原生显示3及双语人工背景GPU各6零差；已有真实对话Prepare成功并重载七资源 | map10完整会话；Prepare52覆盖对话；旧记录不存在的逐子命令数字exit（CLI旧TRX没有独立exit JSON） |
| #27 静态 | `003fba2`；真实scene4/map10显式提取/Prepare/profile、Main/input与Game95；起点/South阻挡/East合法步 | 原版剧情出生、全部地图像素/所有邻点实测 |
| #24 trigger | `84f7e37`，merge `42bdbdc`；Core255及最终显示23；CLEAR无等待、同更新回执/输入消费、路径诊断修复与独立复核 | 正常场景auto/更新型等待/条件调用 |
| #25 搜索宿主 | `67f60cd`：Core316沿用，Game121＋79＋23＝223不同positive，精确提交真实双语headless各2calls；精确提交双语map10 GPU各10×64000像素零差 | 新单轮Game229、物理键盘/桌面屏幕、整幅地图独立oracle |
| #25 身份/兼容 | `cb8c37c`＋`1298835`：真实可失败RED，最终GREEN6；GPU匹配单语10零差与旧无profile6零差；原Standards定点No issues/OK | 新双语全套；旧HEAD/DLL冒充新HEAD实测 |

这两个#25补丁当时仅改fixture/证据门/测试/必要说明，生产Core/Main/显示及独立compositor未变。#26随后改变Core显示时序与Game输入，旧精确67f双语GPU不证明新规则。新的匹配运行已在wt26精确 `5bf481f` / tree `b4fb696` 完成：zh-Hans→en各一次map10/profile，**各10项×64000像素differences0、各2个完整Main calls、实际消费资源身份匹配**；另一次旧无profile zh-Hans六项零差独立保留，不计作map10/2calls。每通道原容差 **1.1/255** 未变，零差不等于PNG字节相等或精确float identity。原件见本机 `26-matched-gpu.md` 及其 `26-matched-gpu-evidence-20261023-a`。

GPU具有全部1049 tracked与Core/Game DLL/PDB的fresh pre-GPU/post-GPU hash/size/mtime恒定证据。gate虽同source tree，但不同worktree且DLL hash不同；gate只有pre-build与post-gate DLL清单，**没有fresh post-build/pre-test清单，不伪造或倒填**。后续doc-only提交没有跑过CPU/GPU；源码、资源契约不变，文档/提交元数据变化不要求重跑。#25新增6定点仍仅证明当时身份与headless兼容；普通headless可只输出证据，graphical或显式任一GPU输入仍必须两项齐全并核真实加载路径。

真实273帧的非透明像素不含F9–FE：真实地图/对象/领队在等待轮转时应不变，人工F9/FA/alpha消费者测试另证实际颜色变化。map-before-dialogue仅是背景恢复oracle，不是完整地图独立渲染器。真实GPU为逻辑帧回读＋程序化输入，不是人工OS屏幕验收。

## Observer 采证缺陷：工具已停用，非 M3 功能门

重新全文核对 #20/#26 批准规格，并与 agent 的 `task-final-gate.md` 执行交接区分后：批准规格要求的是既有完整门的实际退出码、完整 TRX/控制诊断、资源保护、全局串行及受限进程隐私；新增 Observer 与完整内部子孙 PID/UTC/exit 台账是 agent 追加的执行条件，不是 M3 规格要求。此前将 agent 追加的严格采证不足反向阻断 whole M3 的当前摘要在此纠正；这不是用户授权全面豁免隐私要求。历史工具 P1 未修复，与已由双轴关闭的六项source finding分账。外部 `observe.ps1` 存在PID复用身份模型与短命进程handle race；主命令6缺唯一可靠PID/UTC，部分内部退出unknown，build-server退出未观察。463条观察不是463次可靠完整执行记录，不能倒填归属/数字退出，也不能从测试PASS推导所有后代退出。历史 verifier criterion-1=`not-satisfied` 原样保留，独立evidence review支持自动门PASS；不把移出规格外条件写成已满足完整capture。

正式受限Godot/明确repo executor快照仅支持**当时为空**，不证明所有后代归属/释放或observer隐私全程合规。可能采到非任务进程数据；`owned-processes.json` 原件仅本地私密保留，不打开/转录未知commandLine、不上传/入库，不修改/删除/重跑历史helper。该工具已停用，不修复、不再运行、不自动重测；敏感原件仅按上述限制保留，任何处置须另获明确决定。本记录不关闭历史工具P1，也不以人工反馈补齐历史进程未知值。

## 待完成（当前 CI 与发布回填）

- 历史CI39884910037已查询，实际为cancelled/未通过，细节见本页当前候选段；旧“仅有main CI/尚未查询integration”的记录已过时。当前统一候选仍需分别获准执行完整共享门与精确提交CI；没有push/dispatch授权，历史绿不替代M3。
- #20/#26 结果发布回填、推送/dispatch/关票及main合入仍须相应明确授权；当前仅获准统一候选的局部验证、独立双审与候选本地提交，不宣称 M3 整体验收全部完成。辅助工具的私密原件限制见上节，非 M3 功能阻断。

### Windows 人工清单（用户反馈已完成）

**来源：本对话用户反馈，非 agent 独立实测。** 用户先明确“现在人工 Windows 操作验证 按空格已经可以出现对话了”；随后在收到完整清单（释放后 fresh Space 加速、等待继续；结束后头像文字撤销、背景恢复/移动；再次 Space 与同 NPC 完整交谈；英文显示、Alt-Tab 后台释放/回焦、整数缩放、正常关闭）后回复“这些都正常”。据此记录整体清单用户人工验收通过，含两语言、物理输入、Alt-Tab、缩放与关闭；不是逐项仪器记录，没有新增日志/截图、时间戳或耗时测量，也不是新的 GPU/CI 证据，不证明 Observer 修复或补齐历史进程数据。不要求用户重复验收。

以下原命令与清单保留为复现指南，不是待执行任务。从此integration仓库根运行，复用既有本机真实 `res://Assets/NpcInteractionReal25/Profile.tres`，不重新Prepare/author/复制资源。所有engine已释放后再人工运行，不能与自动lane并行；下列为普通非headless命令，用户参数在 `--` 后：

```powershell
& 'C:/Users/xiaofan/scoop/apps/godot-mono/4.7.2/godot-mono.console.exe' --path src/Pal.Game -- `
  --m3-dialogue-profile res://Assets/NpcInteractionReal25/Profile.tres
# 简中正常关闭后，另开英文会话：
& 'C:/Users/xiaofan/scoop/apps/godot-mono/4.7.2/godot-mono.console.exe' --path src/Pal.Game -- `
  --m3-dialogue-profile res://Assets/NpcInteractionReal25/Profile.tres --m3-dialogue-language en
```

1. 两语言分别从初始站位搜索面前owner60（Return/Space/KP Enter/左Ctrl），核地图/32对象/领队与遮挡、NPC转向站立、名称/画像/正文布局、字形阴影。启动搜索键按住不应重复skip或自动重开聊；释放后新的Search/Menu press加速正文，等待提示出现后新press继续，核背景/颜色恢复、返回探索及再次完整交谈。
2. 用实际物理方向键/WASD核移动、碰撞、返回后的press/release；调整窗口核320×200整数缩放/letterbox。记录实际桌面表现和任何异常，不要求上传真实资源或图片。
3. 实际Alt-Tab并在后台释放方向键、回焦，记录按下/释放/交付与最终状态；不预定后台一定移动或停止。Alt本身映射Menu，可能合法加速/确认，不误归为focus错误。
4. 正常关闭窗口并记录是否正常退出/有无错误；默认无M3参数仍为M2入口，但本清单不要求用户补已完成工程测试。人工结果与自动/GPU及CI分列，不说明历史 Observer P1 已修复。

## 保护与异常纪律

gate前后1049 tracked hash/size/mtime全同；原4703、wt25源575、wt26已import目标575及main原5用户文档前后全同。integration新575首import中恰274份 `.png.import` 仅mtime刷新（字节/size变化0），其余301不变，无新增残留，**不是全部5278的mtime不变**。当前integration资源基线为gate实际 `protected-after.json`，不回拨旧mtime。已有原始输入/M1/M2/native/profile与真实新增副本保护，复用而不重新提取、覆盖或自动同步；hash读取可能影响atime，不宣称保护atime。人工测试只沿既有fixture流程回收本次GUID，历史遗留不清理。真实Assets和完整图片/日志不提交上传。

Godot/GdUnit/Prepare/solution全局串行；全局Godot只查询PID/Name，未知先停。会话中止、崩溃、timeout或未知退出保存实际命令、HEAD、原日志/partialTRX、自有PID与资源快照后停链，不自动重跑/增时/放宽/升级。历史M2规格/验收/失败原件不重写，见 [M2验收](../m2/acceptance.md)与[异常说明](../../agents/test-diagnostics.md)。
