# M3 Core trigger 执行与显示回执

本页是 #24 的纯 .NET 组件契约，不是整票、Godot fixture、#25 独立场景或 M3 验收完成声明。格式依据 [events-schema.md](events-schema.md)，显示依据 [dialogue-display.md](dialogue-display.md)。

## 输入和严格校验

`TriggerEventCollection.Parse(indexText, eventTexts)` 接收现代清单文本和 `fileId -> JSON 文本` 的完整集合。Core 不做文件 I/O，不引用 Pal.Extract/Godot，不读 MKF/SLF。外层负责读取清单及其列出的**所有**文件，并将文件身份保留为 fileId；不得只加载选定 NPC 的文件。

解析校验 v1/faithful、来源封套、精确字段/类型/范围、任意深度的重复 JSON 键（包括 `_` 注释内部）、标识符、文件列表和 fileId、入口 owner/slot/kind、完整 `0..instructionCount-1` 地址、命令顺序和标签唯一性、所有入口/next/to 跨文件引用。next 必须为 WORD 顺序后继；65536 条时 FFFF 回绕 0，短集合末条 next 必须 null。入口 0 必须 target=null；控制流的 to=标签0 则是正常引用，不能改成 null。source SHA-256 校验格式，不在运行时重建 chunk 或核对原始归档；来源认证/现代文件完整性与 owner 在实际场景/对象表中的存在性由资源适配层核验。

解析接受 v1 表中的所有基础具名映射并严格核对 source opcode 与动词/模式/box 配对；语义字段允许相对 source 原操作数被编辑，运行以语义字段为准。当前离线工具仅注册 0000/003C/FFFF，其余 raw 仍合法保存。`GetSource(address)` 保留所有命令的原 opcode/三个操作数和文件/标签/原地址，仅用于追溯，绝不是 raw 执行入口。`Entries`/`GetEntry(fileId,id)` 可供宿主核对选定 owner/slot；Core 不推断对象表 union 的类型或全游戏根。

CLI 的 `EventArchive` 混合了文件 I/O、编解码与严格离线校验，不能成为 Core 运行依赖。本组件独立实现窄的现代文本解析，不搬动或重构 CLI，也不增加公共编解码框架。

## 支持闭包与执行状态

`TriggerScriptSession(events, language, inheritedColor=0x4F)` 绑定一个已校验集合和一个已选语言包；不支持中途换语言。`Start(fileId,entryId,caller)` 在任何 caller/对话状态变更前，检查选定 trigger 的完整可执行闭包和所需全部语言范围，并以独立的真实显示会话预检所有文字控制。未支持动作、raw（包括 008E）、auto、非 upper box 或非零 rngWord 都先拒绝，错误包含文件、标签、原始地址。未选定 raw 或停止之后的命令不会因此执行。支持集合是有限的顺序路径，遇到未支持分支/调用直接拒绝整个调用，而不是只检查其当前条件选中的路径；没有 VM、自动脚本、更新场景型等待或任意 yield。

实际执行仅支持：

- `setDialog(box=upper,rngWord=0)`：头像和具名颜色上下文。color=0 继承当前颜色（包括有效调色板索引0），不解释 source 操作数。
- `showDialog`：根据已验证 next，合并 ID 严格递增的连续指令，按原始 `(startMessageId,span)` 查语言包；跨度不是翻译行数。ID 不连续、重复或倒退则另查范围，不会凭空添加清屏/玩家等待。相邻 showDialog 的显示条目拼接交给同一个 `DialogueDisplaySession`，使名称、正文页、颜色、速度和 CLEAR 保持参考显示语义。
- `end(mode=keep)`：结束，返回保存入口不变。PC=0 依 trigger 规则停止，不执行归档下标0。

`Phase` 为 Ready / WaitingForDisplay / Completed。`BlocksSceneUpdates` 仅在普通显示请求未回执期间为真，不是焦点暂停。没有失焦参数或焦点冻结时钟。

`CurrentPosition` 是实际继续位置（显示请求挂起时通常指向下一 setDialog 或 end），不是返回保存入口。目标 0188..018B 在 Start 内直接推进到显示请求，继续位置是 018B，`ReturnEntry` 始终为 0188，caller 为调用者60。0000 完成后可 Start 同一入口再次完整执行。`caller=FFFF` 沿用此执行器实例上次成功调用的 caller（初始0）；owner 不是 caller 的替代品，宿主应在同一调度上下文复用执行器实例。

每次调用重置速度为3单位/24ms。颜色和速度控制由现有显示会话处理；完成回执继承 `TextColor` 和 `DelayMilliseconds`，包括行尾无可见字形的控制。setDialog 不重置速度。结束恢复 upper、无画像、默认色4F、非RNG；速度按参考在下次调用重新设为24ms，而不是在 EndDialog 额外改写。

## 正常宿主消费示例

以下变量均来自宿主已经读取/加载的现代资源；Core 不负责路径或引擎节点：

```csharp
TriggerEventCollection events = TriggerEventCollection.Parse(indexText, eventTextsByFileId);
var trigger = new TriggerScriptSession(events, selectedLanguage.ToCorePackage());
trigger.Start("shared", "event-object-60-trigger", caller: 60);

// Start 后立即消费 PendingDisplay；不要再用 tick 调一条指令。
TriggerDialogueRequest? request = trigger.PendingDisplay;
if (request is not null)
{
    // 先装载并验证 Context.PortraitId 对应画像、字体、提示及场景调色板。
    // 如需预检全部字形，用 Content 构造独立 preview，不能推进实际 Display。
    var preview = new DialogueDisplaySession(request.Content, request.Context.Color, 0,
        request.Context.DelayMilliseconds);

    // 装配时先消费初始 Current.ClearDisplay（包括已完成请求）。
    // 将 request.Display 本身交给显示适配器；只允许一个所有者每实际更新推进一次。
    if (request.Display.Current.Phase != DialogueDisplayPhase.Completed)
        request.Display.Update(elapsedMilliseconds, advancePressed, accelerateHeld);
    DialogueDisplaySnapshot snapshot = request.Display.Current;
    // 由适配器渲染 snapshot，连接完整场景调色板变化及背景恢复。
    if (snapshot.Phase == DialogueDisplayPhase.Completed)
    {
        // 显示完成/恢复已应用后，回执；它不接收按键或时间。
        bool accepted = trigger.AcknowledgeDisplay(request.Id);
        // 如果产生新的 PendingDisplay，用其新 Id 装配；本次输入/elapsed 不再转交。
    }
}
// Completed 后由调用方回写 ReturnEntry；搜索后50ms延迟/清瞬时输入属于 #25。
```

`TriggerDialogueRequest` 提供：唯一 `Id`、不可变 `Context`、精确 `MessageReferences`（每范围的源位置/start/span）、`Content`（所有有序显示条目）、实际 `Display` 会话。Content 是为显示拼接的容器，其 start/end 元数据使用第一个范围；不得把它误当作不连续消息的新的原始范围。原始引用以 MessageReferences 为准。字体预检和显示装配不必访问私有状态或重新解析事件。

`PalDialogueDisplay.Start(request, font, portrait, prompts, palette)` 直接装配 `request.Display` 本身；旧的 language/start/span 重载继续创建独立显示会话并保持源调用兼容。实际时间、键盘、字体/画像资源、调色板和背景由外层提供；不得在 Core 之外复制逐字算法。

`AcknowledgeDisplay(id)` 仅在 id 匹配当前请求且**该真实 Display 已 Completed** 时返回 true，并继续执行到下一个显示请求或完成。提前、重复、陈旧、跨调用/跨执行器回执返回 false，不修改状态。请求未回执时 Start 另一调用会明确抛错。

首条正文前的 CLEAR 没有玩家等待，但初始 `Display.Current.ClearDisplay` 为 true，宿主必须在首次推进正文前实际恢复背景。仅名称、clear-only 等初始已 Completed 的显示也可能携带恢复效果：**仍发布真实请求**，宿主装配、应用 `ClearDisplay` 后立即回执，并同步排空后续已完成请求，直到真正需要时间/输入的显示或脚本 Completed。不能因 Display 已 Completed 就丢弃其恢复效果，不能为这些请求增加玩家输入或引擎帧等待。`BlocksSceneUpdates` 在请求未回执时为 true；正常宿主在 Start 返回前同步处理无等待请求，不把它们变成场景暂停帧。已有正文后的 CLEAR 仍先等待输入再清屏。名称不计正文行，初始颜色/延迟及旧三参数显示构造保持不变。

## Godot / I/O 宿主

`TriggerEventLoader.Load(indexPath)` 只读取现代 UTF-8 index 和其清单中的**全部**同目录 `<fileId>.json`（支持 `res://`、绝对路径及 Windows 分隔符），再交给 Core 严格校验；文件 ID 先验证再拼接路径。不会读取 MKF/SLF、离线 M1 包或调用提取器。未选定的 raw 仍存档，选定 raw 明确拒绝。

`PalTriggerDialogue.Initialize(display, indexPath, dialogueDirectory, currentScenePalette, sceneData, language="zh-Hans")` 绑定已有显示节点、完整事件集合、一个已准备的原生对话目录和实际场景元数据。目录使用 #23 准备器已有命名：`Language.zh-Hans.tres` / `Language.en.tres`、`Font.tres`、`Portrait-059.tres`（按请求画像 ID 格式化）、`Prompt-00.tres`～`Prompt-02.tres`。只加载所选语言，不 fallback。原生来源身份不得为空；所选语言身份、版本、消息、字形位图、画像/提示平面及256色 palette 均需有效。运行前以独立 Core preview 检查整个选定链和全部显示资源，不推进真实请求。

`Start(fileId, entryId, caller)` 校验所选 scene/eventObject owner 和 slot/sourceAddress 与绑定的实际 `PalSceneData` 一致；object union 调用尚不支持。caller 不拿 owner 代替。实例复用保留 last caller。整个 `Start` 同步执行至第一个真正等待或完成；所有已完成但携带恢复效果的请求都先绑定实际显示、消费恢复并立即回执。实际显示后续由 **PalDialogueDisplay._Process 唯一推进**；协调器没有第二个 `_Process`，不直接调用 `Display.Update`。`UpdateCompleted` 在实际显示更新的末端、完整 palette/背景恢复与 QueueRedraw 后发送；协调器在该边界回执并装配新请求，但不将本次按键/elapsed 转交新请求。宿主不要在活跃 trigger 外另行 Start/Update 同一个显示节点。

显示节点只在活跃显示中接收参考按键，每次装配清空旧瞬时队列，一批按键只形成一次 advance；held 仍按原规则加速，不跨等待自动确认。没有 focus 门禁。`ActivePaletteChanged` 继续提供完整 palette；`BackgroundRestoreRequested` 在装配初始 CLEAR、翻页和完成时要求宿主恢复保留的场景背景。Godot 对话覆盖层本身通过当前快照和重绘撤销旧内容，不复制像素/字形规则。`UpdateCompleted` 是业务更新完成边界，不是 GPU draw 完成信号。

正常消费者（`display` 已入树，`sceneData`、`currentScenePalette`、`RestoreSavedSceneBackground` 和 `ApplyScenePalette` 来自当前宿主；本组件不替代 #25 场景搜索）：

```csharp
var dialogue = new PalTriggerDialogue();
AddChild(dialogue);
display.ActivePaletteChanged += ApplyScenePalette;
display.BackgroundRestoreRequested += RestoreSavedSceneBackground;
dialogue.Initialize(display, eventsIndexPath, nativeDialogueDirectory,
    currentScenePalette, sceneData); // 默认 zh-Hans；显式末参 "en" 选择英文
// 当前范围可在 Initialize 后由源入口元数据选择 file/entry；这里是正常原生命名示例。
dialogue.Start("shared", "event-object-60-trigger", caller: 60);
// 使用 dialogue.Session 的 Phase / BlocksSceneUpdates / ReturnEntry / CurrentPosition。
// 不自己调用显示 Update 或 AcknowledgeDisplay；请求和回执已接合。
// #25 在 Completed 后回写 ReturnEntry、结束50ms延迟并清探索瞬时输入。
```

## 本组件证据边界

Core 确定性时间/入口/回执与完整集合验证独立于 Godot。人工 `TriggerDialogueFixtureTests` 从磁盘加载完整396地址、两个跨文件现代 JSON 和原生 A/B 位图/画像/提示/语言，真实 `Input.ParseInputEvent` 驱动等待、完成和再次调用；不同翻译行数不改 PC。状态在实际显示 `UpdateCompleted` 边界保存，不以协程恢复/观测耗时判断业务截止。新增负例覆盖缺文件、跨文件坏引用、raw、缺语言/身份不符、缺画像/损坏平面、owner不符、坏UTF-8/重复键和缺字形；无等待 CLEAR/名称请求的同步排空与真实背景恢复有单独人工覆盖。合成资源无真实游戏文字、字形或画像，自己的 GUID 变体只按 fixture 正常生命周期清理。

本组件不改 Main/搜索/独立探索模式，不执行真实 map10、真实事件/资源提取、sharedgate/solution、GPU或物理键盘/桌面验收。相关 #23 三项 headless fixture 回归与完整 Core 回归需随组件证据核对；不把这些结果当作真实 GPU、#25 闭环、远端 CI 或整票独立审查通过。
