import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { native } from './io.ts';
import { rangeCompleted } from './checks.ts';

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
