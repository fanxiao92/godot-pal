import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { hash, type Check } from './contract.ts';
import { acquire, atomicJson, json, native, NativeCheckFailure, noLinks, QuiescenceUnknownError, runCheck } from './io.ts';

export const unitProjects = ['tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj', 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj'] as const;
export const gameBatches = ['process', 'scene-dialogue', 'native-map', 'scene-realfull'] as const;
const fqnFilter = /^FullyQualifiedName[=~][A-Za-z_][\w.]*(?:\.)?$/;
export interface ExpectedFailure { test: string; message: string }
export interface EngineOptions { godotPath: string; fixedPackageDirectory: string }
export interface Selection { scope: 'unit' | 'prepare' | 'game'; project?: string; batch?: string; filter?: string; expectedFailure?: ExpectedFailure }
const here = path.dirname(fileURLToPath(import.meta.url));
export const engineLock = path.join(os.tmpdir(), 'godot-pal-long-running-engine.lock');
export function assertEngineIdle(): void {
  if (process.platform !== 'win32') throw new Error('Engine PID/Name guard is currently supported on Windows only');
  const rows = JSON.parse(native(here, 'pwsh', ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'engine-processes.ps1')])) as { Id: number; ProcessName: string }[];
  if (!Array.isArray(rows) || rows.some(row => !Number.isSafeInteger(row.Id) || typeof row.ProcessName !== 'string')) throw new Error('Engine process ownership is unknown');
  if (rows.length) throw new Error(`Existing Godot processes; confirm ownership before running: ${JSON.stringify(rows)}`);
}
export function buildCheck(cwd: string, directory: string, selection: Selection, engine?: EngineOptions): Check {
  if (selection.expectedFailure && ((!/^[A-Za-z_][\w.]+(?:[:(\[].*)?$/.test(selection.expectedFailure.test) || /[\x00-\x1f]/.test(selection.expectedFailure.test)) || !selection.expectedFailure.message.trim())) throw new Error('Expected failure needs an exact test identity/FQN and a literal message');
  if (selection.scope === 'unit') {
    if (!unitProjects.includes(selection.project as typeof unitProjects[number]) || selection.batch) throw new Error('Unit requires one whole approved project; no batch narrowing');
    const args = ['test', selection.project!, '--configuration', 'Debug', '--settings', 'tests/unit.runsettings', '--logger', 'trx;LogFileName=results.trx', '--results-directory', path.join(directory, 'trx')];
    if (selection.filter) {
      if (!fqnFilter.test(selection.filter)) throw new Error('Use one existing FQN method/class-prefix filter, not DisplayName or Boolean narrowing');
      args.push('--filter', selection.filter);
    }
    return { kind: 'non_engine', executable: 'dotnet', args, timeoutMs: 300_000 };
  }
  if (!engine || !path.isAbsolute(engine.godotPath) || !path.isAbsolute(engine.fixedPackageDirectory)) throw new Error('Prepare/Game requires explicit --engine-checks, --godot-path and --fixed-package-directory authority');
  if (selection.project || (selection.scope === 'prepare' && (selection.batch || selection.filter)) || (selection.scope === 'game' && !gameBatches.includes(selection.batch as typeof gameBatches[number]))) throw new Error('Use the existing Prepare whole scope or an explicit positive Game batch');
  if (selection.filter && !fqnFilter.test(selection.filter)) throw new Error('Use one existing FQN method/class-prefix filter, not DisplayName or Boolean narrowing');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(cwd, 'scripts/Test-Range.ps1'), '-RepositoryRoot', cwd, '-Scope', selection.scope, '-OutputDirectory', path.join(directory, 'range'), '-GodotPath', engine.godotPath, '-FixedPackageDirectory', engine.fixedPackageDirectory];
  if (selection.batch) args.push('-GameBatch', selection.batch);
  if (selection.filter) args.push('-Filter', selection.filter);
  return { kind: 'engine', executable: 'pwsh', args, timeoutMs: 900_000 };
}
export interface CheckOutcome { code: number; expectedRed: boolean; summary: string }
export function rangeCompleted(directory: string, scope: string): void {
  const report = json(path.join(directory, 'range', 'range-run.json')) as { status: string; projects: { scope: string }[] };
  if (report.status !== 'completed' || !report.projects.some(project => project.scope === scope)) throw new QuiescenceUnknownError('Range did not certify a complete selected test session; preserve original evidence');
}
// Evidence/adapter failures stop the attempt, but do not assert that a normally exited process is alive.
export class CheckEvidenceError extends Error {}
// Range quality-gate failure prefixes that a real complete, deterministic, clean-exit attempt has proven to
// be an ordinary failure. Add each prefix one by one from that real-attempt evidence only; every other
// prefix keeps unknown quiescence (source-count: #143 attempt f9eacb62 game-oYuPvv proved the batch
// source-count gate is a deterministic count pin after a complete clean-exit run).
const gateFailurePrefixes = ['warning-delta', 'discovery', 'source-count'] as const;
export function rangeFailureContext(report: { status: string; failure?: string; commands?: { exitCode?: number; exitKnown?: boolean; rootExited?: boolean; outputComplete?: boolean; cleanupComplete?: boolean; failureKind?: string }[] }): string {
  const last = report.commands?.at(-1);
  return `status=${report.status}; exitCode=${last?.exitCode ?? 'n/a'}; exitKnown=${last?.exitKnown ?? 'n/a'}; rootExited=${last?.rootExited ?? 'n/a'}; outputComplete=${last?.outputComplete ?? 'n/a'}; cleanupComplete=${last?.cleanupComplete ?? 'n/a'}; failureKind=${last?.failureKind ?? 'n/a'}; failure=${(typeof report.failure === 'string' ? report.failure : '').slice(0, 300)}`;
}
export function classifyFailure(cwd: string, directory: string, selection: Selection, code: number): CheckOutcome {
  if (code !== 1) {
    // The abnormal-session path (OutputDrainTimeout/ProcessTimeout/...) exits 2 before any TRX exists
    // (scripts/Test-Range.ps1 sets exitCode 2 then Fail 'abnormal-session'). Carry the same range context
    // instead of a bare message so the blocked text names the failureKind and gate text.
    const rangePath = path.join(directory, 'range', 'range-run.json');
    if (selection.scope !== 'unit' && fs.existsSync(rangePath)) {
      let report: Parameters<typeof rangeFailureContext>[0] | undefined;
      try { report = json(rangePath) as Parameters<typeof rangeFailureContext>[0]; } catch { report = undefined; }
      if (report) throw new QuiescenceUnknownError(`Unclassified native exit (${rangeFailureContext(report)}); not eligible for automatic correction`);
    }
    throw new QuiescenceUnknownError('Unclassified native exit; not eligible for automatic correction');
  }
  let trx = path.join(directory, 'trx', 'results.trx');
  let raw = fs.readFileSync(path.join(directory, 'stdout.log'), 'utf8') + '\n' + fs.readFileSync(path.join(directory, 'stderr.log'), 'utf8');
  if (selection.scope !== 'unit') {
    const report = json(path.join(directory, 'range', 'range-run.json')) as { status: string; failure: string; commands: { exitKnown: boolean; rootExited: boolean; outputComplete: boolean; cleanupComplete: boolean; exitCode: number; failureKind: string; stdout: string; stderr: string }[] };
    const last = report.commands.at(-1);
    const rangeContext = rangeFailureContext(report);
    if (report.status !== 'failed' || !last || !last.exitKnown || !last.rootExited || !last.outputComplete || !last.cleanupComplete) throw new QuiescenceUnknownError(`Range failure is not a completed normal native check (${rangeContext})`);
    // A whitelisted gate-verdict prefix is deterministic: the last command finished with a clean process-tree
    // exit and the range wrapper stopped on its own quality verdict. That is a complete normal failure, not
    // unknown quiescence: it enters the recordFailure/request_correction loop without any TRX evidence.
    if (typeof report.failure === 'string' && gateFailurePrefixes.some(prefix => report.failure.startsWith(`[range:${prefix}]`))) {
      atomicJson(path.join(directory, 'failure-classification.json'), { status: 'classified', classification: 'gate_failure', nativeExit: code, scope: selection.scope, expectedRed: false, failed: 0, failures: [] });
      return { code, expectedRed: false, summary: report.failure };
    }
    if (last.exitCode !== 1 || !/^\[range:command-exit\]/.test(report.failure)) throw new QuiescenceUnknownError(`Range failure is not a completed normal native check (${rangeContext})`);
    for (const name of [last.stdout, last.stderr]) {
      const file = path.resolve(directory, 'range', name);
      if (!file.startsWith(path.resolve(directory, 'range') + path.sep)) throw new Error('Invalid range evidence path');
      raw += '\n' + fs.readFileSync(file, 'utf8');
    }
    trx = path.join(directory, 'range', selection.scope, 'results.trx');
  }
  if (/Aborting test run|Test Run Aborted|test host.*crash|Unhandled exception|WindowsStdOutHook|test run timeout/i.test(raw)) {
    const keyLines = raw.split(/\r?\n/).filter(line => /Aborting test run|Test Run Aborted|test host.*crash|Unhandled exception|WindowsStdOutHook|test run timeout|failureKind=|\[range:[a-z-]+\]|error [A-Z]+\d+|empty or duplicate identity|guarded content/i.test(line)).slice(0, 5).join(' | ').slice(0, 700);
    throw new QuiescenceUnknownError(`Abnormal test session; preserve evidence and stop. Key lines: ${keyLines}`);
  }
  if (!fs.existsSync(trx)) {
    if (!selection.expectedFailure && /\berror (?:CS|CA|IDE)\d+\b/.test(raw)) return { code, expectedRed: false, summary: 'Located compiler/analyzer error; inspect original diagnostic before requesting correction' };
    throw new CheckEvidenceError(`Missing test result after native exit ${code}; cannot classify failure or declared RED. Inspect ${trx}, ${path.join(directory, 'stdout.log')} and ${path.join(directory, 'stderr.log')}; do not retry automatically.`);
  }
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-RepositoryRoot', cwd, '-TrxPath', trx, '-Scope', selection.scope];
  if (selection.expectedFailure) args.push('-ExpectedTest', selection.expectedFailure.test, '-ExpectedMessage', selection.expectedFailure.message);
  let classification: { expectedRed: boolean; failures: { test: string; message: string }[] };
  const evidenceFile = path.join(directory, 'failure-classification.json');
  try { classification = JSON.parse(native(cwd, 'pwsh', args)); }
  catch (error) {
    const cause = error instanceof Error ? error.message : String(error);
    atomicJson(evidenceFile, { status: 'classification_failed', nativeExit: code, scope: selection.scope, trx, cause });
    const diagnostic = `Failure classification failed after native exit ${code}: ${cause}\nEvidence: ${evidenceFile}; original TRX: ${trx}. No automatic retry; inspect the classifier separately from process quiescence.`;
    if (cause.includes('[check:abnormal]')) throw new QuiescenceUnknownError(diagnostic, { cause: error });
    throw new CheckEvidenceError(diagnostic, { cause: error });
  }
  atomicJson(evidenceFile, { status: 'classified', nativeExit: code, scope: selection.scope, trx, ...classification });
  const failures = classification.failures.slice(0, 3).map(failure => `${failure.test}: ${failure.message.slice(0, 500)}`).join('; ');
  return { code, expectedRed: classification.expectedRed, summary: classification.expectedRed ? `Exact predeclared RED observed; not a passing candidate. ${failures}. Evidence: ${evidenceFile}` : `Normal assertion/fixture failure: ${failures}. Evidence: ${evidenceFile}; diagnose before requesting bounded correction` };
}
export async function executeCheck(cwd: string, directory: string, stateDirectory: string, selection: Selection, signal: AbortSignal, engine?: EngineOptions): Promise<CheckOutcome> {
  const check = buildCheck(cwd, directory, selection, engine);
  let release: (() => void) | undefined, unknown = false;
  try {
    if (selection.scope !== 'unit') {
      assertEngineIdle();
      release = acquire(cwd, stateDirectory, engineLock);
      assertEngineIdle();
    }
    // Declaration is frozen before launch; no retroactive RED classification.
    fs.writeFileSync(path.join(directory, 'selection.json'), JSON.stringify(selection, null, 2), { flag: 'wx', encoding: 'utf8' });
    try { await runCheck(cwd, check, directory, signal); }
    catch (error) {
      if (!(error instanceof NativeCheckFailure)) throw error;
      return classifyFailure(cwd, directory, selection, error.code);
    }
    if (selection.scope === 'unit') {
      if (!fs.existsSync(path.join(directory, 'trx', 'results.trx'))) throw new QuiescenceUnknownError('Native Unit produced no expected TRX');
    } else rangeCompleted(directory, selection.scope);
    if (selection.expectedFailure) throw new Error('Declared RED unexpectedly passed; inspect the test premise rather than accepting it');
    return { code: 0, expectedRed: false, summary: `${selection.scope} selected feedback only; not GPU/desktop/full acceptance` };
  } catch (error) {
    unknown = error instanceof QuiescenceUnknownError;
    throw error;
  } finally {
    if (release && !unknown) {
      // Do not release a global engine lock while a late child may still be alive.
      try { assertEngineIdle(); }
      catch { throw new QuiescenceUnknownError('Engine processes remain or ownership is unknown; retain both locks for explicit recovery'); }
      release();
    }
  }
}

// Godot 4.4+ generates companion `<script>.uid` files for scripts and shaders
// (not imported resources, which carry `.import`). A check run that first sees a
// new `.cs` lets the engine generate the `.uid` mid-run, which drifts the source
// snapshot and trips "Native check changed source identity". Stabilize the `.uid`
// files *before* the before-snapshot: only when some are missing, so a complete
// tree pays nothing.
const uidNoiseDirs = new Set(['.git', 'node_modules', '.godot', 'bin', 'obj', '.packages', '.vs', '.idea']);

export function godotProjectRoot(cwd: string): string | undefined {
  const candidate = path.join(cwd, 'src', 'Pal.Game');
  return fs.existsSync(path.join(candidate, 'project.godot')) ? candidate : undefined;
}

// Returns scripts under the real Godot project root whose companion .uid is genuinely new
// (the script is untracked). A tracked script whose .uid is missing is a deletion/rename of a
// committed uid and is deliberately NOT stabilized, so the deletion stays visible.
function isUntracked(cwd: string, rel: string): boolean {
  const result = spawnSync('git', ['ls-files', '--error-unmatch', '--', rel], { cwd, shell: false, windowsHide: true, timeout: 30_000 });
  if (result.error || result.signal) throw new QuiescenceUnknownError('Cannot determine tracking state for ' + rel + ': ' + (result.error?.message ?? result.signal));
  if (result.status === 0) return false;
  if (result.status === 1) return true;
  throw new QuiescenceUnknownError('git ls-files failed (status ' + result.status + ') for ' + rel);
}

export function missingUidScripts(cwd: string, projectRoot: string): string[] {
  const results: string[] = [];
  const walk = (dir: string): void => {
    let entries: fs.Dirent[];
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); }
    catch { return; }
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!uidNoiseDirs.has(entry.name)) walk(full);
        continue;
      }
      if (!/\.(cs|gd|shader)$/.test(entry.name)) continue;
      if (!fs.existsSync(full + '.uid')) results.push(path.relative(cwd, full).split(path.sep).join('/'));
    }
  };
  walk(projectRoot);
  return results.filter(rel => isUntracked(cwd, rel + '.uid')).sort();
}

// Snapshot every tracked .uid under the project root: hash when present, null when a tracked uid
// is currently deleted. Used to prove an import did not rebuild a deleted tracked uid or rewrite
// an existing one (which would mask a real deletion/tamper behind the before-snapshot).
function trackedUidSnapshot(cwd: string, projectRoot: string): Map<string, string | null> {
  const rel = path.relative(cwd, projectRoot).split(path.sep).join('/');
  const listed = spawnSync('git', ['ls-files', '-z', '--', rel], { cwd, shell: false, encoding: 'utf8', windowsHide: true, timeout: 30_000 });
  if (listed.error || listed.signal || listed.status !== 0) throw new QuiescenceUnknownError('Cannot enumerate tracked files under ' + rel + ' for .uid stabilization');
  const snapshot = new Map<string, string | null>();
  for (const name of listed.stdout.split('\0').filter(Boolean)) {
    if (!name.endsWith('.uid')) continue;
    const abs = path.join(cwd, name);
    if (fs.existsSync(abs)) { noLinks(abs); snapshot.set(name, hash(fs.readFileSync(abs))); }
    else snapshot.set(name, null);
  }
  return snapshot;
}

// Runs the Godot editor import through the same async spawn path as a normal engine check: evidence
// (stdout/stderr/result) is written under `directory`, and the session AbortSignal can cancel it.
export async function runGodotImport(projectRoot: string, godotPath: string, directory: string, signal: AbortSignal): Promise<void> {
  await runCheck(projectRoot, { kind: 'engine', executable: godotPath, args: ['--headless', '--path', projectRoot, '--editor', '--import', '--quit'], timeoutMs: 300_000 }, directory, signal);
}

// Returns the first tracked .uid whose before/after state differs (rebuilt deleted uid, or a
// rewritten hash). null means a tracked uid that was absent (deletion) before the import.
export function uidChangeViolation(before: Map<string, string | null>, after: Map<string, string | null>): string | undefined {
  for (const [name, previous] of before) {
    const current = after.get(name) ?? null;
    if (current !== previous) return name;
  }
  return undefined;
}

// Stabilize Godot-generated .uid files before the before-snapshot. Guarded by the same engine
// idle check and global engine lock as a normal engine check; only runs when authorized and not
// a Unit check (Unit is non-engine and never needs .uid). Tracked .uid files are snapshotted
// before and after so an import that rebuilds a deleted tracked uid (or rewrites one) fails
// loudly instead of being absorbed into the before-snapshot.
export async function stabilizeGodotUids(cwd: string, engine: EngineOptions, stateDirectory: string, signal: AbortSignal): Promise<void> {
  const projectRoot = godotProjectRoot(cwd);
  if (!projectRoot) return;
  if (missingUidScripts(cwd, projectRoot).length === 0) return;
  const trackedBefore = trackedUidSnapshot(cwd, projectRoot);
  assertEngineIdle();
  const release = acquire(cwd, stateDirectory, engineLock);
  assertEngineIdle();
  const importDirectory = fs.mkdtempSync(path.join(stateDirectory, 'uid-import-'));
  let unknown = false;
  try {
    try {
      await runGodotImport(projectRoot, engine.godotPath, importDirectory, signal);
    } catch (error) {
      if (error instanceof QuiescenceUnknownError) throw error;
      // ranCheck rejects a launch failure (e.g. ENOENT) with the raw Error and a nonzero exit with
      // NativeCheckFailure; only the launch failure is retryable, anything the engine actually ran is fatal.
      if (error instanceof NativeCheckFailure) throw new QuiescenceUnknownError('Godot import exited ' + error.code + '; evidence ' + importDirectory + '; engine may have run');
      throw new Error('Godot import failed to start: ' + (error instanceof Error ? error.message : String(error)) + '; evidence ' + importDirectory);
    }
    const still = missingUidScripts(cwd, projectRoot);
    if (still.length) throw new QuiescenceUnknownError('Godot import left ' + still.length + ' script(s) without .uid: ' + still.slice(0, 5).join(', ') + '; evidence ' + importDirectory);
    const violated = uidChangeViolation(trackedBefore, trackedUidSnapshot(cwd, projectRoot));
    if (violated) throw new QuiescenceUnknownError('Godot import changed tracked .uid ' + violated + ' (rebuilt a deleted uid or rewrote an existing one); a deletion or tamper would be masked; evidence ' + importDirectory);
  } catch (error) {
    unknown = error instanceof QuiescenceUnknownError;
    throw error;
  } finally {
    if (!unknown) {
      try { assertEngineIdle(); }
      catch { throw new QuiescenceUnknownError('Engine processes remain or ownership is unknown; retain both locks for explicit recovery'); }
      release();
    }
  }
}
