# 固定 GdUnit 源码包消费（#18）

本页是当前操作规则：只说明如何消费仓库锁定的固定包、何时需要 bootstrap 以及离线回归边界。来源、发布、固定包验收和历史门禁年表见[历史记录](../archive/m2/gdunit-source-acceptance-history.md)；不在本页复制历史证据或把包发布记录当作当前运行结果。

## 当前锁定与消费边界

`Pal.Game.csproj` 使用仓库锁定的 `gdUnit4.api` **5.1.0-rc6.pal.a5f20fb8.1**、`gdUnit4.test.adapter` **3.2.0-pal.a5f20fb8.1**；Godot.NET.Sdk **4.7.2**、`net10.0`、repository SDK **10.0.402**、Microsoft.NET.Test.Sdk **18.0.1** 及 Core 依赖保持当前配置。版本和文件身份以 `scripts/gdunit-packages.lock.json`、`.csproj`、`global.json` 和脚本为准，不从本页推断升级或降级。

固定锁包含来源、recipe、producer run、文件名、长度、SHA256 和 Release 地址。只消费锁定的四文件 bundle：两个 `.nupkg`、`provenance.json`、`SHA256SUMS`。真实游戏资源、私密证据和个人路径不进入包或提交。

## Bootstrap 与运行时机

从仓库根、PowerShell 7 调用。已有受核 bundle 可显式离线输入；发布后或已有验证缓存可使用默认入口：

```powershell
./scripts/Get-GdUnitPackages.ps1 -InputDirectory 'C:/path/to/accepted/artifact'
./scripts/Get-GdUnitPackages.ps1
```

每次检查四个文件的长度和散列；缺失/不完整/未知缓存拒绝并保留现场，不能静默修复或覆盖。显式输入只复制，不移动或执行包内容。`Test-M0.ps1` 在首个 SDK 命令前调用 bootstrap；普通直接 `dotnet` 流程必须先显式 bootstrap。只有获准运行 Game/完整门时才进入 SDK、Godot 或 GdUnit；本文档维护不授权运行。

仓库 NuGet 配置只把两个 GdUnit ID 映射到 `.packages/gdunit`，其余依赖按现有 wildcard 映射。源映射不认证全局缓存中的既有包；独立可复现运行使用新缓存。API 不兼容必须停下复核，不猜测迁移或修改断言。

## 离线回归

```powershell
pwsh -NoProfile -File tests/Pal.Game.Tests/GdUnitPackages.Tests.ps1 -InputDirectory 'C:/path/to/accepted/artifact'
```

该回归只验证锁定身份、映射、输入保持、错误长度/散列、残缺/陈旧 bundle、损坏缓存、固定 URL mock 和失败清理；不下载依赖，不运行 SDK、引擎、Prepare、Game 或真实 Release。离线通过不是运行时或完整门验收。

工具链异常、输出捕获、历史门禁和来源验收按[测试异常处理规则](../agents/test-diagnostics.md)及[历史年表](../archive/m2/gdunit-source-acceptance-history.md)触发查阅；不把旧失败改写为忽略项。
