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

    // 将 request.Display 本身交给显示适配器；只允许一个所有者每实际更新推进一次。
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

已有 `PalDialogueDisplay.Start` 自行创建显示会话；后续 Godot 适配器需要增加接收此 request/既有 `Display` 的接合方式，不能先更新一个会话却渲染另一个。保留原公开 Start 调用兼容性。实际时间、键盘、字体/画像资源、调色板和背景由外层提供；不得在 Core 之外复制逐字算法。

`AcknowledgeDisplay(id)` 仅在 id 匹配当前请求且**该真实 Display 已 Completed** 时返回 true，并继续执行到下一个真正显示边界或完成。提前、重复、陈旧、跨调用/跨执行器回执返回 false，不修改状态。等待中 Start 另一调用会明确抛错。仅名称或无正文的 CLEAR 没有正文等待，会在同一次 Start 中直接完成，不人为要求输入回执；已有正文后的 CLEAR 则仍由显示会话等待输入后清屏。

## 本组件证据边界

人工 Core 测试覆盖完整/跨文件集合、所有基础字段映射、WORD 回绕和零禁用、选定闭包先拒绝、0188..018B 人工形状、连续/不连续/重复 ID、语言行数与 CLEAR、内部入口、时间截止前/截止时/晚到、颜色/速度继承与恢复、caller、重复调用/陈旧回执及消费者内容预检。既有 `DialogueDisplaySessionTests` 继续验证逐字、页面、提示和调色板时间边界；没有 Sleep、反射、私有状态修改或替代生产显示逻辑。

本组件未执行真实完整事件集合、实际 Godot/GdUnit、资源准备、solution、GPU、map10/Main 或人工键盘验收。事件 JSON→实际 Godot 对话 fixture、资源失败传播、输入与实际更新连接、整票独立审查由后续适配器及父集成安排；不能由 Core 测试通过推定通过。
