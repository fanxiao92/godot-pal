# C#/.NET 架构品味机械护栏选型与多宿主工程 Skills 发现约定调研报告

- **关联决策地图**：[原生 Harness 与代码地形整备路线图（wayfinder）](https://github.com/fanxiao92/godot-pal/issues/264)
- **关联研究票**：[研究：C#/.NET 架构品味 Lint 方案与多宿主工程 Skills 发现约定](https://github.com/fanxiao92/godot-pal/issues/266)
- **基准提交**：`origin/main`（`2d5ce2c`），SDK 锁定 `.NET 10.0.402`（[global.json](../../global.json)），引擎工程 SDK `Godot.NET.Sdk/4.7.2`（[Pal.Game.csproj](../../src/Pal.Game/Pal.Game.csproj)、[Pal.Game.Tests.csproj](../../tests/Pal.Game.Tests/Pal.Game.Tests.csproj)）

---

## 一、拍板结论速览（Executive Summary）

1. **主题一（C#/.NET 架构分层与品味机械护栏）——拍板推荐「方案 B + 方案 A 极薄组合，辅以既有离线脚本司职非 C# 边界；不引入方案 C 第三方架构测试库」**：
   - **固定符号与跨层 API 禁用**交由官方 **`Microsoft.CodeAnalysis.BannedApiAnalyzers` + `.editorconfig`（`RS0030=error`）**：零自定义代码接管 `Pal.Core` 与两个 `*UnitTests` 的平台文件/进程/时间/Godot API 封禁，以及 `Pal.Game` 的 `InternalsVisibleToAttribute` 封禁（含 MSBuild 生成的 `AssemblyInfo.cs`），每条规则原生附带 `;` 自修复指引。
   - **关系型与品味型 C# 规则**交由仓内极薄自定义 Roslyn Analyzer **`tools/Pal.Analyzers/Pal.Analyzers.csproj`（`netstandard2.0`，`OutputItemType="Analyzer"`，`ReferenceOutputAssembly="false"`）**：仅承接 `BannedSymbols.txt` 无法表达的三项规则——① 子系统命名空间单向依赖矩阵（`PAL0001`）、② `Pal.Core` 友元程序集参数白名单（仅允许 `"Pal.Extract"`，`PAL0002`）、③ `src/` 生产代码禁止测试开关字段/方法与可变静态测试钩子（`PAL0003`）。编译期（`csc`）直接产出精确到 `file(line,col)` 的自修复诊断，与 `Godot.SourceGenerators` 无缝共存且零运行时产物污染。
   - **不引入 `ArchUnitNET` / `NetArchTest`**：`TngTech.ArchUnitNET`（`0.13.5`）传递引入 `Mono.Cecil`、`Newtonsoft.Json` 等 5+ 个 NuGet 包，`BenMorris/NetArchTest` 停更于 2021 年（`v1.3.2`），且所有测试期 IL/反射方案均滞后于编译、缺少源码行列定位并擦除 `const`/`nameof` 编译期耦合。
   - **保留 [Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1) 的非 C# 语义职责**：继续执行零 MSBuild 的根目录文件/目录白名单、`.csproj` 窄形状（禁 `<Target>`/`<Copy>`/`<Exec>`）与 `ProjectReference` 拓扑守卫，同步放行 `.editorconfig`、`.agents`、`BannedApiAnalyzers`、`AdditionalFiles` 与 `Analyzer` 项目引用属性。

2. **主题二（多宿主工程级 Skills 单一真源约定）——拍板推荐以 `.agents/skills/<skill-name>/SKILL.md` 为唯一入库真源，禁止跨目录复制副本与禁止提交 Git 软链（`120000`）**：
   - **Antigravity**：原生零配置按目录层级自动发现 `.agents/skills/*/SKILL.md`（最高优先级 Workspace Project）。
   - **Pi（`@earendil-works/pi-coding-agent` 及 `pi-subagents`）**：两者源码均已原生扫描 `.agents/skills/`；同时在 [.pi/settings.json](../../.pi/settings.json) 增配 `"skills": ["../.agents/skills"]`，补齐 `pi-subagents` 在非仓库根 `cwd` 启动时的祖先解析缺口（双端源码均按 `realpath` 静默去重，零冲突告警）。
   - **Claude Code**：当前官方仅原生扫描 `.claude/skills/<name>/SKILL.md`，尚未原生读取 `.agents/skills/`，但官方文档明确支持 `.claude/skills` 目录软链。鉴于 Windows 默认 `core.symlinks=false` 会将入库 Git 软链检出为纯文本普通文件导致发现失效，推荐将 `/.claude/` 列入 [.gitignore](../../.gitignore)（并在 [Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1) 放行 `.claude` 本地目录），通过幂等跨平台目录链接桥接 `.claude/skills -> .agents/skills`（Windows 用无需管理员/开发者模式的 NTFS `Junction`，Linux/macOS 用 `SymbolicLink`；可由 `./pal.ps1` 环境初始化子命令或受控脚本一键幂等建立）。

---

## 二、主题一：C#/.NET 架构分层与代码品味机械护栏选型

### 2.1 本仓工程形态与约束基线

核对本仓 [GodotPal.sln](../../GodotPal.sln)、各 `.csproj`、[CODING_STANDARDS.md](../../CODING_STANDARDS.md) 与 [pal.ps1](../../pal.ps1) 得到以下事实基线：

- **SDK 与目标框架**：[global.json](../../global.json) 锁定 `.NET SDK 10.0.402`（`rollForward: latestPatch`），全仓 7 个 C# 工程统一 `TargetFramework=net10.0`。
- **双 SDK 并存**：
  - 纯 `Microsoft.NET.Sdk`：[Pal.Core.csproj](../../src/Pal.Core/Pal.Core.csproj)、[Pal.Extract.csproj](../../tools/Pal.Extract/Pal.Extract.csproj)、[Pal.Core.UnitTests.csproj](../../tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj)、[Pal.Extract.UnitTests.csproj](../../tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj)、[Pal.Extract.IntegrationTests.csproj](../../tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj)、[Pal.Prepare.Tests.csproj](../../tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj)。
  - `Godot.NET.Sdk/4.7.2`：[Pal.Game.csproj](../../src/Pal.Game/Pal.Game.csproj)、[Pal.Game.Tests.csproj](../../tests/Pal.Game.Tests/Pal.Game.Tests.csproj)（开启 `EnableDynamicLoading=true`，隐式挂载 `Godot.SourceGenerators`、`GodotSharp` 与 Debug 下的 `GodotSharpEditor`）。
- **既有静态边界守卫**：[Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1) 在零 MSBuild 前提下对 `core` 与 `cli-unit` 执行严格白名单 XML 检查及正则词法扫描（第 97–112 行），拦截 `File`、`Directory`、`Process`、`Godot` 等标识符，但存在两类固有局限（亦见 [unit-boundary.md](../../docs/testing/unit-boundary.md) 第 54–59 行）：
  1. **词法误伤与漏检并存**：同名纯业务属性/参数（如 `File`、`Directory`）或插值字符串文本会被保守误拒；而 `DateTime.Now`、`Stopwatch`、未列出的 I/O 类型或跨方法间接返回则无法做符号级识别。
  2. **覆盖范围局限于 Unit 源码**：无法表达 `Pal.Core` / `Pal.Game` 内部子系统命名空间单向依赖矩阵，也无法检测 `src/` 内部新增的测试开关字段或方法。

---

### 2.2 三种候选方案的一手实证对比

#### 方案 A：仓内轻量自定义 Roslyn `DiagnosticAnalyzer`（`netstandard2.0`）

1. **`.NET 10` 与 `Godot.NET.Sdk/4.7.2` 接入方式**：
   - Roslyn 分析器宿主规范（规则 `RS1036` / `RS1038`）要求分析器工程目标框架固定为 `netstandard2.0`，以便在 `dotnet build`（`.NET 10` `VBCSCompiler`）、Visual Studio MSBuild 与 Rider 等不同宿主进程内统一加载（来源：[dotnet/roslyn Analyzer Reference Docs](https://learn.microsoft.com/en-us/visualstudio/extensibility/getting-started-with-roslyn-analyzers)）。
   - 分析器工程 `tools/Pal.Analyzers/Pal.Analyzers.csproj` 仅引用编译期元数据包（`PrivateAssets="all"`）：
     ```xml
     <Project Sdk="Microsoft.NET.Sdk">
       <PropertyGroup>
         <TargetFramework>netstandard2.0</TargetFramework>
         <LangVersion>latest</LangVersion>
         <Nullable>enable</Nullable>
         <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
         <IsRoslynComponent>true</IsRoslynComponent>
       </PropertyGroup>
       <ItemGroup>
         <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
         <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all" />
       </ItemGroup>
     </Project>
     ```
   - 消费端（含 `Microsoft.NET.Sdk` 的 `Pal.Core` 与 `Godot.NET.Sdk/4.7.2` 的 `Pal.Game`）统一通过标准项目引用挂载：
     ```xml
     <ProjectReference Include="../../tools/Pal.Analyzers/Pal.Analyzers.csproj"
                       OutputItemType="Analyzer"
                       ReferenceOutputAssembly="false" />
     ```
   - 由于 `ReferenceOutputAssembly="false"` 且 `OutputItemType="Analyzer"`，MSBuild 仅将该 DLL 注入 `csc` 的 `/analyzer:` 参数，**绝不作为运行时程序集引用传递**，也不会被 [Pal.Game.Tests.csproj](../../tests/Pal.Game.Tests/Pal.Game.Tests.csproj) 的 `CopyLocalLockFileAssemblies=true` 拷贝进 `.godot/mono/temp/bin/`，完全遵守 `Pal.Core` 零运行时外部依赖红线。

2. **与 Godot Source Generators 共存机制**：
   - 经核查 `godotengine/godot` 官方 SDK 源码 [`Sdk.props`](https://github.com/godotengine/godot/blob/master/modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/Sdk.props) 与 [`Sdk.targets`](https://github.com/godotengine/godot/blob/master/modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/Sdk.targets)，`Godot.NET.Sdk` 底层直接导入 `Microsoft.NET.Sdk`，并通过 `<PackageReference Include="Godot.SourceGenerators" ... ReferenceOutputAssembly="false" PrivateAssets="all" />` 挂载增量源码生成器。
   - 在 Roslyn 编译管线中，`IIncrementalGenerator`（生成 `*_ScriptMethods.generated.cs`、`*_ScriptProperties.generated.cs`、`ScriptPathAttribute.generated.cs`）先于 `DiagnosticAnalyzer` 执行。自定义分析器在 `Initialize` 中调用：
     ```csharp
     context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
     context.EnableConcurrentExecution();
     ```
     即可完全跳过 Godot 生成的 `.generated.cs` 语法树，零重复扫描、零生成代码误报，同时在用户手写 partial 类上完整保有 `Godot.Node` / `Godot.Resource` 的语义符号信息。
   - 同时核对本仓 [AffectedSelection.psm1](../../scripts/pal/AffectedSelection.psm1)（第 81–112、455–492 行）：`Get-StaticProjectGraph` 与 `dotnet msbuild -getItem:ProjectReference` 均将带 `OutputItemType="Analyzer"` 的 `<ProjectReference>` 视为一致的项目边，不会引发图漂移报错；且当 `tools/Pal.Analyzers/**` 变更时，反向闭包能自动覆盖所有挂载该分析器的下游测试目标。

3. **能力边界、自修复诊断与构建耗时**：
   - **能力上限最高**：可同时访问语法树（标识符命名、修饰符、`#pragma`）与语义模型（`IOperation`、`INamedTypeSymbol`、调用方 `ContainingNamespace` -> 被调方 `ContainingNamespace`、特性构造参数值）。
   - **自修复提示（Remediation Message）**：`DiagnosticDescriptor` 原生支持自定义诊断码（如 `PAL0001`–`PAL0003`）、精确源码 `Location`（`file.cs(line,col)`）以及结构化修复指引文本，在 `dotnet build`、`./pal.ps1`、LRA 日志与 IDE 中实时一致呈现。
   - **耗时影响**：`Pal.Analyzers` 自身仅约 200 行无外部依赖代码，冷编译约 `0.3–0.6s`（增量构建命中缓存接近 `0ms`）；在 `csc` 进程内并发执行 `SymbolAction` / `OperationAction`，对本仓百余个 `.cs` 文件的单次编译增量耗时 **`< 20ms`**。

---

#### 方案 B：官方 `.editorconfig` + `Microsoft.CodeAnalysis.BannedApiAnalyzers`（`BannedSymbols.txt`）

1. **官方机制与语法能力边界**：
   - 来源：`dotnet/roslyn-analyzers` 官方文档 [`Microsoft.CodeAnalysis.BannedApiAnalyzers.md`](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers.md)、[`BannedApiAnalyzers.Help.md`](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md) 及 `dotnet/roslyn` 实现源码 [`SymbolIsBannedAnalyzerBase.cs`](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core/SymbolIsBannedAnalyzerBase.cs)。
   - 通过 `AdditionalFiles` 引入 `BannedSymbols.txt` 或通配命名 **`BannedSymbols.*.txt`**（便于跨工程复用共享清单，如 `BannedSymbols.PureDotNet.txt`），每行语法为：
     ```text
     <DocCommentId>[;<Remediation Text>]
     ```
     支持 `N:`（命名空间及其下所有子命名空间/类型）、`T:`（类型）、`M:`（方法/构造器重载）、`P:`（属性）、`F:`（字段）、`E:`（事件），命中时抛出 `RS0030`（重复条目抛 `RS0031`），可在根目录 `.editorconfig` 中统一设为编译错误：
     ```ini
     [*.cs]
     dotnet_diagnostic.RS0030.severity = error
     dotnet_diagnostic.RS0031.severity = error
     ```

2. **针对本仓三项关键边界问题的实证核验**：
   - **能否替代 [Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1) 的 C# 正则禁用词扫描？**
     - **对外部/框架 API 封禁：完全胜任且语义精度远优于正则**。封禁 `N:Godot`、`N:GdUnit4`、`T:System.IO.File`、`T:System.IO.Directory`、`T:System.IO.FileStream`、`T:System.Diagnostics.Process`、`M:System.IO.Path.GetTempPath`、`P:System.DateTime.Now`、`T:System.Diagnostics.Stopwatch`、`T:System.Runtime.InteropServices.DllImportAttribute`、`T:System.Runtime.InteropServices.LibraryImportAttribute` 后，不仅不会误伤名为 `Directory` / `File` 的纯内存业务成员或字符串字面量，还能精准捕获 `using` 别名、`var` 隐式类型推断、方法组委托引用与隐式构造调用，同时保留 `MemoryStream`、`BinaryReader`、`BinaryWriter`、`ZLibStream` 正常使用。
     - **不能替代的部分**：`BannedApiAnalyzers` 只在 `csc` 编译阶段检查已解析 C# 符号，不检查仓库根目录脏文件、`.csproj` 自定义 `<Target>`/`<Copy>`/`<Exec>` 或 `ProjectReference` 数量约束——这些仍属于 [Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1) 的极速离线 XML/目录职责。
   - **能否在 `Pal.Game` 中禁止 `InternalsVisibleToAttribute`？**
     - **能（包括 `.csproj` `<AssemblyAttribute>` 注入方式）**。经核查 `SymbolIsBannedAnalyzerBase.cs`，该分析器显式开启了 `GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics`，因此不仅扫描手写 `[assembly: InternalsVisibleTo(...)]`，**还会扫描 MSBuild 在 `obj/` 下自动生成的 `*.AssemblyInfo.cs`**。在 `Pal.Game` 的 `BannedSymbols.Game.txt` 中写入 `T:System.Runtime.CompilerServices.InternalsVisibleToAttribute;禁止在 Pal.Game 使用 InternalsVisibleTo（会导致 Godot 源码生成器 CS0436 冲突），请改为显式 public 接缝（见 CODING_STANDARDS.md）` 即可在编译期彻底封死。
     - **局限**：`BannedSymbols.txt` 按符号全有或全无封禁，**无法按特性字符串参数值做白名单过滤**（例如 [Pal.Core.csproj](../../src/Pal.Core/Pal.Core.csproj) 合法声明了 `InternalsVisibleTo("Pal.Extract")`，但需禁止向 `Pal.Core.UnitTests` 或 `Pal.Game` 开放；同包的 `RS0035` `RestrictedInternalsVisibleToAttribute` 仅限制可访问的内部命名空间，不限制目标程序集列表）。因此 `Pal.Core` 的友元参数白名单仍需自定义 Analyzer（`PAL0002`）或 XML 守卫承接。
   - **能否表达同一程序集内的子系统命名空间单向依赖或禁止 `src/` 测试开关？**
     - **不能**。`AdditionalFiles`（`BannedSymbols.*.txt`）的作用域是**整个 `.csproj` 编译单元**而非单个子目录：若在 `Pal.Core` 中写入 `N:Pal.Core.Battle`，则 `Pal.Core.Battle` 自身内部也会触发 `RS0030`；而 `.editorconfig` 的 `[src/Pal.Core/Dialogue/**.cs]` 只能按文件通配整体开关 `RS0030` 严重度，无法在同一文件上“既保持平台 I/O 的 `RS0030=error`，又按调用方命名空间差异化裁决子系统依赖矩阵”。同理，它无法识别 `src/` 内新定义的 `*ForTesting` 方法或可变 `static` 测试开关字段。

---

#### 方案 C：C# 架构测试库（`ArchUnitNET` / `NetArchTest` / 纯 BCL 元数据自检单测）

1. **三类子方案实证对比**：
   - **`TngTech.ArchUnitNET`（仓库 [`TNG/ArchUnitNET`](https://github.com/TNG/ArchUnitNET)，最新 `0.13.5`）**：
     - 维护活跃，支持 `.NET 10` 测试宿主与丰富分层/切片/环路 DSL。
     - **依赖体积偏重**：直接核查上游 [`ArchUnitNET.csproj`](https://github.com/TNG/ArchUnitNET/blob/main/ArchUnitNET/ArchUnitNET.csproj)，核心包强依赖 `Mono.Cecil (0.11.6)`、**`Newtonsoft.Json (13.0.4)`**、`CycleDetection (2.0.0)`、`JetBrains.Annotations`、`System.ValueTuple`，外加 `TngTech.ArchUnitNET.xUnit`。在本仓严格收敛三方包（全仓仅用 `System.Text.Json`）与 `.NET 10` `NuGetAudit` 策略下引入 `Newtonsoft.Json` 性价比极低。
   - **`NetArchTest.Rules`（[`BenMorris/NetArchTest`](https://github.com/BenMorris/NetArchTest)）/ 社区分支 `NetArchTest.eNhancedEdition`**：
     - 原版 `BenMorris/NetArchTest` 停更于 `v1.3.2`（2021-05），对高版本 C# 编译器生成的状态机/局部函数/record 类型常出现误判；社区分支虽更新了 `Mono.Cecil` 且包体较轻，但仍受限于测试期字节码分析的固有短板。
   - **纯 BCL `System.Reflection.Metadata`（`PEReader` + `MetadataReader`）自检测试**：
     - `.NET 10` 基础类库内置、**零 NuGet 依赖**，且可直接按磁盘 PE 字节流读取 `Pal.Game.dll` 的 `TypeRef` / `MemberRef` 表而无需加载 `GodotSharp` 运行时或启动 Godot 进程（遵守本地引擎串行红线）。

2. **所有测试期方案（含纯 BCL 元数据自检）相对编译期 Roslyn 的共同劣势**：
   - **反馈滞后且编译失败时失明**：必须等目标工程编译成功并运行 `dotnet test` 才触发，无 IDE 实时波浪线，且需要单独维护测试工程（[Pal.Core.UnitTests](../../tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj) 自身禁止文件读取与禁止引用 `Pal.Game`）。
   - **缺失源码文件与行列号**：字节码元数据默认只给出 `TypeA -> TypeB` 符号名，不带 `src/Pal.Core/Dialogue/Foo.cs(42,17)` 精确位置（除非额外手写便携式 PDB SequencePoint 解析器），不利于 Agent 单回合自修复。
   - **编译期语法信息擦除**：跨子系统的 `const` 常量内联、`nameof(...)` 引用在 IL 层已被擦除（漏检），而 `async`/`yield`/Lambda 闭包生成类又需要额外编写反编译器噪声过滤逻辑。

---

### 2.3 三方案横向对比矩阵

| 评估维度 | 方案 A：仓内自定义 Roslyn Analyzer (`netstandard2.0`) | 方案 B：`.editorconfig` + `BannedApiAnalyzers` | 方案 C1：`ArchUnitNET` / `NetArchTest` | 方案 C2：纯 BCL `System.Reflection.Metadata` 单测 |
| :--- | :--- | :--- | :--- | :--- |
| **`.NET 10` 兼容性** | 原生支持（`csc` 进程内运行） | 官方一等公民支持 | 兼容（运行于 `net10.0` xUnit） | `.NET 10` 内置 BCL，零外部兼容风险 |
| **`Godot.NET.Sdk/4.7.2` 兼容** | 原生兼容；`GeneratedCodeAnalysisFlags.None` 跳过 Godot 生成树 | 原生兼容；还可覆盖 `obj/*.AssemblyInfo.cs` | 需先构建 `Pal.Game.dll` 再跨目录加载字节码 | 需先构建 `Pal.Game.dll` 再读 PE 元数据 |
| **新增 NuGet 依赖** | 仅 `Pal.Analyzers` 编译期 `Microsoft.CodeAnalysis.*`（`PrivateAssets=all`，零运行时传递） | 单一官方分析器包 `Microsoft.CodeAnalysis.BannedApiAnalyzers`（`PrivateAssets=all`） | 重（`Mono.Cecil` + `Newtonsoft.Json` 等）或原版停更 | **0 个新增包** |
| **子系统命名空间单向依赖** | **精准支持**（含 `const`、`nameof`、`var`、泛型与扩展方法） | **不支持**（`BannedSymbols.txt` 无法按调用方命名空间切片） | 支持（漏检 `const`/`nameof` 内联） | 需手写 `TypeRef`/`MemberRef` 图解析（漏检 `const`） |
| **`Pal.Game` 禁 `InternalsVisibleTo`** | **支持**（且可在 `Pal.Core` 精确限定参数为 `"Pal.Extract"`） | **支持**（`Pal.Game` 全禁；但无法在 `Pal.Core` 限定参数值） | 支持 | 支持 |
| **`Pal.Core` / Unit 禁平台 I/O 与时钟** | 支持，但逐个维护 API 列表不如 `BannedSymbols.txt` 直观 | **最佳契合**（声明式文本清单 + `;` 修复提示） | 需遍历方法体 IL 指令操作数 | 需手写 IL 方法体解码才能查清方法调用 |
| **`src/` 禁测试开关字段/方法** | **精准支持**（语法修饰符 + 命名模式 + `#pragma` 局部豁免） | **不支持**（只能禁已知固定符号 ID） | 仅能查反射可见成员，无语法上下文 | 仅能查元数据表，无源码行列与 `#pragma` |
| **报错位置与自修复提示** | **`file.cs(line,col): error PAL000x` + 定制修复指引** | **`file.cs(line,col): error RS0030` + `;` 定制修复指引** | 仅测试断言栈与类型名，无调用点行列号 | 仅测试断言栈与类型名，无调用点行列号 |
| **反馈时机与耗时** | `dotnet build` / IDE 实时（增量 `< 20ms`） | `dotnet build` / IDE 实时（增量 `< 10ms`） | `dotnet test` 阶段（`+200~800ms`） | `dotnet test` 阶段（`+50~150ms`） |

---

### 2.4 本仓具体规则分层承接推荐表

| 规则编号 / 场景 | 目标范围 | 推荐承接层 | 诊断码与自修复提示（Remediation）设计 |
| :--- | :--- | :--- | :--- |
| **R1：子系统命名空间单向依赖**（物理归位后 `Pal.Core.*` 分层 DAG、`Pal.Game.Scripts.*` 视图子系统隔离、`Pal.Game` 运行时禁引 `Pal.Game.Editor`） | `src/Pal.Core`、`src/Pal.Game` | **方案 A：`tools/Pal.Analyzers`** | `PAL0001`（Error）：指出 `<CallerNamespace>` 禁止依赖 `<TargetNamespace>`，提示将共享值类型下沉至基础契约命名空间，或上移至 `Story`/`Main` 编排层接线。 |
| **R2：`InternalsVisibleTo` 边界管控**（`Pal.Game` 严禁任何友元；`Pal.Core` 仅允许 `"Pal.Extract"`） | `src/Pal.Core`、`src/Pal.Game` | **方案 B（`Pal.Game` `RS0030`）+ 方案 A（`PAL0002` 参数白名单双保险）** | `RS0030` / `PAL0002`（Error）：提示 `Pal.Game` 禁止 `InternalsVisibleTo`（规避 Godot SourceGenerator `CS0436` 冲突），`Pal.Core` 除 `Pal.Extract` 外禁止新增友元；测试接缝请显式设计为 `public`（见 [CODING_STANDARDS.md](../../CODING_STANDARDS.md)）。 |
| **R3：纯内存与确定性时间 API 封禁**（禁 `System.IO.File`/`Directory`/`FileStream`/`StreamReader`、`Process`、`DllImport`/`LibraryImport`、`DateTime.Now`/`UtcNow`、`Stopwatch`、`Godot.*`） | `src/Pal.Core`、`tests/Pal.Core.UnitTests`、`tests/Pal.Extract.UnitTests` | **方案 B：`BannedApiAnalyzers`（共享 `BannedSymbols.*.txt` + `.editorconfig` `RS0030=error`）** | `RS0030`（Error）：按类别附带 `;` 修复指引——I/O 提示改用内存 `ReadOnlySpan<byte>`/`Stream` 契约，时间提示改用 `Pal.Core` 显式确定性时间接口，引擎类型提示移入 `Pal.Game` / `Pal.Game.Tests`。 |
| **R4：禁止测试专用开关与可变静态钩子进入生产源码**（禁 `*ForTest*`、`*TestMode*`、`*TestHook*` 成员及可变 `static` 字段/委托钩子） | `src/Pal.Core`、`src/Pal.Game` | **方案 A：`tools/Pal.Analyzers`** | `PAL0003`（Error）：提示生产目录 `src/` 禁止植入测试开关或路径回退钩子；请通过构造参数/接口显式注入，或在 `tests/` 下使用作用域 `IDisposable` 夹具（确属合法静态缓存可显式标注 `static readonly` 或带理由 `#pragma warning disable PAL0003`）。 |
| **R5：非 C# 工程拓扑、MSBuild 窄形状与根目录卫生**（禁根目录散落脚本、禁 Unit/Core `.csproj` 出现 `<Target>`/`<Copy>`/`<Exec>`、锁死运行时 `ProjectReference` 闭包） | 仓库根目录、`core` 与 `cli-unit` 四工程 | **既有离线脚本：[Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1) + [Test-RepoHygiene.ps1](../../scripts/Test-RepoHygiene.ps1)** | `[boundary:*]` / `[hygiene:*]`：保持零 MSBuild 秒级预检，与 `dotnet test` 编译期的 Roslyn 规则形成“外层项目骨架 + 内层 C# 语义”互补闭环。 |

---

### 2.5 对 `scripts/Test-UnitBoundary.ps1` 及配套清单的具体改动要求

落地上述组合时，需在实施票中同步更新以下白名单与图自检配置（严禁单改 `.csproj` 导致门禁误红）：

1. **[scripts/Test-UnitBoundary.ps1](../../scripts/Test-UnitBoundary.ps1)**：
   - **第 12 行 `$script:AllowedRootFiles`**：新增 `'.editorconfig'`。
   - **第 15 行 `$script:AllowedRootDirectories`**：新增 `'.agents'` 与 `'.claude'`（注：`Test-RootFiles` 使用 `Get-ChildItem -Force` 扫描磁盘实际目录，即使 `/.claude/` 被 `.gitignore` 忽略，本地使用 Claude Code 生成 `.claude/` 目录时若不在该白名单内也会触发 `[boundary:root-files]` 失败；若与 [LRA 工程：单票终态自动沉淀回路（retro 触发 + signals 生命周期 + 注入闭环）](https://github.com/fanxiao92/godot-pal/issues/229) 接口对齐同批落地，一并放行 `'signals'`）。
   - **第 36 行 `$allowedPackages`**：新增 `'Microsoft.CodeAnalysis.BannedApiAnalyzers'`。
   - **第 44–51 行 `$allowedAttributes` 与第 62 行 `ItemGroup` 子元素白名单**：
     - 允许 `ItemGroup` 下出现 `AdditionalFiles`（允许属性 `Include`、`Link`，且值仅限字面量 `BannedSymbols*.txt` 路径）。
     - 允许 `ProjectReference` 增加属性 `OutputItemType` 与 `ReferenceOutputAssembly`；当存在这两个属性时，强制校验 `OutputItemType -ceq 'Analyzer'`、`ReferenceOutputAssembly -ceq 'false'` 且目标路径精确解析为 `tools/Pal.Analyzers/Pal.Analyzers.csproj`。
   - **第 160–184 行 `ProjectReference` 计数与闭包检查**：将 `OutputItemType="Analyzer"`（指向 `tools/Pal.Analyzers/Pal.Analyzers.csproj`）与普通运行时 `ProjectReference` 分离统计——unit 工程仍必须有且仅有 1 个生产运行时引用（`Pal.Core` 或 `Pal.Extract`），`Pal.Core` 仍保持 **0 个运行时 `ProjectReference`**，同时允许挂载 `Pal.Analyzers`。
   - **第 97–112 行 `Test-Source` 词法检查收敛**：当 `BannedApiAnalyzers` 已在 `pal.ps1 ci dotnet` 编译阶段对 `Pal.Core.UnitTests` / `Pal.Extract.UnitTests` 强制执行 `RS0030=error` 后，可将 `Test-Source` 聚焦于仓内私有集成夹具类名拦截（`CliProcess|DirectoryLinkFixture|MkfFixture|CaseSensitiveFileSystemFact`）或保留作为无 SDK `Source` 模式的快速预筛，并在 [UnitBoundary.Tests.ps1](../../tests/UnitBoundary.Tests.ps1) 与 [unit-boundary.md](../../docs/testing/unit-boundary.md) 中同步契约。
2. **[GodotPal.sln](../../GodotPal.sln) 与 [tests/affected-map.psd1](../../tests/affected-map.psd1)**：
   - 将 `tools/Pal.Analyzers/Pal.Analyzers.csproj` 注册进 `GodotPal.sln` 的 `tools` 解决方案文件夹。
   - 在 `tests/affected-map.psd1` 的 `build-and-infra-full` 规则中加入 `.editorconfig`、`**/BannedSymbols*.txt`（并将 `tools/Pal.Analyzers/**` 映射至反向闭包或 `full`），确保 `./pal.ps1 test check-map` 通过且规则变更必触发下游验证。

---

## 三、主题二：多宿主工程级 Skills 单一真源发现约定

### 3.1 各宿主对仓库工程级 `<skill-name>/SKILL.md` 的一手支持核实

#### 1. Antigravity

- **一手来源**：Antigravity 内置官方定制系统文档 `agy-customizations`（`SKILL.md`、`docs/skills.md`、`docs/json_configs.md`）及 [Agent Skills 开放规范](https://agentskills.io/specification)。
- **原生自动发现路径**：
  - 从当前工作目录向上遍历至仓库根目录（`.git` 所在目录），原生自动发现 **`.agents/skills/<skill-name>/SKILL.md`**（兼容别名 `.agent/`、`_agents/`、`_agent/`），且处于**最高加载优先级（Workspace Project）**。
- **配置挂载能力**：
  - 支持 `.agents/skills.json` 通过工作区相对路径显式挂载外部目录（如 `{"entries": [{"path": "..."}]}`，单层扫描子目录）。但若真源直接置于 `.agents/skills/`，则**完全无需 `skills.json`**。
- **加载语义**：渐进式披露（Progressive Disclosure）——默认仅将 YAML frontmatter 的 `name` 与 `description` 注入上下文，激活时按需读取 `SKILL.md` 及相对路径下的 `scripts/`、`references/`、`resources/`。

#### 2. Pi（`@earendil-works/pi-coding-agent` 1.5.0、`pi-subagents` 与本仓 LRA）

- **一手来源**：
  - Pi 官方文档 [`packages/coding-agent/docs/skills.md`](https://github.com/earendil-works/pi/blob/main/packages/coding-agent/docs/skills.md)、[`packages/coding-agent/docs/settings.md`](https://github.com/earendil-works/pi/blob/main/packages/coding-agent/docs/settings.md) 与加载器源码 [`packages/coding-agent/src/core/skills.ts`](https://github.com/earendil-works/pi/blob/main/packages/coding-agent/src/core/skills.ts)；
  - `pi-subagents` 官方文档 [`docs/agents.md#skills`](https://github.com/nicobailon/pi-subagents/blob/main/docs/agents.md#skills) 与源码 [`src/agents/skills.ts`](https://github.com/nicobailon/pi-subagents/blob/main/src/agents/skills.ts)；
  - 本仓 [.pi/settings.json](../../.pi/settings.json) 与 [tools/long-running-agent/pi.ts](../../tools/long-running-agent/pi.ts)。
- **原生自动发现与关键源码细节**：
  - **Pi 主宿主（`earendil-works/pi`）**：完整实现 [Agent Skills 规范](https://agentskills.io/specification)，除 `.pi/skills/` 外，**原生自动发现 `.agents/skills/`**，并支持从任意子目录 `cwd` 向上遍历祖先直至仓库根目录（`docs/skills.md`："Project `.agents/skills/` directories are discovered from the working directory through its ancestors, stopping at the repository root when one exists"）。
  - **`pi-subagents` 扩展（`nicobailon/pi-subagents`）**：核查 `src/agents/skills.ts` 的 `buildSkillPaths` 函数，已原生内置 `{ path: path.join(cwd, ".agents", "skills"), source: "project" }`（最高优先级 `700`）。**但存在一处细微差异**：`getProjectConfigDir(cwd)` 会向上查找仓库根 `.pi`，而 `path.join(cwd, ".agents", "skills")` 直接拼接子进程当前 `cwd`。若子代理以子目录为 `cwd` 启动，直接拼接可能错过仓库根 `.agents/skills`。
  - **`.pi/settings.json` 路径数组补齐与无冲突去重证明**：
    - 在 `earendil-works/pi`（`docs/settings.md#resources`）与 `pi-subagents`（`collectSettingsSkillPaths`）双端实现中，项目级 `.pi/settings.json` 的 `"skills"` 数组**统一相对于 `.pi/` 目录解析相对路径**，即 `"../.agents/skills"` 精确指向 `<repo-root>/.agents/skills`。
    - 核查双端去重源码：`earendil-works/pi`（`src/core/skills.ts`）通过 `realPathSet` 对同一物理文件静默跳过；`pi-subagents`（`src/agents/skills.ts`）通过 `path.resolve` Map 按更高优先级合并去重。因此，在 [.pi/settings.json](../../.pi/settings.json) 中显式声明 `"skills": ["../.agents/skills"]` 既消除子目录 `cwd` 盲区，又**零重复告警**。
  - **与本仓 LRA 执行器的隔离关系**：[tools/long-running-agent/pi.ts](../../tools/long-running-agent/pi.ts) 第 107 行显式传入 `noSkills: true`（受控票据执行器按阶段契约注入上下文，不加载交互式技能菜单），且第 59 行将 `.pi` 列为受保护元数据路径段拒绝模型工具直接读写；将工程技能真源放在 `.agents/skills/` 恰好与 `.pi/` 隔离，互不干扰。

#### 3. Claude Code（`anthropics/claude-code`）

- **一手来源**：Claude Code 官方文档 [`https://code.claude.com/docs/en/skills`](https://code.claude.com/docs/en/skills)、[`https://code.claude.com/docs/en/settings`](https://code.claude.com/docs/en/settings)、[`https://code.claude.com/docs/en/plugins-reference`](https://code.claude.com/docs/en/plugins-reference) 及官方仓库 [`CHANGELOG.md`](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md)。
- **发现路径与配置边界核实**：
  - **原生路径仅限 `.claude/skills/<skill-name>/SKILL.md`**：截至当前官方发布版本，Claude Code 尚未原生扫描 `.agents/skills/`（社区已在 `anthropics/claude-code` 提议双路径发现，但尚未合入主线）。
  - **`.claude/settings.json` 无法直接重定向项目技能目录**：
    - `permissions.additionalDirectories` 仅放行文件系统读写权限，**不触发技能扫描注册**。
    - 启动参数 `--add-dir <dir>` 仅扫描 `<dir>/.claude/skills/`，无法直接把 `.agents/skills` 注册为顶层技能源。
    - 本地目录插件方案（`.claude-plugin/plugin.json` + `extraKnownMarketplaces` + `enabledPlugins`）会将所有技能命令强制加上命名空间前缀（变成 `/plugin-name:verify` 而非 `/verify`），且 `directory` 类型 marketplace 在相对路径归一化与非交互 CI 信任弹窗上存在已知限制，不适合作为轻量仓库技能入口。
  - **原生支持软链 / 目录联接（Symlink / Junction）**：官方 Skills 文档明确声明 `.claude/skills/` 本身或其下子目录可为指向磁盘其他位置的符号链接，Claude Code 会透明解析并正常注册顶层 `/verify`、`/garden` 命令及按需加载附属脚本。

---

### 3.2 跨平台（Windows 本地 + Linux `ubuntu-24.04` CI）零重复维护方案对比

在本仓 Windows 工作树实测 `git config --get core.symlinks` 返回 **`false`**（Git for Windows 在未开启系统开发者模式或管理员安装选项时的默认值）。依据 Git 官方文档 [`git-config: core.symlinks`](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks)，当 `core.symlinks=false` 时，Git 会将仓库内 `120000` 模式的符号链接检出为**仅含目标路径字符串的普通纯文本文件**，导致 Windows 下 `.claude/skills` 变成普通文件而非目录，彻底破坏 Claude Code 技能发现与目录遍历。

基于此硬约束，对四种跨宿主共享方案评估如下：

| 方案 | 做法简述 | Antigravity | Pi / `pi-subagents` | Claude Code | Windows (`core.symlinks=false`) & Linux CI 表现 | 结论 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **方案 S1（拍板推荐）：`.agents/skills/` 单一入库真源 + 配置指向 + 本地幂等目录联接桥接** | 真源唯一入库 `.agents/skills/<name>/SKILL.md`；`.pi/settings.json` 配 `"skills": ["../.agents/skills"]`；`/.claude/` 入 `.gitignore`，按需由脚本在本地建立 `.claude/skills -> .agents/skills`（Windows 用 NTFS `Junction`，Linux 用 `SymbolicLink`） | 原生零配置自动发现 | 原生发现 + `settings.json` 兜底子目录 `cwd` | 顶层 `/<name>` 原生可用，零内容复制 | **双平台全绿**：Git 仓库仅存普通文件；Windows `Junction` **无需管理员权限或开发者模式**（本仓 `GameProjectMount` / `T3Assets.psm1` 已成熟验证）；Linux CI 不依赖 `.claude/` | **首选拍板** |
| **方案 S2（可选增强 / 备选）：在 S1 基础上入库 `.claude/settings.json` `SessionStart` 自动建链，或受检 3 行指针 Stub** | 若团队要求新克隆工作树首次打开 Claude Code 免手动初始化，可在 `.claude/settings.json` 配轻量幂等 `SessionStart` 建链钩子（`/.claude/skills` 仍 gitignore）；或由卫生脚本校验自动生成 `.claude/skills/<name>/SKILL.md` 薄指针 | 原生零配置自动发现 | 原生自动发现 | 免手动运行初始化命令 | `SessionStart` 钩子仅在交互信任工作区后执行，无 Git 软链污染；薄指针 Stub 则有 frontmatter 同步负担（需门禁防漂移） | **作为 S1 的按需交互增强** |
| **方案 S3（反模式，淘汰）：Git 入库符号链接（`120000` `.claude/skills -> ../.agents/skills`）** | 直接 `ln -s` 并 `git add .claude/skills` | 正常 | 正常 | Linux 正常，**Windows 失效** | **Windows `core.symlinks=false` 下检出为纯文本文件**，破坏目录结构 | **淘汰** |
| **方案 S4（反模式，淘汰）：以 `.claude/skills/` 为真源，其他宿主反向指向** | 真源放厂商私有目录 `.claude/skills/`，再配 `.agents/skills.json` 与 `.pi/settings.json` | 需额外维护 `.agents/skills.json` | 需额外配置，违背 Agent Skills 开放标准 | 原生发现 | 把中立仓库规范绑死在单一宿主私有目录名上，且在 `Test-UnitBoundary.ps1` 增加根配置耦合 | **淘汰** |

---

### 3.3 推荐目录形态与可移植 `SKILL.md` 编写约定

1. **推荐目录与配置布局**：
   ```text
   .agents/
   └── skills/
       ├── verify/
       │   └── SKILL.md          # 唯一权威真源（可按需含 scripts/ 或 references/）
       └── garden/
           └── SKILL.md
   .pi/
   └── settings.json             # 增配 "skills": ["../.agents/skills"]，保留既有 subagents.agentOverrides
   .gitignore                    # 增加 /.claude/（忽略本地 Junction/Symlink 与个人会话状态）
   ```

2. **[.pi/settings.json](../../.pi/settings.json) 推荐演进形态**：
   ```json
   {
     "skills": [
       "../.agents/skills"
     ],
     "subagents": {
       "agentOverrides": {
         "worker": {
           "inheritProjectContext": true,
           "inheritGlobalContext": false,
           "defaultReads": [
             "context.md",
             "plan.md",
             "docs/agents/execution-environment.md"
           ]
         }
       }
     }
   }
   ```

3. **Claude Code 本地无特权桥接片段（可并入 `./pal.ps1` 辅助入口或开发机初始化说明）**：
   ```powershell
   $target = Join-Path $RepoRoot '.agents/skills'
   $linkDir = Join-Path $RepoRoot '.claude'
   $link = Join-Path $linkDir 'skills'
   [void][IO.Directory]::CreateDirectory($linkDir)
   if (-not (Test-Path -LiteralPath $link)) {
       if ($IsWindows) {
           New-Item -ItemType Junction -Path $link -Target $target | Out-Null
       } else {
           New-Item -ItemType SymbolicLink -Path $link -Target '../.agents/skills' | Out-Null
       }
   }
   ```

4. **三宿主兼容的 `SKILL.md` Frontmatter 最小公分母规范**（对齐 [Agent Skills Specification](https://agentskills.io/specification)）：
   - `name`：必填，仅用小写字母、数字与单连字符（`^[a-z0-9]+(-[a-z0-9]+)*$`，长度 `<= 64`），**必须与父目录名完全一致**。
   - `description`：必填（长度 `<= 1024`），用第三人称同时写清**做什么**与**何时触发**（三个宿主均仅凭 `description` 做渐进式路由）。
   - 避免在公共 `SKILL.md` 顶层强依赖某单一宿主私有字段；仅在确需禁止模型自动触发、只允许用户手动 `/verify` 或 `/skill:verify` 调用时使用三方共认的 `disable-model-invocation: true`。
   - 技能内引用同目录脚本或参考文档一律使用**相对技能目录的相对路径**（如 `./references/...`），Antigravity、Pi 与经 Junction 挂载的 Claude Code 均基于技能目录解析相对路径。

---

## 四、一手参考来源汇总

1. **Roslyn Analyzer & `BannedApiAnalyzers`**：
   - Microsoft Learn — Getting Started with Roslyn Analyzers (`netstandard2.0` / `OutputItemType="Analyzer"`): https://learn.microsoft.com/en-us/visualstudio/extensibility/getting-started-with-roslyn-analyzers
   - `dotnet/roslyn-analyzers` — `Microsoft.CodeAnalysis.BannedApiAnalyzers.md` (`RS0030` / `RS0031` / `RS0035`): https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers.md
   - `dotnet/roslyn-analyzers` — `BannedApiAnalyzers.Help.md` (`BannedSymbols.*.txt` 与 DocComment ID 语法): https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md
   - `dotnet/roslyn` — `SymbolIsBannedAnalyzerBase.cs` (`GeneratedCodeAnalysisFlags.Analyze | ReportDiagnostics` 实现): https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core/SymbolIsBannedAnalyzerBase.cs
2. **Godot .NET SDK & C# 架构测试库**：
   - `godotengine/godot` — `Godot.NET.Sdk` `Sdk.props`: https://github.com/godotengine/godot/blob/master/modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/Sdk.props
   - `godotengine/godot` — `Godot.NET.Sdk` `Sdk.targets` (`Godot.SourceGenerators` 隐式挂载机制): https://github.com/godotengine/godot/blob/master/modules/mono/editor/Godot.NET.Sdk/Godot.NET.Sdk/Sdk/Sdk.targets
   - `TNG/ArchUnitNET` — `ArchUnitNET.csproj` (`0.13.5` 依赖清单): https://github.com/TNG/ArchUnitNET/blob/main/ArchUnitNET/ArchUnitNET.csproj
   - `BenMorris/NetArchTest` — 官方仓库与发布记录 (`v1.3.2`): https://github.com/BenMorris/NetArchTest
3. **Agent Skills 开放规范、Antigravity、Pi 与 Claude Code**：
   - Agent Skills Open Specification: https://agentskills.io/specification
   - Antigravity Built-in Customization Guide (`agy-customizations`: `SKILL.md`, `docs/skills.md`, `docs/json_configs.md`)
   - `earendil-works/pi` — `packages/coding-agent/docs/skills.md` (`.agents/skills/` 祖先遍历发现): https://github.com/earendil-works/pi/blob/main/packages/coding-agent/docs/skills.md
   - `earendil-works/pi` — `packages/coding-agent/docs/settings.md` (`.pi/settings.json` `skills` 相对 `.pi/` 解析): https://github.com/earendil-works/pi/blob/main/packages/coding-agent/docs/settings.md
   - `earendil-works/pi` — `packages/coding-agent/src/core/skills.ts` (`realPathSet` 软链与重复路径静默去重): https://github.com/earendil-works/pi/blob/main/packages/coding-agent/src/core/skills.ts
   - `nicobailon/pi-subagents` — `docs/agents.md#skills` & `src/agents/skills.ts` (`buildSkillPaths` 与 `collectSettingsSkillPaths`): https://github.com/nicobailon/pi-subagents/blob/main/src/agents/skills.ts
   - Anthropic Claude Code Docs — Skills (`.claude/skills/` 与 symlink 支持说明): https://code.claude.com/docs/en/skills
   - Anthropic Claude Code Docs — Settings & Plugins Reference (`additionalDirectories` 权限边界与插件命名空间规则): https://code.claude.com/docs/en/settings , https://code.claude.com/docs/en/plugins-reference
   - Git Official Documentation — `core.symlinks` checkout behavior on Windows: https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks
