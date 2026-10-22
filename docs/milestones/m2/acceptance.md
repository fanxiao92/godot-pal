# M2 Windows 探索切片：分层验收记录

## 当前结论与来源

**M2 已完成（2026-10-22）：功能、本地自动/人工验收、收尾文档独立双轴终审及后续远端完整 Windows headless CI 均已接受。** 成果已合入并通过 SSH 推送 main，已验证交付提交为 `a76f83bf08c421e83892cc9515cbdd4c032014ec`。[最终验收 #17](https://github.com/fanxiao92/godot-pal/issues/17) 于 05:39:50Z 关闭，[父规格 #11](https://github.com/fanxiao92/godot-pal/issues/11) 于 05:39:58Z 关闭；发布后跨盘测试缺陷 [#19](https://github.com/fanxiao92/godot-pal/issues/19) 修复并于 07:21:05Z 关闭，[新远端 CI 39738074108](https://github.com/fanxiao92/godot-pal/actions/runs/39738074108) 为 completed/success。M3 尚未启动，音频、导出包和其他平台未验收。

本页是进度与分层证据汇总；普通执行日志仍在 Issue / 本机证据目录。入口与 typed profile 前置见 [M2 计划](plan.md#3-资源准备与测试配置)，包身份与 bootstrap 见 [固定 GdUnit 包说明](../../testing/gdunit-source-packages.md)。原本地完整门、人工检查、后续主干交付和远端 CI 分别保留原时间线；后来的成功不改写旧失败，也不将 headless 结果外推为新的 GPU/物理操作验收。

- [完整自动门及同字节发布](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6320185765)。
- [提交与本地合入](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6328157497)。
- [#13 地图/原生资源](https://github.com/fanxiao92/godot-pal/issues/13#issuecomment-6284911110)、[#14 静态角色/遮挡](https://github.com/fanxiao92/godot-pal/issues/14#issuecomment-6290204888)、[#15 行走闭环](https://github.com/fanxiao92/godot-pal/issues/15#issuecomment-6294877837)、[#16 双键位/焦点](https://github.com/fanxiao92/godot-pal/issues/16#issuecomment-6308012692)。
- [最终人工阶段准备与清单](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6330915508)记录的是反馈前状态；后续[实际操作者确认与父接受](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6331298410)另行记录，不改写原件。
- [收尾文档独立双轴终审接受](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6331624334)：Standards、Spec/evidence 均为 OK / No issues found；原九文档已随 `e1bc730` 提交，#17 / #11 随后关闭。
- [main 推送与 worktree 清理](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6333983593)，以及[首次远端 CI 失败原件核对](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6334057358)。
- [#19 修复、红绿验证与新远端完整 CI 通过](https://github.com/fanxiao92/godot-pal/issues/19#issuecomment-6335139243)；[父规格后续结果回填](https://github.com/fanxiao92/godot-pal/issues/11#issuecomment-6335142895)。

## #17 十二项验收矩阵

顺序对应已关闭 #17 的十二项 criterion，均已按限定证据范围完成；后续 #19 和远端 CI 补证另列，不将本页当作新运行或扩展范围的授权。

| # | criterion | 证据 / 当前判定 |
|---|---|---|
| 1 | 前置验收/阻塞真实满足 | #12 关卡实际通过；#13～#16 与追加 #18 已关闭，#17 直接前置 #16/#18 已解除。已接受。 |
| 2 | solution/Core/Godot/CLI/import/startup 回归 | 原本地完整门及后续远端完整 CI 各有独立9TRX/10主命令和原生启动证据，非只审阅。已接受。 |
| 3 | 共享自动化/CI，人工资源 | `Test-M0.ps1` 在本地及远端 Windows CI39738074108 完整通过，含实际 Game 场景/输入/帧测试；CI 仅生成并消费人工资源，无原版数据上传。 |
| 4 | 真实包、测试配置、资源保护 | #13 显式导入 scene2/map12/palette0/day；#14 独立 profile 来源/起点核对。人工验收时P/S各816 Assets、P416个已验收 M1 文件/S0且保护相等；后续 worktree 清理先行归档，主目录资源保留，真实数据不提交/上传。已接受。 |
| 5 | 桌面地图/透明/帧/锚点/两层遮挡 | #13～#15 的原生加载、GPU与操作者分层证据；不以 headless 排序断言冒充像素。已接受原范围。 |
| 6 | 真实键盘/步进/碰撞/相机/移动遮挡/初值 | #15/#16 操作者反馈已接受；真实 scene2 对象专属阻挡点为 N/A（有界64候选无可用点），规则另由独立人工 fixture 自动化覆盖，不称真实对象碰撞人工 PASS。 |
| 7 | resize/整数缩放/黑边/焦点 | #16 已接受实际失焦/恢复；本次最终窗口清单获操作者完整确认，见下文。已接受。 |
| 8 | 正常关闭/错误/残留/实际操作者 | 本次 owned PID50896 exit0、stderr0、diagnostics[]；操作者确认 X 关闭；02:28:11.5226017Z 最小 Godot PID/Name 查询0。已接受该时点/范围，不是全机.NET或完整后代盘点。 |
| 9 | 错误明确/独立期望 | 准备/加载坏包、坏配置及缺资源回归；三指定失败控制和独立字面量期望，未宽放 ERROR。已接受。 |
| 10 | README/前置/分层证据/风险 | 原九文档已随e1bc730提交：当时9文档/90本地相对链接、围栏/事实/保护静态检查通过，独立 Standards / Spec-evidence 均 OK；后续交付与 #19/CI 进度在本页另行同步，不冒称对本次文字更新重新独立终审。 |
| 11 | 定位/修复/回归且不扩范围 | Main 早退焦点断连、安全进程采集、focus 首次实际更新判据、#18 固定包及后续 #19 跨盘夹具均有各自回归；旧失败保留，未扩展事件/对话/多队员。已接受。 |
| 12 | 回填本票/父规格最终收尾权限 | 明确授权后先回填/关闭#17，再回填/关闭#11；后续#19亦已修复关闭，父票已同步远端CI实际通过结果。服务器状态均已回读核实。 |

## 父规格 #11 故事覆盖

| 故事编号 | 类别与证据 |
|---|---|
| 1～8 | 直接 Main、真实 scene2/map12、透明/遮挡/初始对象、role0及已核测试起点：#13/#14；起点不是原版开场或剧情快照。 |
| 9～20 | 四向位移、按/持/放、优先级/别名/repeat、100ms/帧/碰撞/相机：Core确定性边界 + Godot真实更新 + #15/#16操作者；对象专属真实点限制如上。 |
| 21～23 | 320×200同视野、8:5/整数缩放/黑边：最终操作者；焦点冻结/清键/fresh press与不补走：#16。焦点与回焦立即首步是明确扩展，不冒称原版SDL行为。 |
| 24～30 | 缺失/损坏失败、显式原生准备、路径/散列保护、独立typed profile和角色来源、对象静态初值：Prepare/Game回归及 #13/#14。Prepare不author profile。 |
| 31～37 | Core公开Update与不可变计划、GdUnit实际场景/输入/帧、#12关卡、失败控制/进程边界、人工资源CI路线和分层证据：原本地完整门与后续远端完整Windows headless CI均通过。 |
| 38～40 | 正常关闭及Windows限定范围：最终操作者/owned exit；CLI/Core/import/startup既有回归：原完整门。 |

## 原完整自动门（不是本轮文档重跑）

执行于源候选工作树 `godot-pal-m2-issue13-integration`，基线 `98b138ee67086cdcac760648fb99c635eb66051c` 加已审核两文件 focus 补丁和八文件 consumer 补丁；完整候选归档 SHA256 为 `f7faeea98d84072b177fd1f1da22c35ffba32d473bbf0fb2821b7a1039a46812`。后续 focus 提交 `597ef1e` 与 consumer 提交 `04f8e91` 本地合入，唯二内容差异是门后发布说明和测试 README 的事实同步，生产/测试实现字节未变；并非在最终主树又跑了一次完整门。

实际外层命令为 `pwsh -NoProfile -NonInteractive -File scripts/Test-M0.ps1 -GodotPath <本机4.7.2 console> -EvidenceDirectory <R>/issue18-gdunit-consumer-gate-02/shared-gate`，完整本机绝对路径保留于原 `gate-run/outer-command.json`。UTC **2026-10-21T15:27:30.327803–15:33:44.046449Z**，outer0、timedOut=false、PASS存在。10主dotnet命令实际退出为 `[0,0,0,0,0,0,0,1,1,1]`，无timeout；独立raw review OK，父核105/105文件长度/SHA。

| 原命令/检查（结果目录位于上述 shared-gate） | 实际结果 |
|---|---|
| `dotnet --info`；`restore GodotPal.sln --no-http-cache`；`build GodotPal.sln --no-restore` | 各exit0 |
| `dotnet test GodotPal.sln --no-build --no-restore --logger trx --results-directory <solution>` | Core129、CLI300pass/302total＋2准确Windows大小写歧义skip、Prepare52、Game170；各正例TRX Completed |
| `dotnet test src/Pal.Game --no-build --no-restore --list-tests` | 173唯一发现＝170正例＋3控制 |
| `dotnet test src/Pal.Game --no-build --no-restore --logger trx --results-directory <game或game-repeat>`，原两轮 | 各170pass、0fail/skip、Completed、exit0 |
| 同一Game命令分别 `--filter IntentionalAssertionMismatch` / `IntentionalTimeout` / `NativeReadyException`，独立结果目录 | 各1fail、exit1、Summary Failed；指定诊断依次为321 vs 320、`timed out after 500ms`、`ISSUE13_EXPECTED_READY_EXCEPTION`，不是崩溃/会话中止 |
| 原共享 `Test-GameStartup.ps1` 的 version/fixture-build/prepare/profile/import/实际Main headless启动链 | 各exit0，所选native map与 `PAL_GAME_READY:Core ready` 完整 |

共9TRX（solution4、独立1、repeat1、控制3）；重复轮不合并虚增为独立用例数。Godot4.7.2 / SDK10.0.402 / net10 / TestSdk18.0.1及Core原xUnit基线保留；#18 固定 API `5.1.0-rc6.pal.a5f20fb8.1`、adapter `3.2.0-pal.a5f20fb8.1` 的发布身份以包页/锁为准。包图、cache与copied net10 DLL hash证明文件级身份，不是进程内加载路径探针。

## 主干交付与 worktree 收尾

`04f8e91` 是修复/固定包本地合入检查点；原收尾九文档以 `e1bc730` 提交，#17 / #11 依序关闭。用户随后批准主干交付：所有26个非main本地分支的提交均已包含在成果中，本地main从 `b6f7238` 快进44个提交，远端main从 `2637aa0` 增加45个提交到 `e1bc730`，普通SSH推送，无强推或逐支重复合并。后续#19单测试文件修复 `a76f83b` 也已推送main，当前已验证交付提交如页首。

先确认远端已保存，再正常移除10个附加worktree，目前仅保留主目录main工作树，分支引用保留供回溯。清理前将19份历史TRX与837份原生资源（共856文件，含21份早期独有RuntimeHandoff资源）按原相对路径同卷、无覆盖移入工程外归档，逐件hash/size/mtime校验；主目录原始数据、M1包、816 Assets与既有缓存保持。此为后续获准清理，不倒写为原验收执行时已移除源工作树。

## 远端 Windows CI 与跨盘修复

首推 `e1bc730` 自动触发的 [CI39731421949](https://github.com/fanxiao92/godot-pal/actions/runs/39731421949) 仍保留为失败：Core129通过、CLI300pass＋原2skip，Prepare51pass/1fail。`Project_contained_input_is_rejected_unchanged_before_Godot_can_start` 在测试夹具 `Directory.Move(Input, projectInput)` 阶段抛 `Source and destination path must have identical roots. Move will not work across volumes.`，尚未进入正式RunPrepare/断言；不是缺版权数据、预期失败控制、旧hook异常或零字节scene错误。

[#19](https://github.com/fanxiao92/godot-pal/issues/19) 仅修复 `PreparePathSafetyTests.cs`：复用现有 `M1Bundle.Write` 在目标位置直接生成人工包，取消跨根搬运，finally清理本用例GUID人工包；原退出码、精确路径拒绝、输入hash/mtime不变、无输出及不启动不存在Godot路径的断言全部保留。原代码同根PASS1、不同DOS路径根RED1（同一异常）；修复后原case不同根GREEN1，16项路径安全检查正常根/不同根各16PASS。临时SUBST盘符已移除；这是本地路径根模拟，不冒称两物理盘，也未启动本地Godot。

`a76f83b` 推送后自动触发的 [CI39738074108](https://github.com/fanxiao92/godot-pal/actions/runs/39738074108) 在Windows2022上 **completed/success**，未手工dispatch或重跑。已下载并核验51份原始证据、9TRX及10主dotnet命令，退出序列为 `[0,0,0,0,0,0,0,1,1,1]`，均非timeout且符合各自预期：

- Core129；CLI300pass/302total＋原准确2Windows大小写歧义skip；**Prepare52全通过，原失败FQN明确Passed**。
- solution/独立/repeat Game各170，discovery173含3控制；重复轮不虚增独立用例数。
- 三控制各恰1fail，诊断分别为321 vs 320、500ms、`ISSUE13_EXPECTED_READY_EXCEPTION`；TRX Summary/RunInfos/counters无异常会话中止，不把任意失败当作控制通过。
- fixture-build/version/native prepare/profile/import/Main startup各exit0、notimeout，含 `PAL_GAME_READY:Core ready`；受限最终进程快照count0。CI原件记录人工输入在C:、工程输出在D:，提供真实远端跨根环境补证。

因此远端人工资源headless CI已实际通过，不再只是接线或本地结果；真实输入和派生游戏资源仍不上传。此前本地GPU/人工验收按原范围保留，本次CI不增加新的物理操作、图形像素、音频、导出包或其他平台验收。#19与父票均已完成结果回填，原失败与本地RED原件不改写。

## 真实资源与最终操作者时间线

#13/#14 已显式准备并核对真实切片；#15 的GPU/操作者行走反馈和 #16 的物理键位、别名、repeat、Alt-Tab/焦点清单已分别接受，不伪造新截图或重做这些验收。对象保持原始初值，无脚本/计时；默认起点 `(736,688)` / South0 / Layer0 / StepPhase0，role0原始 WalkFramesWord0来自 DATA chunk3 offset768 的 `rgwWalkFrames`。该配置是测试入口，不是原版出生点。

最终主树准备仅执行必要的 `dotnet build src/Pal.Game --nologo`：**2026-10-22T01:44:30.527835–01:44:41.746157Z**，exit0、0warnings/errors（依赖Core build，不是Core测试）。固定发布URL bootstrap四文件已验证；当前主树使用普通user NuGet cache，不是原gate隔离cache。准备manifest32/32由父核验。

本次实际命令为官方 Godot4.7.2 console `--path C:/Users/xiaofan/Repos/godot-pal/src/Pal.Game`，普通Main、默认原生profile、RTX3070；owned **PID50896 / creation134371092375406819**，**02:20:37.5406819–02:26:36.2513578Z**。原stdout含两个完整ready/map标记，exit0、stderr0、diagnostics[]。操作者随后报告“以上窗口检查未发现异常，游戏窗口已关闭。”，覆盖默认1280×800、宽矮/窄高/非8:5/非整数尺寸、nearest整数放大与黑边、同320×200视野、返回8:5及正常X关闭全部新清单。此为 **operator-reported**，不是agent物理观察或新逐像素diff。

原 `manual-result.json` 在人反馈前记录 `manualAccepted=false` 并保持不可变；父以单独 `parent-manual-acceptance.json` 接受后续反馈。wrapper显示的两行 `System.Threading.Tasks.VoidTaskResult` 是两个CopyToAsync返回值，不是Godot ERROR，未为掩盖日志改脚本。父于 **02:28:11.5226017Z** 仅查询全局Godot PID/Name，结果0；只证明该时点。P947/S945源文件、P/S各816Assets、P416M1/S0、空RuntimeProfiles、四归档及旧证据在人工前后路径集合/字节/size/mtime相等；后续文档写入以此为新基线白名单，不把正常engine cache变化算成原资源变化。

## 保留限制与证据位置

| 范围 | 限制 |
|---|---|
| 环境/CI | 本地Windows开发运行与远端Windows人工资源headless完整CI均已通过；fork pack Actions仍不是GameCI证据，二者分开。导出包和其它平台未验收。 |
| 缓存 | 原完整门使用全新证据内NuGet/HTTP cache，但保留既有`.godot`；不能称全新engine cache。 |
| 图形/人工 | headless、GPU逻辑像素、物理OS输入、操作者反馈各有范围；真实scene2对象专属阻挡点N/A，不造对象或改初值补PASS。 |
| 音频 | 历史WASAPI失败/dummy回退仍未验收；本次stderr0不证明音频根治。 |
| 历史失败 | hook abort、85ms观测判据失败、零字节scene Parse Error、包装/代理失败及首次CI跨盘夹具失败均保留原结论；各次修复、归档与后续运行分别有授权和证据，成功不改写旧失败或证明概率零。详[测试异常说明](../../agents/test-diagnostics.md)。 |
| 功能边界 | 无事件/对话/语言包/字库、多队员、对象动态脚本/计时；M3需另行规划批准，未来事件JSON设计未取消。 |

私有本机证据根 `R=C:/Users/xiaofan/.pi/agent/task-context/godot-pal-m2`：`issue18-gdunit-consumer-gate-02`（原完整门、105文件manifest与四归档）、`issue18-integration-01`（合入）、`issue17-final-manual-01`（准备/build、owned命令/退出、原stdout/stderr、父接受和保护）、`main-delivery-01`（主干推送、worktree清理/856文件归档、首次CI失败原件）、`ci-path-safety-fix-01`（#19原源码/红绿TRX/临时盘符清理、单文件提交与新CI全51原件及核验）。这些不是可移植Markdown链接，也不提交真实资源、缓存或私有原始日志。本轮文档只做静态链接/围栏/接口事实与保护核对，未重新执行SDK/引擎/测试。
