# 轻量 unit 边界检查（#32-B）

`Test-UnitBoundary.ps1` 是**只读项目/XML + C# 限定词法检查**，不是单元测试执行入口、工程检查聚合器或完整验收。它不启动 SDK、MSBuild、测试发现、CLI、Godot、GdUnit、Prepare、bootstrap 或网络；仅需 PowerShell 7。人工回归另起自有 PowerShell 进程并在自有临时目录写小型夹具，这些宿主操作不是被审查业务的 I/O。

本页只覆盖 #32-B；[CLI 迁移说明](cli-test-migration.md) 的既有构建/发现/执行证据仍独立，不能用本检查替代它们或宣称 #32 已完成。

## 公开签名与调用

```powershell
# 默认仓库根目录取脚本位置，不依赖当前 cwd；默认两个项目都检查。
pwsh -NoProfile -File scripts/Test-UnitBoundary.ps1

# 单独检查 Core；不接受任意 csproj 或自动推断影响范围。
pwsh -NoProfile -File scripts/Test-UnitBoundary.ps1 -Project core

# PowerShell 调用，多选必须是数组，不是一个含逗号的字符串。
& ./scripts/Test-UnitBoundary.ps1 -RepositoryRoot 'C:/work/godot-pal 空格' -Project @('core', 'cli-unit')

# 本命令独立的离线人工正反例回归（不是 Core/CLI unit 测试集合）。
pwsh -NoProfile -File tests/UnitBoundary.Tests.ps1
```

稳定参数：`-RepositoryRoot <string>` 默认脚本上一级；`-Project <string[]>` 默认 `@('core','cli-unit')`，允许其非空子集，不允许未知名、空名称或重复名。人工项目 seam 通过 `RepositoryRoot` 提供相同相对身份，不修改真实项目。脚本是命令而非可 dot-source 的库，需捕获数值退出码时用独立 `pwsh` 调用。

| 身份 | unit 项目 | 唯一允许的直接生产引用 |
| --- | --- | --- |
| core | `tests/Pal.Core.Tests/Pal.Core.Tests.csproj` | `src/Pal.Core/Pal.Core.csproj` |
| cli-unit | `tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj` | `tools/Pal.Extract/Pal.Extract.csproj` |

每个 unit 必须明确 `IsTestProject=true`、且恰有一个上述引用；两生产项目均不允许进一步 ProjectReference。允许生产 CLI 的 `OutputType=Exe`，**引用它不等于启动它**。不读取 solution 或扫描生产 CLI 所有 C# I/O 来否定这种合法引用。默认成功各输出一行 `PASS unit-boundary: <identity> (<n> sources inspected; ...)`；只有整个命令数值退出 **0** 表示所有选择均完成，数值退出 **1** 表示检查失败。后一个项目失败时，前一个项目的 PASS 不代表整条命令通过。

## 项目与构建检查：明确支持的窄形状

这是当前四个项目的有限声明模型，不是 MSBuild 求值器；无法识别的形状**失败并要求审查**，不猜测条件求值后的结果。XML 元素及属性必须使用下列规范大小写（例如 `Compile` / `ProjectReference` / `Include`）；非规范写法拒绝，避免与区分大小写的 XPath 选择模型不一致。

- 只接受无 XML namespace 的 `Project Sdk="Microsoft.NET.Sdk"`。项目 XML 禁止 DTD。
- `PropertyGroup` 只允许非空字面值的 `TargetFramework`、`ImplicitUsings`、`Nullable`、`IsPackable`、`IsTestProject`、`OutputType`。
- `ItemGroup` 只允许字面路径 `ProjectReference Include`、`Compile Include/Link`，以及明确的 `Microsoft.NET.Test.Sdk`、`xunit`、`xunit.runner.visualstudio` 包及字面版本；包只允许 `PrivateAssets` / `IncludeAssets` 子元素。版本不在此升级或统一，检查不替代依赖锁定/供应链验证。
- 所有自定义 `Target`（包括 `CopyCliForProcessTests`）、`Copy`、`Exec` 都拒绝，生产项目也查；不试图证明“这个 Target 当前条件下不会运行”。
- `Import`、`UsingTask`、其它 SDK/包/程序集引用、条件、动态属性/元数据表达式、部署 Content/None 项、自定义构建属性与其它未支持元素/属性都拒绝。从项目目录到指定仓库根目录的 `Directory.Build.props` / `Directory.Build.targets` / `Directory.Packages.props` 也拒绝，避免隐式导入漏查。
- 因此 integration/Prepare/Game 引用、Godot SDK/GdUnit 包和 CLI 部署构建链不能作为被支持的项目声明通过。不会依据绝对路径中 `godot-pal` 或 `NuGet.Config` 中仅列出的 `.packages/gdunit` feed 字符串误判依赖。

假设沿用仓库批准的 SDK/包和受信工作树。此命令不评估仓库外祖先配置、环境变量、命令行 MSBuild 覆盖、SDK/package 内部目标或符号链接语义；不是对恶意输入的隔离沙箱，也不证明真实构建图已执行。实际项目构建/产物证据需另外保留。

## 源码检查：覆盖与限制

扫描所选 unit 项目目录递归 `.cs`（排除路径段 `bin` / `obj`）以及显式 Compile 链接；不是只寻找 `[Fact]` 的方法体。因而同文件的构造函数、静态/字段初始化、属性类/属性标注、数据提供器、私有帮助方法都在同一检查面内。预处理条件不求值，未激活分支也保守扫描。

唯一允许的显式共享源是 `tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs`，必须带 `Link="Fixtures/Yj2TestData.cs"`；实际源内容也检查，文件缺失失败。禁止通配/动态 Compile、其它共享文件（尤其 integration 帮助器），不共享整个程序集、不建第三个夹具项目。默认源和链接源都为空也失败。

限定词法规则：

- 跳过行/块注释、普通/逐字/原始字符串和 char 字面量；原始字符串按实际引号分隔符匹配。保留行号。插值字符串保守保留内容，因此其文本中若出现禁止词也会拒绝，不宣称精确解析插值表达式。
- 拒绝代码中完整标识符 `CliProcess`、`DirectoryLinkFixture`、`MkfFixture`、`CaseSensitiveFileSystemFact[Attribute]`；也拒绝 `File`、`Directory`、`FileInfo`、`DirectoryInfo`、`FileStream`、`FileSystemWatcher`、`StreamReader`、`StreamWriter`、`Process`、`ProcessStartInfo`、`Godot`、`GdUnit4`、`DllImport`、`LibraryImport`，以及 `Path.GetTempPath(...)` / `Path.GetTempFileName(...)`。这些名字保留给已知不适合此 unit 边界的 API/帮助器；同名自定义纯类也会保守拒绝。
- 普通/全限定/空白分隔写法，以及把 `System.IO.File` 等显式别名导入的源码仍含上述标识符，能识别。构造或初始化中使用它们与测试体使用同样拒绝，发现期文件系统属性没有方法体豁免。
- `System.IO` namespace 本身、`MemoryStream`、`BinaryReader`、`BinaryWriter`、`ZLibStream` 不被禁，故 `PngWriterTests` 的内存解压/读取可通过；不是按 `Stream`、`IO`、`godot` 子串粗暴禁止。

**不提供任意间接调用无 I/O 的语义证明**：经其它程序集/包装函数、反射、delegate、动态调用、未列 API、Unicode 转义标识符、复杂/新型 C# 词法结构等可超出识别范围。没有跨程序集调用图、类型/属性继承解析或数据流分析；未知自定义属性/数据源必须人工审查定义和调用链。生产 CLI 源本身的合法文件操作不扫描；测试夹具对生产方法的调用是否纯内存仍需代码审查和实际构建/发现/运行证据互补。通过本命令不能把未来不纯的间接调用自动认定为 unit。

## 诊断与回归

失败输出 `[boundary:<code>]` 加原路径/原因，源码失败含行号及命中词。常用 code：`selection`、`project-missing`、`project-read`、`project-reference`、`project-shape`、`build-target`、`test-project`、`source-selection`、`source-forbidden`。不要把不支持形状伪称真实执行过的副作用，也不要把脚本/SDK 崩溃当作指定负例成功。

回归使用独立写出的人工 XML/C#，从非仓库 cwd 调用公开命令；临时根目录同时有中文、空格、`godot-pal`。包括：正常内存源/生产 Exe I/O/feed 允许；错误 integration/Prepare/Game 引用；直接和传递 Copy/Exec；不支持 SDK/包/导入/构建形状；禁止 helper、构造/静态/属性/provider 回流；显式链接内容/通配/缺失；未知/重复/空选择和缺失项目/空源；中文诊断；多引号原始字符串之后初始化不能被遮蔽；连续确定失败后的正常调用。所有负例要求数字退出 1 和指定诊断，清理只触及回归自建的唯一临时目录。

修改或生成 PowerShell 后先用 `System.Management.Automation.Language.Parser.ParseFile` 检查该文件，再执行。例如：

```powershell
$tokens = $null; $parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path scripts/Test-UnitBoundary.ps1), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
# 回归文件也同样预检，然后运行上面的命令。
```

本阶段实际当前项目检查：Core **11** 份源、CLI unit **6** 份源（含 Yj2 链接），两者退出 0。这里只是限定源码/项目检查，不是重新运行 328/142 测试，不报告耗时收益、完整门或远端 CI 通过。
