# 独立 NPC 搜索与返回探索（Core）

本页是 #25 的纯 Core 组件契约；实际 Main 的显式启动、组合资源配置、输入/更新与palette接合见 [Godot宿主契约](npc-interaction-host.md)。不是 scene 4 正常场景调度或 M3 验收通过声明。参考固定 `play.c:PAL_GetSearchTriggerRange/PAL_Search/PAL_StartFrame`、`input.c:PAL_ClearKeyState` 和 `game.c` / `util.c` 的等待循环。事件与显示沿用 [trigger-execution](trigger-execution.md)，不复制执行器、tokenizer 或逐字规则。

## 元数据与查询

`SceneObject.TriggerMode` / `TriggerEntry` 是原始 `wTriggerMode` / `wTriggerScript` WORD 的非位置 init 属性。默认0保留旧构造/解构签名及普通静态入口；**交互宿主必须在现有原生对象转换处逐个填入实际值**，不能用默认0启动交互或只保留 owner60。Game的 `NativeExplorationProfile` 已在完整转换处填写这两个实际WORD；普通静态入口不因此调度事件。Core组件不依赖Game。

`StaticSceneRules.FindSearchTarget(leader)` 返回 `SceneSearchMatch(ObjectId, SampleIndex)` 或 null。它按13个参考采样优先、每采样按传入的原对象顺序扫描，不按ID重排，不是 nearest/raycast：

- sample0 是队伍脚点，其后四组各三点；North/East 的 xOffset=16，其余=-16；East/South 的 yOffset=8，其余=-8。
- 每个点按 PAL_XY 打包为 signed SHORT，再以 C 的整除/余数求 `(x/32,y/16,x%32!=0)`；对象坐标仍是原始 unsigned WORD。不使用碰撞 diamond 转换或 BYTE 截断。
- 仅过滤 `State<=0`、`TriggerMode>=4`、`TriggerMode*6-4<=sample` 与不匹配的网格。vanish、sprite/可见性、碰撞及入口0都不额外过滤。
- 返回 global one-based ObjectId，不是本场景数组下标。无匹配不改变元数据。

独立真实起点 `(1232,1336), South0` 与原始 owner60 `(1216,1344), mode3` 的静态结果是 sample0空、sample1命中60。自动化使用相同独立常量和人工对象，不把它当作真实现代资源加载的证明。

## 独立交互会话

`NpcInteractionSession(rules, leader, selectedEntry)` 组合既有 `ExplorationSession` / `ExplorationInput`，独占探索推进。selectedEntry 必须来自已解析的完整事件集合，必须是 eventObject60 / wTriggerScript / trigger / source0188，且实际对象 TriggerEntry 相同。只支持该入口，不预造通用事件总线。`TriggerScriptSession.Entries` 只转发既有集合的只读入口元数据，供宿主从 **同一已初始化的 PalTriggerDialogue.Session** 选入口，不重复文件读取或另建运行执行器；原 Start/显示/回执行为不变。

公开边界：

- `Press(key,repeat)` / `Release(key)` 完全委托现有方向输入；正常方向别名、优先级、repeat、held释放规则不变。
- `PressSearch()` 收集本次实际交付的瞬时搜索按下（含宿主交付的OS repeat），不自行模拟轮询或按住重复。初始开聊按键不再转交显示。
- `Update(uint ticksMilliseconds)`：Exploring 时只经现有输入推进；到期的正常探索步（包括位移/碰撞/转向）完成后才消费搜索，符合 PAL_StartFrame 的顺序。早于100ms截止的搜索留至实际到期步。无匹配仅消费一次搜索。
- 首个命中不是60则明确抛 `InvalidDataException`（带owner/sample），不跳过它再找60，也不把它当执行成功。
- 命中60后，仅当 `SpriteFrames*4>CurrentFrame` 才将运行时对象 frame0/反向，并将领队绘制帧设为 `direction*3`；这是 PAL_Search 的显式三帧站立规则，不按 WalkFramesWord 猜测。条件不成立时不强制站立/重画。原坐标、state、layer、sprite身份、全对象顺序和碰撞不改。
- `Current` 是实际可画的 `ExplorationState/StaticScenePlan`；`Objects` 是只读运行时快照。pose/入口仅在会话副本保存，不修改传入 rules 或 Godot Resource。所有原始对象始终保留，未选定对象继续绘制/碰撞。
- `Phase` 为 Exploring / Trigger / Returning，后两者 `BlocksSceneUpdates=true`。Trigger 阶段不推进探索；`PendingTrigger` 保持同一请求，宿主必须只 Start 一次。

`NpcInteractionRequest` 提供唯一 Id、真实 FileId/EntryId、global Caller=60、SourceAddress=0188 和命中 SampleIndex。它只是对正常 Start 的请求，不持有/执行第二份脚本。宿主必须调用既有 **PalTriggerDialogue.Start**，保留其实际owner表核对、整链能力/资源预检及唯一显示更新/自动回执，不能直接绕过适配器调用 Core Start 来运行游戏。

显示恢复已应用、正常适配器完成后，宿主在实际完成边界调用 `CompleteTrigger(request.Id, dialogue.Session, completionTicks)`。该方法检查当前请求ID、真实 TriggerScriptPhase.Completed、Caller和ReturnEntry；拒绝未完成、陈旧ID、错误caller/保存入口，成功将实际 ReturnEntry 写入运行时对象并进入 Returning。宿主必须回传**消费了该请求的同一正常会话**；此接口不是外部不可信回执的认证协议。PC018B不是保存入口0188；本切片0000保留0188，返回后可再次完整调用。

Returning 的50ms从显示恢复/trigger真正完成的 `completionTicks` 开始，不从后来宿主观察时刻重新计。49ms仍阻塞，50ms（含uint回绕）可返回；使用与探索相同 `<2^31ms` 的 SDL_TICKS_PASSED 比较前提。晚到更新不补走，正常探索下一截止仍从实际步的当前时间+100ms计算；不重置原有尚未到期的探索截止。

到期先丢弃50ms期间收集的瞬时搜索，再恢复正常探索步，**不清held方向或别名顺序、不要求fresh press、不加focus门禁**。没有预存显示输入、转发结束键或跨等待自动确认；显示节点继续负责其自己的瞬时输入/held加速规则。普通方向输入在对话/返回期间仍须交付 Press/Release，让真实释放生效；不要把焦点当作暂停或私自制造粘键。

## 正常 Godot 宿主调用示例

以下是Core消费顺序示例，不是第二个 `_Process`；实际Main和启动配置见宿主契约。profile须由既有完整原生验证得来；必须核 scene4/map10、全部32原始对象、原始入口/owner身份及已批准起点。默认旧 Main / 静态模式应保留。`dialogue.Initialize` 等完整现代资源初始化沿用 #24。

```csharp
// 原生转换时：所有对象均以实际WORD填充，不修改Resource。
var obj = new SceneObject(id, x, y, state, vanish, layer,
    spriteNumber, spriteFrames, direction, currentFrame, sprite)
{
    TriggerMode = rawTriggerMode,
    TriggerEntry = rawTriggerEntry
};

// dialogue已Initialize，display已入树并接完整场景palette/背景恢复。
var selected = dialogue.Session!.Entries.Single(e =>
    e.OwnerType == "eventObject" && e.OwnerId == 60 && e.Slot == "wTriggerScript");
var interaction = new NpcInteractionSession(checkedProfile.Rules, checkedProfile.Leader, selected);
NpcInteractionRequest? active = null;

// 在实际探索更新中：方向_Input无论阶段都交付Press/Release。
// 搜索使用Return/Space/KP Enter/Left Ctrl，不默认等同ui_accept。
// 只有Exploring阶段的搜索键用于PressSearch；活跃显示输入由原显示节点处理。
if (interaction.Update(nowTicks))
    map.UpdateScene(interaction.Current.Plan);
if (active is null && interaction.PendingTrigger is { } request)
{
    active = request; // 先标记，禁止每帧重复Start。
    // 上面已发布参考转向后的完整plan；如需保证画面提交，宿主按实际渲染边界安排。
    dialogue.Start(request.FileId, request.EntryId, request.Caller);
    // Start可同步完成clear-only/no-wait链；此时也立即执行下述完成处理。
}

// 在原PalDialogueDisplay.UpdateCompleted的完成边界处理（#24自动回执之后），
// 以及Start同步返回Completed时：
if (active is { } call && dialogue.Session!.Phase == TriggerScriptPhase.Completed)
{
    if (!interaction.CompleteTrigger(call.Id, dialogue.Session, actualCompletionTicks))
        throw new InvalidOperationException("Interaction completion mismatch.");
    active = null;
}
// 不再Update exploration或request.Display；不另建TriggerScriptSession执行同一请求。
// 后续实际更新继续interaction.Update(nowTicks)，由它负责50ms/清瞬时搜索。
```

宿主需要保证实际完成时间、信号顺序与唯一更新所有者。`PalTriggerDialogue` 在显示 `UpdateCompleted` 后自动回执，宿主订阅应在其初始化后，确认该次真实更新与恢复已完成再回传，不能以等待协程恢复或本地帧计数代替。上述 Core 时间测试与 Godot 实际输入/更新连接验证互补。

独立模式不运行owner57触碰热点、其他auto/进入/出口；不是关闭这些对象或正常场景自由探索。缺资源、不支持闭包或非目标显式命中须失败；不自动提取/补配置、不裁完整事件集合。
