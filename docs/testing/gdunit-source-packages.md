# Pinned GdUnit source packages (#18)

Only the Game GdUnit API and adapter change. `Pal.Game.csproj` uses exact NuGet
ranges for `gdUnit4.api` **5.1.0-rc6.pal.a5f20fb8.1** and
`gdUnit4.test.adapter` **3.2.0-pal.a5f20fb8.1**. Godot.NET.Sdk **4.7.2**,
`net10.0`, repository SDK **10.0.402**, Microsoft.NET.Test.Sdk **18.0.1**,
and Core dependencies remain unchanged. The packages' GodotSharp 4.7.0 minimum
does not authorize downgrading the project's direct SDK baseline.

## Current delivery status (2026-10-22)

The consumer and focus fixes were integrated at `04f8e91`; the accepted M2
closeout documents were committed at `e1bc730`. M2 is now complete: #11/#17/#18
and the subsequent cross-drive test-fixture bug #19 are closed, and the accepted
delivery commit `a76f83b` is published on `main` via SSH. The separate full
[Windows Game CI run 39738074108](https://github.com/fanxiao92/godot-pal/actions/runs/39738074108)
passed using synthetic resources and the same pinned packages. The first remote
CI failed during fixture setup; that failure remains preserved, not reclassified.
See the [M2 layered acceptance record](../milestones/m2/acceptance.md) for the
local gate, actual operator acceptance, main delivery, #19 regression and remote
CI evidence. Audio, exports and other platforms remain unvalidated; passing gates
do not prove the exact historical stdout-hook cause or zero recurrence risk.

The publication barrier below records the earlier package-release checkpoint,
not today's integration or M2 status. No package identity changed after release.

## Immutable identity and publication barrier

`scripts/gdunit-packages.lock.json` is the checked-in consumption lock: source,
recipe, producer run, exact filenames, lengths, SHA256 values and fixed Release
location. The hashes bind both packages, `provenance.json`, and `SHA256SUMS`;
checksums downloaded alongside the packages are not the trust anchor.

- Source: `godot-gdunit-labs/gdUnit4Net@a5f20fb8c12864b17c77d433cad14f9bf0094458`
  (upstream #527).
- Pack recipe: `fanxiao92/gdUnit4Net@d6d2aea6812debce526aeb3b03b6094edb82a608`.
- Actual successful pack-only run: [39608085368, attempt 1](https://github.com/fanxiao92/gdUnit4Net/actions/runs/39608085368/attempts/1).
  This was not a Game test run or remote game CI acceptance.
- Temporary Actions artifact: `12348323804`, expires **2026-11-04T13:27:50Z**.
- Published fixed prerelease: [pal-gdunit-a5f20fb8-1](https://github.com/fanxiao92/gdUnit4Net/releases/tag/pal-gdunit-a5f20fb8-1)
  in `fanxiao92/gdUnit4Net`, published **2026-10-21T15:47:10Z**.
  Base URL: `https://github.com/fanxiao92/gdUnit4Net/releases/download/pal-gdunit-a5f20fb8-1/`.

**Publication used the same accepted Actions artifact bytes, not a rebuild.**
Independent consumer, preparation and raw-result reviews passed their respective
scopes. After accepting the complete local gate below, the parent uploaded the
four original files, downloaded and verified the draft assets, then published the
prerelease. All four unauthenticated fixed download URLs returned HTTP200 with
lengths and SHA256 values matching the checked-in lock. The tag targets the fixed
recipe commit above. No `latest`, overwritten version, official-source fallback,
public NuGet push or binary commit is used.

### Accepted local automatic gate and remaining limits

On **2026-10-21T15:27:30Z–15:33:44Z**, the original complete Windows headless gate
passed on HEAD `98b138ee67086cdcac760648fb99c635eb66051c` plus the reviewed
focus-test and consumer patches (full candidate archive SHA256
`f7faeea98d84072b177fd1f1da22c35ffba32d473bbf0fb2821b7a1039a46812`).
It produced all **9 TRX / 10 primary dotnet command records**: Core129,
CLI300 plus the original two filesystem skips, Prepare52, solution Game170,
standalone Game170 and the original repeat170; discovery173 included the three
controls. All three controls failed for their exact intended reasons, and the
original native prepare/profile/import/Main-startup chain passed. Outer exit0,
PASS and an empty protection diff were independently reviewed; the parent also
verified all105 runtime evidence file hashes. The existing `.godot` cache was
retained; NuGet/HTTP caches were new and evidence-local.

Four historical synthetic crash leftovers were explicitly approved for recoverable
archival outside the Godot project before this run. The previous gate had failed
because Godot scanned a preserved zero-byte scene; that failure remains failed.
The archive's original bytes, mtime and creation-time history remained protected,
with access-time effects of hash reads recorded honestly. No Prepare error checks,
Game assertions, timeouts, capture behavior, repeat rounds or controls were relaxed.

This local gate alone did not prove zero-probability stdout-hook failure, remote
Game CI, manual resize/GPU/audio/export/other-platform acceptance, primary-branch
integration or whole-M2 completion. Package graphs, evidence-cache hashes and
copied DLLs establish file-level identity; in-process loading paths/hashes were
not directly probed. At that publication checkpoint the candidate was still
uncommitted and unmerged, with the original user documentation preserved.
Subsequent integration and acceptance are recorded above, not inferred from the
pack-only Actions run or retroactively assigned to this local gate.

## Bootstrap before restore

Run from the repository root with PowerShell 7. To use the accepted four-file
Actions bundle as an explicit offline input instead of downloading it:

```powershell
./scripts/Get-GdUnitPackages.ps1 -InputDirectory 'C:/path/to/accepted/artifact'
```

This copies, never moves or changes, the input files. The input must contain
exactly the two locked `.nupkg` files, `provenance.json`, and `SHA256SUMS`.
No machine-specific artifact path is embedded in the code. No package content
is loaded or executed by bootstrap.

After publication, or with an already verified local bundle:

```powershell
./scripts/Get-GdUnitPackages.ps1
```

The result is the absolute path to ignored `.packages/gdunit/`. Each call checks
all four lengths and hashes. Explicit input is checked even if the cache exists.
A missing cache is staged in a fresh sibling directory, verified completely, then
renamed atomically into place. Downloads use only the fixed locked Release URLs.
A bad/incomplete/unknown existing cache is rejected, never overwritten or silently
repaired; partial download failures do not expose a ready source. Inspect and
preserve unexpected files before any separately authorized manual removal.
Bootstrap does not change user/global NuGet configuration, credentials or caches.

Root `NuGet.Config` clears inherited sources/mappings. Exact-ID source mappings
route only these two GdUnit IDs to `.packages/gdunit`; all other dependencies use
nuget.org's wildcard mapping (NuGet prefers exact IDs over `*`). There is no
official-source fallback for the GdUnit IDs. Source mappings do not validate
packages already in a NuGet global cache; the shared gate retains its isolated
empty `NUGET_PACKAGES` cache. For independent reproducibility use a fresh cache,
not a replacement of an existing global package identity.

`scripts/Test-M0.ps1` calls bootstrap once before its first SDK command and
restore. The existing CI workflow already calls that shared entry, so it needs
no separate bootstrap step or trigger changes. The accepted local gate used the
verified preseeded source before publication. Ordinary direct `dotnet` workflows
bypass the shared entry and therefore require explicit bootstrap first. These
examples are usage instructions, not authorization to execute runtime checks:

```powershell
./scripts/Get-GdUnitPackages.ps1
# Only when separately authorized:
dotnet restore GodotPal.sln
./scripts/Test-M0.ps1 -GodotPath 'C:/path/to/Godot_console.exe' -EvidenceDirectory 'C:/new/gate-evidence'
```

No assertions, timeout budgets, discovery counts, repeat rounds, expected failure
controls, process diagnostics or startup checks are relaxed by this seam.
API compile incompatibility must stop for review, not trigger guessed test or
game behavior migration.

## Offline regression (no SDK, engine, CIM or real network)

```powershell
pwsh -NoProfile -File tests/Pal.Game.Tests/GdUnitPackages.Tests.ps1 -InputDirectory 'C:/path/to/accepted/artifact'
```

The suite copies the checked-in bootstrap/lock into isolated temporary repository
fixtures; it does not inject alternative production hashes. It checks exact
pins/mappings/entry order, accepted and cached success, input byte/mtime
preservation, wrong hash/length, incomplete/stale input, corrupt/partial existing
sources, no overwrite, fixed-URL mocked downloads, and fail-closed cleanup on
download failure/corruption. `Invoke-WebRequest` is mocked in child scopes and
restored; no dependencies are downloaded for these tests. Offline success is
not restore/build/engine/Game acceptance or a live Release download check.
