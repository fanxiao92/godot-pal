import assert from 'node:assert/strict';
import path from 'node:path';
import { test } from 'node:test';
import { approveCorrection, requireChangedAfterFailure, requireWritable, type Correction, type CorrectionLedger } from './policy.ts';
import { buildCheck, unitProjects } from './checks.ts';
import { parsePlan, type Source } from './contract.ts';

const source: Source = { head: 'a'.repeat(40), fingerprint: 'before', indexFingerprint: 'index', dirty: true, files: {}, modes: {} };
const correction = (kind: Correction['kind'] = 'check'): Correction => ({ kind, cause: 'Located wrong consumer field', change: 'Read the required slot field', contract: 'Existing slot contract and independent expected value unchanged', evidence: '/owned/check', sourceFingerprint: source.fingerprint, utc: '2026-11-13T00:00:00Z' });

test('normal failure requires an evidenced substantive correction before writes/recheck', () => {
  const ledger: CorrectionLedger = { pendingFailure: { directory: '/owned/check', sourceFingerprint: 'before', summary: 'normal failed assertion' } };
  assert.throws(() => requireWritable(ledger), /request_correction/);
  assert.throws(() => approveCorrection(ledger, { ...correction(), change: '' }), /substantive/);
  approveCorrection(ledger, correction());
  requireWritable(ledger);
  assert.throws(() => requireChangedAfterFailure(ledger, source), /same check/);
  requireChangedAfterFailure(ledger, { ...source, fingerprint: 'fixed' });
});
test('implementation and review repairs share two rounds and survive serialized fresh attempts', () => {
  const ledger: CorrectionLedger = {};
  approveCorrection(ledger, correction());
  const restored: CorrectionLedger = JSON.parse(JSON.stringify(ledger));
  approveCorrection(restored, correction('review'));
  assert.equal(restored.corrections?.length, 2);
  assert.throws(() => approveCorrection(restored, correction()), /budget exhausted/);
  restored.extraCorrections = 1;
  approveCorrection(restored, correction());
  assert.equal(restored.corrections?.length, 3);
  assert.throws(() => approveCorrection(restored, correction()), /budget exhausted/);
});
test('Unit remains complete-project feedback and rejects model narrowing', () => {
  const check = buildCheck(process.cwd(), '/owned/check', { scope: 'unit', project: unitProjects[0] });
  assert.equal(check.executable, 'dotnet');
  assert.ok(check.args.includes('tests/unit.runsettings'));
  assert.ok(!check.args.some(arg => /--no-build|--no-restore|--filter/.test(arg)));
  assert.throws(() => buildCheck('.', '.', { scope: 'unit', project: unitProjects[0], filter: 'FullyQualifiedName=X.Y' }), /no filters/);
});
test('engine capability is explicit and uses only existing bounded Prepare/Game range commands', () => {
  const engine = { godotPath: path.resolve('approved-godot.exe'), fixedPackageDirectory: path.resolve('verified-bundle') };
  assert.throws(() => buildCheck('.', '.', { scope: 'game', batch: 'native-map' }), /explicit/);
  assert.throws(() => buildCheck('.', '.', { scope: 'prepare', filter: 'FullyQualifiedName=X.Y' }, engine), /whole scope/);
  assert.throws(() => buildCheck('.', '.', { scope: 'game', batch: 'failure-control' }, engine), /positive Game batch/);
  for (const filter of ['DisplayName=X', 'FullyQualifiedName=X.Y|FullyQualifiedName=A.B', 'FullyQualifiedName~X;exit']) {
    assert.throws(() => buildCheck('.', '.', { scope: 'game', batch: 'native-map', filter }, engine), /FQN/);
  }
  const check = buildCheck(process.cwd(), path.resolve('owned-check'), { scope: 'game', batch: 'native-map', filter: 'FullyQualifiedName~Pal.Game.Tests.Native.' }, engine);
  assert.equal(check.kind, 'engine'); assert.equal(check.timeoutMs, 900_000);
  assert.ok(check.args.some(arg => arg.endsWith('Test-Range.ps1')));
  assert.ok(!check.args.includes('-SkipAffectedBatchGuard'));
  assert.ok(!check.args.some(arg => /Test-M0|data.raw|TestSessionTimeout/.test(arg)));
});
test('explicit authorized path sets may exceed ten without weakening path authorization', () => {
  const plan = parsePlan({ version: 1, repository: 'fanxiao92/godot-pal', cwd: path.resolve('approved-repo'), baseHead: 'a'.repeat(40), sdkRoot: path.resolve('sdk'), agentDir: path.resolve('agent'), provider: 'explicit', model: 'explicit', approval: { id: 'test', approvedBy: 'operator', deadlineUtc: '2030-01-01T00:00:00Z', localCommits: false, autoAdvance: false }, limits: { maxTurns: 1, timeoutMs: 1000 }, prerequisites: [], tasks: [{ issue: 2, dependsOn: [], contextIssues: [], allowedWritePaths: Array.from({ length: 12 }, (_, index) => `src/consumer${index}.cs`), readPaths: ['src'], checks: [{ kind: 'non_engine', executable: process.execPath, args: [], timeoutMs: 1000 }] }] });
  assert.equal(plan.tasks[0].allowedWritePaths.length, 12);
});
test('planned RED requires an exact test identity and nonempty independent literal diagnostic', () => {
  buildCheck('.', '.', { scope: 'unit', project: unitProjects[0], expectedFailure: { test: 'Synthetic.FailureTests.One:0 ("idle")', message: 'missing field' } });
  assert.throws(() => buildCheck('.', '.', { scope: 'unit', project: unitProjects[0], expectedFailure: { test: 'A.B;exit', message: 'expected' } }), /exact test identity/);
  assert.throws(() => buildCheck('.', '.', { scope: 'unit', project: unitProjects[0], expectedFailure: { test: 'A.B', message: ' ' } }), /literal/);
});
