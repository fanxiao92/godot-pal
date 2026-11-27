# ADR-0006：游戏测试拆出 `tests/Pal.Game.Tests` 并切回官方 GdUnit4 包

> Status: Accepted（2026-11-26）
> 关联规格：[M7-1 #250](https://github.com/fanxiao92/godot-pal/issues/250)

## 背景

M2–M6 期间，Godot 集成测试直接存放在生产工程 `src/Pal.Game/Tests/` 中，并依赖仓库私有的固定源码构建 GdUnit4 包（`scripts/Get-GdUnitPackages.ps1` + `gdunit-packages.lock.json` + `NuGet.Config` fork 映射）。该布局带来四项长期摩擦：

1. **生产与测试耦合**：`src/Pal.Game.csproj` 被迫引用 `gdUnit4.api`、`gdUnit4.test.adapter` 与 `Microsoft.NET.Test.Sdk`，生产代码目录混入测试夹具、测试场景与 92 KB 的 `src/Pal.Game/Tests/README.md`。
2. **自定义包维护成本高**：新环境与 Linux CI 需额外 bootstrap 私有包，且无法直接享受上游 `gdUnit4Net` 对 Godot 4.7 与 Linux headless 的修复。
3. **无法安全使用 `InternalsVisibleTo`**：Godot C# 源生成器在友元程序集场景下会触发 `CS0436` 类型冲突。
4. **测试配置与产物污染生产工程**：`.godot` 导入缓存与人工测试夹具 `.uid` 文件容易污染 `src/Pal.Game`。

## 决策

1. **独立测试工程 `tests/Pal.Game.Tests/`**：将全部游戏集成测试、测试场景、`game.runsettings` 与测试辅助迁出 `src/Pal.Game`，成立独立 Godot .NET 测试工程 `tests/Pal.Game.Tests/Pal.Game.Tests.csproj`，由 `GameProjectMount` 在测试期挂载生产工程资源与脚本。
2. **纯 `Pal.Game` 生产工程，禁止 `InternalsVisibleTo`**：`src/Pal.Game` 保持零测试依赖、零测试模式开关；需要被测试观测的接缝显式公开为 `public`，不使用 `InternalsVisibleTo`。
3. **切回 nuget.org 官方 GdUnit4 包**：`tests/Pal.Game.Tests` 直接消费 nuget.org 官方 `gdUnit4.api` 与 `gdUnit4.test.adapter`（遵循 `gdUnit4Net` 上游默认 `SubViewport` 与 `ISceneRunner` 实践），彻底退役私有 fork 映射、`Get-GdUnitPackages.ps1`、`gdunit-packages.lock.json` 与 `src/Pal.Game/Tests/README.md`。
4. **按功能区 `[TestCategory]` 分组与 CI 分区**：游戏测试按功能区标记 `[TestCategory]`（如 `Profiles`、`Prepare`、`Smoke`、`E2E` 等），支持 T0 增量按类别收窄；`pal.ps1 ci game` 排除 `RealAssets` 与 `E2E`，并在需要时通过 `--blame-hang` 诊断挂死。

## 代价与边界

- 拆出独立工程后，测试侧直接调用 `PrepareStoryPackage.Prepare` 或独立 `.tscn` 观察器时需通过 `GameProjectMount` 挂载/暂存生产样例（规则记录于根 `CODING_STANDARDS.md`）。
- 依赖版本升级仍受授权边界约束，不因切回官方包而自动升级或跨项目统一测试框架版本。
