import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { native } from './io.ts';
import { spawnSync } from 'node:child_process';
import { palCompleted, classifyFailure, CheckEvidenceError, godotProjectRoot, missingUidScripts, runGodotImport, stabilizeGodotUids, uidChangeViolation } from './checks.ts';
import { QuiescenceUnknownError } from './io.ts';

const here = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(here, '../..');
const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-harness-checks-'));
console.log(`Offline check adapter evidence retained at ${directory}`);
const trx = (message: string, infos = '') => `<?xml version="1.0" encoding="utf-8"?>
<TestRun id="11111111-1111-1111-1111-111111111111" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult testId="a" executionId="e1" testName="Synthetic.FailureTests.One" outcome="Failed"><Output><ErrorInfo><Message>${message}</Message></ErrorInfo></Output></UnitTestResult><UnitTestResult testId="b" executionId="e2" testName="Synthetic.FailureTests.Two" outcome="Passed"/></Results>
<TestDefinitions><UnitTest id="a" name="Synthetic.FailureTests.One"><Execution id="e1"/><TestMethod className="Synthetic.FailureTests" name="One"/></UnitTest><UnitTest id="b" name="Synthetic.FailureTests.Two"><Execution id="e2"/><TestMethod className="Synthetic.FailureTests" name="Two"/></UnitTest></TestDefinitions>
<TestEntries><TestEntry testId="a" executionId="e1"/><TestEntry testId="b" executionId="e2"/></TestEntries>
<ResultSummary outcome="Failed"><Counters total="2" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" notExecuted="0"/>${infos}</ResultSummary></TestRun>`;

function writePalSummary(output: string, summary: object) {
  fs.mkdirSync(path.join(output, 'pal', 'trx'), { recursive: true });
  fs.writeFileSync(path.join(output, 'pal', 'run-summary.json'), JSON.stringify(summary), 'utf8');
}

test('self-contained identity reader distinguishes exact UTF8 RED, wrong failure and abnormal/partial sessions', () => {
  const file = path.join(directory, 'results.trx');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-TrxPath', file, '-ExpectedTest', 'Synthetic.FailureTests.One', '-ExpectedMessage', '缺少 slot0 idleAnimSpeed'];
  fs.writeFileSync(file, trx('缺少 slot0 idleAnimSpeed'), 'utf8');
  const result = JSON.parse(native(repository, 'pwsh', args));
  assert.equal(result.expectedRed, true); assert.equal(result.failed, 1);
  const parameterized = trx('缺少 slot0 idleAnimSpeed').replaceAll('testName="Synthetic.FailureTests.One"', 'testName="Synthetic.FailureTests.One:0 (&quot;idle&quot;)"').replace('name="Synthetic.FailureTests.One"', 'name="Synthetic.FailureTests.One:0 (&quot;idle&quot;)"');
  fs.writeFileSync(file, parameterized, 'utf8');
  const parameterArgs = [...args]; parameterArgs[parameterArgs.indexOf('-ExpectedTest') + 1] = 'Synthetic.FailureTests.One:0 ("idle")';
  assert.equal(JSON.parse(native(repository, 'pwsh', parameterArgs)).expectedRed, true);
  fs.writeFileSync(file, trx('Different assertion'), 'utf8');
  assert.throws(() => native(repository, 'pwsh', args), /declared test and literal/);
  fs.writeFileSync(file, trx('缺少 slot0 idleAnimSpeed', '<RunInfos><RunInfo outcome="Error">Test Run Aborted</RunInfo></RunInfos>'), 'utf8');
  assert.throws(() => native(repository, 'pwsh', args), /Abnormal/);
  fs.writeFileSync(file, trx('缺少 slot0 idleAnimSpeed').replace('<TestEntry testId="b" executionId="e2"/>', ''), 'utf8');
  assert.throws(() => native(repository, 'pwsh', args), /Incomplete TRX identity/);
});
test('Unit failures allow xUnit theory executions sharing a definition; Game retains strict identity validation', () => {
  const file = path.join(directory, 'theory.trx');
  const repeated = trx('Expected six-enemy rejection')
    .replace('testId="b" executionId="e2" testName="Synthetic.FailureTests.Two"', 'testId="a" executionId="e2" testName="Synthetic.FailureTests.One(data: 2)"')
    .replace('<UnitTest id="b" name="Synthetic.FailureTests.Two"><Execution id="e2"/><TestMethod className="Synthetic.FailureTests" name="Two"/></UnitTest>', '')
    .replace('<TestEntry testId="b" executionId="e2"/>', '<TestEntry testId="a" executionId="e2"/>');
  fs.writeFileSync(file, repeated, 'utf8');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-TrxPath', file];
  const result = JSON.parse(native(repository, 'pwsh', [...args, '-Scope', 'unit']));
  assert.equal(result.classification, 'normal_test_failure');
  assert.equal(result.failed, 1);
  assert.equal(result.failures[0].test, 'Synthetic.FailureTests.One');
  assert.match(result.failures[0].message, /six-enemy/);
  assert.equal(JSON.parse(native(repository, 'pwsh', [...args, '-Scope', 'unit', '-ExpectedTest', 'Synthetic.FailureTests.One', '-ExpectedMessage', 'Expected six-enemy rejection'])).expectedRed, true);
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'game']), /Incomplete TRX identity/);
  fs.writeFileSync(file, repeated.replace('total="2"', 'total="3"'), 'utf8');
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'unit']), /Incomplete counter: total/);
  fs.writeFileSync(file, repeated.replace('outcome="Passed"', 'outcome="NotExecuted"'), 'utf8');
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'unit']), /complete normal assertion/);
});
test('ordinary assertion RunInfo Error stays a normal failure in every scope; abnormal markers still stop every scope', () => {
  const file = path.join(directory, 'unit-runinfo.trx');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-TrxPath', file];
  fs.writeFileSync(file, trx('Expected six-enemy rejection', '<RunInfos><RunInfo outcome="Error">[xUnit.net] Synthetic.FailureTests.One [FAIL]</RunInfo></RunInfos>'));
  assert.equal(JSON.parse(native(repository, 'pwsh', [...args, '-Scope', 'unit'])).classification, 'normal_test_failure');
  assert.equal(JSON.parse(native(repository, 'pwsh', [...args, '-Scope', 'game'])).classification, 'normal_test_failure');
  fs.writeFileSync(file, trx('Expected six-enemy rejection', '<RunInfos><RunInfo outcome="Error">Test Run Aborted</RunInfo></RunInfos>'));
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'unit']), /check:abnormal/);
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'game']), /check:abnormal/);
});
test('GdUnit ordinary assertion RunInfo Error reports the failed case instead of abnormal across scopes', () => {
  const file = path.join(directory, 'gdunit-assertion-runinfo.trx');
  const xml = `<?xml version="1.0" encoding="utf-8"?>
<TestRun id="11111111-1111-1111-1111-111111111111" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult testId="a" executionId="e1" testName="Pal.Game.Tests.NativeBattleContentTests.Synthetic.SyntheticCase:33 (&quot;enemies-overcapacity&quot;)" outcome="Failed"><Output><ErrorInfo><Message>Expecting: 'True' but is 'False'</Message></ErrorInfo></Output></UnitTestResult><UnitTestResult testId="b" executionId="e2" testName="Pal.Game.Tests.NativeBattleContentTests.Synthetic.SyntheticOther" outcome="Passed"/></Results>
<TestDefinitions><UnitTest id="a" name="SyntheticCase:33 (&quot;enemies-overcapacity&quot;)"><Execution id="e1"/><TestMethod adapterTypeName="executor://gdunit4.testadapter/" className="Pal.Game.Tests.NativeBattleContentTests.Synthetic" name="SyntheticCase:33 (&quot;enemies-overcapacity&quot;)"/></UnitTest><UnitTest id="b" name="SyntheticOther"><Execution id="e2"/><TestMethod adapterTypeName="executor://gdunit4.testadapter/" className="Pal.Game.Tests.NativeBattleContentTests.Synthetic" name="SyntheticOther"/></UnitTest></TestDefinitions>
<TestEntries><TestEntry testId="a" executionId="e1"/><TestEntry testId="b" executionId="e2"/></TestEntries>
<ResultSummary outcome="Failed"><Counters total="2" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" notExecuted="0"/><RunInfos><RunInfo outcome="Error">Expecting: 'True' but is 'False'</RunInfo></RunInfos></ResultSummary></TestRun>`;
  fs.writeFileSync(file, xml, 'utf8');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-TrxPath', file];
  for (const scope of ['unit', 'game'] as const) {
    const result = JSON.parse(native(repository, 'pwsh', [...args, '-Scope', scope]));
    assert.equal(result.classification, 'normal_test_failure');
    assert.equal(result.failed, 1);
    assert.match(result.failures[0].test, /enemies-overcapacity/);
    assert.match(result.failures[0].message, /Expecting: 'True' but is 'False'/);
  }
});
test('failure feedback preserves the failed case, classifier cause, errorMsg, native exit and evidence without inventing unknown quiescence', () => {
  const output = fs.mkdtempSync(path.join(directory, 'feedback-'));
  writePalSummary(output, {
    command: 'test unit',
    status: 'failed',
    errorMsg: "Test step 'Pal.Core.UnitTests' failed with exit code 1.",
    counts: { total: 2, passed: 1, failed: 1, skipped: 0 },
    trxFiles: ['trx/Pal.Core.UnitTests.trx'],
  });
  const file = path.join(output, 'pal/trx/Pal.Core.UnitTests.trx');
  fs.writeFileSync(path.join(output, 'stdout.log'), 'Failed: 1, Passed: 1, Total: 2');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  const selection = { scope: 'unit' as const, project: 'tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj' };
  fs.writeFileSync(file, trx('Expected six-enemy rejection'));
  assert.match(classifyFailure(repository, output, selection, 1).summary, /Synthetic\.FailureTests\.One.*six-enemy/);
  fs.writeFileSync(file, trx('Expected six-enemy rejection').replace('total="2"', 'total="3"'));
  assert.throws(() => classifyFailure(repository, output, selection, 1), (error: unknown) => {
    assert.ok(error instanceof CheckEvidenceError);
    assert.equal(error instanceof QuiescenceUnknownError, false);
    assert.match(error.message, /native exit 1/i);
    assert.match(error.message, /Incomplete counter: total/);
    assert.ok(error.message.includes(output));
    return true;
  });
  const evidence = JSON.parse(fs.readFileSync(path.join(output, 'failure-classification.json'), 'utf8'));
  assert.equal(evidence.nativeExit, 1);
  assert.equal(evidence.errorMsg, "Test step 'Pal.Core.UnitTests' failed with exit code 1.");
  assert.match(evidence.cause, /Incomplete counter: total/);
  fs.writeFileSync(file, trx('Different assertion'));
  assert.throws(() => classifyFailure(repository, output, { ...selection, expectedFailure: { test: 'Synthetic.FailureTests.One', message: 'Expected six-enemy rejection' } }, 1), /declared test and literal/);
  fs.writeFileSync(file, trx('Different assertion', '<RunInfos><RunInfo outcome="Error">Test Run Aborted</RunInfo></RunInfos>'));
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
  fs.writeFileSync(path.join(output, 'stdout.log'), 'Test Run Aborted');
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
});
test('TRX-only abnormal markers take precedence over incomplete normal evidence', () => {
  const output = fs.mkdtempSync(path.join(directory, 'trx-only-'));
  writePalSummary(output, {
    command: 'test unit',
    status: 'failed',
    errorMsg: "Test step 'Pal.Core.UnitTests' failed with exit code 1.",
    counts: { total: 2, passed: 1, failed: 1, skipped: 0 },
    trxFiles: ['trx/Pal.Core.UnitTests.trx'],
  });
  fs.writeFileSync(path.join(output, 'stdout.log'), 'Failed: 1');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  const file = path.join(output, 'pal/trx/Pal.Core.UnitTests.trx');
  const cases = [
    trx('assertion').replace('outcome="Failed"><Counters', 'outcome="Aborted"><Counters'),
    trx('assertion').replace('outcome="Passed"', 'outcome="Timeout"'),
    trx('assertion').replace('aborted="0"', 'aborted="1"'),
    trx('assertion', '<RunInfos><RunInfo outcome="Timeout">expired</RunInfo></RunInfos>').replace('total="2"', 'total="9"'),
    trx('TimeoutException').replace('outcome="Passed"', 'outcome="NotExecuted"'),
  ];
  for (const xml of cases) {
    fs.writeFileSync(file, xml);
    assert.throws(() => classifyFailure(repository, output, { scope: 'unit' }, 1), (error: unknown) => {
      assert.ok(error instanceof QuiescenceUnknownError, 'TRX abnormal must retain unknown quiescence');
      assert.match(error.message, /check:abnormal/);
      return true;
    });
  }
  fs.writeFileSync(file, trx('assertion').replace('outcome="Passed"', 'outcome="Unrecognized"'));
  assert.throws(() => classifyFailure(repository, output, { scope: 'unit' }, 1), CheckEvidenceError);
});
test('pal.ps1 compiler errors without TRX classify as bounded correction failures while setup errors surface errorMsg', () => {
  const output = fs.mkdtempSync(path.join(directory, 'pal-compile-'));
  writePalSummary(output, {
    command: 'test game',
    status: 'failed',
    errorMsg: "Test step 'Pal.Game.Tests' failed with exit code 1.",
    counts: { total: 0, passed: 0, failed: 0, skipped: 0 },
    trxFiles: [],
  });
  fs.writeFileSync(path.join(output, 'stdout.log'), 'src/Pal.Game/Scripts/Foo.cs(12,5): error CS1002: ; expected');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  const selection = { scope: 'game' as const, category: 'Profiles' as const };
  const outcome = classifyFailure(repository, output, selection, 1);
  assert.equal(outcome.code, 1);
  assert.equal(outcome.expectedRed, false);
  assert.match(outcome.summary, /compiler\/analyzer error/i);
  assert.match(outcome.summary, /Pal\.Game\.Tests/);

  fs.writeFileSync(path.join(output, 'stdout.log'), 'no compiler diagnostic');
  writePalSummary(output, {
    command: 'test game',
    status: 'error',
    errorMsg: 'Godot version mismatch: expected 4.7.2.',
    counts: { total: 0, passed: 0, failed: 0, skipped: 0 },
    trxFiles: [],
  });
  assert.throws(() => classifyFailure(repository, output, selection, 1), (error: unknown) => {
    assert.ok(error instanceof CheckEvidenceError);
    assert.match(error.message, /Godot version mismatch/);
    return true;
  });

  writePalSummary(output, {
    command: 'test game',
    status: 'error',
    errorMsg: 'Existing Godot process detected (godot(1234)); stop and confirm ownership before starting Godot.',
    counts: { total: 0, passed: 0, failed: 0, skipped: 0 },
    trxFiles: [],
  });
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
});
test('pal.ps1 completion must come from run-summary.json and an existing TRX, never a wrapper exit alone', () => {
  const output = fs.mkdtempSync(path.join(directory, 'pal-complete-'));
  assert.throws(() => palCompleted(output, 'game'), /run-summary\.json/);
  writePalSummary(output, { command: 'test game', status: 'failed', trxFiles: ['trx/Pal.Game.Tests.trx'] });
  assert.throws(() => palCompleted(output, 'game'), /complete selected/);
  writePalSummary(output, { command: 'test integration', status: 'passed', trxFiles: ['trx/Pal.Prepare.Tests.trx'] });
  fs.writeFileSync(path.join(output, 'pal/trx/Pal.Prepare.Tests.trx'), trx('unused'), 'utf8');
  assert.throws(() => palCompleted(output, 'game'), /complete selected/);
  palCompleted(output, 'prepare');
});
test('fixture negative mutations assert field existence rather than silently removing an obsolete address', () => {
  const source = fs.readFileSync(path.join(repository, 'tests/Pal.Game.Tests/Tests/AuthoredStoryFixture.cs'), 'utf8');
  const negativeBlock = source.match(/if \(missingPresentation == "effect"\)[\s\S]*?if \(missingPresentation == "idle"\)[^\n]*/)?.[0] ?? '';
  assert.ok(negativeBlock.length > 0, 'missingPresentation negative-mutation block must exist');
  assert.doesNotMatch(negativeBlock, /\.AsObject\(\)\.Remove\(/);
  assert.match(source, /if\s*\(!owner\.Remove\(field\)\)\s*throw new InvalidOperationException/);
  assert.equal((negativeBlock.match(/RemoveRequired\([^\n]+"(?:attackEffectSpriteUri|attackEffectIndex|numberSpriteUri|idleAnimSpeed)"\)/g) ?? []).length, 4);
});
test('PID-only helper and check-failure.ps1 parse through the existing PowerShell syntax entry', () => {
  for (const script of ['tools/long-running-agent/engine-processes.ps1', 'tools/long-running-agent/check-failure.ps1']) {
    const result = native(repository, 'pwsh', ['-NoProfile', '-File', path.join(repository, 'scripts/Test-PowerShellSyntax.ps1'), '-Path', script]);
    assert.match(result, /PASS/);
  }
  const source = fs.readFileSync(path.join(here, 'engine-processes.ps1'), 'utf8');
  assert.doesNotMatch(source, /CommandLine|ParentProcessId|Win32_Process/);
  assert.match(source, /Select-Object Id, ProcessName/);
});
test('engine-processes.ps1 emits UTF8 JSON arrays for zero, one and multiple PID/Name rows without probing engines', () => {
  const fixture = path.join(directory, '进程 JSON fixture.ps1');
  fs.writeFileSync(fixture, [
    'param([int]$Count)',
    'function Get-Process {',
    '  param($Name, $ErrorAction)',
    '  if ($Name -ne "*godot*") { throw "Unexpected process query" }',
    '  if ($Count -ge 1) { [pscustomobject]@{ Id = 101; ProcessName = "godot 合成 one"; PrivateField = "not exported" } }',
    '  if ($Count -ge 2) { [pscustomobject]@{ Id = 202; ProcessName = "godot two"; PrivateField = "not exported" } }',
    '}',
    `. '${path.join(here, 'engine-processes.ps1').replaceAll("'", "''")}'`,
  ].join('\n'), 'utf8');
  const expected = [
    [],
    [{ Id: 101, ProcessName: 'godot 合成 one' }],
    [{ Id: 101, ProcessName: 'godot 合成 one' }, { Id: 202, ProcessName: 'godot two' }],
  ];
  for (const count of [0, 1, 2]) {
    const raw = native(directory, 'pwsh', ['-NoProfile', '-NonInteractive', '-File', fixture, '-Count', String(count)]);
    fs.writeFileSync(path.join(directory, `pid-name-${count}.json`), raw, 'utf8');
    assert.deepEqual(JSON.parse(raw), expected[count]);
  }
});
test('offline CI guard wires strict typing and regressions separately from the game Unit workflow', () => {
  const workflow = fs.readFileSync(path.join(repository, '.github/workflows/long-running-agent.yml'), 'utf8');
  assert.match(workflow, /npm ci --prefix tools\/long-running-agent --ignore-scripts/);
  assert.match(workflow, /npm --prefix tools\/long-running-agent run typecheck/);
  assert.match(workflow, /npm --prefix tools\/long-running-agent test/);
  assert.match(workflow, /PAL_PI_SDK_ROOT/);
  assert.doesNotMatch(workflow, /cli\.ts run|GODOT_BIN|api[_-]?key/i);
});

test('missingUidScripts scans only the Godot project root and skips deleted committed uids', () => {
  const repo = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-uid-scan-'));
  spawnSync('git', ['init', '-q'], { cwd: repo, shell: false });
  const project = path.join(repo, 'src', 'Pal.Game');
  fs.mkdirSync(project, { recursive: true });
  fs.writeFileSync(path.join(project, 'project.godot'), '');
  // New untracked script without .uid -> reported for stabilization.
  fs.writeFileSync(path.join(project, 'new.cs'), '');
  // Tracked script whose committed .uid was deleted -> NOT reported (deletion must stay visible).
  fs.writeFileSync(path.join(project, 'old.cs'), '');
  fs.writeFileSync(path.join(project, 'old.cs.uid'), 'uid://old');
  spawnSync('git', ['add', 'src/Pal.Game/old.cs', 'src/Pal.Game/old.cs.uid'], { cwd: repo, shell: false });
  fs.rmSync(path.join(project, 'old.cs.uid'));
  // Script outside the Godot project root -> ignored (Godot never gives it a uid).
  fs.mkdirSync(path.join(repo, 'tests'));
  fs.writeFileSync(path.join(repo, 'tests', 'x.cs'), '');
  assert.equal(godotProjectRoot(repo), project);
  assert.deepEqual(missingUidScripts(repo, project), ['src/Pal.Game/new.cs']);
});

test('runGodotImport rejects a start failure through the async runCheck path', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-uid-import-'));
  await assert.rejects(() => runGodotImport(dir, path.join(dir, 'no-such-godot-binary'), dir, new AbortController().signal), /ENOENT|failed to start/);
});
test('stabilizeGodotUids is a no-op without a Godot project root or with no missing uid', async () => {
  const noProject = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-uid-noproj-'));
  const engine = { godotPath: path.join(noProject, 'no-godot') };
  await assert.doesNotReject(() => stabilizeGodotUids(noProject, engine, noProject, new AbortController().signal));
  const complete = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-uid-complete-'));
  const project = path.join(complete, 'src', 'Pal.Game');
  fs.mkdirSync(project, { recursive: true });
  fs.writeFileSync(path.join(project, 'project.godot'), '');
  fs.writeFileSync(path.join(project, 'a.cs'), '');
  fs.writeFileSync(path.join(project, 'a.cs.uid'), 'uid://a');
  await assert.doesNotReject(() => stabilizeGodotUids(complete, engine, complete, new AbortController().signal));
});

test('abnormal pal.ps1 exits (non-1) surface run-summary.json status and errorMsg instead of a bare message', () => {
  const output = fs.mkdtempSync(path.join(directory, 'pal-attr-'));
  fs.writeFileSync(path.join(output, 'stdout.log'), 'pal.ps1 stdout');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  writePalSummary(output, {
    command: 'test game',
    status: 'error',
    errorMsg: 'Test step Pal.Game.Tests timed out.',
    counts: { total: 0, passed: 0, failed: 0, skipped: 0 },
    trxFiles: [],
  });
  const selection = { scope: 'game' as const, category: 'Dialogue' as const };
  assert.throws(() => classifyFailure(repository, output, selection, 2), (error: unknown) => {
    assert.ok(error instanceof QuiescenceUnknownError);
    assert.match((error as Error).message, /timed out/);
    assert.match((error as Error).message, /status=error/);
    assert.match((error as Error).message, /command=test game/);
    return true;
  });
});

test('uidChangeViolation flags a rebuilt deleted uid and a rewritten existing uid', () => {
  const before = new Map<string, string | null>([['a.uid', null], ['b.uid', 'h1'], ['c.uid', 'h2']]);
  assert.equal(uidChangeViolation(before, new Map([['a.uid', 'hA'], ['b.uid', 'h1'], ['c.uid', 'h2']])), 'a.uid');
  assert.equal(uidChangeViolation(before, new Map([['a.uid', null], ['b.uid', 'h1'], ['c.uid', 'h2b']])), 'c.uid');
  assert.equal(uidChangeViolation(before, new Map([['a.uid', null], ['b.uid', 'h1'], ['c.uid', 'h2']])), undefined);
});

test('missingUidScripts fails loudly when git cannot determine tracking (not a repo)', () => {
  const notRepo = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-uid-notgit-'));
  const project = path.join(notRepo, 'src', 'Pal.Game');
  fs.mkdirSync(project, { recursive: true });
  fs.writeFileSync(path.join(project, 'project.godot'), '');
  fs.writeFileSync(path.join(project, 'a.cs'), '');
  assert.throws(() => missingUidScripts(notRepo, project), /git ls-files failed|Cannot determine tracking/);
});
