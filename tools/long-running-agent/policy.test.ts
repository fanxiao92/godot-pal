import assert from 'node:assert/strict';
import path from 'node:path';
import { test } from 'node:test';
import { approveCorrection, attributeValidation, requireChangedAfterFailure, requireWritable, type Correction, type CorrectionLedger } from './policy.ts';
import * as policyModule from './policy.ts';
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
test('report parsing accepts prose-wrapped fenced or last balanced JSON while rejecting arrays and garbage', () => {
  const report = { status: 'blocked', summary: 'Blocked pending {dependency} acceptance', evidence: ['#3 acceptance comment'] };
  assert.equal(parseReport(JSON.stringify(report), 'implement').status, 'blocked');
  assert.equal(parseReport(`Progress prose before the answer.\n\n\`\`\`json\n${JSON.stringify(report)}\n\`\`\`\nTrailing notes.`, 'implement').status, 'blocked');
  assert.equal(parseReport(`Prose ${JSON.stringify({ unrelated: 'ignored' })} middle ${JSON.stringify(report)} tail.`, 'implement').status, 'blocked');
  assert.throws(() => parseReport('[{"status":"blocked"}]', 'implement'), /Invalid agent result fields/);
  assert.throws(() => parseReport('Prose with no report object at all.', 'implement'), SyntaxError);
  assert.throws(() => parseReport(`${JSON.stringify(report)} trailing ${JSON.stringify({ unrelated: true })}`, 'implement'), /Invalid implement result status/);
  assert.throws(() => parseReport('Fence only.\n\n\`\`\`json\n[1, 2]\n\`\`\`', 'implement'), /Invalid agent result fields/);
  assert.throws(() => parseReport('{"status":"blocked","summary":"s","evidence":["e"]} trailing {"unrelated":true}', 'spec'), /Invalid spec result status/);
});
test('report parsing ignores harmless unknown keys on the report and finding objects without weakening required validation', () => {
  const structured = { problemId: 'slot-field', evidenceKey: 'original:src/slot.cs#field', cause: 'Required field omitted', evidence: ['/owned/review.json'] };
  const parsed = parseReport({ status: 'changes_requested', summary: 'Located repair', evidence: ['src/slot.cs'], generatedAt: '2026-11-14T00:00:00Z', findings: [{ ...structured, confidence: 'high' }] }, 'standards');
  assert.deepEqual(JSON.parse(JSON.stringify(parsed)), { status: 'changes_requested', summary: 'Located repair', evidence: ['src/slot.cs'], findings: [structured] });
  assert.equal(parseReport(JSON.stringify({ status: 'passed', summary: 's', evidence: ['e'], modelNote: 'extra' }), 'spec').status, 'passed');
  assert.throws(() => parseReport({ status: 'changes_requested', summary: 's', evidence: ['e'], findings: [{ ...structured, problemId: 5 }] }, 'standards'), /problemId must be nonblank/);
  assert.throws(() => parseReport({ status: 'changes_requested', summary: 's', evidence: ['e'], findings: [{ ...structured, evidence: [' '] }] }, 'standards'), /Problem finding requires original evidence/);
  assert.throws(() => parseReport({ status: 'changes_requested', summary: 's', evidence: ['e'], findings: [{ problemId: structured.problemId, evidenceKey: structured.evidenceKey, cause: structured.cause }] }, 'standards'), /Problem finding requires original evidence/);
  assert.throws(() => parseReport({ status: 'changes_requested', summary: 's', evidence: ['e'], findings: 'legacy' }, 'standards'), /findings must be an array/);
  assert.throws(() => parseReport({ summary: 's', evidence: ['e'] }, 'spec'), /result status/);
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
test('Unit remains complete-project feedback and accepts only one FQN filter', () => {
  const check = buildCheck(process.cwd(), '/owned/check', { scope: 'unit', project: unitProjects[0] });
  assert.equal(check.executable, 'dotnet');
  assert.ok(check.args.includes('tests/unit.runsettings'));
  assert.ok(!check.args.some(arg => /--no-build|--no-restore|--filter/.test(arg)));
  const filtered = buildCheck(process.cwd(), '/owned/check', { scope: 'unit', project: unitProjects[0], filter: 'FullyQualifiedName~Pal.Core.UnitTests.' });
  assert.ok(filtered.args.includes('--filter'));
  assert.ok(filtered.args.includes('FullyQualifiedName~Pal.Core.UnitTests.'));
  for (const filter of ['DisplayName=X', 'FullyQualifiedName=X.Y|FullyQualifiedName=A.B', 'FullyQualifiedName~X;exit']) {
    assert.throws(() => buildCheck('.', '.', { scope: 'unit', project: unitProjects[0], filter }), /FQN/);
  }
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
test('external evidence resolution binds pending check and review corrections and unblocks unchanged source', () => {
  const bind = (policyModule as Record<string, unknown>).bindEvidenceResolution as ((ledger: CorrectionLedger, resolution: { problemId: string; file: string; sha256: string; note: string; utc: string }) => void) | undefined;
  assert.ok(typeof bind === 'function', 'policy must expose the external evidence resolution guard bindEvidenceResolution');
  const resolution = (problemId: string, sha256 = 'a'.repeat(64)) => ({ problemId, file: 'C:/evidence/m6/operator-original.txt', sha256, note: 'Operator external evidence', utc: '2026-11-14T00:00:00Z' });
  const ledger: CorrectionLedger = {};
  approveCorrection(ledger, { ...correction('review', 'external-review-problem'), sourceFingerprint: 'unchanged' });
  assert.throws(() => requireChangedAfterFailure(ledger, { ...source, fingerprint: 'unchanged' }), /No substantive source correction/, 'unresolved review correction with unchanged source stays blocked (the original stuck point)');
  bind(ledger, resolution('external-review-problem'));
  requireChangedAfterFailure(ledger, { ...source, fingerprint: 'unchanged' });
  requireChangedAfterFailure(ledger, { ...source, fingerprint: 'changed-after-resolution' });
  const fresh = (): CorrectionLedger => { const next: CorrectionLedger = {}; approveCorrection(next, { ...correction('review', 'external-review-problem'), sourceFingerprint: 'x' }); return next; };
  assert.throws(() => bind(fresh(), resolution('wrong-problem')), /pending correction problemId/i, 'wrong problemId rejected');
  assert.throws(() => bind(fresh(), resolution('external-review-problem', 'not-hex')), /sha256/i, 'malformed sha256 rejected');
  assert.throws(() => bind({}, resolution('external-review-problem')), /pending correction problemId/i, 'missing correction rejected');
  const checkLedger: CorrectionLedger = {};
  approveCorrection(checkLedger, { ...correction('check', 'environmental-absence'), sourceFingerprint: 'unchanged' });
  assert.throws(() => requireChangedAfterFailure(checkLedger, { ...source, fingerprint: 'unchanged' }), /No substantive source correction/, 'unresolved check correction with unchanged source stays blocked (the #209 stuck point)');
  bind(checkLedger, resolution('environmental-absence'));
  requireChangedAfterFailure(checkLedger, { ...source, fingerprint: 'unchanged' });
  assert.throws(() => bind(checkLedger, resolution('environmental-absence')), /already/i, 'duplicate check-kind binding rejected');
  const duplicated = fresh();
  bind(duplicated, resolution('external-review-problem'));
  assert.throws(() => bind(duplicated, resolution('external-review-problem')), /already/i, 'duplicate binding rejected');
});
