# M2 Windows 探索切片：分层验收记录

## 当前结论与来源

**功能、Windows 自动/人工验收及文档独立双轴终审均已接受（2026-10-22）。** 已验收实现基线为 `integration/m2-exploration@04f8e91713da4f69531f9461a1ff83c78a170baf`；本尾批仅为独立文档提交，不改变生产/测试实现，本地交付未推送。用户已明确允许定向提交九文档并按 [#17](https://github.com/fanxiao92/godot-pal/issues/17) → [父规格 #11](https://github.com/fanxiao92/godot-pal/issues/11) 回填/关闭；最终提交与服务器状态以关联 Issue 为准。

本页保留值得长期查阅的证据层次和边界，普通执行日志仍在 Issue / 本机证据目录。入口与 typed profile 前置见 [M2 计划](plan.md#3-资源准备与测试配置)；包身份、bootstrap 与发布检查点见 [固定 GdUnit 包说明](../../testing/gdunit-source-packages.md)。该包说明的“尚未合入”是发布当时检查点，后续已本地合入，不能当作当前状态。

- [完整自动门及同字节发布](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6320185765)。
- [提交与本地合入](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6328157497)。
- [#13 地图/原生资源](https://github.com/fanxiao92/godot-pal/issues/13#issuecomment-6284911110)、[#14 静态角色/遮挡](https://github.com/fanxiao92/godot-pal/issues/14#issuecomment-6290204888)、[#15 行走闭环](https://github.com/fanxiao92/godot-pal/issues/15#issuecomment-6294877837)、[#16 双键位/焦点](https://github.com/fanxiao92/godot-pal/issues/16#issuecomment-6308012692)。
- [最终人工阶段准备与清单](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6330915508)记录的是反馈前状态；后续[实际操作者确认与父接受](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6331298410)另行记录，不改写原件。
- [文档独立双轴终审接受](https://github.com/fanxiao92/godot-pal/issues/17#issuecomment-6331624334)：Standards、Spec/evidence 均为 OK / No issues found；随后用户明确授权本次定向文档提交和最终回填/关闭。

## #17 十二项验收矩阵

顺序对应 Issue 的十二项 criterion；“已接受”指该项证据范围，不预先勾选 Issue 或授权关闭。

| # | criterion | 证据 / 当前判定 |
|---|---|---|
| 1 | 前置验收/阻塞真实满足 | #12 关卡实际通过；#13～#16 与追加 #18 已关闭，#17 直接前置 #16/#18 已解除。已接受。 |
| 2 | solution/Core/Godot/CLI/import/startup 回归 | 下述原完整门9TRX/10主命令及原生启动链，非只审阅。已接受。 |
| 3 | 共享自动化/CI，人工资源 | `Test-M0.ps1` 与现有 Windows workflow 接线已交付；本地完整通过，远端 Game CI 未运行。已接受此限定范围。 |
| 4 | 真实包、测试配置、资源保护 | #13 显式导入 scene2/map12/palette0/day；#14 独立 profile 来源/起点核对。P/S各816 Assets，P416个已验收 M1 文件、S0，后续保护相等；真实数据不提交/上传。已接受。 |
| 5 | 桌面地图/透明/帧/锚点/两层遮挡 | #13～#15 的原生加载、GPU与操作者分层证据；不以 headless 排序断言冒充像素。已接受原范围。 |
| 6 | 真实键盘/步进/碰撞/相机/移动遮挡/初值 | #15/#16 操作者反馈已接受；真实 scene2 对象专属阻挡点为 N/A（有界64候选无可用点），规则另由独立人工 fixture 自动化覆盖，不称真实对象碰撞人工 PASS。 |
| 7 | resize/整数缩放/黑边/焦点 | #16 已接受实际失焦/恢复；本次最终窗口清单获操作者完整确认，见下文。已接受。 |
| 8 | 正常关闭/错误/残留/实际操作者 | 本次 owned PID50896 exit0、stderr0、diagnostics[]；操作者确认 X 关闭；02:28:11.5226017Z 最小 Godot PID/Name 查询0。已接受该时点/范围，不是全机.NET或完整后代盘点。 |
| 9 | 错误明确/独立期望 | 准备/加载坏包、坏配置及缺资源回归；三指定失败控制和独立字面量期望，未宽放 ERROR。已接受。 |
| 10 | README/前置/分层证据/风险 | 九文档同步真实入口、profile 与缓存/平台边界；9文档/90本地相对链接、围栏/事实/保护静态检查通过，独立 Standards / Spec-evidence 均 OK，用户已允许定向提交。最终提交记录见 #17。 |
| 11 | 定位/修复/回归且不扩范围 | Main 早退焦点断连、安全进程采集、focus 首次实际更新判据及 #18 固定包均有各自回归；旧失败保留，未扩展事件/对话/多队员。已接受。 |
| 12 | 回填本票/父规格最终收尾权限 | 用户已明确授权父按 #17 → #11 完成最终证据回填/关闭；实际提交和服务器状态以关联 Issue 为准，不把文档写入本身当作关票成功。 |

## 父规格 #11 故事覆盖

| 故事编号 | 类别与证据 |
|---|---|
| 1～8 | 直接 Main、真实 scene2/map12、透明/遮挡/初始对象、role0及已核测试起点：#13/#14；起点不是原版开场或剧情快照。 |
| 9～20 | 四向位移、按/持/放、优先级/别名/repeat、100ms/帧/碰撞/相机：Core确定性边界 + Godot真实更新 + #15/#16操作者；对象专属真实点限制如上。 |
| 21～23 | 320×200同视野、8:5/整数缩放/黑边：最终操作者；焦点冻结/清键/fresh press与不补走：#16。焦点与回焦立即首步是明确扩展，不冒称原版SDL行为。 |
| 24～30 | 缺失/损坏失败、显式原生准备、路径/散列保护、独立typed profile和角色来源、对象静态初值：Prepare/Game回归及 #13/#14。Prepare不author profile。 |
| 31～37 | Core公开Update与不可变计划、GdUnit实际场景/输入/帧、#12关卡、失败控制/进程边界、人工资源CI路线和分层证据：原完整门；远端GameCI未执行。 |
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

## 真实资源与最终操作者时间线

#13/#14 已显式准备并核对真实切片；#15 的GPU/操作者行走反馈和 #16 的物理键位、别名、repeat、Alt-Tab/焦点清单已分别接受，不伪造新截图或重做这些验收。对象保持原始初值，无脚本/计时；默认起点 `(736,688)` / South0 / Layer0 / StepPhase0，role0原始 WalkFramesWord0来自 DATA chunk3 offset768 的 `rgwWalkFrames`。该配置是测试入口，不是原版出生点。

最终主树准备仅执行必要的 `dotnet build src/Pal.Game --nologo`：**2026-10-22T01:44:30.527835–01:44:41.746157Z**，exit0、0warnings/errors（依赖Core build，不是Core测试）。固定发布URL bootstrap四文件已验证；当前主树使用普通user NuGet cache，不是原gate隔离cache。准备manifest32/32由父核验。

本次实际命令为官方 Godot4.7.2 console `--path C:/Users/xiaofan/Repos/godot-pal/src/Pal.Game`，普通Main、默认原生profile、RTX3070；owned **PID50896 / creation134371092375406819**，**02:20:37.5406819–02:26:36.2513578Z**。原stdout含两个完整ready/map标记，exit0、stderr0、diagnostics[]。操作者随后报告“以上窗口检查未发现异常，游戏窗口已关闭。”，覆盖默认1280×800、宽矮/窄高/非8:5/非整数尺寸、nearest整数放大与黑边、同320×200视野、返回8:5及正常X关闭全部新清单。此为 **operator-reported**，不是agent物理观察或新逐像素diff。

原 `manual-result.json` 在人反馈前记录 `manualAccepted=false` 并保持不可变；父以单独 `parent-manual-acceptance.json` 接受后续反馈。wrapper显示的两行 `System.Threading.Tasks.VoidTaskResult` 是两个CopyToAsync返回值，不是Godot ERROR，未为掩盖日志改脚本。父于 **02:28:11.5226017Z** 仅查询全局Godot PID/Name，结果0；只证明该时点。P947/S945源文件、P/S各816Assets、P416M1/S0、空RuntimeProfiles、四归档及旧证据在人工前后路径集合/字节/size/mtime相等；后续文档写入以此为新基线白名单，不把正常engine cache变化算成原资源变化。

## 保留限制与证据位置

| 范围 | 限制 |
|---|---|
| 环境/CI | 本地Windows开发运行；远端GameCI未跑，fork pack Actions不是GameCI；导出包和其它平台未验收。 |
| 缓存 | 原完整门使用全新证据内NuGet/HTTP cache，但保留既有`.godot`；不能称全新engine cache。 |
| 图形/人工 | headless、GPU逻辑像素、物理OS输入、操作者反馈各有范围；真实scene2对象专属阻挡点N/A，不造对象或改初值补PASS。 |
| 音频 | 历史WASAPI失败/dummy回退仍未验收；本次stderr0不证明音频根治。 |
| 历史失败 | hook abort、85ms观测判据失败、零字节scene Parse Error及包装/代理失败均保留原结论；四synthetic获准工程外可恢复归档后才执行额外一次完整门，一次PASS不证明概率零。详[测试异常说明](../../agents/test-diagnostics.md)。 |
| 功能边界 | 无事件/对话/语言包/字库、多队员、对象动态脚本/计时；M3需另行规划批准，未来事件JSON设计未取消。 |

私有本机证据根 `R=C:/Users/xiaofan/.pi/agent/task-context/godot-pal-m2`：`issue18-gdunit-consumer-gate-02`（原完整门、105文件manifest与四归档）、`issue18-integration-01`（合入）、`issue17-final-manual-01`（准备/build、owned命令/退出、原stdout/stderr、父接受和保护）。这些不是可移植Markdown链接，也不提交真实资源、缓存或私有原始日志。本轮文档只做静态链接/围栏/接口事实与保护核对，未重新执行SDK/引擎/测试。
