# Native battle visual binding（#46）

`PalBattleVisualProfile : Resource` 是独立、显式作者配置；`ResourceSaver.Save` 保存 `.tres`，`NativeBattleVisualProfile.Load(uri, CheckedStoryProfile story)` 返回 `CheckedBattleVisualProfile`。它只绑定已校验 story 所选闭包需要的单角色、单目标队伍视觉，不创建 Node、trigger、battle、RNG，不推进动画或改变数值规则。未选 numeric teams 可以继续保留合法非零数据；这不是对它们的执行能力授权。

## 作者 schema

- 根 `FormatVersion` 默认 **-1**，作者必须显式写1；`SourceIdentity` 必须非空，仅为声明，不是素材来源认证或人工/真实模式开关。
- `RoleId` 必须等于 story 的 Core `BattleContent.Player.RoleId`。`PlayerSpriteUri` 指向既有 `PalSprite`；它的 `SpriteId` 是图档索引声明，**不是 roleId**。
- `PlayerPosition` 是作者显式 bottom-center 坐标，两个分量均为 SHORT；缺省哨兵不合法。参考单角色位置 `(240,170)` 不是强制作者坐标。
- `PaletteUri` 指向显式准备的 `PalPalette`，要求 RGB768，并逐色等于 story 当前 active palette。当前无 palette-changing VM slice；不推测独立战斗调色板规则，不从 RGB 图片反推索引。
- `Teams : PalBattleVisualTeam[]` 的 `TeamId` 集合精确等于 story requirements 的 `BattleTeamIds`，无缺失、多余、重复；每项 `TargetId` 必须等于对应 Core enemy 的 `Id`（战斗目标身份，**不是 enemy 定义、对象或图片 ID**）。
- 每个 team 显式写 `BackgroundUri`（既有 `PalDialogueIndexedImage`，SourceIdentity 非空、320×200、完整 indices/alpha）、`EnemySpriteUri`（既有 `PalSprite`）、SHORT `EnemyPosition`（bottom-center，作者已应用所需 Y offset）、`IdleFrames/MagicFrames/AttackFrames/ActWaitFrames`。这些 metadata 为 WORD，IdleFrames>0，ActWaitFrames=0 合法，不在此推进时间。
- 两个 sprite 要有非空 `Source` 声明、WORD `SpriteId`、正的受限 decoded length/FrameCount、全部 nominal `0..FrameCount-1`；保留额外合法可寻址 frame aliases。每个 frame 的 WORD Index 唯一、SourceOffset 非负，size 与 indices/alpha 完整匹配。player 必须可寻址0..9，enemy 分段所需每个索引都必须存在，包括 aliases。
- `PalFrame.Texture` **不要求提供**；后续显示可以用原 indices/alpha 与已校验 active palette 着色。没有新增第二图像 DTO、JSON/blob、registry 或 runtime 准备入口。

## 依赖、隔离及所有权

根采用字符串 URI，先检查 root 的真实外部依赖图，再 IgnoreDeep 加载，避免重型 leaf 缺失在根深加载时成为引擎 ERROR。实际 leaf 图在加载之前按同一 internal `NativeContentDependencies` 检查规范路径、类型、UID/fallback、循环、纹理 source/.import、文件存在与精确 SHA-256 keys；外引 team metadata/frame 也纳入闭包。根配置自身是作者声明，**不自 hash**、不得回链 root；leaf 是正式 native Resource，而非 JSON 转装。

该 helper 是从 Story 原私有实现的最小提取；Story 的校验算法/诊断保持，额外可用的无 hash preflight 仅供 visual root 加载前验证。完成后再核声明文件 bytes；不锁定文件系统、不保证返回后或晚期并发替换。hash 不认证 `.cs/.gd`、引擎、`.godot` import output、缓存同步、GPU 像素或原始版权来源。详见 [Story 的证据边界](native-story-content.md#局部依赖与证据边界)。

返回 role/坐标/metadata 是值投影，teams/hash 是独立只读集合；持有本次 IgnoreDeep 的 sprite/palette/background 实例。**原生 Resource 仍可变**，调用方须按 prepared-resource 只读契约消费，不保证深不可变或热重载，不 clone-on-get。没有 Node 实例化，失败不留下视觉节点。

## 参考与明确留待后续的行为

只读 `reference/sdlpal/battle.c`：27 单角色位置表；880–949 按 role→F 图档、enemy object→enemy definition→ABC 图档加载，enemy 位置含 Y offset；124/199 bottom-center；735 当前 palette/night；982–987 FBP320×200。`fight.c`：2079/2120 普攻 player8/9；4987–5045 enemy idle/magic/attack 分段，attack loop `<=AttackFrames` 末索引为 idle+magic+attack−1；zero attack 取 idle−1。这里保留全原索引帧和必要分段 metadata，不猜动画帧索引、坐标或时间。

人工测试使用独立 literal 2×1 indexed frames、320×200 background 和作者坐标；SpriteId41/97 与 role0/team18/target7 特意不同。它们是可提交 synthetic 数据，不是原档复制或来源认证。`NativeBattleVisualProfileTests` 覆盖真实 ResourceSaver→严格 Load、版本/身份/精确映射、未选 numeric 数据、帧 aliases/平面、palette、hash/缺失/type/UID/cycle/外引 metadata 和隔离；旧 `NativeStoryProfileTests` 验证兼容。

**未交付**：攻击 effect 图档/索引绑定、动作/动画时间推进、renderer/GPU、Main/input/HUD、完整 package Prepare、真实 SceneRunner 往返。后续 UI 必须消费同一真实 Core 生命周期与本次校验资源，不另写规则引擎。冻结 `first-mandatory-battle-pre-trigger-v1` 与真实 owner28/@09C3 gate 不变，不能靠人工 visual 绑定绕开；#46/M4 尚未完成。

后续 [Story package production prepare](story-package-preparation.md) 已提供 typed Story/Visual 的统一离线绑定入口（checkpoint A，复用已准备依赖）；本 visual binding 自身不负责 Prepare、动作推进或 Main。完整可见人工作者数据包 B 现已交付，见下节；正常 Main 尚未接线。


## #46 checkpoint B: original authored sample

完整可提交人工作者 native 静态/双语非空bitmap字形/索引视觉资源及 Win/Loss 现代输入已提供，见 [正常准备命令、来源、搜索和数值设计](../../samples/authored-battle/README.md)。正常 numeric/story wrappers 分别准备两种场景；两个精确 Game 数据用例核双语 strict Load、真实搜索/可见平面和公开 Core Won/Lost 续接。本文上文 checkpoint A 的历史范围不改写；本B仍不接 Main/HUD/input/动作/effect，不签 SceneRunner/GPU/人工/CI、#46或M4完成。


## #46 D2-A: optional reference-presentation data, not playback

Version1 remains the base format; the root still defaults to −1 and the writer explicitly writes1. `AttackEffectSpriteUri=""` and `AttackEffectIndex=-1` mean absent, as does team `IdleAnimSpeed=-1`. Old C native resources and author inputs remain loadable; no effect or idle speed is synthesized. Present effect fields must be paired, the group must be WORD0..65535, and all three **int** indices `group*3 + {0,1,2}` must be addressable in the checked `PalSprite`. High groups never wrap to WORD frame0. Existing nominal frame/extra-alias/index-plane validation applies unchanged. Every provided idle speed must be WORD, including legal0; no zero-to-one coercion. Root hashes include the effect's full native dependency graph using the existing exact closure.

`CheckedBattleVisualProfile.AttackEffectSprite` and read-only `AttackEffectFrames` retain the loaded native instances; `AttackEffectIndex` and team `IdleAnimSpeed` are value projections. `RequireReferencePresentation()` is a pure selected-capability check: missing effect or missing speed for any selected team throws `InvalidDataException`. It does not load a URI, advance a battle, consume randomness or change state. Resources are still mutable and must be consumed read-only. Format-valid unselected numeric teams remain outside these requirements.

The group is the resolved **column1** mapping for the actual player battle sprite (`fight.c2055`), not a role, enemy target, equipment object or effect sprite's own ID. `global.c1978` resolves role base battle sprite then nonzero equipment overrides; current no-equipment author content declares sprite41→group0 itself. This is an authored mapping, not authentication of retail tables. `PAL_SpriteGetFrame` takes **INT** (`palcommon.c803`), not WORD. No new layout copy is needed: existing SHORT bottom-center positions and frame dimensions are the inputs to `fight.c2125/2208`'s effect bottom-center `enemy(x,y-height/3+10)`, then `(−16,+16)` per frame. Applying this algorithm remains D2-B.

B now supplies three different original indexed diagonal light-stroke frames, shared active palette and alpha; normal Story Prepare publishes them through the same strict graph. D2-A does **not** connect these data to Main/renderer. Player windup/approach/effects/recoil/return, persistent per-battle idle WORD counter (including decrement0→65535), floating-number feedback and actual presentation capability selection remain D2-B. Existing C/D1 rendering continues; no GPU/manual/CI/fullgate/#46/M4 completion is claimed. Audio playback stays out of scope.


D2-B now selects this capability at normal Story startup, before map/session/RNG creation. Old v1 absence is still legal for strict **format** loading, but no longer runs normal Story silently in C/D1 fallback. Playback and live WORD counters consume these same checked resources; see [the D2-B runtime boundary](main-story-roundtrip.md#d2-b-player-effects-persistent-enemy-idle-and-impact-hp). Floating-number/reference label lifecycle remains D2-C; this is not full D/#46 completion.
