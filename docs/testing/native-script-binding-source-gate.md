# #176：native script-binding 公共 API / ownership 源级门

## 结论与范围

本票是 [#168](https://github.com/fanxiao92/godot-pal/issues/168) 的独立 [#176 SOURCE 前置](https://github.com/fanxiao92/godot-pal/issues/176)，不是 WORD parity、引擎修复或运行许可。仅新增本文，不修改生产、fixture、测试注册、包、缓存或原失败。

**本次补齐**：固定版本 `GetScript()` 的托管签名推导、Variant 转换/释放、managed Dispose 到 native 分派，以及公开加载的 RefCounted wrapper 接管/释放 native 引用的源码链。旧方案中的这几处“未取得源码”不再需要猜测。**仍未补齐**：实际故障同一 script pointer/refcount/finalizer/清表事件、安装二进制可重现构建证明，以及任何运行对照。源码链闭合不等于本案因果闭合。

- 当前仓库基线：`execution/m6-loop@763d6ace10963cb967b7c4bc5f28f222752cbbb8`；#167 的已集成提交 `1d0e0e9a49296cca102b076add4bf4da8f18b586` 是其祖先。
- 官方源固定到 **`ed1daf0bf001b61586d9930840f2f1394092c079`**；由 GitHub commit API 解析原日志短 SHA，不换 latest/master。该源身份与原 banner 一致，不冒称本轮重新认证了安装二进制。
- `source` 表示固定源直接支持；`inference` 表示带前提的解释；`open` 表示本轮未证明。文中 API 形状由源码推导，**未编译或运行**。
- 既有研究复用而非重做：`C:/evidence/m6/authorized-continuation/native-script-binding-fatal-primary-research.md` 与 `native-script-binding-followup-source-plan.md`；本文收窄后者第4/5节缺口，不接受或执行其 A/B/C 建议。

## 1. `public Variant GetScript()`：不是从 API XML 猜返回类型

下列链的每段均为 `source`：

| 环节 | 固定源证据 | 结论 |
| --- | --- | --- |
| 实际对象方法 | [object.cpp:1010–1012][object-get]、[1852][object-bind] | `Variant Object::get_script() const`；有 instance 时构造 `Variant(script_instance->get_script())`，否则空 Variant；无参绑定 `get_script`。不能拿 instance 的 `Ref<Script>` 直接当 C# 签名。 |
| 返回 metadata | [type_info.h:153–158][type-variant] | `GetTypeInfo<Variant>` 为 NIL + `PROPERTY_USAGE_NIL_IS_VARIANT`，不是 void。 |
| 生成器映射 | [bindings_generator.cpp:4117–4122][gen-return]、[4910–4922][gen-variant] | 前述 metadata 映为 Variant；`cs_type="Variant"`，native 返回经 `Variant.CreateTakingOwnershipOfDisposableValue` 接管。 |
| 名称与可见性 | [4172][gen-name]、[3101–3122][gen-method] | snake→Pascal、输出返回类型和参数；得到 `GetScript()`。默认 public（[header:186][gen-header]）；[4233–4244][gen-internal] 的 internal 特例只针对下划线属性 accessor，不是本方法。 |
| 官方文档交叉核对 | [Object.xml:692–697][object-doc] | 无参、Variant、未挂 script 时 null；是输入/文档旁证，不是 managed XML member ID 编码了返回类型。 |

因此固定源生成的公共形状为 **`public Variant GetScript()`**，不是 `Script GetScript()`。没有取得安装包 generated `GodotObject.cs`，本结论明确采用 Issue 允许的“实际生成器 + 输入 metadata + 类型映射”路线，而不是反射、反编译或 SDK 生成。

### 转换与临时所有权

- [Variant.cs:11–104][variant-own]：Variant 是 `IDisposable` struct；对象值持有负责 native value 的 Disposer；`Dispose()` 释放该值并清空自身。对象类别不能按“不需要释放的整型值”处理。
- [Variant.cs:357–358][variant-as] → [VariantUtils.cs:487–499][variant-convert]：`AsGodotObject()` 通过 ObjectId 查询现存 native 对象，再走 `InteropUtils.UnmanagedGetManaged`；非 Object/零 ID 得到零指针并可返回 null。它不是无条件 cast 成 Script，也不创建另一份脚本。
- 若将来批准观测，调用方应先在明确短作用域取得一次 Variant，检查 `VariantType`，再用 `AsGodotObject()` 和 `is Script`/`is CSharpScript` 判别，记录失败或身份；离开作用域只释放这份 Variant。不能对未知值直接假定 Script、调用私有 map、调用 `Script.New` 或替换 typed new。
- Variant 作为 struct 的普通拷贝会拷贝同一个 Disposer 引用（[字段及 Dispose][variant-own]）；不要复制给多个“独立 owner”分别使用/释放，也不要在释放后继续读取副本。这里不新造公共 helper。
- **inference**：`GetScript()` 本身构造临时 Variant/Ref，转换还可能创建 managed wrapper。即使不 Load、不长期保存，也不是零分配/零引用寿命干预；dispose 临时 Variant 不保证 wrapper 已 finalizer，也不保证脚本立刻析构。成功构造后才可观测，无法回溯已在构造中 fatal 的对象。

## 2. Dispose / finalizer 的中间 native 分派

### 2.1 入口与两类 owner（source）

[GodotObject.base.cs:98–151][object-dispose]：finalizer 调 `Dispose(false)`；显式 Dispose 调 `Dispose(true)` 并 suppress finalizer。`_memoryOwn` 为真时调 `godotsharp_internal_refcounted_disposed(NativePtr, handle, !disposing)`，随后清 NativePtr；有防重复释放及 handle target 检查。显式 Dispose 不是 GC.Collect。

[runtime_interop.cpp:167–218][dispose-dispatch] 补齐旧研究未读的中间体：

1. 若 owner 实际挂有 CSharpInstance，且非正在析构，调用 `mono_object_disposed_baseref`；根据输出选择 `memdelete(rc)` 或 `set_script_instance(nullptr)`；该分支不是一律删除。
2. 否则 native wrapper binding 路径执行 `pre_unsafe_unreference` → `rc->unreference()`；为零删除，否则释放相应 binding handle，包含引用数降到1时 handle 已交换为 weak 的处理。
3. **分派依据是对象的 script instance，不是 C# 类名包含 Script。** 加载得到的 CSharpScript 是脚本资源对象；不要把它的 native wrapper binding 与“挂在 PalDialogueIndexedImage 上的 CSharpInstance”混为一谈。

### 2.2 用户 Resource 的 owner 与 script（source / inference）

[csharp_script.cpp:1708–1748][owner-ref]：managed 用户实例算 owner 的一个引用，`init_ref`/`unsafe_referenced`/`unreference` 跟踪其责任；删除 owner 延迟给调用方。

[1785–1819][instance-dispose]：若释放到零，要求外层删除；若 owner 尚活，先释放 handle。显式 Dispose 请求移除 script instance；finalizer 且非 scripts domain finalizing 则可能 `_internal_new_managed()` 重建，失败才请求移除。故“自然 finalizer 必然立即删除 script”错误。

[1980–2053][instance-dtor]：`get_script()` 返回成员，构造时 `script(p_script)`；析构断开 signals、释放 handle，在 owner 仍需存活时用 scope Ref 保护并把 owner 引用责任转到 native binding，最后移出 script.instances。**inference**：正常 C++ 成员析构随后释放 script Ref；析构体未写 `script=null` 不表示永久保活。

本段说明可行生命周期，不证明旧失败恰好由某个 finalizer 触发，也不排除 ALC/reload/其它路径。

## 3. 公开 Load 返回 wrapper 的引用接管

以下是正常成功路径的 `source`，不是运行观察：

1. [ResourceLoaderExtensions.cs:25–28][load-generic] 的真实签名为 `Load<T>(string path, string typeHint = null, CacheMode cacheMode = CacheMode.Reuse) where T : class`；仅 cast 非泛型 Load。泛型不替调用者验证 null/实际类型；不能改写约束为 `T : Resource`。
2. 具体非泛型入口不是凭名称套生成器：[core_bind.h:77][load-decl] 声明 `Ref<Resource> load(...)`；[core_bind.cpp:78–84、150][load-bind] 实际实现转交现有 ResourceLoader 并绑定 `load`；[type_info.h:186–193][type-ref] 的 `GetTypeInfo<Ref<T>>` 给出 OBJECT/RESOURCE_TYPE/T 类名。
3. [generator:3910–3911、3928–3941][gen-owner] 对 RefCounted 后代设 `memory_own=true`、返回转换 `UnmanagedGetManaged(ret.Reference)`；[3480–3502、3551–3566][gen-icall] 为该返回使用 `using godot_ref ret`，在 wrapper 转换期间保留 native Ref。继承输入由固定官方 [CSharpScript][csharp-doc] → [Script][script-doc] → [Resource][resource-doc] → RefCounted 文档链交叉核对；不是只靠泛型名字推断。
4. [InteropUtils.cs:11–47][unmanaged-get] 先查 CSharpInstance，再取已有 binding handle 的 target；存在即返回**同一 wrapper**，收集后才请求 replacement。
5. [csharp_script.cpp:1169–1190][binding-setup] 在创建 managed native-wrapper binding 后明确 `rc->reference()`/`post_unsafe_reference`；上游注释明确 managed 存活且 native 无其它引用时，refcount 应为1而非0。[runtime_interop.cpp:260–306][binding-replace] 的 replacement 路径也获取这一引用。
6. [generator:2394–2406][gen-ctor] 的 IntPtr ctor 先赋 NativePtr、使用上述 memory-own，再调用 ConstructAndInitialize；[base.cs:47–75][object-ctor] 已有指针走 pre-setup，[InteropUtils.cs:79–87][pre-setup] native type 相同即 return。**不是在 pre-setup ctor 重复加引用**；外层 native binding 接管才是此处责任。
7. 临时 Ref 释放链已补：[InteropStructs.cs:38–57][godot-ref] `godot_ref.Dispose()` → [runtime_interop.cpp:381–383][ref-destroy] 显式 Ref 析构 → [ref_counted.h:199–211、226–228][ref-unref] unreference/必要时 memdelete。临时返回 Ref 的释放与 wrapper 持有的那一个引用是两份责任，不把前者离开作用域当作后者归零。
8. [csharp_script.cpp:1251–1326][binding-callback] 引用数仅剩 managed 的1时可把 GCHandle 从 strong 改 weak；**没有在此撤销 managed 所占 native 引用**。正常最终释放仍经第2节 disposed 分派。

**有条件结论（inference）**：在正常成功加载并建立该 binding、实际对象有效且无主动 Dispose/Free、无 shutdown/ALC 破坏的前提下，持续强可达的未 Dispose wrapper 能阻止其正常 GC finalizer 释放 managed 所占 native 引用。这为单类型 scoped 保活**对照设计**提供源码依据；`typeof(T)`、URI、数字 ID、raw pointer 或 weak reference 不提供同一责任。

**共享别名边界（source + inference）**：已有 binding 可返回同一 wrapper。任何别名显式 Dispose 会把这同一 managed 对象标记 disposed、清 NativePtr 并释放其引用；其它别名保持强可达不能阻止主动 Dispose。native 对象还可能因其它 Ref 存活，但不使已 disposed wrapper 恢复可用。因此结束“本方保活”应撤销本方强引用，不能擅自 Dispose/Free Reuse 可能共享的脚本 wrapper。

未证明安装 DLL 的每个生成字节与当前源可重现相等；没有执行实际 Load/GetScript 或 refcount 探针。以上足以消除旧方案的**源级机制缺口**，不是给 public API 添加原子 map acquire、线程安全、始终成功或 editor/export 兼容保证。

## 4. 当前调用方与方案收敛（不执行）

当前执行树实读链仍为 `PrepareStoryPackageTests:655–671` 的 `using new Preparation` → `Preparation.Native` initializer（708）→ `NativeBattleVisualProfileFixture.Background` initializer（22）→ 用户 Resource 构造。发生在 fixture 构造体与 public Prepare 调用之前。`NativeStoryProfileFixture:122–123、155–156` 也创建同类型 prompts/portraits；其 Dispose193–197 删除本方目录，visual fixture Dispose85 转交 Story，不是已存在的 script keeper。

对当前 `.cs` 定点检索未见 GetScript/Script pin helper；现有公开 ResourceLoader、ResourceSaver、using/Dispose 足够描述候选，不需新 loader、walker、runner 或接口。没有移植停放 dirty164/168/165。

| 候选 | 可得到的证据 | 不能保证 / 下一 gate |
| --- | --- | --- |
| 保持代码不变，仅保存既有失败 | 不改变原运行事实；本轮所处状态 | 无新增 runtime 事件；不是再次诊断运行。 |
| A：目标构造边界的公开观测，无显式 Load/长期 pin | 首次构造阶段；成功后短作用域 GetScript/公开 identity | GetScript/日志/包装本身仍有干预；不能看到构造失败对象、raw map/refcount/线程。须另冻最小 probe source 并先独立 SOURCE 审查。 |
| A-load：显式 Load 但不长期保存 | acquisition 阶段及成功返回 identity | 改变加载/缓存/寿命顺序；不称未干预基线。不保存也不保证立即释放。 |
| B：仅目标脚本的 scoped 强引用对照 | 成功取得的同一 script 在本 owner 范围内保活是否改变观察 | 首次取得仍可能失败；其它类型、fixture 间隙、ALC/shared Dispose 未保证。acquisition 必须在 Background initializer **之前**；放当前构造体开头已晚。不能自动改成 static/全项目 hoard。 |
| C：生产 workaround | 本轮没有足够依据实施 | 先证明生产入口需求、现有 owner 起止/异常释放、运行/export/editor/ALC 支持；不能从测试对照直接推广。 |

此表收敛的是**可审范围**，不在 A/B/C 之间替操作者作行为选择。以后若要运行旧失败链，除候选 SOURCE 审查外，还必须由操作者具体批准票号、source/公开入口、次数和 STOP 边界；`allowRuntime=true` 不替代缺失的 retryApproval。原生预算、capture、现有断言与原 executor 不变；任一新 crash/abort/unknown/不完整输出立即保全停止，不自动第二次。

## 5. 未闭合契约与本轮验证

- 旧 #168 753 discovery /474 selected 实际466唯一 Passed、缺8、Summary Failed、300000ms abort；fatal 文本不等 Godot child 数字退出（后者未记录）。新 WORD 单例 Passed 不等整批 GREEN。旧 #165 cold83/85、120000ms abort/OutputDrainTimeout 也未解决。
- Type→raw pointer 命中/Ref 获取与销毁清表竞态仍是已有研究中的候选，非本案 root cause；本文不重复审 bimap、不把第三方 issue 评论当官方因果结论。
- 本轮没有 red-capable runtime feedback loop：无具体 retryApproval，按用户 SOURCE 前置授权停在契约研究；不能跳称已完成 bug 复现/最小化/修复/回归。未来公共观察即便通过也不等 native causal proof。
- 仍欠原整叶 #168 Native 矩阵/public Prepare/docs/whole-leaf 双审/实际集成；#164 Texture/整叶、#165 机制、#161 私有真实 precondition/闭包与共享门、#146 cold 门均不由本票签收。#129/#147 不解锁；工程叶计数17、storyCredit0、fullM6false不因本文增加。
- 本轮实做只读 Git/Issue/依赖、直接调用方、固定官方源对照与下载散列；所有新脚本仅用于交付/校验 JSON，先经现有语法/AST 门。没有执行 Unit/SDK/build/import/Prepare/Godot、GPU、人工或远端 CI；没有改公共 API 或行为、测试计数、工具链或真实资产。
- 原件/命令/退出、source hash、收据与候选状态放在本轮外部输出目录；后续外层独立 Standards/Spec 验收前不关闭本票。没有把研究者报告或检查器 exit0 当整M6通过。

本轮原件根：`C:/evidence/m6/loop/runs/0eef45019b3448de8715aa291e06087f/iteration-0001-e4e0f6501ec6489792eeb8bc8fde301f/worker-0/`。入口为 `acceptance.md`、`ownership-research.md`、`official-source-final.sha256` 与 `godot-commit.json`；官方源副本只在该外部目录，未提交上游源码。

## 固定 primary-source 索引

所有链接均固定同一完整 commit；精确行段是审阅入口，不以最新在线 API 文档代替。

[object-get]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/object/object.cpp#L1010-L1012
[object-bind]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/object/object.cpp#L1852
[type-variant]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/variant/type_info.h#L153-L158
[gen-return]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L4117-L4122
[gen-variant]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L4910-L4922
[gen-name]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L4172
[gen-method]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L3101-L3122
[gen-header]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.h#L186
[gen-internal]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L4233-L4244
[object-doc]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/doc/classes/Object.xml#L692-L697
[variant-own]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/Variant.cs#L11-L104
[variant-as]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/Variant.cs#L357-L358
[variant-convert]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/NativeInterop/VariantUtils.cs#L487-L499
[object-dispose]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/GodotObject.base.cs#L98-L151
[dispose-dispatch]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/runtime_interop.cpp#L167-L218
[owner-ref]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/csharp_script.cpp#L1708-L1748
[instance-dispose]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/csharp_script.cpp#L1785-L1819
[instance-dtor]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/csharp_script.cpp#L1980-L2053
[load-generic]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/Extensions/ResourceLoaderExtensions.cs#L25-L28
[load-decl]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/core_bind.h#L77
[load-bind]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/core_bind.cpp#L78-L150
[type-ref]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/variant/type_info.h#L186-L193
[gen-owner]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L3910-L3941
[gen-icall]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L3480-L3566
[csharp-doc]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/doc_classes/CSharpScript.xml#L2
[script-doc]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/doc/classes/Script.xml#L2
[resource-doc]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/doc/classes/Resource.xml#L2
[unmanaged-get]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/NativeInterop/InteropUtils.cs#L11-L47
[binding-setup]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/csharp_script.cpp#L1169-L1190
[binding-replace]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/runtime_interop.cpp#L260-L306
[gen-ctor]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/editor/bindings_generator.cpp#L2394-L2406
[object-ctor]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/GodotObject.base.cs#L47-L75
[pre-setup]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/NativeInterop/InteropUtils.cs#L79-L87
[godot-ref]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/NativeInterop/InteropStructs.cs#L38-L57
[ref-destroy]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/runtime_interop.cpp#L381-L383
[ref-unref]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/object/ref_counted.h#L199-L228
[binding-callback]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/csharp_script.cpp#L1251-L1326
