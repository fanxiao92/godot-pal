import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { native } from './io.ts';
import { spawnSync } from 'node:child_process';
import { rangeCompleted, classifyFailure, CheckEvidenceError, godotProjectRoot, missingUidScripts, runGodotImport, stabilizeGodotUids, uidChangeViolation } from './checks.ts';
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

test('existing identity reader distinguishes exact UTF8 RED, wrong failure and abnormal/partial sessions', () => {
  const file = path.join(directory, 'results.trx');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-RepositoryRoot', repository, '-TrxPath', file, '-ExpectedTest', 'Synthetic.FailureTests.One', '-ExpectedMessage', '缺少 slot0 idleAnimSpeed'];
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
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-RepositoryRoot', repository, '-TrxPath', file];
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
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-RepositoryRoot', repository, '-TrxPath', file];
  fs.writeFileSync(file, trx('Expected six-enemy rejection', '<RunInfos><RunInfo outcome="Error">[xUnit.net] Synthetic.FailureTests.One [FAIL]</RunInfo></RunInfos>'));
  assert.equal(JSON.parse(native(repository, 'pwsh', [...args, '-Scope', 'unit'])).classification, 'normal_test_failure');
  assert.equal(JSON.parse(native(repository, 'pwsh', [...args, '-Scope', 'game'])).classification, 'normal_test_failure');
  fs.writeFileSync(file, trx('Expected six-enemy rejection', '<RunInfos><RunInfo outcome="Error">Test Run Aborted</RunInfo></RunInfos>'));
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'unit']), /check:abnormal/);
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'game']), /check:abnormal/);
});
test('GdUnit ordinary assertion RunInfo Error reports the failed case instead of abnormal across scopes', () => {
  // Mirrors the real #207 Game TRX: complete counters, one failed case and GdUnit's routine RunInfo
  // outcome="Error" echoing the plain assertion text; see classifier-round/real-trx-facts.txt.
  const file = path.join(directory, 'gdunit-assertion-runinfo.trx');
  const xml = `<?xml version="1.0" encoding="utf-8"?>
<TestRun id="11111111-1111-1111-1111-111111111111" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult testId="a" executionId="e1" testName="Pal.Game.Tests.NativeBattleContentTests.Synthetic.SyntheticCase:33 (&quot;enemies-overcapacity&quot;)" outcome="Failed"><Output><ErrorInfo><Message>Expecting: 'True' but is 'False'</Message></ErrorInfo></Output></UnitTestResult><UnitTestResult testId="b" executionId="e2" testName="Pal.Game.Tests.NativeBattleContentTests.Synthetic.SyntheticOther" outcome="Passed"/></Results>
<TestDefinitions><UnitTest id="a" name="SyntheticCase:33 (&quot;enemies-overcapacity&quot;)"><Execution id="e1"/><TestMethod adapterTypeName="executor://gdunit4.testadapter/" className="Pal.Game.Tests.NativeBattleContentTests.Synthetic" name="SyntheticCase:33 (&quot;enemies-overcapacity&quot;)"/></UnitTest><UnitTest id="b" name="SyntheticOther"><Execution id="e2"/><TestMethod adapterTypeName="executor://gdunit4.testadapter/" className="Pal.Game.Tests.NativeBattleContentTests.Synthetic" name="SyntheticOther"/></UnitTest></TestDefinitions>
<TestEntries><TestEntry testId="a" executionId="e1"/><TestEntry testId="b" executionId="e2"/></TestEntries>
<ResultSummary outcome="Failed"><Counters total="2" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" notExecuted="0"/><RunInfos><RunInfo outcome="Error">Expecting: 'True' but is 'False'</RunInfo></RunInfos></ResultSummary></TestRun>`;
  fs.writeFileSync(file, xml, 'utf8');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-RepositoryRoot', repository, '-TrxPath', file];
  for (const scope of ['unit', 'game'] as const) {
    const result = JSON.parse(native(repository, 'pwsh', [...args, '-Scope', scope]));
    assert.equal(result.classification, 'normal_test_failure');
    assert.equal(result.failed, 1);
    assert.match(result.failures[0].test, /enemies-overcapacity/);
    assert.match(result.failures[0].message, /Expecting: 'True' but is 'False'/);
  }
});
test('failure feedback preserves the failed case, classifier cause, native exit and evidence without inventing unknown quiescence', () => {
  const output = fs.mkdtempSync(path.join(directory, 'feedback-'));
  fs.mkdirSync(path.join(output, 'trx'));
  const file = path.join(output, 'trx/results.trx');
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
  fs.mkdirSync(path.join(output, 'trx'));
  fs.writeFileSync(path.join(output, 'stdout.log'), 'Failed: 1');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  const file = path.join(output, 'trx/results.trx');
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
test('range warning-delta gate verdicts classify as complete normal failures instead of unknown quiescence', () => {
  const output = fs.mkdtempSync(path.join(directory, 'warning-delta-'));
  fs.mkdirSync(path.join(output, 'range', 'game'), { recursive: true });
  fs.writeFileSync(path.join(output, 'stdout.log'), 'range wrapper stdout');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  // Mirrors the real #209 attempt shape: the build command itself exited cleanly (compiler warnings are
  // non-fatal) and Test-Range.ps1 threw its own [range:warning-delta] gate verdict after the command.
  const complete = { exitKnown: true, rootExited: true, outputComplete: true, cleanupComplete: true, exitCode: 0, failureKind: 'NormalExit', stdout: 'build.stdout.log', stderr: 'build.stderr.log' };
  const failure = '[range:warning-delta] game build emitted 16 compiler warnings (baseline 14). Fix the new warning or intentionally update scripts/warning-baseline.json in its own commit.';
  const writeReport = (failureText: string, command: object) => fs.writeFileSync(path.join(output, 'range', 'range-run.json'), JSON.stringify({ status: 'failed', failure: failureText, projects: [], commands: [command] }), 'utf8');
  const selection = { scope: 'game' as const, batch: 'native-map' as const };
  // A malformed TRX proves the gate verdict never consults the TRX classifier path.
  fs.writeFileSync(path.join(output, 'range', 'game', 'results.trx'), 'not a TRX: a gate verdict must not consult test results');
  writeReport(failure, complete);
  const outcome = classifyFailure(repository, output, selection, 1);
  assert.equal(outcome.code, 1);
  assert.equal(outcome.expectedRed, false);
  assert.equal(outcome.summary, failure);
  const evidence = JSON.parse(fs.readFileSync(path.join(output, 'failure-classification.json'), 'utf8'));
  assert.equal(evidence.status, 'classified');
  assert.equal(evidence.classification, 'gate_failure');
  assert.deepEqual(evidence.failures, []);
  assert.equal(classifyFailure(repository, output, { ...selection, expectedFailure: { test: 'Synthetic.Capability', message: 'Expected new capability' } }, 1).expectedRed, false, 'A gate verdict has no expectedFailure RED semantics');
  writeReport(failure, { ...complete, cleanupComplete: false });
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
  writeReport('[range:selection] Unknown Game batch', complete);
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
  fs.writeFileSync(path.join(output, 'range', 'build.stdout.log'), 'game build stdout without diagnostics');
  fs.writeFileSync(path.join(output, 'range', 'build.stderr.log'), '');
  fs.rmSync(path.join(output, 'range', 'game', 'results.trx'), { force: true });
  writeReport('[range:command-exit] game exited 1', { ...complete, exitCode: 1 });
  assert.throws(() => classifyFailure(repository, output, selection, 1), (error: unknown) => {
    assert.ok(error instanceof CheckEvidenceError, 'command-exit still walks the TRX classifier path unchanged');
    assert.match(error.message, /Missing test result after native exit 1/);
    return true;
  });
});
test('range discovery gate verdicts classify as complete normal failures instead of unknown quiescence', () => {
  const output = fs.mkdtempSync(path.join(directory, 'discovery-'));
  fs.mkdirSync(path.join(output, 'range', 'prepare'), { recursive: true });
  fs.writeFileSync(path.join(output, 'stdout.log'), 'range wrapper stdout');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  // Mirrors the real #209 attempt 0264f751 repair-1 prepare shape: the discover command itself exited
  // cleanly and Test-Range.ps1 threw its own [range:discovery] count gate after the command.
  const complete = { exitKnown: true, rootExited: true, outputComplete: true, cleanupComplete: true, exitCode: 0, failureKind: 'NormalExit', stdout: 'prepare.discover.stdout.log', stderr: 'prepare.discover.stderr.log' };
  const failure = '[range:discovery] Prepare must discover exactly the 101 approved cases, got 102.';
  const writeReport = (failureText: string, command: object) => fs.writeFileSync(path.join(output, 'range', 'range-run.json'), JSON.stringify({ status: 'failed', failure: failureText, projects: [], commands: [command] }), 'utf8');
  const selection = { scope: 'prepare' as const };
  // A malformed TRX proves the gate verdict never consults the TRX classifier path.
  fs.writeFileSync(path.join(output, 'range', 'prepare', 'results.trx'), 'not a TRX: a gate verdict must not consult test results');
  writeReport(failure, complete);
  const outcome = classifyFailure(repository, output, selection, 1);
  assert.equal(outcome.code, 1);
  assert.equal(outcome.expectedRed, false);
  assert.equal(outcome.summary, failure);
  const evidence = JSON.parse(fs.readFileSync(path.join(output, 'failure-classification.json'), 'utf8'));
  assert.equal(evidence.status, 'classified');
  assert.equal(evidence.classification, 'gate_failure');
  assert.deepEqual(evidence.failures, []);
  assert.equal(classifyFailure(repository, output, { ...selection, expectedFailure: { test: 'Synthetic.Capability', message: 'Expected new capability' } }, 1).expectedRed, false, 'A gate verdict has no expectedFailure RED semantics');
  writeReport(failure, { ...complete, cleanupComplete: false });
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
  for (const usage of ['[range:selection] Unknown Game batch', '[range:filter-syntax] Explicit Filter must not be empty.', '[range:batch-scope] Game requires explicit positive GameBatch selection; other scopes do not accept batches.', '[range:prerequisite] Prepare/Game require explicit existing GodotPath.', '[range:output-exists] Use a new output directory', '[range:evidence] Cannot record a new command.']) {
    writeReport(usage, complete);
    assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError, `Usage prefix must keep unknown quiescence: ${usage}`);
  }
  fs.writeFileSync(path.join(output, 'range', 'prepare.discover.stdout.log'), 'prepare discovery stdout without diagnostics');
  fs.writeFileSync(path.join(output, 'range', 'prepare.discover.stderr.log'), '');
  fs.rmSync(path.join(output, 'range', 'prepare', 'results.trx'), { force: true });
  writeReport('[range:command-exit] prepare exited 1', { ...complete, exitCode: 1 });
  assert.throws(() => classifyFailure(repository, output, selection, 1), (error: unknown) => {
    assert.ok(error instanceof CheckEvidenceError, 'command-exit still walks the TRX classifier path unchanged');
    assert.match(error.message, /Missing test result after native exit 1/);
    return true;
  });
});
test('range source-count gate verdicts classify as complete normal failures instead of unknown quiescence', () => {
  const output = fs.mkdtempSync(path.join(directory, 'source-count-'));
  fs.mkdirSync(path.join(output, 'range', 'game'), { recursive: true });
  fs.writeFileSync(path.join(output, 'stdout.log'), 'range wrapper stdout');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  // Mirrors the real #143 attempt f9eacb62 game-oYuPvv shape: the game discovery command exited cleanly
  // and GameTestBatches.ps1 threw its batch source-count gate verdict after the command.
  const complete = { exitKnown: true, rootExited: true, outputComplete: true, cleanupComplete: true, exitCode: 0, failureKind: 'NormalExit', stdout: 'game.discover.stdout.log', stderr: 'game.discover.stderr.log' };
  const failure = '[range:source-count] Batch scene-realfull full source count differs: 39/40.';
  const writeReport = (failureText: string, command: object) => fs.writeFileSync(path.join(output, 'range', 'range-run.json'), JSON.stringify({ status: 'failed', failure: failureText, projects: [], commands: [command] }), 'utf8');
  const selection = { scope: 'game' as const, batch: 'scene-realfull' as const };
  // A malformed TRX proves the gate verdict never consults the TRX classifier path.
  fs.writeFileSync(path.join(output, 'range', 'game', 'results.trx'), 'not a TRX: a gate verdict must not consult test results');
  writeReport(failure, complete);
  const outcome = classifyFailure(repository, output, selection, 1);
  assert.equal(outcome.code, 1);
  assert.equal(outcome.expectedRed, false);
  assert.equal(outcome.summary, failure);
  const evidence = JSON.parse(fs.readFileSync(path.join(output, 'failure-classification.json'), 'utf8'));
  assert.equal(evidence.status, 'classified');
  assert.equal(evidence.classification, 'gate_failure');
  assert.deepEqual(evidence.failures, []);
  assert.equal(classifyFailure(repository, output, { ...selection, expectedFailure: { test: 'Synthetic.Capability', message: 'Expected new capability' } }, 1).expectedRed, false, 'A gate verdict has no expectedFailure RED semantics');
  // The unprefixed bare text of the real #143 attempt stays unknown quiescence: only the prefixed
  // verdict thrown by GameTestBatches.ps1 qualifies as an ordinary gate failure.
  writeReport('Batch scene-realfull full source count differs: 39/40.', complete);
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
  writeReport(failure, { ...complete, cleanupComplete: false });
  assert.throws(() => classifyFailure(repository, output, selection, 1), QuiescenceUnknownError);
});
test('range completion must come from the selected existing contract, never a wrapper exit alone', () => {
  const output = path.join(directory, 'range'); fs.mkdirSync(output);
  const file = path.join(output, 'range-run.json');
  fs.writeFileSync(file, JSON.stringify({ status: 'incomplete', projects: [] }), 'utf8');
  assert.throws(() => rangeCompleted(directory, 'game'), /complete selected/);
  fs.writeFileSync(file, JSON.stringify({ status: 'completed', projects: [{ scope: 'prepare' }] }), 'utf8');
  assert.throws(() => rangeCompleted(directory, 'game'), /complete selected/);
  rangeCompleted(directory, 'prepare');
});
test('fixture negative mutations assert field existence rather than silently removing an obsolete address', () => {
  const source = fs.readFileSync(path.join(repository, 'src/Pal.Game/Tests/AuthoredStoryFixture.cs'), 'utf8');
  assert.doesNotMatch(source, /\.AsObject\(\)\.Remove\(/);
  assert.match(source, /if\s*\(!owner\.Remove\(field\)\)\s*throw new InvalidOperationException/);
  assert.equal((source.match(/RemoveRequired\([^\n]+"(?:attackEffectSpriteUri|attackEffectIndex|numberSpriteUri|idleAnimSpeed)"\)/g) ?? []).length, 4);
});
test('new PID-only helper parses through the existing PowerShell syntax entry', () => {
  const result = native(repository, 'pwsh', ['-NoProfile', '-File', path.join(repository, 'scripts/Test-PowerShellSyntax.ps1'), '-Path', 'tools/long-running-agent/engine-processes.ps1']);
  assert.match(result, /PASS/);
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
  const engine = { godotPath: path.join(noProject, 'no-godot'), fixedPackageDirectory: noProject };
  await assert.doesNotReject(() => stabilizeGodotUids(noProject, engine, noProject, new AbortController().signal));
  const complete = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-uid-complete-'));
  const project = path.join(complete, 'src', 'Pal.Game');
  fs.mkdirSync(project, { recursive: true });
  fs.writeFileSync(path.join(project, 'project.godot'), '');
  fs.writeFileSync(path.join(project, 'a.cs'), '');
  fs.writeFileSync(path.join(project, 'a.cs.uid'), 'uid://a');
  await assert.doesNotReject(() => stabilizeGodotUids(complete, engine, complete, new AbortController().signal));
});

test('abnormal range failures (exit 2) surface failureKind and failure text instead of a bare message', () => {
  const output = fs.mkdtempSync(path.join(directory, 'range-attr-'));
  fs.mkdirSync(path.join(output, 'range', 'game'), { recursive: true });
  fs.writeFileSync(path.join(output, 'stdout.log'), 'range wrapper stdout');
  fs.writeFileSync(path.join(output, 'stderr.log'), '');
  // Mirrors scripts/Test-Range.ps1 abnormal-session: exitCode 2, status 'incomplete', outputComplete false,
  // failureKind OutputDrainTimeout (the #213 shape that previously yielded only the generic message).
  const abnormal = { exitKnown: true, rootExited: true, outputComplete: false, cleanupComplete: true, exitCode: 1, failureKind: 'OutputDrainTimeout', stdout: 'game.stdout.log', stderr: 'game.stderr.log' };
  const failure = '[range:abnormal-session] game.test lacks successful root/EOF/cleanup completion; failureKind=OutputDrainTimeout. Stop; retain.';
  fs.writeFileSync(path.join(output, 'range', 'range-run.json'), JSON.stringify({ status: 'incomplete', failure, projects: [], commands: [abnormal] }), 'utf8');
  const selection = { scope: 'game' as const, batch: 'scene-dialogue' as const };
  assert.throws(() => classifyFailure(repository, output, selection, 2), (error: unknown) => {
    assert.ok(error instanceof QuiescenceUnknownError);
    assert.match((error as Error).message, /OutputDrainTimeout/);
    assert.match((error as Error).message, /abnormal-session/);
    assert.match((error as Error).message, /outputComplete=false/);
    assert.match((error as Error).message, /status=incomplete/);
    return true;
  });
});

test('uidChangeViolation flags a rebuilt deleted uid and a rewritten existing uid', () => {
  const before = new Map<string, string | null>([['a.uid', null], ['b.uid', 'h1'], ['c.uid', 'h2']]);
  // Import rebuilt the deleted a.uid and left b/c untouched -> violation on a.uid.
  assert.equal(uidChangeViolation(before, new Map([['a.uid', 'hA'], ['b.uid', 'h1'], ['c.uid', 'h2']])), 'a.uid');
  // Import rewrote c.uid -> violation on c.uid.
  assert.equal(uidChangeViolation(before, new Map([['a.uid', null], ['b.uid', 'h1'], ['c.uid', 'h2b']])), 'c.uid');
  // Nothing changed -> no violation (normal stabilization of untracked scripts is not a tracked uid).
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
