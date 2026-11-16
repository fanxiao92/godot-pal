import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Check } from './contract.ts';
import { acquire, atomicJson, json, native, NativeCheckFailure, QuiescenceUnknownError, runCheck } from './io.ts';

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
// prefix keeps unknown quiescence.
const gateFailurePrefixes = ['warning-delta', 'discovery'] as const;
export function classifyFailure(cwd: string, directory: string, selection: Selection, code: number): CheckOutcome {
  if (code !== 1) throw new QuiescenceUnknownError('Unclassified native exit; not eligible for automatic correction');
  let trx = path.join(directory, 'trx', 'results.trx');
  let raw = fs.readFileSync(path.join(directory, 'stdout.log'), 'utf8') + '\n' + fs.readFileSync(path.join(directory, 'stderr.log'), 'utf8');
  if (selection.scope !== 'unit') {
    const report = json(path.join(directory, 'range', 'range-run.json')) as { status: string; failure: string; commands: { exitKnown: boolean; rootExited: boolean; outputComplete: boolean; cleanupComplete: boolean; exitCode: number; failureKind: string; stdout: string; stderr: string }[] };
    const last = report.commands.at(-1);
    if (report.status !== 'failed' || !last || !last.exitKnown || !last.rootExited || !last.outputComplete || !last.cleanupComplete) throw new QuiescenceUnknownError('Range failure is not a completed normal native check');
    // A whitelisted gate-verdict prefix is deterministic: the last command finished with a clean process-tree
    // exit and the range wrapper stopped on its own quality verdict. That is a complete normal failure, not
    // unknown quiescence: it enters the recordFailure/request_correction loop without any TRX evidence.
    if (typeof report.failure === 'string' && gateFailurePrefixes.some(prefix => report.failure.startsWith(`[range:${prefix}]`))) {
      atomicJson(path.join(directory, 'failure-classification.json'), { status: 'classified', classification: 'gate_failure', nativeExit: code, scope: selection.scope, expectedRed: false, failed: 0, failures: [] });
      return { code, expectedRed: false, summary: report.failure };
    }
    if (last.exitCode !== 1 || !/^\[range:command-exit\]/.test(report.failure)) throw new QuiescenceUnknownError('Range failure is not a completed normal native check');
    for (const name of [last.stdout, last.stderr]) {
      const file = path.resolve(directory, 'range', name);
      if (!file.startsWith(path.resolve(directory, 'range') + path.sep)) throw new Error('Invalid range evidence path');
      raw += '\n' + fs.readFileSync(file, 'utf8');
    }
    trx = path.join(directory, 'range', selection.scope, 'results.trx');
  }
  if (/Aborting test run|Test Run Aborted|test host.*crash|Unhandled exception|WindowsStdOutHook|test run timeout/i.test(raw)) throw new QuiescenceUnknownError('Abnormal test session; preserve evidence and stop');
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
