# Coding Standards

Review-time standards for `/code-review` (Standards axis). Deterministic checks belong in `scripts/Test-UnitBoundary.ps1`, `scripts/Test-RepoHygiene.ps1`, and CI — this file holds judgement-call rules that no linter enforces.

## Production vs. Test Boundary

- **Pure production code in `src/`**: Keep `src/Pal.Core` and `src/Pal.Game` free of test-only static switches, path fallback hooks, or test-mode flags.
- **Test-owned staging in `tests/`**: When tests need temporary copies of samples or fixtures, stage them under `tests/` inside a scoped `IDisposable` that cleans up deterministically and throws (`AggregateException`) on cleanup failure rather than silently swallowing errors with empty `catch { }`.
- **Explicit public seams over `InternalsVisibleTo` in `Pal.Game`**: Do not add `InternalsVisibleTo` to `src/Pal.Game/Pal.Game.csproj` (avoids Godot source-generator `CS0436` conflicts). Expose required test seams explicitly as `public`.

## Architecture & Testing

- **Pure .NET Core**: Keep `src/Pal.Core` free of Godot types, rendering, and platform I/O. Verify exact business timing through Core's deterministic explicit-time interfaces; verify engine wiring in `tests/Pal.Game.Tests` at real production completion boundaries (`UpdateCompleted`).
- **Shared test helpers**: Extract repeated input-dispatch or project-mount boilerplate into shared helpers (`SceneRunnerInput`, `GameProjectMount`) instead of duplicating it across test suites.
- **Standalone Godot observers**: Every `.tscn` observer launched directly via `godot --path tests/Pal.Game.Tests` (outside GdUnit) must mount `src/Pal.Game` via `GameProjectMount.EnsureMounted()` before loading production scenes or resources.

## Documentation & Repo Hygiene

- **Keep pointers synchronized**: When moving or deleting files, update every referencing path in `AGENTS.md`, `docs/`, and `tools/` in the same change.
- **Isolate non-Godot fixtures**: Place `.gdignore` in any non-Godot fixture directory under a Godot project root so engine scans never emit stray `.uid` files.
