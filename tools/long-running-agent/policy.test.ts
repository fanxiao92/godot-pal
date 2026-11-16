import assert from 'node:assert/strict';
import path from 'node:path';
import { test } from 'node:test';
import { approveCorrection, attributeValidation, requireChangedAfterFailure, requireWritable, type Correction, type CorrectionLedger } from './policy.ts';
import { buildCheck, unitProjects } from './checks.ts';
import { parsePlan, parseReport, type Source } from './contract.ts';

const source: Source = { head: 'a'.repeat(40), fingerprint: 'before', indexFingerprint: 'index', dirty: true, files: {}, modes: {} };
const correction = (kind: Correction['kind'] = 'check', problemId = 'slot-field'): Correction => ({ problemId, evidenceKey: `original:src/slot.cs#${problemId}`, kind, cause: 'Located wrong consumer field', change: 'Read the required slot field', contract: 'Existing slot contract and independent expected value unchanged', evidence: '/owned/check', sourceFingerprint: source.fingerprint, utc: '2026-11-13T00:00:00Z' });

test('normal failure requires an evidenced substantive correction before writes/recheck', () => {
  const ledger: CorrectionLedger = { pendingFailure: { directory: '/owned/check', sourceFingerprint: 'before', summary: 'normal failed assertion' } };
  assert.throws(() => requireWritable(ledger), /request_correction/);
  assert.throws(() => approveCorrection(ledger, { ...correction(), change: '' }), /substantive/);
  approveCorrection(ledger, correction());
  requireWritable(ledger);
  assert.throws(() => requireChangedAfterFailure(ledger, source), /same check/);
  requireChangedAfterFailure(ledger, { ...source, fingerprint: 'fixed' });
});
test('same problem has three rounds across check review and JSON while other problems are independent', () => {
  const ledger: CorrectionLedger = {};
  approveCorrection(ledger, correction());
  const restored: CorrectionLedger = JSON.parse(JSON.stringify(ledger));
  approveCorrection(restored, correction('review'));
  approveCorrection(restored, correction());
  assert.equal(restored.corrections?.length, 3);
  restored.extraCorrections = 100;
  assert.throws(() => approveCorrection(restored, correction()), /three|3|exhausted/);
  approveCorrection(restored, correction('check', 'different-root'));
  assert.equal(restored.corrections?.length, 4);
});
test('same original evidence cannot be renamed and unmapped legacy repairs cannot silently disappear', () => {
  const ledger: CorrectionLedger = {};
  approveCorrection(ledger, correction());
  assert.throws(() => approveCorrection(ledger, { ...correction(), problemId: 'renamed' }), /evidence.*identity|same evidence/i);
  assert.throws(() => approveCorrection({}, { ...correction(), problemId: undefined }), /problemId/);
  const legacy = { ...correction(), problemId: undefined, evidenceKey: undefined };
  const restored: CorrectionLedger = JSON.parse(JSON.stringify({ corrections: [legacy], extraCorrections: 99 }));
  assert.throws(() => approveCorrection(restored, correction()), /legacy.*migration/i);
  assert.throws(() => requireWritable(restored), /legacy.*migration/i);
  assert.equal(restored.corrections?.length, 1);
  assert.equal(restored.corrections?.[0].problemId, undefined);
});
test('structured problem findings survive reports while explicit legacy reports remain valid', () => {
  const findings = [{ problemId: 'slot-field', evidenceKey: 'original:src/slot.cs#field', cause: 'Required field omitted', evidence: ['/owned/review.json'] }];
  const parsed = parseReport({ status: 'changes_requested', summary: 'Located repair', evidence: ['src/slot.cs'], findings }, 'standards');
  assert.deepEqual(JSON.parse(JSON.stringify(parsed)).findings, findings);
  assert.equal(parseReport({ status: 'changes_requested', summary: 'Legacy read-only finding', evidence: ['source'] }, 'spec').findings, undefined);
  assert.throws(() => parseReport({ ...parsed, findings: [{ ...findings[0], problemId: '' }] }, 'spec'), /problemId/);
});
test('third validation attributes a new root cause independently instead of declaring the repaired problem exhausted', () => {
  const ledger: CorrectionLedger = {};
  const same = { problemId: 'slot-field', evidenceKey: correction().evidenceKey!, cause: 'Same located root cause', evidence: ['/owned/validation'] };
  for (let round = 1; round <= 3; round++) {
    approveCorrection(ledger, { ...correction(), sourceFingerprint: `failure-${round}` });
    if (round < 3) attributeValidation(ledger, [same], `/owned/round-${round}`, false);
  }
  const other = { ...same, problemId: 'other-root', evidenceKey: 'original:src/other.cs#contract' };
  attributeValidation(ledger, [other], '/owned/review-after-third', true);
  assert.deepEqual(ledger.corrections!.map(item => item.outcome), ['failed', 'failed', 'passed']);
  approveCorrection(ledger, { ...correction(), problemId: other.problemId, evidenceKey: other.evidenceKey });
  assert.equal(ledger.corrections!.length, 4);
});
test('third same-problem failure ends writes immediately and keeps all original validations through JSON', () => {
  let ledger: CorrectionLedger = {};
  const same = { problemId: 'slot-field', evidenceKey: correction().evidenceKey!, cause: 'Same located root cause', evidence: ['/owned/validation'] };
  for (let round = 1; round <= 3; round++) {
    approveCorrection(ledger, { ...correction(), sourceFingerprint: `failure-${round}` });
    if (round === 3) assert.throws(() => attributeValidation(ledger, [same], `/owned/round-${round}`, false), /three repair rounds failed/);
    else attributeValidation(ledger, [same], `/owned/round-${round}`, false);
    ledger = JSON.parse(JSON.stringify(ledger));
  }
  assert.throws(() => requireWritable(ledger), /three repair rounds failed/);
  assert.deepEqual(ledger.corrections!.map(item => item.validation), ['/owned/round-1', '/owned/round-2', '/owned/round-3']);
  assert.equal(ledger.corrections!.length, 3);
  assert.throws(() => approveCorrection(ledger, { ...correction(), problemId: 'renamed' }), /Same evidence/);
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
