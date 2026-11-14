import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawnSync } from 'node:child_process';
import type * as Pi from '@earendil-works/pi-coding-agent';
import { hash } from './contract.ts';
import { atomicJson, git, json, lockFile, QuiescenceUnknownError, snapshot } from './io.ts';
import { checklistChildren, loadGraph, stateDirectory, type GitHubReader, type IssueContext } from './github.ts';
import { GitHubHost, type CodingEvent, type CodingExecutor, type CodingRequest } from './github-host.ts';
import { codingResourceLoader, codingTools, GitHubPiExecutor } from './github-agent.ts';
import { formatStatus, githubMain, issueNumber } from './github-cli.ts';

const suite = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-github-coding-'));
console.log(`Synthetic GitHub workflow evidence retained at ${suite}`);
let sequence = 0;
const signal = () => new AbortController().signal;
function fixture(count = 2) {
  const directory = path.join(suite, String(++sequence)); fs.mkdirSync(directory);
  const cwd = path.join(directory, 'repo'); fs.mkdirSync(cwd);
  git(cwd, 'init', '--quiet'); git(cwd, 'config', 'user.name', 'Synthetic GitHub Test'); git(cwd, 'config', 'user.email', 'test@example.invalid');
  git(cwd, 'config', 'core.hooksPath', path.join(directory, 'no-hooks')); git(cwd, 'config', 'commit.gpgsign', 'false');
  fs.writeFileSync(path.join(cwd, 'a.txt'), 'baseline'); fs.writeFileSync(path.join(cwd, 'b.txt'), 'baseline');
  git(cwd, 'add', '.'); git(cwd, 'commit', '--quiet', '-m', 'Synthetic baseline');
  const head = git(cwd, 'rev-parse', 'HEAD').trim();
  const issues: Record<number, IssueContext> = {};
  const children: Record<number, number[]> = { 1: Array.from({ length: count }, (_, index) => index + 2) };
  const blockers: Record<number, number[]> = {};
  for (let number = 1; number <= count + 1; number++) issues[number] = {
    number, title: `Synthetic issue ${number}`, body: `Approved synthetic issue ${number}`, state: 'open', html_url: `https://github.com/fanxiao92/godot-pal/issues/${number}`,
    labels: [{ name: 'ready-for-agent' }], assignees: [], comments: [], sub_issues_summary: { total: number === 1 ? count : 0 },
  };
  const reader: GitHubReader = {
    issue: number => structuredClone(issues[number]),
    children: parent => ({ issues: (children[parent.number] ?? []).map(number => structuredClone(issues[number])) }),
    blockers: number => (blockers[number] ?? []).map(id => structuredClone(issues[id])),
    context: number => structuredClone(issues[number]),
  };
  const calls: string[] = [], events: CodingEvent[] = [];
  let agent: CodingExecutor = { run: async request => {
    calls.push(`${request.issue}:${request.role}`);
    if (request.role === 'implement') {
      fs.writeFileSync(path.join(cwd, request.issue === 2 ? 'a.txt' : 'b.txt'), `issue-${request.issue}`);
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(cwd).fingerprint, code: 0 });
    } else assert.doesNotMatch(request.prompt, /UNTRUSTED_IMPLEMENTER_VERDICT/);
    request.emit({ type: 'session', role: request.role, sessionFile: path.join(request.directory, 'synthetic-session.jsonl') });
    request.emit({ type: 'tool_start', role: request.role, tool: 'synthetic_tool' });
    return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: request.role === 'implement' ? 'UNTRUSTED_IMPLEMENTER_VERDICT' : 'Independent synthetic review', evidence: ['Synthetic requirement and source references'] };
  } };
  const state = path.join(directory, 'state');
  const host = () => new GitHubHost(fs.realpathSync(cwd), 1, state, { reader, source: snapshot, agent: { run: request => agent.run(request) } }, event => events.push(event));
  return { directory, cwd: fs.realpathSync(cwd), state, head, issues, children, blockers, reader, calls, events, host, setAgent: (next: CodingExecutor) => { agent = next; }, getAgent: () => agent };
}
test('bounded check and review repairs share one persistent ledger, without a manual restart', async () => {
  const f = fixture(1); let implementations = 0, reviews = 0;
  f.setAgent({ run: async request => {
    f.calls.push(`${request.issue}:${request.role}`);
    if (request.role === 'implement') {
      implementations++;
      if (implementations === 1) {
        request.recordFailure!(request.directory, snapshot(f.cwd), 'Located synthetic fixture error');
        assert.throws(() => request.beforeWrite!(), /request_correction/);
        request.requestCorrection!('Fixture addressed the old field', 'Use the approved slot field', 'Same approved per-slot contract');
      }
      request.beforeWrite!();
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `fixed-${implementations}`);
      request.recordCheck({ project: 'synthetic native', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    } else if (request.role === 'standards' && ++reviews === 1) {
      return { status: 'changes_requested', summary: 'Located omitted consumer within the same contract', evidence: ['a.txt consumer'] };
    }
    return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: 'Synthetic contract verified', evidence: ['Synthetic scope and native evidence'] };
  } });
  const result = await f.host().run({ commit: true }, signal());
  assert.equal(result.specStatus, 'passed');
  const ledger = f.host().state()!.records[2].corrections!;
  assert.deepEqual(ledger.map(item => item.kind), ['check', 'review']);
  assert.equal(implementations, 2); assert.equal(reviews, 2);
});
test('review exhaustion stays blocked across fresh attempts and explicit extras are separately recorded', async () => {
  const f = fixture(1); const agent = f.getAgent(); let fail = true, revisions = 0;
  f.setAgent({ run: async request => {
    if (request.role === 'standards' && fail) return { status: 'changes_requested', summary: 'Located same-contract repair', evidence: ['Synthetic consumer'] };
    const report = await agent.run(request);
    if (request.role === 'implement') {
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `synthetic repair ${++revisions}`);
      request.recordCheck({ project: 'synthetic native', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    }
    return report;
  } });
  const stopped = await f.host().run({ commit: true }, signal());
  assert.equal(stopped.counts.blocked, 1); assert.equal(f.host().state()!.records[2].corrections!.length, 2);
  await assert.rejects(f.host().run({}, signal()), /--resume/);
  fail = false;
  const resumed = await f.host().run({ commit: true, resume: true, extraCorrections: 1 }, signal());
  assert.equal(resumed.specStatus, 'passed');
  assert.equal(f.host().state()!.records[2].corrections!.length, 2, 'fresh sessions do not reset spent rounds');
  assert.equal(f.host().state()!.extraApprovals![0].count, 1);
});
test('twelve complete-impact files are allowed by real SDK source tools and reviewed, not hard-split', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1); const agent = f.getAgent();
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      const tools = codingTools(sdk, request, signal(), error => { throw error; });
      const write = tools.find(tool => tool.name === 'scoped_write')!;
      for (let index = 0; index < 12; index++) await write.execute('scope-test', { path: `consumer-${index}.txt`, content: 'One atomic synthetic contract migration' }, undefined, undefined, {} as Pi.ExtensionToolContext);
    }
    return agent.run(request);
  } });
  const result = await f.host().run({ commit: true }, signal());
  assert.equal(result.specStatus, 'passed'); assert.equal(result.counts.local_completed, 1);
  assert.ok(f.events.some(event => event.type === 'scope_warning'));
});
test('leaf exhaustion runs a separate parent audit, blocks on gaps and invalidates on source/spec/evidence drift', async () => {
  const f = fixture(1); const agent = f.getAgent(); let audits = 0;
  f.setAgent({ run: async request => {
    if (request.issue === 1) { audits++; return { status: 'blocked', summary: 'Parent clause has an uncovered consumer', evidence: ['Parent clause / consumer'] }; }
    return agent.run(request);
  } });
  const result = await f.host().run({ commit: true }, signal());
  assert.equal(result.counts.local_completed, 1); assert.equal(result.specStatus, 'blocked'); assert.equal(audits, 1);
  assert.match(formatStatus(result), /Parent clause has an uncovered consumer/);
  await f.host().run({ commit: true }, signal()); assert.equal(audits, 1, 'no blind repeat of an unchanged parent gap');
  const offline = f.host().status(true); assert.equal(offline.stale, true); assert.equal(offline.specStatus, 'stale');
  f.issues[1].comments.push({ body: 'Clarified parent acceptance', html_url: 'https://github.com/fanxiao92/godot-pal/issues/1#issuecomment-clarified' });
  assert.equal(f.host().status().specStatus, 'stale');
  await f.host().run({ commit: true }, signal()); assert.equal(audits, 2);
  const audit = f.host().state()!.parentAudit!;
  fs.appendFileSync(path.join(f.state, audit.directory, 'report.json'), ' ');
  assert.equal(f.host().status().specStatus, 'stale');
  await assert.rejects(f.host().run({ commit: true }, signal()), /explicit --resume/);
  await f.host().run({ commit: true, resume: true }, signal());
  assert.equal(audits, 3, 'explicit resume makes a fresh audit instead of accepting modified evidence');
});
test('live parent audit reports working/session/tool, never a completed leaf queue as a stopped audit', async () => {
  const f = fixture(1); f.issues[2].state = 'closed';
  f.setAgent({ run: async request => {
    assert.equal(f.host().status().specStatus, 'working');
    request.emit({ type: 'session', role: 'spec', sessionFile: path.join(request.directory, 'synthetic-session.jsonl') });
    request.emit({ type: 'tool_start', role: 'spec', tool: 'scoped_read', turns: 1 });
    const status = f.host().status();
    assert.equal(status.activity?.tool, 'scoped_read'); assert.ok(status.activity?.sessionFile);
    return { status: 'passed', summary: 'Synthetic parent coverage complete', evidence: ['Synthetic current-source clause proof'] };
  } });
  assert.equal((await f.host().run({}, signal())).specStatus, 'passed');
});
test('unknown parent-audit quiescence preserves the writer lock rather than auto-retrying', async () => {
  const f = fixture(1); f.issues[2].state = 'closed';
  f.setAgent({ run: async () => { throw new QuiescenceUnknownError('Synthetic SDK stop not confirmed'); } });
  await assert.rejects(f.host().run({}, signal()), /not confirmed/);
  assert.equal(fs.existsSync(lockFile(f.cwd)), true);
  assert.equal(f.host().state()!.parentAudit!.unknownQuiescence, true);
  assert.equal(f.host().status().specStatus, 'interrupted');
  assert.match(f.host().status().nextAction, /--confirm-quiescent/);
});
test('Issue selection accepts number/own URL, rejects other repositories and inappropriate options', async () => {
  assert.equal(issueNumber('126'), 126); assert.equal(issueNumber('https://github.com/fanxiao92/godot-pal/issues/126'), 126);
  for (const input of ['0', 'NaN', 'https://github.com/another/repo/issues/1']) assert.throws(() => issueNumber(input));
  await assert.rejects(githubMain(['status', '--issue', '1', '--commit']), /Inappropriate/);
  await assert.rejects(githubMain(['run', '--issue', '1', '--confirm-quiescent']), /requires --resume/);
  await assert.rejects(githubMain(['run', '--issue', '1', '--max-iterations', '0']), /positive/);
  assert.notEqual(stateDirectory('/agent', '/repo-a', 1), stateDirectory('/agent', '/repo-b', 1));
});
test('GitHub graph follows nested native children, detects cycles and only parses explicit task-list fallback', () => {
  const f = fixture(); f.children[2] = [4]; f.issues[2].sub_issues_summary = { total: 1 };
  f.issues[4] = { ...f.issues[3], number: 4, title: 'Nested leaf', html_url: 'https://github.com/fanxiao92/godot-pal/issues/4' };
  const graph = loadGraph(f.reader, 1); assert.deepEqual(graph.nodes[4].parents, [2]); assert.deepEqual(graph.nodes[2].children, [4]);
  f.children[4] = [1]; assert.throws(() => loadGraph(f.reader, 1), /Cyclic/);
  f.children[4] = []; f.issues[4].html_url = 'https://github.com/other/repo/issues/4';
  assert.throws(() => loadGraph(f.reader, 1), /Cross-repository/);
  assert.deepEqual(checklistChildren('- [ ] #12 implement\nReference #99\n- [x] https://github.com/fanxiao92/godot-pal/issues/13\n- [ ] https://github.com/other/repo/issues/14'), [12, 13]);
});
test('status analyzes live GitHub without creating state or launching models', () => {
  const f = fixture(5);
  f.issues[2].state = 'closed'; f.issues[3].labels = []; f.issues[4].assignees = [{ login: 'someone' }]; f.blockers[5] = [6];
  const status = f.host().status();
  assert.equal(status.counts.total, 5); assert.equal(status.counts.github_closed, 1); assert.equal(status.counts.not_approved, 1);
  assert.equal(status.counts.assigned, 1); assert.equal(status.counts.waiting_dependencies, 1); assert.equal(status.counts.ready, 1);
  assert.equal(fs.existsSync(f.state), false); assert.equal(f.calls.length, 0); assert.match(formatStatus(status), /not proof of local integration/);
});
test('root-only run needs no plan.json, streams activity, persists progress and waits for a manual commit', async () => {
  const f = fixture(); const status = await f.host().run({}, signal());
  assert.equal(status.rows.find(row => row.number === 2)?.status, 'awaiting_commit'); assert.equal(f.calls.length, 3);
  assert.deepEqual(f.calls, ['2:implement', '2:standards', '2:spec']);
  assert.equal(fs.existsSync(path.join(f.state, 'plan.json')), false); assert.ok(fs.existsSync(path.join(f.state, 'progress.md')));
  assert.ok(f.events.some(event => event.type === 'tool_start')); assert.equal(git(f.cwd, 'rev-parse', 'HEAD').trim(), f.head);
  assert.ok(status.rows.find(row => row.number === 2)?.sessionFiles?.implement); assert.match(status.nextAction, /commit/);
  assert.equal(JSON.stringify(json(path.join(f.state, 'github-state.json'))).includes('"files"'), false, 'Current index must not accumulate historical source maps');
  await f.host().run({}, signal()); assert.equal(f.calls.length, 3, 'same command must not repeat an awaiting candidate');
});
test('same command reconciles a manually committed exact candidate and continues with fresh sessions', async () => {
  const f = fixture(); await f.host().run({}, signal());
  git(f.cwd, 'add', 'a.txt'); git(f.cwd, 'commit', '--quiet', '-m', 'Explicit operator candidate commit');
  const status = await f.host().run({ maxIterations: 1 }, signal());
  assert.equal(status.rows.find(row => row.number === 2)?.status, 'local_completed');
  assert.equal(status.rows.find(row => row.number === 3)?.status, 'awaiting_commit'); assert.equal(f.calls.length, 6);
  assert.equal(f.issues[2].state, 'open', 'host must not close GitHub issues');
});
test('explicit --commit continuously advances ready dependent leaves without remote edits', async () => {
  const f = fixture(); f.blockers[3] = [2];
  const status = await f.host().run({ commit: true }, signal());
  assert.equal(status.counts.local_completed, 2); assert.equal(snapshot(f.cwd).dirty, false);
  assert.equal(git(f.cwd, 'rev-list', '--count', 'HEAD').trim(), '3'); assert.equal(f.issues[2].state, 'open'); assert.equal(f.issues[3].state, 'open');
});
test('max iterations stops cleanly and the same root command resumes subsequent leaves', async () => {
  const f = fixture();
  const first = await f.host().run({ commit: true, maxIterations: 1 }, signal()); assert.equal(first.counts.local_completed, 1);
  const second = await f.host().run({ commit: true, maxIterations: 1 }, signal()); assert.equal(second.counts.local_completed, 2);
  assert.equal(f.calls.length, 6);
});
test('remote closed progress is separate, and a closed prerequisite must prove actual local integration', async () => {
  const f = fixture(); f.issues[2].state = 'closed'; f.blockers[3] = [2];
  f.issues[2].comments.push({ body: `Accepted at ${f.head}`, html_url: 'https://github.com/fanxiao92/godot-pal/issues/2#issuecomment-test' });
  const agent = f.getAgent();
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      assert.equal(request.pendingDependencies.has(2), true);
      request.verifyDependency(2, f.head, f.issues[2].comments[0].html_url);
    }
    return agent.run(request);
  } });
  const status = await f.host().run({ commit: true }, signal());
  assert.equal(status.counts.github_closed, 1); assert.equal(status.counts.local_completed, 1); assert.equal(status.counts.effectiveDone, 2);
  const g = fixture(); g.issues[2].state = 'closed'; g.blockers[3] = [2];
  const blocked = await g.host().run({ commit: true }, signal()); assert.equal(blocked.counts.blocked, 1); assert.match(g.host().state()!.records[3].attempts[0].error!, /Unverified dependency/);
});
test('test failure blocks, does not run reviewers and requires explicit fresh resume', async () => {
  const f = fixture(); const agent = f.getAgent();
  f.setAgent({ run: async request => { if (request.role === 'implement') { f.calls.push('failure'); throw new Error('Synthetic Unit assertion failed'); } return agent.run(request); } });
  const status = await f.host().run({ commit: true }, signal()); assert.equal(status.counts.blocked, 1); assert.deepEqual(f.calls, ['failure']);
  await assert.rejects(f.host().run({ commit: true }, signal()), /--resume/);
  f.setAgent(agent);
  const resumed = await f.host().run({ resume: true, commit: true, maxIterations: 1 }, signal());
  assert.equal(resumed.counts.local_completed, 1); assert.equal(f.host().state()!.records[2].attempts.length, 2);
});
test('no-op candidate can verify already integrated behavior without an artificial commit', async () => {
  const f = fixture(1);
  f.setAgent({ run: async request => {
    if (request.role === 'implement') request.recordCheck({ project: 'synthetic existing behavior', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: 'Already integrated; verified current behavior', evidence: ['Synthetic existing-source evidence'] };
  } });
  const status = await f.host().run({}, signal()); assert.equal(status.counts.local_completed, 1); assert.equal(snapshot(f.cwd).head, f.head);
});
test('frozen candidate edits, parent comments and gate tampering invalidate previous results', async () => {
  const f = fixture(1); await f.host().run({}, signal());
  fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'post-review edit');
  assert.equal((await f.host().run({}, signal())).counts.blocked, 1);
  const g = fixture(1); const agent = g.getAgent();
  g.setAgent({ run: async request => { const result = await agent.run(request); if (request.role === 'spec') g.issues[1].comments.push({ body: 'Changed approved behavior', html_url: 'synthetic-parent-comment' }); return result; } });
  assert.equal((await g.host().run({ commit: true }, signal())).counts.blocked, 1);
  const h = fixture(1); await h.host().run({ commit: true }, signal());
  const attempt = h.host().state()!.records[2].attempts[0]; fs.appendFileSync(path.join(h.state, attempt.directory, 'gate.json'), ' ');
  assert.equal(h.host().status().counts.integration_drift, 1); await assert.rejects(h.host().run({ commit: true }, signal()), /integration commit/);
});
test('offline status clearly marks cached GitHub state and retains all progress categories', async () => {
  const f = fixture(1); await f.host().run({}, signal());
  f.reader.issue = () => { throw new Error('Synthetic network failure'); };
  assert.throws(() => f.host().status(), /network/);
  const status = f.host().status(true); assert.equal(status.stale, true); assert.match(formatStatus(status), /OFFLINE \/ STALE/);
  assert.equal(status.counts.awaiting_commit, 1);
});
test('dirty initial checkout and colliding evidence directories never get silently adopted', async () => {
  const f = fixture(); fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'external work');
  await assert.rejects(f.host().run({ commit: true }, signal()), /clean approved/); assert.equal(f.calls.length, 0);
  const g = fixture(); fs.mkdirSync(g.state); fs.writeFileSync(path.join(g.state, 'unrelated.txt'), 'keep');
  await assert.rejects(g.host().run({}, signal()), /Existing directory/); assert.equal(fs.readFileSync(path.join(g.state, 'unrelated.txt'), 'utf8'), 'keep');
});
test('unknown execution quiescence retains writer lock and cannot steal a live owner', async () => {
  const f = fixture(); f.setAgent({ run: async () => { throw new QuiescenceUnknownError('Synthetic unknown owned check descendants'); } });
  assert.equal((await f.host().run({}, signal())).counts.blocked, 1); assert.ok(fs.existsSync(lockFile(f.cwd)));
  await assert.rejects(f.host().run({ resume: true, confirmQuiescent: true }, signal()), /still exists/);
});
test('actual owner-process interruption is shown in status and the root command resumes only with confirmation', async () => {
  const f = fixture(1), script = path.join(f.directory, 'owner-exit.mjs');
  atomicJson(path.join(f.directory, 'synthetic-issues.json'), f.issues);
  fs.writeFileSync(script, [
    `import fs from 'node:fs';`,
    `import { GitHubHost } from ${JSON.stringify(new URL('./github-host.ts', import.meta.url).href)};`,
    `import { snapshot } from ${JSON.stringify(new URL('./io.ts', import.meta.url).href)};`,
    `const issues = JSON.parse(fs.readFileSync(${JSON.stringify(path.join(f.directory, 'synthetic-issues.json'))}, 'utf8'));`,
    `const reader = { issue: n => issues[n], context: n => issues[n], children: i => ({issues:i.number === 1 ? [issues[2]] : []}), blockers: () => [] };`,
    `await new GitHubHost(${JSON.stringify(f.cwd)}, 1, ${JSON.stringify(f.state)}, {reader,source:snapshot,agent:{run:async()=>process.exit(19)}}).run({}, new AbortController().signal);`,
  ].join('\n'));
  const exited = spawnSync(process.execPath, [script], { cwd: f.directory, encoding: 'utf8', timeout: 20000, shell: false });
  atomicJson(path.join(f.directory, 'owner-exit.json'), { code: exited.status, signal: exited.signal, stdout: exited.stdout, stderr: exited.stderr });
  assert.equal(exited.status, 19); assert.equal(f.host().status().counts.interrupted, 1);
  await assert.rejects(f.host().run({}, signal()), /--resume --confirm-quiescent/);
  const status = await f.host().run({ resume: true, confirmQuiescent: true, commit: true, maxIterations: 1 }, signal());
  assert.equal(status.counts.local_completed, 1); assert.equal(f.host().state()!.records[2].attempts.length, 2);
});
test('SDK coding sessions inherit Pi configuration, are fresh, stream progress and fail closed', async () => {
  const actual = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  const created: Pi.CreateAgentSessionOptions[] = []; let mode = 'normal', disposed = 0;
  const fakeSdk: typeof actual = { ...actual, createAgentSession: async (options = {}) => {
    created.push(options);
    const contextFiles = options.resourceLoader!.getAgentsFiles().agentsFiles;
    assert.ok(contextFiles.some(file => file.path === path.join(f.directory, 'environment.json') && file.content.includes('"engineChecksAuthorized": false')));
    assert.ok(contextFiles.some(file => file.path === path.join(f.directory, 'environment-contract.txt') && file.content.includes('# Fresh session 执行环境契约')));
    let listener: Parameters<Pi.AgentSession['subscribe']>[0] = () => {};
    const session = {
      model: { id: 'configured-model', provider: 'configured-provider' },
      messages: [{ role: 'assistant', stopReason: mode === 'provider-error' ? 'error' : 'stop' }],
      sessionFile: path.join(f.directory, `mock-${created.length}.jsonl`),
      getActiveToolNames: () => mode === 'tool-leak' ? ['bash'] : [...options.tools!],
      subscribe: (callback: typeof listener) => { listener = callback; return () => {}; },
      prompt: async () => {
        listener({ type: 'message_update', assistantMessageEvent: { type: 'text_delta', delta: 'Synthetic streamed progress' } } as Parameters<typeof listener>[0]);
        if (mode === 'turn-limit') { listener({ type: 'turn_start' }); listener({ type: 'turn_start' }); }
      },
      getLastAssistantText: () => mode === 'invalid-json' ? 'not JSON' : JSON.stringify({ status: 'passed', summary: 'Synthetic independent review', evidence: ['source reference'] }),
      abort: async () => {}, dispose: () => { disposed++; },
    } as unknown as Pi.AgentSession;
    return { session, extensionsResult: options.resourceLoader!.getExtensions() };
  } };
  const events: CodingEvent[] = [];
  const request: CodingRequest = { cwd: f.cwd, issue: 2, role: 'spec', directory: f.directory, contexts: [], prompt: 'Synthetic', input: snapshot(f.cwd), signal: signal(), emit: event => events.push(event), checks: [], pendingDependencies: new Set(), verifyDependency: () => {}, recordCheck: () => {} };
  const executor = new GitHubPiExecutor(fakeSdk, f.directory, { maxTurns: 1 });
  await executor.run(request); await executor.run(request);
  assert.equal(created.length, 2); assert.notEqual(created[0].sessionManager, created[1].sessionManager);
  assert.equal(created[0].model, undefined, 'Let SDK use existing Pi model settings');
  assert.ok(events.some(event => event.type === 'text')); assert.equal(disposed, 2);
  for (mode of ['provider-error', 'invalid-json', 'tool-leak', 'turn-limit']) await assert.rejects(executor.run(request), /completed|JSON|capability|turn limit/);
  assert.equal(disposed, 6);
});
test('real SDK tools allow normal source work, deny shell/private assets/unverified writes and isolate reviews', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  const request: CodingRequest = { cwd: f.cwd, issue: 2, role: 'implement', directory: f.directory, contexts: [], prompt: 'Synthetic', input: snapshot(f.cwd), signal: signal(), emit: () => {}, checks: [], pendingDependencies: new Set([99]), verifyDependency: () => {}, recordCheck: () => {} };
  const tools = codingTools(sdk, request, signal(), error => { throw error; });
  const execute = (name: string, params: unknown) => tools.find(tool => tool.name === name)!.execute('synthetic', params, undefined, undefined, {} as Pi.ExtensionToolContext);
  assert.equal(tools.some(tool => ['bash', 'powershell'].includes(tool.name)), false);
  await assert.rejects(execute('scoped_write', { path: 'a.txt', content: 'denied' }), /verified dependency/);
  request.pendingDependencies.clear(); await execute('scoped_write', { path: 'a.txt', content: 'SDK source write' });
  assert.equal(fs.readFileSync(path.join(f.cwd, 'a.txt'), 'utf8'), 'SDK source write');
  await assert.rejects(execute('scoped_read', { path: '.git/config' }), /Protected/);
  await assert.rejects(execute('scoped_read', { path: 'data/raw/private' }), /Private/);
  const listing = await execute('scoped_list', { path: '.' }); assert.ok(JSON.stringify(listing).includes('a.txt')); assert.ok(!JSON.stringify(listing).includes('.git'));
  const readonly = codingTools(sdk, { ...request, role: 'spec' }, signal(), () => {});
  assert.equal(readonly.some(tool => ['scoped_write', 'scoped_edit', 'unit_test', 'verify_dependency'].includes(tool.name)), false);
  const settingsManager = sdk.SettingsManager.inMemory({ retry: { enabled: false } });
  const loader = new sdk.DefaultResourceLoader({ cwd: f.cwd, agentDir: f.directory, settingsManager, noExtensions: true, noSkills: true, noPromptTemplates: true, noThemes: true }); await loader.reload();
  const runtime = await sdk.ModelRuntime.create({ authPath: path.join(f.directory, 'no-auth.json'), modelsPath: null, refreshOnCreate: false, allowModelNetwork: false });
  const model = runtime.getModel('anthropic', 'claude-sonnet-4-5'); assert.ok(model);
  const { session } = await sdk.createAgentSession({ cwd: f.cwd, agentDir: f.directory, model, modelRuntime: runtime, resourceLoader: loader, settingsManager, tools: tools.map(tool => tool.name), customTools: tools, sessionManager: sdk.SessionManager.create(f.cwd, path.join(f.directory, 'sessions')) });
  try { assert.deepEqual(session.getActiveToolNames().toSorted(), tools.map(tool => tool.name).toSorted()); }
  finally { await session.abort(); session.dispose(); }
});
test('codingResourceLoader injects saved host facts and contract into real fresh SDK implement and review prompts offline', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  fs.writeFileSync(path.join(f.cwd, 'AGENTS.md'), 'Synthetic project instructions retained');
  const settingsManager = sdk.SettingsManager.inMemory({ retry: { enabled: false }, compaction: { enabled: false } });
  const runtime = await sdk.ModelRuntime.create({ authPath: path.join(f.directory, 'no-auth.json'), modelsPath: null, refreshOnCreate: false, allowModelNetwork: false });
  const model = runtime.getModel('anthropic', 'claude-sonnet-4-5'); assert.ok(model);
  for (const role of ['implement', 'standards', 'spec'] as const) {
    const directory = path.join(f.directory, role); fs.mkdirSync(directory);
    const request: CodingRequest = { cwd: f.cwd, issue: 2, role, directory, contexts: [], prompt: 'Not sent', input: snapshot(f.cwd), signal: signal(), emit: () => {}, checks: [], pendingDependencies: new Set(), verifyDependency: () => {}, recordCheck: () => {} };
    const tools = codingTools(sdk, request, signal(), error => { throw error; });
    const loader = await codingResourceLoader(sdk, request, f.directory, settingsManager, tools);
    const raw = fs.readFileSync(path.join(directory, 'environment.json'), 'utf8');
    const environment = JSON.parse(raw);
    assert.deepEqual(Object.keys(environment).sort(), ['arch', 'capturedUtc', 'contract', 'cwd', 'engineChecksAuthorized', 'nativeExecution', 'node', 'nodeExecutable', 'platform', 'release', 'tools']);
    assert.equal(environment.platform, process.platform); assert.equal(environment.release, os.release()); assert.equal(environment.arch, process.arch);
    assert.equal(environment.cwd, f.cwd); assert.equal(environment.node, process.version); assert.equal(environment.nodeExecutable, process.execPath);
    assert.equal(environment.engineChecksAuthorized, false);
    assert.deepEqual(environment.nativeExecution, { shell: false, arguments: 'argv', textEncoding: 'utf8' });
    assert.deepEqual(environment.tools, role === 'implement'
      ? ['scoped_read', 'scoped_edit', 'scoped_write', 'scoped_list', 'scoped_search', 'inspect_git', 'check_evidence', 'request_correction', 'verify_dependency', 'unit_test']
      : ['scoped_read', 'scoped_list', 'scoped_search', 'inspect_git', 'check_evidence']);
    const contract = fs.readFileSync(path.join(directory, 'environment-contract.txt'), 'utf8');
    assert.equal(environment.contract.sha256, hash(contract));
    assert.equal(contract, fs.readFileSync(environment.contract.source, 'utf8'));
    const { session } = await sdk.createAgentSession({ cwd: f.cwd, agentDir: f.directory, model, modelRuntime: runtime, resourceLoader: loader, settingsManager, tools: tools.map(tool => tool.name), customTools: tools, sessionManager: sdk.SessionManager.create(f.cwd, directory) });
    try {
      assert.ok(session.systemPrompt.includes(contract));
      assert.ok(session.systemPrompt.includes(raw.trim()));
      assert.ok(session.systemPrompt.includes('Synthetic project instructions retained'));
      assert.deepEqual(session.messages, [], 'No transcript or provider call is needed to assemble the prompt');
      assert.deepEqual(session.getActiveToolNames().toSorted(), environment.tools.toSorted());
      fs.writeFileSync(path.join(directory, 'assembled-system-prompt.txt'), session.systemPrompt, 'utf8');
    } finally { await session.abort(); session.dispose(); }
  }
});
