# M2 #12：GdUnit4Net 隔离兼容性关卡

## 结论与边界

**PASS：本机 Windows x64、headless、公开 CLI 路线的有界兼容性验证通过。** 本次查询与实验始于 `2026-10-19T17:11:25Z`。只验证测试框架，没有实现 M2 游戏或向正式工程添加依赖；Core xUnit 保持不变。

本机 Windows `10.0.26200`、PowerShell `7.6.6`。未执行远端 `windows-2022` CI、图形/桌面验收、物理键盘/OS repeat/失焦、导出包或其他平台；不得外推为这些环境通过。冷启动指全新项目 `.godot` 缓存及 NuGet 包/HTTP 缓存，不包括全新 Windows 用户或引擎全局编辑器缓存。

## 固定组合及一手来源

| 项目 | 实测版本 |
|---|---|
| Godot .NET | `4.7.2.stable.mono.official.ed1daf0bf` |
| Godot.NET.Sdk / GodotSharp / GodotSharpEditor / SourceGenerators | `4.7.2` |
| TFM / SDK / runtime | `net10.0` / `10.0.402`（`latestPatch`、禁止 prerelease）/ `10.0.13` |
| gdUnit4.api | `5.1.0-rc5` |
| gdUnit4.test.adapter | `3.1.1` |
| Microsoft.NET.Test.Sdk / TestPlatform | `18.0.1` |

Godot executable 为 `C:/Users/xiaofan/scoop/apps/godot-mono/4.7.2/godot-mono.console.exe`，SHA256 为 `2445d009a5e0474fc7064b9767100e9d2c09521890ac5625476bfb81cf03f2d4`。可选 GdUnit analyzers 未引入。

- 公开版本查询：[API 索引](https://api.nuget.org/v3-flatcontainer/gdunit4.api/index.json)、[adapter 索引](https://api.nuget.org/v3-flatcontainer/gdunit4.test.adapter/index.json)、[Test.Sdk 索引](https://api.nuget.org/v3-flatcontainer/microsoft.net.test.sdk/index.json)。相应固定版本的 nupkg 均成功下载。
- [master README](https://github.com/godot-gdunit-labs/gdUnit4Net/blob/a5f20fb8c12864b17c77d433cad14f9bf0094458/README.md) 宣称 rc6 / adapter 3.2.0 与 .NET 10 / Godot 4.7.2，但查询时索引未列出这些框架版本，直接下载 API rc6 返回 HTTP 404。不能把 master 声明作为已发布组合。
- API rc5 的官方定义/示例取自其 nupkg nuspec 标明的提交 `d4ac9e9bf61f0a958d10d392ffeb5594a3fd620f`：[API README](https://github.com/godot-gdunit-labs/gdUnit4Net/blob/d4ac9e9bf61f0a958d10d392ffeb5594a3fd620f/Api/README.md)、[ISceneRunner](https://github.com/godot-gdunit-labs/gdUnit4Net/blob/d4ac9e9bf61f0a958d10d392ffeb5594a3fd620f/Api/src/ISceneRunner.cs)、[输入示例](https://github.com/godot-gdunit-labs/gdUnit4Net/blob/d4ac9e9bf61f0a958d10d392ffeb5594a3fd620f/Api.Test/src/core/SceneRunnerInputEventIntegrationTest.cs)。adapter 3.1.1 的 nuspec 提交为 `73160d4547b766b99c9dbc0f6421a5d446c1735d`；Test.Sdk nuspec 指向 vstest 提交 `ef89ad17b5252e082698557246e61c98bef7a5d5`。
- rc5 包只有 net8/net9 资产，声明 GodotSharp 最低依赖 4.4.0；这既不等于支持 net10，也不等于不能运行。**本次只测试这一组公开候选**，实际还原选择 GodotSharp 4.7.2、TestPlatform 18.0.1；最终冷构建 0 warnings / 0 errors。没有降基线、显式覆盖 GodotSharp、压制冲突或源码构建框架。

## 实际结果

样例采用真实场景及输入，不使用 mock；测试通过公开 VSTest CLI 发现和执行，无另装的编辑器插件。adapter 自行部署测试运行脚本到隔离工程，不接入正式游戏。

| 检查 | 退出码 | 发现数 / 结果 |
|---|---:|---|
| 全新缓存 restore / build / Godot import | `0 / 0 / 0` | 最终构建无警告或错误 |
| `dotnet test --list-tests` | `0` | **6** 个：3 个正例、3 个故意失败控制 |
| 正例冷启动 | `0` | **3 passed / 0 failed / 0 skipped** |
| 正例重复两次 | `0 / 0` | 每次 **3 passed / 0 failed / 0 skipped** |
| 故意错误断言 | `1` | 1 failed；11 ≠ 10，定位 `FailureControlTests.cs:10` |
| 实际 `_Ready` 抛异常 | `1` | 1 failed；异常标记及 `ExceptionScene.cs:5`；启用 `[GodotExceptionMonitor]` |
| 用例超时 | `1` | 1 failed；500ms 超时诊断，不挂起 |
| 外层进程 watchdog | kill 后 `-1` | headless editor 达 3 秒超时，进程树终止并等待退出 |
| 控制组及重复组前后进程快照 | — | 无 Godot/testhost；无新增残留 |

正例覆盖：

1. 纯逻辑用例被 CLI 发现并执行。
2. 真实 `.tscn` 入树，`IsInsideTree`、`IsNodeReady`、`_Ready` 标记与实际递增的 `_Process` 计数。
3. `ISceneRunner.SimulateActionPress/Release("ui_right")` 的真实 action 路径：场景 `_Input` 的按下/释放计数、`_Process` 的保持计数、跨帧保持且无重复按下、释放后停止、dispose 清理故意未释放的 action；同用例新场景观察不到残留。**仅证明该 action 注入路径**，不承诺所有按键或输入 API 等价。

正例 TRX 检查实际计数、零跳过及无 ERROR / SCRIPT ERROR / unhandled exception / WARNING。负例报告保留错误，没有压制。默认 120 秒外层超时（restore 240 秒），runsettings 为单 worker、60 秒 session、30 秒编译、10 秒连接、headless/verbose、stdout 与 TRX。故意失败的测试必须分别过滤执行，不能把整套 6 个测试全跑后期待成功。

## 复现及外部证据

所有样例、下载、项目/包缓存及原始证据保存在仓库外：

```text
C:/Users/xiaofan/.pi/agent/task-context/godot-pal-m2/gdunit-probe/
  README.md                     # 完整复制/复现步骤与限制
  sample/                       # csproj、global.json、NuGet.Config、runsettings、人工场景/用例
  Run-Probe.ps1                 # 增量逻辑/场景/输入切片
  Verify-Gate.ps1               # 故意失败、超时、冷启动/重复及计数/日志/进程检查
  sources/                      # 官方索引、固定 nupkg、nuspec、定义与示例
  resolved-packages.json         # 实际解析的依赖版本
  evidence-files.sha256.json    # 样例/脚本/来源/最终证据散列
  runs/final/logs/              # 每个命令的 .log、.result.json 及进程快照
  runs/final/results/           # 正例/负例 TRX
```

复制源文件和两个脚本到**新的仓库外目录**（不复制缓存、生成的 adapter 或旧 results），保持版本并更新 Godot 路径后执行：

```powershell
pwsh -NoProfile -File ./Run-Probe.ps1 -Phase Logic
pwsh -NoProfile -File ./Run-Probe.ps1 -Phase Scene
pwsh -NoProfile -File ./Run-Probe.ps1 -Phase Input
pwsh -NoProfile -File ./Verify-Gate.ps1 -Phase Controls -RunName final
pwsh -NoProfile -File ./Verify-Gate.ps1 -Phase ColdRepeat -RunName final
```

完整展开命令、退出码、超时和 TRX 见外部证据；固定样例包 `repro-source.zip` 也保存在该目录。未提交临时样例、框架包、原版数据或缓存，未改 Issue、推送或创建 PR。正式接入及 M2 后继功能仍属后续任务；本结论需独立审查，不替代后继功能验收。
