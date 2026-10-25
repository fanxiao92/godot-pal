# CLI 测试物理迁移（#32-A）

本页只描述 #32 的**物理拆分与完整门接线**，不宣称 #32 或父规格 #31 全部完成。单元快入口及专门的 unit 边界检查命令属于后续阶段；没有在此新建另一套执行框架。

## 项目与源码映射

迁移基线为 `e2750d244158ea14d5e1d818bb770d1a76c1c328`。原混合项目 `tests/Pal.Extract.Tests/Pal.Extract.Tests.csproj` 已移除，不保留兼容执行入口。所有测试继续使用 `Pal.Extract.Tests` namespace，类、方法、参数、断言和清理逻辑不变。

| 原同名源文件中的测试类 | 新项目 | 实际发现案例数 |
| --- | --- | ---: |
| IndexedImageTests | Pal.Extract.UnitTests | 8 |
| PaletteDecoderTests | Pal.Extract.UnitTests | 9 |
| PngWriterTests | Pal.Extract.UnitTests | 4 |
| SpriteDecoderTests | Pal.Extract.UnitTests | 28 |
| Yj2DecoderTests | Pal.Extract.UnitTests | 93 |
| **unit 合计** | `tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj` | **142** |
| CliTests | Pal.Extract.IntegrationTests | 4 |
| DialogueLanguageCliTests | Pal.Extract.IntegrationTests | 2 |
| EventCliTests | Pal.Extract.IntegrationTests | 27 |
| ExportMapCliTests | Pal.Extract.IntegrationTests | 47 |
| ExportSceneCliTests | Pal.Extract.IntegrationTests | 38 |
| ExtractSceneCliTests | Pal.Extract.IntegrationTests | 34 |
| InspectCliTests | Pal.Extract.IntegrationTests | 38 |
| **integration 合计** | `tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj` | **190** |

- `CliProcess.cs`、`DirectoryLinkFixture.cs`、`MkfFixture.cs` 只迁入 integration。
- `CaseSensitiveFileSystemFactAttribute` 保留在 integration 的 `InspectCliTests.cs` 中；它的构造函数会创建人工临时文件，**发现阶段也有副作用**，因此不能链接到 unit。
- 纯人工 `Yj2TestData.cs` 位于 `tests/Pal.Extract.Tests/Fixtures/`，两项目各通过一个明确的 `<Compile Include="../Pal.Extract.Tests/Fixtures/Yj2TestData.cs" Link="Fixtures/Yj2TestData.cs" />` 链接。没有第三个项目、通配共享或 integration 程序集引用。

冻结清单为 [`tests/Pal.Extract.Tests/Migration/baseline.json`](../../tests/Pal.Extract.Tests/Migration/baseline.json)。它来自移动源码前的独立 discovery 和实际 TRX，而非拆分后反推；包括原项目文件及 16 份 C# 的 SHA-256、旧/新相对路径、332 个案例的 FQN、完整 xUnit 序列化参数字段、显示名、旧 VSTest case ID、xUnit unique ID、execution ID、原结果和目标项目。原 csproj 的 `after=null` 表示被两个项目取代，而不是要求项目配置字节不变。

参数字段用于区分显示名被截断成相同文本的 byte-array 案例；不能仅按显示名去重或只比较数量。真实旧基线中有 **2 个同显示名组、共 8 个案例**，均属于 `SpriteDecoderTests.DecodeFrame_rejects_malformed_or_excessive_data`。先从移动前冻结的 `InvalidSprites` 字面量提供器独立取得 byte-array，再解码旧 discovery 的完整序列化参数逐字节关联到该提供器的第 6、8、10、11、12、13、14、15 行（1-based）；随后以完整 FQN/参数连接新 discovery 与对应 TRX ID，不使用案例执行顺序或显示文本猜测。前六项显示相同的 `[2,0,0,0,1,…]` 前缀，后两项显示相同的 `[2,0,0,0,2,…]` 前缀，完整数组各不相同。迁移证据保留这 8 个源数据行、完整数组与旧/新 VSTest/xUnit ID 的专门映射。

这是固定 xUnit v2 序列化格式、当前冻结字面量和原参数提供器的迁移证明；不是通用任意数据源解析器，也不声称可仅凭这些键区分完全相同 FQN/参数的重复行。原基线此处没有这种键冲突。期望来源是旧源码与旧 discovery/TRX，不由新通过结果反推。

程序集名由 `Pal.Extract.Tests` 分别变为 `Pal.Extract.UnitTests` / `Pal.Extract.IntegrationTests`，本轮全部 332 个 VSTest 和 xUnit case ID 随之改变，不能要求旧/新 ID 相等。迁移证据另外保留每个完整方法/参数身份的旧/新 ID、execution ID 和结果映射。execution ID 是一次执行身份，不是跨运行案例身份。

## 构建图和副作用审查边界

```text
Pal.Extract.UnitTests ──ProjectReference──> Pal.Extract (Exe, net10.0)
                   └──显式源链接────────> 纯 Yj2TestData.cs

Pal.Extract.IntegrationTests ──ProjectReference──> Pal.Extract
                          ├──显式源链接────────> 同一纯 Yj2TestData.cs
                          └──Build 后 CopyCliForProcessTests──> 输出目录 cli/
```

生产 CLI 无进一步的 ProjectReference。unit 不引用 integration、Prepare 或 Game，也没有 CLI 输出部署目标。引用 Exe 程序集不等于运行其入口点；unit 测试直接调用现有的图像、调色板、PNG、Sprite、YJ2 公开 API。integration 仍运行输出目录内**已构建**的 CLI，不改成 `dotnet run`。

本次同时核查：

1. 五份 unit 类的隐式构造、字段/静态初始化、所有属性和数据提供器。属性只有 xUnit 的 Fact / Theory / InlineData / MemberData；`InvalidSprites` 只生成内存 byte-array 集合，`PositionHighCodes` 只生成 `Enumerable.Range(0, 64)`；没有文件系统属性、进程帮助器或初始化钩子。
2. 共享 Yj2 的构造、内部树、列表和编码操作均在内存中；没有调用被测解码器或文件/进程操作。
3. unit 实际评估的 Compile 集合恰为五份测试源码和一个显式 Yj2 链接；ProjectReference 恰为生产 CLI，生产项目没有传递项目引用。
4. 实际 unit diagnostic build / 解析后的依赖资产中没有 integration、Prepare、Game、Godot SDK 或 GdUnit 依赖，也没有 `CopyCliForProcessTests` 目标；unit 输出无 `cli/`，integration 输出有已构建 CLI。

仓库 `NuGet.Config` 声明 `.packages/gdunit` 源，普通 unit restore 的 diagnostic 文本也会列出此源；**源名称出现不等于消费固定包或启动 bootstrap**。本轮没有该固定包目录也能完成显式 unit 构建、发现和执行。unit discovery/test 的 `GODOT_BIN`、`PAL_TEST_GODOT` 已清空。构建、测试宿主和日志的正常文件/进程操作不在“业务及夹具纯内存”的禁令内。

这些是当前明确源码、求值项目项、构建诊断和实际执行的互补证据，不是对任意未来间接调用都无 I/O 的语义证明。本阶段没有实现专门 unit 边界扫描命令；后续轻量规则须单列支持的语法与局限，不能把目录名/耗时或单纯字符串搜索当成隔离证明。

## 直接验证命令

以下都是显式 CLI 项目；无需 Godot、Prepare 或 solution test。构建使用当前 SDK/依赖配置，随后 discovery/test 使用已构建产物：

```powershell
dotnet build tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj
dotnet test tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj --no-build --no-restore --list-tests
dotnet test tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj --no-build --no-restore --logger 'trx;LogFileName=cli-unit.trx' --results-directory <new-unit-results>

dotnet build tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj
dotnet test tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj --no-build --no-restore --list-tests
dotnet test tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj --no-build --no-restore --logger 'trx;LogFileName=cli-integration.trx' --results-directory <new-integration-results>

# 纯离线人工源码/XML/TRX 元数据回归，不启动 SDK/引擎/Prepare：
pwsh -NoProfile -File tests/Pal.Game.Tests/GameBatches.Tests.ps1
```

迁移前真实基线为 332 发现 / 330 Passed + 2 NotExecuted；迁移后为 unit 142 发现/142 Passed、integration 190 发现/188 Passed + 原 2 NotExecuted，各构建/发现/执行命令退出 0。16 份 C# 整文件字节相等，涵盖全部 609 个 `Assert.` 调用、所有参数数据及清理。此处的 Windows 大小写不敏感文件系统只保留原两项条件跳过：

- `Pal.Extract.Tests.ExportMapCliTests.Export_map_rejects_case_ambiguous_archives`
- `Pal.Extract.Tests.InspectCliTests.Inspect_rejects_ambiguous_case_insensitive_names`

这证明集合隔离及当前迁移保持覆盖，不证明同集合性能提速、完整门通过或概率稳定性。

## 唯一完整门接线与后续 API

`Get-GameBatchPlan <repository>` 的 `projects` 从路径数组变为按执行顺序排列的对象数组，每项为 `{ name, path, passed, skipped }`：

| name（同时是非 Game TRX basename） | 项目 | passed / skipped |
| --- | --- | --- |
| core | tests/Pal.Core.Tests/Pal.Core.Tests.csproj | 328 / 0 |
| cli-unit | tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj | 142 / 0 |
| cli-integration | tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj | 188 / 2 |
| prepare | tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj | 52 / 0 |
| game | src/Pal.Game/Pal.Game.csproj | 242 / 0（按原批次执行） |

`Test-M0.ps1` 消费明确身份，不再按 `$gamePlan.projects[0..2]` 重新绑定路径。Prepare 仍先于任何 Game adapter runner；Game 的明确项目身份也用于分批、discovery 和控制。solution 仅同步两个独立项目及配置，没有执行 solution test。

完整门仍只有 `scripts/Test-M0.ps1`：空缓存 restore/build、Core、两个 CLI、Prepare、Game 三轮串行 66/79/97、245 项精确 discovery、三个指定失败控制及其准确诊断、启动检查、退出/日志/TRX/身份严格核验均保持。预期完整 TRX 为 **16**：4 个非 Game 项目结果 + 9 个 Game batch 结果 + 3 个控制结果。没有修改 skip 白名单或时限。

离线回归仍为 12 组，并新增准确项目身份/路径/结果绑定与 Prepare-before-Game 顺序断言；逐一遗漏/重复五个测试项目、增加未知项目、同数量换入未知项目、移除 unit 的 IsTestProject 身份均必须以指定身份诊断失败。原 Game 分区、discovery、TRX graph、重复 ID、异常 RunInfo 和 round 完整性负例继续运行；不是只把 4 改成 5 或 15 改成 16。

**待后续执行而非本页通过证据**：#32-B 的单元快入口/专门边界命令、独立审查、主代理全局串行运行真实完整门。此迁移阶段未运行 Godot、GdUnit、Prepare、solution test、bootstrap 或完整门；headless/GPU/人工验收仍不可互代。
