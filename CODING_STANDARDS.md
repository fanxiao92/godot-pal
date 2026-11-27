# Coding Standards

Review-time standards for `/code-review` (Standards axis). Deterministic checks belong in `scripts/Test-UnitBoundary.ps1`, `scripts/Test-RepoHygiene.ps1`, and CI — this file holds judgement-call rules that no linter enforces.

## Production vs. Test Boundary

- **Pure production code in `src/`**: Keep `src/Pal.Core` and `src/Pal.Game` free of test-only static switches, path fallback hooks, or test-mode flags.
- **Test-owned staging in `tests/`**: When tests need temporary copies of samples or fixtures, stage them under `tests/` inside a scoped `IDisposable` that cleans up deterministically and throws (`AggregateException`) on cleanup failure rather than silently swallowing errors with empty `catch { }`.
- **Explicit public seams over `InternalsVisibleTo` in `Pal.Game`**: Do not add `InternalsVisibleTo` to `src/Pal.Game/Pal.Game.csproj` (avoids Godot source-generator `CS0436` conflicts). Expose required test seams explicitly as `public`.

## Architecture & Testing

- **Pure .NET Core**: Keep `src/Pal.Core` free of Godot types, rendering, and platform I/O. Verify exact business timing through Core's deterministic explicit-time interfaces; verify engine wiring in `tests/Pal.Game.Tests` at real production completion boundaries (`UpdateCompleted`).
- **Shared test helpers**: Extract repeated input-dispatch, link-cleanup, or project-mount boilerplate into shared helpers (`SceneRunnerInput`, `GameProjectMount`, `GameProjectMount.DeleteDirectoryLink`) instead of duplicating it across test suites.
- **Cross-project sample staging**: Any test in `tests/Pal.Game.Tests` calling `PrepareStoryPackage.Prepare` directly must wrap the call in `using (GameProjectMount.StageSamplesInTestProject())` so `res://Samples/AuthoredBattle` resolves on clean Linux/CI runners.
- **Cross-platform filesystem assertions**: Never assume Windows-only `FileAttributes` bit combinations (e.g., `Normal | ReadOnly` normalizes to `ReadOnly` on Linux), Windows-only `FileShare.Read` writer conflicts (Linux `flock` requires `FileShare.None` for exclusive writer locks), or `Directory.Delete` on symlinks (`File.Delete` on non-Windows via `GameProjectMount.DeleteDirectoryLink`).
- **Bounded diagnostic logging in Godot tests**: Print counts, lengths, or SHA256 digests in `GD.Print(...)` — never serialize full raw `byte[]` file dictionaries to stdout.
- **Standalone Godot observers**: Every `.tscn` observer launched directly via `godot --path tests/Pal.Game.Tests` (outside GdUnit) must mount `src/Pal.Game` via `GameProjectMount.EnsureMounted()` before loading production scenes or resources.

## Documentation & Repo Hygiene

- **Keep pointers synchronized**: When moving or deleting files, update every referencing path in `AGENTS.md`, `docs/`, and `tools/` in the same change.
- **Isolate non-Godot fixtures**: Place `.gdignore` in any non-Godot fixture directory under a Godot project root so engine scans never emit stray `.uid` files.

## PowerShell Front-Door & Script Contracts

- **Uniform error persistence**: Every `pal.ps1` command and early-return error path (including argument parsing and `report`) must write `errorMsg` to stderr and persist `run-summary.json` with `status: "error"|"failed"` and exit `1`.
- **Verify TRX outcome alongside exit code**: Test runner wrappers must check both process exit code and TRX `outcome`/`failed` counts; exit `0` alone is not sufficient proof of a completed passing run.
- **Match wrapped script signatures**: When wrapping existing `scripts/*.ps1` entry points (e.g., `assets prepare`), verify and forward every required parameter of each target script rather than assuming a uniform flag set.
