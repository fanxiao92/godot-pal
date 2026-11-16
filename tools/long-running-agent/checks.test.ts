import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { native } from './io.ts';
import { rangeCompleted, classifyFailure, CheckEvidenceError } from './checks.ts';
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
test('Unit distinguishes an ordinary xUnit FAIL RunInfo from host abort while Game keeps its strict diagnostics', () => {
  const file = path.join(directory, 'unit-runinfo.trx');
  const args = ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'check-failure.ps1'), '-RepositoryRoot', repository, '-TrxPath', file];
  fs.writeFileSync(file, trx('Expected six-enemy rejection', '<RunInfos><RunInfo outcome="Error">[xUnit.net] Synthetic.FailureTests.One [FAIL]</RunInfo></RunInfos>'));
  assert.equal(JSON.parse(native(repository, 'pwsh', [...args, '-Scope', 'unit'])).classification, 'normal_test_failure');
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'game']), /check:abnormal/);
  fs.writeFileSync(file, trx('Expected six-enemy rejection', '<RunInfos><RunInfo outcome="Error">Test Run Aborted</RunInfo></RunInfos>'));
  assert.throws(() => native(repository, 'pwsh', [...args, '-Scope', 'unit']), /check:abnormal/);
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
