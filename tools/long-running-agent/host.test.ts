import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawnSync } from 'node:child_process';
import { parsePlan, parseReport, type Plan, type Issue, type AgentRequest } from './contract.ts';
import { Host, initialize, type Services } from './host.ts';
import { acquire, atomicJson, git, json, lockFile, native, QuiescenceUnknownError, recoverLock, snapshot, runCheck } from './io.ts';
import { loadSdk, PiExecutor, scopedPath, scopedTools, type PiSdk } from './pi.ts';
import { main } from './cli.ts';

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-agent-host-tests-'));
console.log(`Synthetic test evidence retained at ${root}`);
let sequence = 0;
function setup(overrides: Partial<Plan['approval']> = {}, count = 1) {
  const directory = path.join(root, String(++sequence)); fs.mkdirSync(directory);
  const cwd = path.join(directory, 'repo'); fs.mkdirSync(cwd);
  git(cwd, 'init', '--quiet'); git(cwd, 'config', 'user.name', 'Synthetic Test'); git(cwd, 'config', 'user.email', 'test@example.invalid');
  git(cwd, 'config', 'core.hooksPath', path.join(directory, 'no-hooks')); git(cwd, 'config', 'commit.gpgsign', 'false');
  fs.writeFileSync(path.join(cwd, 'a.txt'), 'before\n'); fs.writeFileSync(path.join(cwd, 'b.txt'), 'before\n');
  git(cwd, 'add', '.'); git(cwd, 'commit', '--quiet', '-m', 'Synthetic baseline');
  const plan: Plan = {
    version: 1, repository: 'fanxiao92/godot-pal', cwd: fs.realpathSync(cwd), baseHead: git(cwd, 'rev-parse', 'HEAD').trim(),
    sdkRoot: directory, agentDir: directory, provider: 'synthetic', model: 'synthetic',
    approval: { id: 'synthetic-approval', approvedBy: 'test-operator', deadlineUtc: new Date(Date.now() + 600_000).toISOString(), localCommits: false, autoAdvance: false, ...overrides },
    limits: { timeoutMs: 120_000, maxTurns: 10 }, prerequisites: [],
    tasks: Array.from({ length: count }, (_, index) => ({ issue: index + 1, dependsOn: index ? [index] : [], contextIssues: [], allowedWritePaths: [index ? 'b.txt' : 'a.txt'], readPaths: ['a.txt', 'b.txt'], checks: [{ kind: 'non_engine', executable: process.execPath, args: ['--version'], timeoutMs: 5000 }] })),
  };
  const planFile = path.join(directory, 'request.json'); atomicJson(planFile, plan);
  const stateDirectory = path.join(directory, 'state'); initialize(planFile, stateDirectory);
  const calls: string[] = [];
  const services: Services = {
    source: snapshot,
    issue: (_cwd, number) => ({ issue: { number, title: `Synthetic #${number}`, body: 'Approved synthetic behavior', state: 'OPEN', labels: [{ name: 'ready-for-agent' }], comments: [] } satisfies Issue, blockers: number > 1 ? [number - 1] : [] }),
    check: async (_cwd, _check, checkDirectory) => { atomicJson(path.join(checkDirectory, 'result.json'), { code: 0, synthetic: true }); },
    agent: { run: async request => {
      calls.push(`${request.task.issue}:${request.role}`);
      if (request.role === 'implement') fs.writeFileSync(path.join(cwd, request.task.allowedWritePaths[0]), `after-${request.task.issue}\n`);
      return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: 'Synthetic result', evidence: ['Independent synthetic fixture expectation'] };
    } },
  };
  return { cwd: plan.cwd, directory, plan, planFile, stateDirectory, services, calls, host: () => new Host(stateDirectory, services) };
}
const signal = () => new AbortController().signal;
test('plan rejects unknown fields, cycles, missing explicit authority and broad writable scope', () => {
  const f = setup();
  assert.throws(() => parsePlan({ ...f.plan, surprise: true }), /fields/);
  assert.throws(() => parsePlan({ ...f.plan, approval: { ...f.plan.approval, localCommits: undefined } }), /Explicit/);
  assert.throws(() => parsePlan({ ...f.plan, tasks: [{ ...f.plan.tasks[0], allowedWritePaths: ['../escape'] }] }), /relative path/);
  assert.throws(() => parsePlan({ ...f.plan, tasks: [{ ...f.plan.tasks[0], dependsOn: [99] }] }), /receipt/);
  assert.throws(() => parsePlan({ ...f.plan, tasks: [{ ...f.plan.tasks[0], dependsOn: [2] }, { ...f.plan.tasks[0], issue: 2, dependsOn: [1] }] }), /cycle/);
});
test('manual mode uses three independent roles, stops at candidate and never commits', async () => {
  const f = setup({}, 2);
  const execute = f.services.agent;
  f.services.agent = { run: async request => {
    if (request.role !== 'implement') assert.doesNotMatch(request.prompt, /Synthetic result/, 'Independent reviewers must not inherit implementation or peer verdicts');
    return execute.run(request);
  } };
  const result = await f.host().run(signal());
  assert.equal(result.tasks[1].phase, 'awaiting_acceptance'); assert.equal(result.tasks[2].phase, 'pending');
  assert.deepEqual(f.calls, ['1:implement', '1:standards', '1:spec']);
  assert.equal(git(f.cwd, 'rev-parse', 'HEAD').trim(), f.plan.baseHead);
  await assert.rejects(f.host().run(signal()), /manual acceptance/);
});
test('manual acceptance binds exact committed candidate and allows the next ticket', async () => {
  const f = setup({}, 2); await f.host().run(signal());
  git(f.cwd, 'add', 'a.txt'); git(f.cwd, 'commit', '--quiet', '-m', 'Explicit operator integration');
  const receipt = path.join(f.directory, 'operator.txt'); fs.writeFileSync(receipt, 'Synthetic operator reviewed candidate and gate.');
  f.host().accept(1, git(f.cwd, 'rev-parse', 'HEAD').trim(), receipt);
  const state = await f.host().run(signal());
  assert.equal(state.tasks[1].phase, 'accepted'); assert.equal(state.tasks[2].phase, 'awaiting_acceptance');
});
test('explicit automatic local commits advance a dependent queue without a main agent', async () => {
  const f = setup({ localCommits: true, autoAdvance: true }, 2);
  const state = await f.host().run(signal());
  assert.equal(state.tasks[1].phase, 'accepted'); assert.equal(state.tasks[2].phase, 'accepted');
  assert.equal(f.calls.length, 6); assert.equal(snapshot(f.cwd).dirty, false);
  assert.equal(git(f.cwd, 'rev-list', '--count', 'HEAD').trim(), '3');
  assert.equal((json(path.join(f.stateDirectory, 'receipts', `${state.tasks[2].commit}.json`)) as { scope: string }).scope.includes('local candidate only'), true);
});
test('failed native check stops without reviews, retry or downstream execution', async () => {
  const f = setup({ localCommits: true, autoAdvance: true }, 2);
  f.services.check = async () => { throw new Error('Synthetic known assertion failure'); };
  const state = await f.host().run(signal());
  assert.equal(state.tasks[1].phase, 'blocked'); assert.equal(state.tasks[2].phase, 'pending');
  assert.deepEqual(f.calls, ['1:implement']); assert.match(state.tasks[1].attempts[0].error!, /known assertion/);
  await assert.rejects(f.host().run(signal()), /retry approval/);
});
test('review failure, invalid final output and no-op implementation are not accepted', async () => {
  for (const mode of ['review', 'output', 'noop']) {
    const f = setup();
    f.services.agent = { run: async request => {
      if (request.role === 'implement' && mode !== 'noop') fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'changed');
      if (mode === 'output') return { status: 'passed', summary: 'invalid implementer verdict', evidence: ['fixture'] };
      return { status: request.role === 'implement' ? 'candidate' : 'changes_requested', summary: 'synthetic finding', evidence: ['fixture'] };
    } };
    assert.equal((await f.host().run(signal())).tasks[1].phase, 'blocked');
  }
  assert.throws(() => parseReport({ status: 'passed', summary: 'No evidence', evidence: [] }, 'spec'), /evidence/);
});
test('out-of-scope modifications and HEAD changes block candidate', async () => {
  for (const mode of ['scope', 'head']) {
    const f = setup();
    f.services.agent = { run: async () => {
      fs.writeFileSync(path.join(f.cwd, mode === 'scope' ? 'b.txt' : 'a.txt'), 'unexpected');
      if (mode === 'head') { git(f.cwd, 'add', '.'); git(f.cwd, 'commit', '--quiet', '-m', 'Unauthorized synthetic commit'); }
      return { status: 'candidate', summary: 'fixture', evidence: ['fixture'] };
    } };
    const state = await f.host().run(signal());
    assert.equal(state.tasks[1].phase, 'blocked'); assert.match(state.tasks[1].attempts[0].error!, /Out-of-scope|changed HEAD/);
  }
});
test('check and reviewer source mutations invalidate frozen evidence', async () => {
  for (const mode of ['check', 'review']) {
    const f = setup();
    if (mode === 'check') f.services.check = async (_cwd, _check, output) => { fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'mutated'); atomicJson(path.join(output, 'result.json'), { code: 0 }); };
    else {
      const agent = f.services.agent;
      f.services.agent = { run: async request => { const result = await agent.run(request); if (request.role === 'spec') fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'mutated'); return result; } };
    }
    const state = await f.host().run(signal());
    assert.equal(state.tasks[1].phase, 'blocked'); assert.match(state.tasks[1].attempts[0].error!, /modified candidate|changed during review/);
  }
});
test('live issue drift and undeclared blockers fail closed', async () => {
  const f = setup(); let reads = 0;
  const read = f.services.issue;
  f.services.issue = (cwd, issue) => { const response = read(cwd, issue); if (++reads > 1) response.issue.body = 'Changed specification'; return response; };
  assert.equal((await f.host().run(signal())).tasks[1].phase, 'blocked');
  const g = setup(); g.services.issue = (cwd, issue) => ({ ...read(cwd, issue), blockers: [999] });
  await assert.rejects(g.host().run(signal()), /Unbound live dependency/); assert.equal(g.calls.length, 0);
});
test('approval expiration and dirty initial source prevent any SDK dispatch', async () => {
  const f = setup(); fs.writeFileSync(path.join(f.cwd, 'b.txt'), 'external work');
  await assert.rejects(f.host().run(signal()), /clean integration/); assert.equal(f.calls.length, 0);
  const g = setup();
  const plan = { ...g.plan, approval: { ...g.plan.approval, deadlineUtc: '2000-01-01T00:00:00Z' } };
  atomicJson(path.join(g.stateDirectory, 'plan.json'), plan);
  assert.throws(() => g.host(), /identity mismatch/);
  atomicJson(g.planFile, plan);
  assert.throws(() => initialize(g.planFile, path.join(g.directory, 'expired')), /expired/);
});
test('repo-scoped lock excludes another state directory and never steals live owner', async () => {
  const f = setup(); const release = acquire(f.cwd, f.stateDirectory);
  try {
    assert.throws(() => acquire(f.cwd, path.join(f.directory, 'other')), /EEXIST/);
    assert.throws(() => recoverLock(f.cwd, f.stateDirectory), /still exists/);
    assert.throws(() => recoverLock(f.cwd, path.join(f.directory, 'other')), /Unknown writer/);
  } finally { release(); }
});
test('interrupted running state requires explicit recovery and a separate fresh retry', async () => {
  const f = setup();
  f.services.check = async () => { throw new Error('synthetic stop'); };
  await f.host().run(signal());
  const state = f.host().state(); state.tasks[1].phase = 'running'; atomicJson(path.join(f.stateDirectory, 'state.json'), state);
  await assert.rejects(f.host().run(signal()), /recovery/);
  assert.throws(() => f.host().recover(false, 'fixture'), /quiescence/);
  f.host().recover(true, 'Synthetic owner and descendants checked, no active writer');
  assert.equal(f.host().state().tasks[1].phase, 'blocked');
  const before = f.calls.length;
  assert.throws(() => f.host().retry(1, '0'.repeat(64), 'Explicit retry'), /fingerprint/);
  f.host().retry(1, snapshot(f.cwd).fingerprint, 'Explicit bounded fresh attempt');
  assert.equal(f.calls.length, before); assert.equal(f.host().state().tasks[1].phase, 'pending');
  f.services.check = async (_cwd, _check, output) => atomicJson(path.join(output, 'result.json'), { code: 0 });
  const next = await f.host().run(signal());
  assert.equal(next.tasks[1].attempts.length, 2); assert.notEqual(next.tasks[1].attempts[0].id, next.tasks[1].attempts[1].id);
});
test('manual acceptance rejects source drift and tampered gate', async () => {
  for (const mode of ['source', 'gate']) {
    const f = setup(); await f.host().run(signal());
    if (mode === 'source') fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'after-review mutation');
    else {
      const attempt = f.host().state().tasks[1].attempts[0]; fs.appendFileSync(path.join(f.stateDirectory, attempt.directory, 'gate.json'), ' ');
    }
    git(f.cwd, 'add', '.'); git(f.cwd, 'commit', '--quiet', '-m', 'Operator candidate');
    const receipt = path.join(f.directory, 'receipt.txt'); fs.writeFileSync(receipt, 'fixture');
    assert.throws(() => f.host().accept(1, snapshot(f.cwd).head, receipt), /exact frozen|Gate evidence/);
  }
});
test('accepted receipt tampering prevents downstream execution', async () => {
  const f = setup({ localCommits: true }, 2); await f.host().run(signal());
  const state = f.host().state(); fs.appendFileSync(path.join(f.stateDirectory, 'receipts', `${state.tasks[1].commit}.json`), ' ');
  await assert.rejects(f.host().run(signal()), /receipt changed/);
});
test('native check records actual exit, both logs and argv, with shell disabled', async () => {
  const f = setup(); const output = path.join(f.directory, 'native'); fs.mkdirSync(output);
  await runCheck(f.cwd, { kind: 'non_engine', executable: process.execPath, args: ['-e', 'process.stdout.write("synthetic-out"); process.stderr.write("synthetic-err")'], timeoutMs: 5000 }, output, signal());
  const result = json(path.join(output, 'result.json')) as { code: number; cwd: string };
  assert.equal(result.code, 0); assert.equal(result.cwd, f.cwd);
  assert.equal(fs.readFileSync(path.join(output, 'stdout.log'), 'utf8'), 'synthetic-out');
  assert.equal(fs.readFileSync(path.join(output, 'stderr.log'), 'utf8'), 'synthetic-err');
  const failed = path.join(f.directory, 'failed'); fs.mkdirSync(failed);
  await assert.rejects(runCheck(f.cwd, { kind: 'non_engine', executable: process.execPath, args: ['-e', 'process.exit(7)'], timeoutMs: 5000 }, failed, signal()), /failed/);
  assert.equal((json(path.join(failed, 'result.json')) as { code: number }).code, 7);
});
test('io native argv and retained check streams preserve Unicode, spaces and shell metacharacters', async () => {
  const cwd = path.join(root, '原生 cwd with spaces'); fs.mkdirSync(cwd);
  const script = path.join(cwd, '参数 fixture.mjs');
  fs.writeFileSync(script, 'process.stdout.write(JSON.stringify({cwd: process.cwd(), args: process.argv.slice(2)})); process.stderr.write("原始 stderr 雪\\n");', 'utf8');
  const args = [script, '含 空格', 'a&b;$(not-a-command)', 'quote"value', '尾部\\'];
  const expected = { cwd, args: ['含 空格', 'a&b;$(not-a-command)', 'quote"value', '尾部\\'] };
  assert.deepEqual(JSON.parse(native(cwd, process.execPath, args)), expected);
  const output = path.join(cwd, '双流 evidence'); fs.mkdirSync(output);
  await runCheck(cwd, { kind: 'non_engine', executable: process.execPath, args, timeoutMs: 5000 }, output, signal());
  assert.deepEqual(JSON.parse(fs.readFileSync(path.join(output, 'stdout.log'), 'utf8')), expected);
  assert.deepEqual(fs.readFileSync(path.join(output, 'stderr.log')), Buffer.from('原始 stderr 雪\n', 'utf8'));
  const result = json(path.join(output, 'result.json')) as { code: number; args: string[]; signal: null; timedOut: boolean };
  assert.equal(result.code, 0); assert.equal(result.signal, null); assert.equal(result.timedOut, false); assert.deepEqual(result.args, args);
});
test('path guard confines writes, reviewers, credentials, private assets and links', () => {
  const f = setup();
  const request: AgentRequest = { plan: f.plan, task: f.plan.tasks[0], role: 'implement', directory: f.directory, signal: signal(), emit: () => {}, prompt: 'fixture' };
  assert.equal(scopedPath(request, 'a.txt', true), path.join(f.cwd, 'a.txt'));
  assert.throws(() => scopedPath(request, '../outside', false), /escapes/);
  assert.throws(() => scopedPath(request, 'b.txt', true), /Write denied/);
  for (const file of ['a.txt.', 'a.txt ', 'a.txt:stream']) assert.throws(() => scopedPath(request, file, false), /relative path|escapes/);
  assert.throws(() => scopedPath({ ...request, role: 'spec' }, 'a.txt', true), /Write denied/);
  for (const file of ['.git/config', '.env', 'auth.json', 'data/raw/sdlpal/a', 'src/Pal.Game/Assets/private.tres']) assert.throws(() => scopedPath(request, file, false), /Protected|Private/);
  const outside = path.join(f.directory, 'external'); fs.mkdirSync(outside);
  fs.symlinkSync(outside, path.join(f.cwd, 'link'), process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => scopedPath(request, 'link/file', false), /link/);
});
test('cancellation stops before dispatch and CLI rejects irrelevant options', async () => {
  const f = setup(); const controller = new AbortController(); controller.abort();
  await assert.rejects(f.host().run(controller.signal), /cancelled/); assert.equal(f.calls.length, 0);
  await assert.rejects(main(['run', '--state', f.stateDirectory, '--confirm-quiescent']), /inappropriate/);
});
test('SDK executor uses fresh persisted sessions, no automatic retries and no hidden tools', async () => {
  const f = setup(); const options: Record<string, unknown>[] = []; let disposed = 0, sessionCount = 0;
  const tool = (name: string) => ({ name, label: name, description: name, parameters: {}, execute: async () => ({}) });
  const sdk = {
    createReadToolDefinition: () => tool('read'), createEditToolDefinition: () => tool('edit'), createWriteToolDefinition: () => tool('write'),
    SettingsManager: { inMemory: (settings: Record<string, unknown>) => { assert.deepEqual(settings.retry, { enabled: false }); return settings; } },
    SessionManager: { create: (_cwd: string, directory: string) => ({ directory, freshId: ++sessionCount }) },
    ModelRuntime: { create: async () => ({ getModel: () => ({ provider: 'synthetic', id: 'synthetic' }) }) },
    DefaultResourceLoader: class { constructor(settings: Record<string, unknown>) { assert.equal(settings.noExtensions, true); } async reload() {} },
    createAgentSession: async (input: Record<string, unknown>) => { options.push(input); return { session: {
      model: { provider: 'synthetic', id: 'synthetic' }, messages: [{ role: 'assistant', stopReason: 'stop' }],
      prompt: async () => {}, subscribe: () => () => {}, getActiveToolNames: () => input.tools as string[],
      getLastAssistantText: () => JSON.stringify({ status: 'passed', summary: 'synthetic', evidence: ['fixture'] }),
      abort: async () => {}, dispose: () => { disposed++; },
    } }; },
  } satisfies PiSdk;
  const executor = new PiExecutor(sdk);
  for (const role of ['standards', 'spec'] as const) await executor.run({ plan: f.plan, task: f.plan.tasks[0], role, prompt: 'fixture', directory: f.directory, signal: signal(), emit: () => {} });
  assert.equal(sessionCount, 2); assert.equal(disposed, 2);
  assert.deepEqual(options.map(o => o.tools), [['scoped_read', 'scoped_list'], ['scoped_read', 'scoped_list']]);
  assert.notDeepEqual(options[0].sessionManager, options[1].sessionManager);
  const typed: PiSdk = sdk;
  const original = typed.createAgentSession;
  for (const mode of ['error', 'invalid-json', 'tool-leak', 'turn-limit']) {
    typed.createAgentSession = async input => {
      const built = await original(input);
      let listener: (event: Record<string, unknown>) => void = () => {};
      built.session.subscribe = callback => { listener = callback; return () => {}; };
      if (mode === 'error') built.session.messages = [{ role: 'assistant', stopReason: 'error' }];
      if (mode === 'invalid-json') built.session.getLastAssistantText = () => 'not JSON';
      if (mode === 'tool-leak') built.session.getActiveToolNames = () => ['bash'];
      if (mode === 'turn-limit') built.session.prompt = async () => { for (let i = 0; i <= f.plan.limits.maxTurns; i++) listener({ type: 'turn_start' }); };
      return built;
    };
    await assert.rejects(executor.run({ plan: f.plan, task: f.plan.tasks[0], role: 'spec', prompt: 'fixture', directory: f.directory, signal: signal(), emit: () => {} }), /completed|JSON|capability|Turn limit/);
  }
});
test('real installed SDK wiring without provider calls', { skip: !process.env.PAL_PI_SDK_ROOT }, async () => {
  const sdk = await loadSdk(process.env.PAL_PI_SDK_ROOT!);
  const f = setup();
  const settingsManager = sdk.SettingsManager.inMemory({ retry: { enabled: false }, compaction: { enabled: false } });
  const loader = new sdk.DefaultResourceLoader({ cwd: f.cwd, agentDir: f.directory, settingsManager, noExtensions: true, noSkills: true, noThemes: true, noPromptTemplates: true }); await loader.reload();
  const runtime = await sdk.ModelRuntime.create({ authPath: path.join(f.directory, 'no-auth.json'), modelsPath: null, refreshOnCreate: false, allowModelNetwork: false });
  const model = runtime.getModel('anthropic', 'claude-sonnet-4-5'); assert.ok(model, 'Installed static catalog contains the selected smoke model');
  const request: AgentRequest = { plan: f.plan, task: f.plan.tasks[0], role: 'implement', directory: f.directory, signal: signal(), emit: () => {}, prompt: 'fixture' };
  const tools = scopedTools(sdk, request);
  const { session } = await sdk.createAgentSession({ cwd: f.cwd, agentDir: f.directory, modelRuntime: runtime, model, settingsManager, resourceLoader: loader, tools: tools.map(t => t.name), customTools: tools, sessionManager: sdk.SessionManager.create(f.cwd, path.join(f.directory, 'sessions')) });
  try {
    assert.deepEqual(session.getActiveToolNames().sort(), ['scoped_edit', 'scoped_list', 'scoped_read', 'scoped_write']);
    await tools.find(t => t.name === 'scoped_write')!.execute('fixture-write', { path: 'a.txt', content: 'SDK fixture' });
    assert.equal(fs.readFileSync(path.join(f.cwd, 'a.txt'), 'utf8'), 'SDK fixture');
    await assert.rejects(tools.find(t => t.name === 'scoped_write')!.execute('fixture-denied', { path: 'b.txt', content: 'denied' }), /Write denied/);
    assert.ok(session.sessionFile);
  } finally { await session.abort(); session.dispose(); }
});
test('real host-process exit preserves running intent and requires dead-owner reconciliation', async () => {
  const f = setup();
  const script = path.join(f.directory, 'synthetic-owner-exit.mjs');
  fs.writeFileSync(script, [
    `import { Host } from ${JSON.stringify(new URL('./host.ts', import.meta.url).href)};`,
    `import { snapshot, runCheck } from ${JSON.stringify(new URL('./io.ts', import.meta.url).href)};`,
    `const services = { source: snapshot, check: runCheck, issue: (_cwd, number) => ({issue: {number, title:'synthetic', body:'synthetic', state:'OPEN', labels:[{name:'ready-for-agent'}], comments:[]}, blockers:[]}), agent: {run: async () => { process.exit(17); }} };`,
    `await new Host(${JSON.stringify(f.stateDirectory)}, services).run(new AbortController().signal);`,
  ].join('\n'));
  const result = spawnSync(process.execPath, [script], { cwd: f.directory, encoding: 'utf8', timeout: 15_000, shell: false });
  atomicJson(path.join(f.directory, 'owner-exit-result.json'), { executable: process.execPath, args: [script], code: result.status, signal: result.signal, stdout: result.stdout, stderr: result.stderr });
  assert.equal(result.status, 17); assert.equal(result.signal, null);
  assert.equal(f.host().state().tasks[1].phase, 'running'); assert.ok(fs.existsSync(lockFile(f.cwd)));
  await assert.rejects(f.host().run(signal()), /EEXIST/);
  f.host().recover(true, 'Synthetic exited owner and all task descendants confirmed stopped');
  assert.equal(f.host().state().tasks[1].phase, 'blocked'); assert.equal(f.calls.length, 0);
  f.host().retry(1, snapshot(f.cwd).fingerprint, 'Explicit new attempt after synthetic exit');
  assert.equal((await f.host().run(signal())).tasks[1].phase, 'awaiting_acceptance');
});
test('unknown check quiescence keeps writer lock rather than allowing another run', async () => {
  const f = setup(); f.services.check = async () => { throw new QuiescenceUnknownError('Synthetic unknown descendants'); };
  const result = await f.host().run(signal());
  assert.equal(result.tasks[1].phase, 'blocked'); assert.equal(result.tasks[1].attempts[0].quiescenceUnknown, true);
  assert.ok(fs.existsSync(lockFile(f.cwd)));
  await assert.rejects(f.host().run(signal()), /EEXIST/);
  // Owner is this test process: a live owner must not be stolen, even with a confirmation.
  assert.throws(() => f.host().recover(true, 'fixture'), /still exists/);
});
test('native timeout is classified as unknown descendant quiescence with retained originals', async () => {
  const f = setup(), output = path.join(f.directory, 'timeout'); fs.mkdirSync(output);
  await assert.rejects(runCheck(f.cwd, { kind: 'non_engine', executable: process.execPath, args: ['-e', 'setInterval(() => {}, 1000)'], timeoutMs: 150 }, output, signal()), QuiescenceUnknownError);
  const result = json(path.join(output, 'result.json')) as { timedOut: boolean };
  assert.equal(result.timedOut, true); assert.ok(fs.existsSync(path.join(output, 'intent.json')));
});
test('uncommitted exact candidate cannot be manually accepted', async () => {
  const f = setup(); await f.host().run(signal());
  const receipt = path.join(f.directory, 'operator'); fs.writeFileSync(receipt, 'synthetic');
  assert.throws(() => f.host().accept(1, f.plan.baseHead, receipt), /exact frozen/);
});
test('parent and dependency issue contents are passed into the brief', async () => {
  const f = setup();
  // Create a new explicitly approved plan before initialization, never mutate active state.
  const plan = { ...f.plan, tasks: [{ ...f.plan.tasks[0], contextIssues: [126] }] };
  atomicJson(f.planFile, plan); const next = path.join(f.directory, 'with-context'); initialize(f.planFile, next);
  const agent = f.services.agent;
  f.services.agent = { run: async request => { assert.match(request.prompt, /Synthetic #126/); return agent.run(request); } };
  assert.equal((await new Host(next, f.services).run(signal())).tasks[1].phase, 'awaiting_acceptance');
});
test('source identity rejects hidden Git flags and records index changes', () => {
  const f = setup(); const before = snapshot(f.cwd);
  fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'staged fixture'); git(f.cwd, 'add', 'a.txt');
  assert.notEqual(snapshot(f.cwd).indexFingerprint, before.indexFingerprint);
  git(f.cwd, 'update-index', '--assume-unchanged', 'a.txt');
  assert.throws(() => snapshot(f.cwd), /assume-unchanged/);
});
test('explicit prerequisite requires a receipt and an actual ancestor commit', async () => {
  const f = setup(); const receipt = path.join(f.directory, 'upstream'); fs.writeFileSync(receipt, 'Synthetic independent acceptance receipt');
  const plan = { ...f.plan, prerequisites: [{ issue: 99, commit: f.plan.baseHead, receipt }], tasks: [{ ...f.plan.tasks[0], dependsOn: [99] }] };
  atomicJson(f.planFile, plan); const next = path.join(f.directory, 'with-prerequisite'); initialize(f.planFile, next);
  const read = f.services.issue; f.services.issue = (cwd, number) => ({ ...read(cwd, number), blockers: number === 1 ? [99] : [] });
  assert.equal((await new Host(next, f.services).run(signal())).tasks[1].phase, 'awaiting_acceptance');
  const g = setup();
  const bad = { ...g.plan, prerequisites: [{ issue: 99, commit: '0'.repeat(40), receipt }] };
  atomicJson(g.planFile, bad); assert.throws(() => initialize(g.planFile, path.join(g.directory, 'bad-prerequisite')), /failed/);
});

