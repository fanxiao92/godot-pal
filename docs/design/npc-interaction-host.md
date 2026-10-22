# 独立 NPC 对话的 Godot 宿主

这是 #25 的显式开发验收模式，不是原版出生、完整剧情或 scene4 正常自由探索。它复用 [Core 搜索/返回契约](npc-interaction.md)、[trigger 适配器](trigger-execution.md) 和既有原生探索 profile。owner57 的触碰热点覆盖测试起点；本模式不调度它及其他 auto/触碰/进入/出口，不改这些对象的 state，也不宣称其行为成功。全部32对象继续绘制和碰撞。

## 正常启动与兼容性

```powershell
& $env:GODOT_BIN --path src/Pal.Game -- --m3-dialogue-profile res://Assets/NpcInteraction/Profile.tres
# 默认zh-Hans；英文仅在启动时显式选择，共享原事件：
& $env:GODOT_BIN --path src/Pal.Game -- --m3-dialogue-profile res://Assets/NpcInteraction/Profile.tres --m3-dialogue-language en
```

实际 profile URI 由显式 author 的新文件决定；不自动生成、复制、提取或补配置。参数唯一、成对，缺值/重复/未知/混用 M2 或 m3-static 均失败；语言只接受 `zh-Hans`、`en`。不带新参数时仍为原 M2 Main；`--m3-static-resources/--m3-static-profile` 仍是无事件的旧独立静态入口。清洁检出不含真实资源。

方向键/WASD 的 InputMap、别名、优先级、100ms离散节拍、晚到不补走、碰撞、相机锚点(160,112)、遮挡及整数缩放不变。搜索为 Return/Space/KP Enter/左Ctrl；参考 Windows `EnableKeyRepeat=false`，只接受非echo按下，不自建按住重开聊计时器。显示仍独占其参考推进集合及 `Echo=false` 边界，按住仅加速正文、不自动翻等待。方向的真实 press/release 在所有阶段继续交付；无focus暂停、清held或fresh-press门禁。逻辑输入不证明 OS/物理键盘的释放行为。

## 组合原生配置（v1）

`PalNpcInteractionProfile` 是普通 Godot Resource，script=`res://Scripts/Resources/PalNpcInteractionProfile.cs`。字段如下：

| 字段 | 契约 |
|---|---|
| FormatVersion | 1 |
| StaticResources | 已准备的 `res://Assets` 子根，内含 `Scenes/Scene.tscn` |
| StaticProfile | 原 `PalExplorationProfile` 的规范化 `res://` URI；**唯一坐标/行走来源**，不要复制一套 Start/Leader |
| BundleSha256 | 必须匹配原静态 profile 与实际 scene 的 bundle 身份；不硬编码本机散列为所有用户的合法输入 |
| EventsIndex | 完整现代 faithful 集合的 `index.json`，规范化 `res://` URI；不是裁剪0188–018B |
| DialogueDirectory | 七个已准备的 native 文件所在规范化 `res://` 目录 |
| ResourceSha256 | `Dictionary[String, String]`，key 是完整规范化 `res://` URI，value 为小写文件 SHA-256；必须**恰好**覆盖 StaticProfile、EventsIndex、index.files 的全部JSON，以及 Font.tres、Language.zh-Hans.tres、Language.en.tres、Portrait-059.tres、Prompt-00/01/02.tres |

author 前核实际源/目标和来源清单，按准备记录复用已核对产物。需要跨工程复制时只能复制至新不存在路径并保持 `res://` 引用、字节及mtime，不用junction写回原工程；资源本身与组合profile都保持ignored。下面是已存在 native 资源的**显式配置 author** 示例，不运行 Prepare，不读原始归档；先把三个 URI 变量改为已核对的本机路径。只写新输出，已有文件拒绝覆盖：

```powershell
$game = (Resolve-Path src/Pal.Game).Path
$staticRoot = 'res://Assets/StaticScene4'
$staticProfile = "$staticRoot/Profiles/Scene4Static.tres"
$events = 'res://Assets/NpcInteraction/events/index.json'
$dialogue = 'res://Assets/NpcInteraction/Dialogue'
$output = Join-Path $game 'Assets/NpcInteraction/Profile.tres'
if (Test-Path -LiteralPath $output) { throw 'Refusing to overwrite an existing profile' }
function LocalPath([string]$uri) { Join-Path $game $uri.Substring(6) }
$bundle = [regex]::Match((Get-Content -Raw -LiteralPath (LocalPath $staticProfile)), '(?m)^BundleSha256 = "([0-9a-f]{64})"').Groups[1].Value
if ($bundle.Length -ne 64) { throw 'Existing static profile must declare BundleSha256' }
$index = Get-Content -Raw -LiteralPath (LocalPath $events) | ConvertFrom-Json
$paths = @($staticProfile, $events) + @($index.files | ForEach-Object { $events.Substring(0, $events.LastIndexOf('/')) + "/$_.json" })
$paths += @('Font.tres','Language.zh-Hans.tres','Language.en.tres','Portrait-059.tres','Prompt-00.tres','Prompt-01.tres','Prompt-02.tres') | ForEach-Object { "$dialogue/$_" }
$locks = @($paths | ForEach-Object { '"' + $_ + '": "' + (Get-FileHash -LiteralPath (LocalPath $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '"' }) -join ",`n"
$text = @"
[gd_resource type="Resource" script_class="PalNpcInteractionProfile" format=3]
[ext_resource type="Script" path="res://Scripts/Resources/PalNpcInteractionProfile.cs" id="1"]
[resource]
script = ExtResource("1")
FormatVersion = 1
StaticResources = "$staticRoot"
StaticProfile = "$staticProfile"
BundleSha256 = "$bundle"
EventsIndex = "$events"
DialogueDirectory = "$dialogue"
ResourceSha256 = Dictionary[String, String]({
$locks
})
"@
$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text); $stream.Write($bytes) } finally { $stream.Dispose() }
```

SHA锁定的是显式配置的现代文件身份，不是对其原始来源的认证。静态来源仍按原生 loader 的 bundle/archive/role0校验；scene4/map10/32对象、owner60原坐标(1216,1344)/sprite207/state2/vanish0/mode3/trigger0188/auto0、scene enter/teleport0及起点(1232,1336) South0/layer0/phase0均核对。默认role0行走参数和相机不在组合profile重复。全部事件和 native 身份先加载核验；实际搜索请求只通过 `PalTriggerDialogue.Start` 的 owner/整链能力/全显示资源预检，未支持/raw/缺消息/缺字形/坏画像显式失败，不手工跑UI或另建生产executor。

## 实际更新与恢复

Main 的只读 `Exploration/Interaction/Dialogue/Display` 和 `UpdateCompleted` 表示正常业务观察边界，不提供私有状态注入。Interaction 独占探索推进；搜索命中后先把原生 painter 更新为运行时 NPC反向/站立 plan。图形运行在该场景帧 `RenderingServer.FramePostDraw` 后才 Start；headless没有呈现帧，只跨一个实际Main回调保留命令提交顺序，不能据此宣称像素呈现。搜索输入不会转交尚未装配的显示。

唯一显示节点 `_Process` 更新真实 `request.Display`。Main 在适配器自动回执之后订阅该次实际 `Display.UpdateCompleted`，恢复palette/背景后在**同一完成边界**回传真实trigger及完成时钟；不在下一观测帧重起50ms。Start同步完成无等待内容也立即回传。返回入口是0188，不是PC018B。Returning期间50ms及清瞬时搜索由Core公开状态机负责，held方向不清；到期返回正常探索，可再次完整交谈。Core确定性49/50ms/wrap/晚到测试与Godot实际连接检查互补。

场景调色板消费者从已保存 `PalFrame.Indices/Alpha` 和完整256色palette重新创建地图、对象、领队纹理，并继续使用原 `TextureFor` / `PalMapScene` painter；不从RGBA倒推索引，不修改导入纹理/旧Resource。旧M2/static路径不启用新消费者。`BackgroundRestoreRequested` 重画被阻塞的当前完整scene plan；覆盖层撤销自己，不恢复初始profile或读M1包。

## 验证范围

`NpcInteractionHostTests`：显式模式/默认语言、原生mode/entry、人工索引纹理、失败控制、实际Main双语两次完整会话、无等待同步完成。会话从正常输入和真实Main/Display完成回调观察，不私调 `_Process` 或用协程恢复耗时判断50ms；短人工正文可能首次更新已Waiting，不冒称它证明可见Typing/加速。受控“延后一帧回执”应被同完成边界检查捕获。

真实GPU复用原设施，新增可选 `-InteractionProfileUri`，不另造图形框架：

```powershell
./scripts/Test-RealDialogueGpu.ps1 -GodotPath $env:GODOT_BIN `
  -NativeDialogueDirectory src/Pal.Game/Assets/NpcInteraction/Dialogue `
  -PalettePath src/Pal.Game/Assets/StaticScene4/Palettes/Palette.tres `
  -Language zh-Hans -InteractionProfileUri res://Assets/NpcInteraction/Profile.tres `
  -EvidenceDirectory C:/Temp/pal-npc-gpu-new-unique
```

interaction GPU 脚本模式仍须提供 `NativeDialogueDirectory` 和 `PalettePath`，但它们必须分别指向 typed profile 选中的对话目录和实际已加载场景的外部 palette ResourcePath。fixture 在输入/截图前按规范化绝对路径核对（Windows忽略大小写），不以内容 hash 相同冒充路径身份；不一致明确报告 provided/actual 并失败，不生成成功身份凭据。匹配后保存 `resource-identity.json`，脚本仅在该凭据与参数/profile一致时允许成功；既有资源前后 hash/mtime 保护继续保留。无profile旧模式仍直接消费原两个参数，不要求新凭据。普通headless interaction可仅设置 `PAL_DIALOGUE_GPU_EVIDENCE` 输出 `fixture-result.json`，不要求GPU输入或外部palette身份；输出目录本身不是GPU输入契约开关。实际图形运行或显式提供任一 `PAL_DIALOGUE_NATIVE_ROOT` / `PAL_DIALOGUE_PALETTE` 时，必须同时提供两项并完成上述身份检查；只提供一项明确失败。

两语言各独立新目录/进程。fixture实际搜索、完整执行、恢复、返回后再完整交谈，按真实显示完成帧与FramePostDraw保存每次typing/加速/等待/轮转/恢复的实际与独立expected图。文本和布局预期来自native消息、字形位平面与参考literal，不从被测drawplan/当前snapshot取正文；snapshot只决定当前进度。地图前态仅是恢复oracle，不独立证明地图所有像素正确。本次真实273个场景帧的非透明索引没有F9–FE，因此真实地图/对象/领队在提示轮转时**应不变**；fixture先核该事实。人工索引测试另外证明这些消费者的F9/FA改变及alpha，不虚称在真实人物上观察到不存在的颜色变化。

未传新参数时旧人工背景真实GPU入口仍保留。headless状态、GPU像素、物理键盘/Alt-Tab、人工窗口缩放/桌面画面/关闭、远端CI和最终共享门分列；后四类不因本组件自动化通过而被接受。真实资源/截图/完整证据只在ignored或仓库外，不提交上传。全局引擎串行，异常留证后停止；最终共享门由#26统一执行。
