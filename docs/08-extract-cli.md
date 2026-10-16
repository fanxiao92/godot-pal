# 08 · 离线提取工具 CLI 入口（M0 / Issue #4）

任务：[Issue #4](https://github.com/fanxiao92/godot-pal/issues/4)。

状态：CLI 入口及真实进程测试已通过。**当前只提供帮助与参数错误处理，不具备资源提取能力。** M0 仍需 #5 的 Windows CI 与集成验收。

## 范围与依赖

- `tools/Pal.Extract/` 是独立 C#/.NET 控制台项目，目标框架 `net10.0`，沿用根目录 SDK `10.0.402` / `latestPatch` 约定。
- 不引用核心或游戏项目，不依赖 Godot，不读取原版数据，不实现 MKF 解析、解压或资源转换。
- `tests/Pal.Extract.Tests/` 使用与核心测试相同的固定版本 xUnit/Test SDK，验证真实 CLI 进程的 stdout、stderr、退出码。
- 工具与测试已加入 `GodotPal.sln`；也可以单独构建和测试，无需安装或启动 Godot。

## 行为约定

| 调用 | stdout | stderr | 退出码 |
|---|---|---|---:|
| `--help` | 用途、使用方式、尚未实现提取的说明 | 空 | 0 |
| 无参数 | 与帮助相同 | 空 | 0 |
| 未知参数 | 空 | 错误参数及 `--help` 提示 | 2 |
| `--help` 混入其他参数 | 空 | 错误参数及 `--help` 提示 | 2 |

只接受无参数或单个 `--help`；不将其他参数静默忽略，不将 M0 帮助成功误报为提取成功。

## 独立复现

从仓库根目录执行，环境需具备兼容的 .NET SDK；这些命令不需要 Godot：

```powershell
dotnet restore tools/Pal.Extract/Pal.Extract.csproj
dotnet build tools/Pal.Extract/Pal.Extract.csproj --no-restore
dotnet run --project tools/Pal.Extract --no-build -- --help
# PowerShell: $LASTEXITCODE 应为 0

dotnet run --project tools/Pal.Extract --no-build -- --unknown
# PowerShell: $LASTEXITCODE 应为 2，这是预期错误示例

dotnet test tests/Pal.Extract.Tests/Pal.Extract.Tests.csproj
```

也可直接启动编译结果，避免 `dotnet run` 的构建行为参与进程结果：

```powershell
dotnet tools/Pal.Extract/bin/Debug/net10.0/Pal.Extract.dll --help
```

CLI 的目标框架已固定；若以后修改构建输出布局，须同步调整测试工程的 CLI 产物复制约定。

## 测试方式与实际验证

测试工程仅通过构建依赖确保 CLI 已生成，不引用其程序集 API。构建后将 CLI 的 DLL、运行时配置等 `Pal.Extract.*` 文件复制到测试输出下的 `cli/`，测试通过 .NET host 启动该 DLL。

每次进程运行都创建全新、空的临时工作目录，没有原版数据；测试不调用内部参数解析函数，不运行 Godot。stdout 与 stderr 并行读取，15 秒内未结束则终止子进程树并让测试失败，最后清理临时工作目录。

红→绿记录：
1. CLI 起初只返回 0、不输出内容，帮助测试因缺少说明失败；加入帮助说明后通过。
2. CLI 起初对未知参数也返回 0，错误参数测试失败；加入参数检查、stderr 说明及退出码 2 后通过。
3. 完整 CLI 测试覆盖显式帮助、无参数帮助、未知参数及混合参数，共 4 个用例。

本地实际执行独立还原、构建和上述进程检查；全 solution 构建及测试也通过（核心 1 个、CLI 4 个）。这不代表无缓存网络还原或 CI 已通过，后者由 #5 验证。

构建和测试产物继续由仓库已有 `bin/`、`obj/`、`TestResults/` 忽略规则排除。
