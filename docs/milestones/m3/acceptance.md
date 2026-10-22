# M3 分层验收检查点（候选，非最终通过）

## 状态与范围

#26 第一阶段以本地 integration 合入 `6c2e036f984dac1479b22697f73488fa2499f37d` 为起点，仅同步共享脚本/当前文档与匹配检查。#21/#22/#23/#24/#27/#25 成果均在该祖先链；完整 M3 验收尚未完成。GitHub 回读仍为 #20/#21/#23/#24/#25/#26/#27 OPEN，仅 #22 CLOSED，不能以本地实现替代关票/推送或整体验收。

批准范围见 [计划](plan.md)；普通运行与资源身份见 [NPC 宿主契约](../../design/npc-interaction-host.md)。这是 scene4/map10/owner60/0188 的显式独立上下文，不是原版出生、正常自由探索或全量 VM/auto。owner57 热点未改 state，只是不在此上下文调度；32原始对象保留显示/碰撞。默认简中，英文只在启动时显式选择。不带 M3 参数仍走 M2 Main，独立静态模式保留。运行时只读准备好的原生资源和现代事件 JSON，不读原始 MKF/SLF/M.MSG/M1 JSON/参考头文件，不自动提取或补配置。

## 当前共享门要求（不是新运行结果）

共享入口仍为 `scripts/Test-M0.ps1`，CI 调用同一个入口，没有第二门框架。固定 SDK/引擎/GdUnit 包、串行 runsettings、120000ms 会话上限及零测试失败保持。

| 层 | 当前源要求 | 计数来源/限制 |
|---|---:|---|
| Core | 316 pass，0 skip | #25 最终 Core316；公开确定性时间/输入/回执与搜索返回规则 |
| CLI | 317 pass＋2原skip | #22 的314＋#23三项（两项 DialogueLanguageCliTests、一项直接 RLE）；#23保存完整319结果，不沿用旧314作为当前门要求 |
| Prepare | 52 pass，0 skip | 相对 main 未改，仍是 M2静态准备链；**不代表覆盖新增对话 importer** |
| Game positives | 229 pass，0 skip，每轮 | #25 的223个不同案例＋身份/兼容修复新增6；8个positive classes |
| Game discovery | 232 unique | 本阶段已实际发现232，包含229 positives及原3 controls，无重复 |
| controls | 各1 failed、exit1 | AssertionMismatch须320/321；Timeout须指定500ms；NativeReady须ISSUE13_EXPECTED_READY_EXCEPTION，不接受崩溃/会话中止替代 |

最终共享门须执行 solution、独立 Game、repeat、发现与三个单独控制，以及原生 prepare/profile/import/Main 启动链。核实际数字退出、完整 TRX Summary/RunInfo/Times/definitions/entries/results/unique IDs 与指定诊断，不拼接组件结果为整门通过。CLI仅允许两个具名原能力skip；其他层不得skip。当前修改只是门要求，不是预填未来 PASS。

## 已有证据及源码适用性

以下为先前组件运行，本阶段不重复全量。详细原件和数字退出在本机任务交接记录 `C:/Users/xiaofan/.pi/m3-execution/26-pre-gate.md`；该工程外记录不是清洁检出依赖，真实内容从不上传。

| 组件 | 已实现/已核证据 | 不能外推为 |
|---|---|---|
| #21 焦点 | `5806167`取消共享额外暂停；`b87bf0b`修有效信号刺激，RED2→GREEN2，冻结/清键反例均捕获，恢复2＋受影响28 | 物理焦点、OS实际释放/回焦交付 |
| #22 事件 | `30e864c`，merge `73db97d`；完整真实chunk4往返逐字节一致，CLI314＋2原skip，严格字段/跨文件/重编码反例 | 全量 opcode、完整union根分类、全游戏可达性 |
| #23 资源/显示 | `562fd61`，merge `39aada3`；CLI317＋2、Core155、原生显示3及双语人工背景GPU各6零差；已有真实对话Prepare成功并重载七资源 | map10完整会话；Prepare52覆盖对话；旧记录不存在的逐子命令数字exit（CLI旧TRX没有独立exit JSON） |
| #27 静态 | `003fba2`；真实scene4/map10显式提取/Prepare/profile、Main/input与Game95；起点/South阻挡/East合法步 | 原版剧情出生、全部地图像素/所有邻点实测 |
| #24 trigger | `84f7e37`，merge `42bdbdc`；Core255及最终显示23；CLEAR无等待、同更新回执/输入消费、路径诊断修复与独立复核 | 正常场景auto/更新型等待/条件调用 |
| #25 搜索宿主 | `67f60cd`：Core316沿用，Game121＋79＋23＝223不同positive，精确提交真实双语headless各2calls；精确提交双语map10 GPU各10×64000像素零差 | 新单轮Game229、物理键盘/桌面屏幕、整幅地图独立oracle |
| #25 身份/兼容 | `cb8c37c`＋`1298835`：真实可失败RED，最终GREEN6；GPU匹配单语10零差与旧无profile6零差；原Standards定点No issues/OK | 新双语全套；旧HEAD/DLL冒充新HEAD实测 |

最新两个 #25 补丁仅改fixture/证据门/测试/必要说明，生产Core/Main/显示及独立compositor未变。旧精确67f双语GPU适用于未改图形路径，新增6定点证明路径身份与headless evidence-only兼容；不把旧源/DLL身份改写为新运行。普通 headless只输出证据不要求GPU输入，graphical或显式任一GPU输入仍必须两项齐全并核真实加载路径。

真实273帧的非透明像素不含F9–FE：真实地图/对象/领队在等待轮转时应不变，人工F9/FA/alpha消费者测试另证实际颜色变化。map-before-dialogue仅是背景恢复oracle，不是完整地图独立渲染器。真实GPU为逻辑帧回读＋程序化输入，不是人工OS屏幕验收。

## 待完成（不勾通过）

- 固定全M3候选（main `97ec55a` 到最终#26提交）的fresh独立 Standards/Spec 审查、清finding、合入实际integration。
- 在最终integration另行授权完整共享门；本阶段未运行完整Test-M0/solution、完整Game/repeat或三个引擎失败控制全套。共享门额度仍0/2，旧lifecycle修后1/2不变。
- Windows一次性人工清单：真实地图/对象/领队及遮挡/移动、NPC转向/站立；默认简中/显式英文画像锚点、字形/阴影/布局、逐字/加速/等待提示/恢复；完成返回/再次交谈；物理键盘/Alt-Tab与正常关闭。自动工程完成后由父统一交付，不先让用户补工程缺口。
- 当前候选远端人工资源Windows CI。只读查询仅见main历史CI（包括 `97ec55a` 的39744713759与M2交付39738074108），没有当前integration对应结果；本阶段无push/dispatch权限，不拿历史绿替代M3。

## 保护与异常纪律

已有原始输入/M1/M2/native/profile与真实新增副本保护，复用而不重新提取、覆盖或自动同步；hash读取可能影响atime，不宣称保护atime。人工测试只沿既有fixture流程回收本次GUID，历史遗留不清理。真实Assets和完整图片/日志不提交上传。

Godot/GdUnit/Prepare/solution全局串行；全局Godot只查询PID/Name，未知先停。会话中止、崩溃、timeout或未知退出保存实际命令、HEAD、原日志/partialTRX、自有PID与资源快照后停链，不自动重跑/增时/放宽/升级。历史M2规格/验收/失败原件不重写，见 [M2验收](../m2/acceptance.md)与[异常说明](../../agents/test-diagnostics.md)。
