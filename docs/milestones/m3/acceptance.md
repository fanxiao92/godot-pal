# M3 分层验收检查点（自动门 PASS、用户人工确认通过，当前 CI 待验证）

## 状态与范围

#26 以本地 integration `6c2e036f984dac1479b22697f73488fa2499f37d` 为起点，阶段1修正两项字形/标识符边界，阶段2修正四项reference显示输入/时序偏差及直接调用方/fixtures。固定全M3候选经过 Standards/Spec 两轴初审、修复后定点复核，原六项source finding均关闭，无新有效源码问题；最终候选 `5bf481fba44f7f278f555daad046e11ed1eb4eea` 已完成匹配双语GPU，并机械no-ff合入实际 integration `dab79ad85a4a352826a31eb0569a58e1f17eca88`，两者tree均为 `b4fb69689d6820727b31ce6909117b1904a5b540`。本地合入不等于整体验收或已推送。

**当前结论：原项目完整自动门 PASS，经独立 evidence review 支持；本对话用户已确认 Windows 人工清单通过；当前 integration 人工资源 Windows CI 待验证，#20 整体验收及发布回填仍待相应结果和权限。** Observer 是 agent 自行追加、规格外的辅助采证工具，现已停用，其缺陷不作为 M3 功能验收门；历史 verifier criterion-1 仍为 `not-satisfied`，不改写历史报告、不宣称工具修复或隐私风险消除。以下GitHub状态仅截至既有读取：#20/#21/#23/#24/#25/#26/#27 OPEN，仅 #22 CLOSED；本轮没有新网络查询、关票或推送。

批准范围见 [计划](plan.md)；普通运行与资源身份见 [NPC 宿主契约](../../design/npc-interaction-host.md)。这是 scene4/map10/owner60/0188 的显式独立上下文，不是原版出生、正常自由探索或全量 VM/auto。owner57 热点未改 state，只是不在此上下文调度；32原始对象保留显示/碰撞。默认简中，英文只在启动时显式选择。不带 M3 参数仍走 M2 Main，独立静态模式保留。运行时只读准备好的原生资源和现代事件 JSON，不读原始 MKF/SLF/M.MSG/M1 JSON/参考头文件，不自动提取或补配置。

## 实际 integration 共享门结果

共享入口仍为 `scripts/Test-M0.ps1`，CI 调用同一个入口，没有第二门框架。固定 SDK/引擎/GdUnit 包、串行 runsettings、120000ms 会话上限及零测试失败保持。

实测HEAD为上述精确 `dab79ad8`，并非后续文档提交。唯一原入口在 **2026-10-22T21:30:31.1888892Z–21:38:30.5425964Z** 正常约479s完成，outer PID34196、exit0、timedOut=false；10条主dotnet命令前7条exit0、后三个controls各exit1，9份完整TRX，原生startup六个stage均exit0/未超时。完整门额度实际已用 **1/2**；旧lifecycle修后1/2独立记账，未运行第二轮。

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

- 当前integration远端人工资源Windows CI待验证。截至既有只读查询仅见main历史CI（含 `97ec55a` 的39744713759与M2交付39738074108），没有当前integration对应结果；本轮未新查，不称实时。没有push/dispatch授权，历史绿不替代M3。
- #20/#26 结果发布回填、推送/dispatch/关票及main合入仍须相应明确授权；本次仅本地文档提交并交独立短审，不宣称 M3 整体验收全部完成。辅助工具的私密原件限制见上节，非 M3 功能阻断。

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
