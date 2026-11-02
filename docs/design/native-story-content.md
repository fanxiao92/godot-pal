# Native story CONTENT binding（#46）

`PalStoryProfile : Resource` 是显式作者配置；`ResourceSaver.Save(profile, uri)` 保存 `.tres`，`NativeStoryProfile.Load(uri, language = "zh-Hans")` 严格加载为 `CheckedStoryProfile`。这是正常 Main 后续消费的内容绑定，不是 Main 接线、战斗视觉、完整 package Prepare、编辑器面板或运行时解包器。本接口不选择人工/真实标签，不改变冻结的 `first-mandatory-battle-pre-trigger-v1` 和 owner28/@09C3 gate。

## 作者字段

| 字段 | 契约 |
| --- | --- |
| `FormatVersion` | 默认 **-1**；作者必须显式设置1，缺失不默认为已支持 |
| `StaticResources` | 规范 `res://Assets` 或子目录；实际加载其 `Scenes/Scene.tscn` |
| `StaticProfile` / `BundleSha256` | 规范原生探索profile URI；小写64位散列与真实scene、profile一致 |
| `EventsIndex` | 完整现代 faithful index URI；所有文件均由 `TriggerEventLoader` / Core严格读取，不裁剪未选数组 |
| `EntryFileId` / `EntryId` / `OwnerId` / `EntryAddress` | 必须对应所选入口及实际静态scene的eventObject / wTriggerScript / trigger，地址非零 |
| `BattleContentUri` | `NativeBattleContent.Load` 的真实typed数值资源URI；不做Resource→JSON→Parse |
| `DialogueDirectory` | 规范URI，显式准备 `Language.zh-Hans.tres`、`Language.en.tres`、`Font.tres`、`Prompt-00/01/02.tres` |
| `Portraits` | `Dictionary<int,string>`，所选闭包的非零portrait ID→规范原生画像URI，**精确集合**，缺漏、多余或0键均拒绝；同一画像文件可被多个合法ID显式引用 |
| `ResourceSha256` | `Dictionary<string,string>`，下述局部data闭包的**精确**路径→小写SHA-256，缺失/多余/非规范路径/字节不符均拒绝 |
| `SourceIdentity` | 必须非空；只是作者声明，**不是**原档来源认证 |

保留现有 `NativeExplorationProfile` 的完整静态sprite/map验证以及role0 / DATA3@768行走来源限制；本story绑定要求数值初始role也为0。这是组合切片的leader限制，**不是**native numeric格式只支持role0。未宣称scene-enter、任意leader或正常场景剧情全量支持。

## 预检与所有权

- `TriggerScriptSession.ValidateEntry` 返回深不可变的 `TriggerEntryRequirements`：`DisplayBlocks` 中每项保留 `Content` 和现有 `MessageReferences`（含源位置），以及排序去重的 `PortraitIds` / `BattleTeamIds`。使用同一个walker与 `ReadDisplay`；可选collector仅纯预检开启。画像ID来自支持的具名setDialog（即使随后覆写或到停止前未显示也保守保留），team来自具名startBattle，不从retained raw operands猜测。
- `ValidateEntry` 的void→返回值是未发布M4接口演进；当前仓库statement/lambda调用可继续编译，无委托/协议注册。仓库外的已编译使用者或void委托绑定不承诺二进制/源码兼容，须重编译/调整。
- 两种语言各自执行同一完整next/loss/flee闭包与精确消息range解析。随后对每个真实block调用 `PalDialogueDisplay.ValidateContent`，验证实际可见Rune/字形位图；三prompts与所有闭包画像分别校验真实原生图像平面和声明身份。归档未引用数据不自动当可执行能力；战后各分支不遗漏。
- 不构造trigger session、battle、假RNG或半有效session，不调用Start/预演获胜。原Start仍按当前settled role/color预检，原无battle配置拒绝边界不变。
- profile、数值、语言、font、画像、prompts及验证PackedScene均用IgnoreDeep。临时地图仅为实际静态内容验证而实例化，**不入树**，成功或失败都finally Free；不把这称为Main/SceneRunner验证。
- 返回 `CheckedStoryProfile` 的事件、所选语言、数值、requirements、静态Core规则/leader均为不可变投影。哈希清单、palette以及native prompts/portraits集合有独立只读copy。返回本次已校验的Font/Prompts/Portraits实例供后续消费者直接使用，不强迫其凭URI重新加载。
- **原生Resource本身可变**；集合只读并不令其深不可变。调用方须按prepared-resource只读契约使用；不做clone-on-every-get，不承诺热更新。原始作者对象/普通缓存对象与本次IgnoreDeep资源隔离；更改文件后必须显式重新准备/加载，不期待import缓存自动同步。

## 局部依赖与证据边界

固定seeds为scene/profile、numeric、两language、font、三prompts及显式画像。完整index与所有现代event文件另行全部加入。对native seeds递归 `ResourceLoader.GetDependencies`，涵盖外部/嵌套资源和实际Texture2D源文件；重复引用去重，循环明确拒绝，未知类型/非规范URI明确拒绝。允许native `.tres/.res/.tscn/.scn` 及 `.png/.svg/.jpg/.jpeg/.webp/.bmp` 源；纹理必须已有显式准备的 `.import` remap，**sidecar字节也加入清单**，不在加载时触发作者准备。

Godot4.7.2实际探针核定：依赖项是一段path，或三段 `uid::::fallback`（第二段为空，不是type）；有注册UID时要求其实际路径与规范fallback一致，避免对A的hash认证却加载B。未注册合法UID沿引擎fallback约定。当前实现不做全局通用审计器。

加载前核声明文件、依赖闭包和精确keys；完成验证后再次核全部声明字节以发现本次加载期间变化。`.cs/.gd`代码依赖明确排除；不认证代码、引擎或 `.godot` 生成import输出。纹理源与sidecar的hash **不证明**缓存输出对应源、GPU像素、原始版权素材或作者身份，也不防止返回后修改文件/可变Resource/晚期I/O失败。没有锁定全局文件系统或实现安全沙箱；后续Main应使用已校验实例并保留正常失败传播。

## 匹配测试与尚未交付

`NativeStoryProfileTests` 通过真实ResourceSaver→ResourceLoader和独立literal synthetic完整事件、typed数值、双语言、font/prompts/画像、external role/glyph/palette及导入SVG验证接口。manifest预期为人工列明路径，不由生产closure反推。验证版本/类型/identity、hash漏多改混、完整faithful、next/loss/flee字形画像、静态role/owner、UID/未知路径/循环、无孤儿节点及隔离。Core requirements行为测试沿同一公开入口。

此内容绑定不包含战场/敌我帧映射、正式人工package准备、Main/input/display连接、真实战斗往返、GPU、人工桌面或远端CI验收；#46和整个M4仍未完成。
