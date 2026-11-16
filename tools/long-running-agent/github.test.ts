import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawnSync } from 'node:child_process';
import type * as Pi from '@earendil-works/pi-coding-agent';
import { hash } from './contract.ts';
import { acquire, atomicJson, git, json, lockFile, QuiescenceUnknownError, snapshot } from './io.ts';
import { checklistChildren, loadGraph, stateDirectory, type GitHubReader, type IssueContext } from './github.ts';
import { unitProjects } from './checks.ts';
import { GitHubHost, type CodingEvent, type CodingExecutor, type CodingRequest } from './github-host.ts';
import { codingResourceLoader, codingTools, GitHubPiExecutor } from './github-agent.ts';
import { approveCorrection, requireWritable } from './policy.ts';
import { formatStatus, githubMain, issueNumber } from './github-cli.ts';

const suite = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-github-coding-'));
console.log(`Synthetic GitHub workflow evidence retained at ${suite}`);
let sequence = 0;
const signal = () => new AbortController().signal;
const finding = (request: CodingRequest, problemId = 'consumer-contract', anchor = problemId) => ({ problemId, evidenceKey: `${request.input.fingerprint}:a.txt#${anchor}`, cause: 'Located omitted consumer contract', evidence: [path.join(request.directory, 'review-source.txt')] });
const syntheticDeclaration = { behavior: 'Approved synthetic behavior', seam: 'Synthetic public consumer', paths: ['contract-fixture.txt'], expectedFailure: { test: 'Synthetic.Capability', message: 'Expected new capability' } };
function certifySyntheticRed(request: CodingRequest) {
  if (request.checks.some(check => check.expectedRed) || request.pendingDependencies.size) return;
  request.declareRed!(syntheticDeclaration);
  request.recordCheck({ project: 'synthetic native', directory: path.join(request.directory, 'red'), sourceFingerprint: snapshot(request.cwd).fingerprint, code: 1, expectedRed: true, expectedFailure: syntheticDeclaration.expectedFailure });
}
function fixture(count = 2, testFirst = true) {
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
      if (testFirst) certifySyntheticRed(request);
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
test('dependency proofs are idempotent across fresh review repair and resume without weakening validation', async () => {
  const f = fixture(2); f.issues[3].state = 'closed'; f.blockers[2] = [3];
  f.issues[3].comments.push({ body: `Accepted integration ${f.head}` } as IssueContext['comments'][number]);
  const agent = f.getAgent(); let implementations = 0, reviews = 0;
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      implementations++;
      request.verifyDependency(3, f.head, `Accepted integration ${f.head}`);
      request.verifyDependency(3, f.head, `Accepted integration ${f.head}`);
      assert.throws(() => request.verifyDependency(1, f.head, f.head), /Dependency/);
      assert.throws(() => request.verifyDependency(3, 'b'.repeat(40), 'fabricated'), /Dependency/);
      assert.throws(() => request.verifyDependency(3, f.head, 'fabricated acceptance'), /Dependency/);
      if (implementations === 2) return { status: 'blocked', summary: 'Synthetic explicit pause for resume', evidence: ['Original dependency proof'] };
    } else if (request.role === 'standards' && ++reviews === 1) {
      return { status: 'changes_requested', summary: 'Located consumer repair', evidence: ['a.txt'], findings: [finding(request)] };
    }
    const report = await agent.run(request);
    if (request.role === 'implement' && implementations > 1) {
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `repair-${implementations}`);
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    }
    return report;
  } });
  await f.host().run({}, signal());
  assert.equal(implementations, 2, 'Repeated valid dependency proof must allow repair');
  assert.equal(f.host().state()!.records[2].attempts[0].dependencyProofs.length, 1);
  const result = await f.host().run({ resume: true }, signal());
  assert.equal(result.counts.awaiting_commit, 1);
  assert.equal(f.host().state()!.records[2].attempts[1].dependencyProofs.length, 1);
});
test('dependency evidence contract stays discoverable in the tool description and the host rejection', async () => {
  const f = fixture(2); f.issues[3].state = 'closed'; f.blockers[2] = [3];
  f.issues[3].comments.push({ body: `Accepted integration ${f.head}` } as IssueContext['comments'][number]);
  const agent = f.getAgent();
  let rejectionMessage = '';
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      try { request.verifyDependency(3, f.head, 'fabricated acceptance excerpt'); } catch (error) { rejectionMessage = (error as Error).message; }
      request.verifyDependency(3, f.head, `Accepted integration ${f.head}`);
    }
    return agent.run(request);
  } });
  const status = await f.host().run({}, signal());
  assert.equal(status.rows.find(row => row.number === 2)?.status, 'awaiting_commit');
  assert.ok(rejectionMessage, 'verifyDependency must reject fabricated evidence');
  assert.match(rejectionMessage, /exactly equal the acceptance comment\/issue URL/);
  assert.match(rejectionMessage, /verbatim excerpt of that comment body/);
  assert.match(rejectionMessage, /full 40-hex SHA or 7-hex short form/);
  const sdk = await import('@earendil-works/pi-coding-agent'); const g = fixture(1);
  const request: CodingRequest = { cwd: g.cwd, issue: 2, role: 'implement', directory: g.directory, contexts: [], prompt: 'Synthetic', input: snapshot(g.cwd), signal: signal(), emit: () => {}, checks: [], pendingDependencies: new Set(), verifyDependency: () => {}, recordCheck: () => {} };
  const description = codingTools(sdk, request, signal(), () => {}).find(tool => tool.name === 'verify_dependency')!.description ?? '';
  assert.match(description, /exactly equal to the acceptance comment\/issue URL/);
  assert.match(description, /verbatim excerpt of that comment\/issue body/);
  assert.match(description, /URL equality or body inclusion/);
  assert.match(description, /full 40-hex SHA or its 7-hex short form/);
});
test('TDD rejects undeclared production writes at the codingTools boundary', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      const write = codingTools(sdk, request, signal(), error => { throw error; }).find(tool => tool.name === 'scoped_write')!;
      const denied = await write.execute('undeclared', { path: 'a.txt', content: 'production before RED' }, undefined, undefined, {} as Pi.ExtensionToolContext);
      assert.equal(denied.isError, true, 'Production writes must wait for a declared and certified RED');
      assert.match(JSON.stringify(denied), /RED declaration/);
      assert.equal(fs.readFileSync(path.join(f.cwd, 'a.txt'), 'utf8'), 'baseline');
    }
    return { status: 'blocked', summary: 'Boundary probe completed', evidence: ['SDK scoped_write'] };
  } });
  await f.host().run({}, signal());
  assert.match(f.host().state()!.records[2].attempts[0].error!, /Boundary probe completed/);
});
test('host protocol rejections become in-session tool errors instead of killing the attempt', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      let fatalCalls = 0;
      const tools = codingTools(sdk, request, signal(), error => { fatalCalls++; throw error; });
      const correction = tools.find(tool => tool.name === 'request_correction')!;
      const rejected = await correction.execute('protocol-miss', { findings: [finding(request)], change: 'premature diagnosis', contract: 'Approved contract unchanged' }, undefined, undefined, {} as Pi.ExtensionToolContext);
      assert.equal(rejected.isError, true, 'A protocol rejection must be a session-local tool error, not an attempt stop');
      assert.match(JSON.stringify(rejected), /No recorded normal failure to correct/);
      const deniedDependency = await tools.find(tool => tool.name === 'verify_dependency')!.execute('fabricated', { issue: 9, commit: f.head, evidence: 'fabricated acceptance' }, undefined, undefined, {} as Pi.ExtensionToolContext);
      assert.equal(deniedDependency.isError, true);
      assert.match(JSON.stringify(deniedDependency), /Dependency evidence must exactly equal/);
      certifySyntheticRed(request);
      const refrozen = await tools.find(tool => tool.name === 'declare_red')!.execute('refrozen', { ...syntheticDeclaration, paths: ['a.txt'] }, undefined, undefined, {} as Pi.ExtensionToolContext);
      assert.equal(refrozen.isError, true);
      assert.match(JSON.stringify(refrozen), /frozen across repair\/resume/);
      assert.equal(fatalCalls, 0, 'Non-fatal protocol rejections must not stop the session');
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'corrected protocol flow');
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    }
    return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: 'Protocol miss corrected within the same session', evidence: ['Original rejection text returned to the model'] };
  } });
  const result = await f.host().run({}, signal());
  assert.equal(result.counts.awaiting_commit, 1, 'The same attempt must recover from its protocol mistakes');
  assert.equal(f.host().state()!.records[2].attempts.length, 1);
});
test('quiescence unknown stays fatal at the degraded tool boundary', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  const request: CodingRequest = { cwd: f.cwd, issue: 2, role: 'implement', directory: f.directory, contexts: [], prompt: 'Synthetic', input: snapshot(f.cwd), signal: signal(), emit: () => {}, checks: [], pendingDependencies: new Set(),
    verifyDependency: () => {}, recordCheck: () => {}, beforeCheck: () => { throw new QuiescenceUnknownError('Native check changed source identity'); } };
  let fatalCalls = 0;
  const unit = codingTools(sdk, request, request.signal, error => { fatalCalls++; throw error; }).find(tool => tool.name === 'unit_test')!;
  await assert.rejects(unit.execute('quiescent', { project: unitProjects[0] }, undefined, undefined, {} as Pi.ExtensionToolContext), QuiescenceUnknownError);
  assert.equal(fatalCalls, 1, 'Quiescence-unknown must still reach fatal and stop the attempt');
});
test('TDD rejects changed candidates without RED at the GitHubHost boundary', async () => {
  const f = fixture(1, false);
  const result = await f.host().run({}, signal());
  assert.equal(result.counts.blocked, 1, 'Changed candidates without RED must be blocked');
  assert.match(f.host().state()!.records[2].attempts[0].error!, /RED/);
  assert.deepEqual(f.calls, ['2:implement']);
});
test('TDD stage paths permit migration only, exact RED unlocks production and stays frozen', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      const tools = codingTools(sdk, request, signal(), error => { throw error; });
      const execute = (name: string, params: unknown) => tools.find(tool => tool.name === name)!.execute('tdd-stage', params, undefined, undefined, {} as Pi.ExtensionToolContext);
      await execute('declare_red', syntheticDeclaration);
      await execute('scoped_write', { path: 'contract-fixture.txt', content: 'Migrated old rejection; independent new capability expectation' });
      const tooEarly = await execute('scoped_write', { path: 'a.txt', content: 'too early' });
      assert.equal(tooEarly.isError, true); assert.match(JSON.stringify(tooEarly), /certified RED/);
      assert.throws(() => request.beforeCheck!(snapshot(f.cwd), { scope: 'unit', expectedFailure: { test: 'Wrong.Test', message: 'Wrong diagnostic' } }), /frozen declaration/);
      assert.throws(() => request.recordCheck({ project: 'synthetic', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 1, expectedRed: true, expectedFailure: { test: 'Wrong.Test', message: 'Wrong diagnostic' } }), /frozen declaration/);
      request.beforeCheck!(snapshot(f.cwd), { scope: 'unit', expectedFailure: syntheticDeclaration.expectedFailure });
      request.recordCheck({ project: 'synthetic native', directory: path.join(request.directory, 'red'), sourceFingerprint: snapshot(f.cwd).fingerprint, code: 1, expectedRed: true, expectedFailure: syntheticDeclaration.expectedFailure });
      const refrozen = await execute('declare_red', { ...syntheticDeclaration, paths: ['a.txt'] });
      assert.equal(refrozen.isError, true); assert.match(JSON.stringify(refrozen), /frozen/);
      await execute('scoped_write', { path: 'a.txt', content: 'production after exact RED' });
      request.recordCheck({ project: 'synthetic native', directory: path.join(request.directory, 'green'), sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    } else assert.ok(request.prompt.includes('Synthetic.Capability'));
    return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: 'Test-first migration verified', evidence: ['Public tool and host boundaries'] };
  } });
  assert.equal((await f.host().run({}, signal())).counts.awaiting_commit, 1);
  const record = f.host().state()!.records[2];
  assert.equal(record.tdd!.required, true); assert.equal(record.tdd!.red!.code, 1);
  assert.deepEqual(record.corrections ?? [], [], 'Certified RED consumes no correction');
});
test('TDD rejects GREEN before RED even for the same source fingerprint', async () => {
  const f = fixture(1);
  f.setAgent({ run: async request => {
    request.declareRed!(syntheticDeclaration);
    fs.writeFileSync(path.join(f.cwd, 'contract-fixture.txt'), 'New test contract');
    const sourceFingerprint = snapshot(f.cwd).fingerprint;
    request.recordCheck({ project: 'synthetic', directory: path.join(request.directory, 'early-green'), sourceFingerprint, code: 0 });
    request.recordCheck({ project: 'synthetic', directory: path.join(request.directory, 'red'), sourceFingerprint, code: 1, expectedRed: true, expectedFailure: syntheticDeclaration.expectedFailure });
    return { status: 'candidate', summary: 'GREEN was too early', evidence: ['Synthetic ordering'] };
  } });
  assert.equal((await f.host().run({}, signal())).counts.blocked, 1);
  assert.match(f.host().state()!.records[2].attempts[0].error!, /after RED/);
});
test('TDD obligation survives dirty resume and cannot relabel a recorded failure', async () => {
  const f = fixture(1, false);
  assert.equal((await f.host().run({}, signal())).counts.blocked, 1);
  f.setAgent({ run: async request => {
    assert.throws(() => request.beforeWrite!('a.txt'), /RED declaration/);
    assert.throws(() => request.beforeCheck!(snapshot(f.cwd)), /Production changed/);
    request.recordCheck({ project: 'synthetic normal failure', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 1 });
    assert.throws(() => request.declareRed!(syntheticDeclaration), /retroactively/);
    return { status: 'candidate', summary: 'Must still be blocked', evidence: ['Retained dirty source'] };
  } });
  assert.equal((await f.host().run({ resume: true }, signal())).counts.blocked, 1);
  const record = f.host().state()!.records[2];
  assert.equal(record.attempts.length, 2); assert.deepEqual(record.tdd, { required: true });
});
test('TDD certified RED survives a stopped review and fresh resume without replay', async () => {
  const f = fixture(1); const agent = f.getAgent();
  f.setAgent({ run: async request => {
    if (request.role === 'standards') throw new Error('Located review blocker requires explicit resume');
    return agent.run(request);
  } });
  assert.equal((await f.host().run({}, signal())).counts.blocked, 1);
  const originalRed = f.host().state()!.records[2].tdd!.red;
  f.setAgent(agent);
  assert.equal((await f.host().run({ resume: true }, signal())).counts.awaiting_commit, 1);
  const record = f.host().state()!.records[2];
  assert.equal(record.attempts.length, 2);
  assert.deepEqual(record.tdd!.red, originalRed);
  assert.equal(record.attempts.flatMap(attempt => attempt.checks).filter(check => check.expectedRed).length, 1);
});
test('TDD legacy records retain historical candidates without backfilling RED', async () => {
  const f = fixture(1, false);
  f.setAgent({ run: async () => { throw new Error('Historical stopped attempt'); } });
  await f.host().run({}, signal());
  const state = f.host().state()!;
  delete state.records[2].tdd; // Recreate the pre-enforcement persisted record schema, not a dirty/new-attempt exemption.
  atomicJson(path.join(f.state, 'github-state.json'), state);
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      assert.throws(() => request.declareRed!(syntheticDeclaration), /Legacy/);
      request.beforeWrite!('a.txt');
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'Retained historical candidate');
      request.recordCheck({ project: 'synthetic legacy', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    }
    return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: 'Historical contract retained', evidence: ['Original record'] };
  } });
  assert.equal((await f.host().run({ resume: true }, signal())).counts.awaiting_commit, 1);
  assert.equal(f.host().state()!.records[2].tdd, undefined);
});
test('structured review axes deduplicate one problem repair and persist the shared original identity', async () => {
  const f = fixture(1); const agent = f.getAgent(); let implementations = 0;
  f.setAgent({ run: async request => {
    if (request.role !== 'implement' && request.issue === 2 && implementations === 1) return { status: 'changes_requested', summary: `${request.role} located the same root cause`, evidence: ['a.txt'], findings: [finding(request)] };
    const report = await agent.run(request);
    if (request.role === 'implement') {
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `revision-${++implementations}`);
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    }
    return report;
  } });
  const result = await f.host().run({}, signal());
  assert.equal(result.counts.awaiting_commit, 1, 'Structured findings must permit a single deduplicated repair');
  const record = f.host().state()!.records[2];
  assert.equal(record.corrections!.length, 1, 'Same finding from both axes is one repair');
  assert.equal(record.corrections![0].problemId, 'consumer-contract');
  assert.equal(implementations, 2);
});
test('automatic review repair rejects summary-only legacy findings and conflicting identities before charging rounds', async () => {
  for (const conflict of [false, true]) {
    const f = fixture(1); const agent = f.getAgent();
    f.setAgent({ run: async request => {
      if (request.role !== 'implement' && request.issue === 2) return { status: 'changes_requested', summary: 'Located consumer', evidence: ['a.txt'],
        ...(conflict ? { findings: [{ ...finding(request), problemId: request.role === 'spec' ? 'renamed-root' : 'consumer-contract' }] } : {}) };
      return agent.run(request);
    } });
    const result = await f.host().run({}, signal());
    assert.equal(result.counts.blocked, 1);
    const record = f.host().state()!.records[2];
    assert.equal(record.corrections?.length ?? 0, 0);
    assert.match(record.attempts.at(-1)!.error!, conflict ? /Same evidence/ : /structured problem findings/);
  }
});
test('bounded check and review repairs share one persistent ledger, without a manual restart', async () => {
  const f = fixture(1); let implementations = 0, reviews = 0;
  f.setAgent({ run: async request => {
    f.calls.push(`${request.issue}:${request.role}`);
    if (request.role === 'implement') {
      implementations++;
      certifySyntheticRed(request);
      if (implementations === 1) {
        request.recordFailure!(request.directory, snapshot(f.cwd), 'Located synthetic fixture error');
        assert.throws(() => request.beforeWrite!(), /request_correction/);
        request.requestCorrection!([finding(request, 'slot-field')], 'Use the approved slot field', 'Same approved per-slot contract');
      }
      request.beforeWrite!();
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `fixed-${implementations}`);
      request.recordCheck({ project: 'synthetic native', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    } else if (request.role === 'standards' && ++reviews === 1) {
      return { status: 'changes_requested', summary: 'Located omitted consumer within the same contract', evidence: ['a.txt consumer'], findings: [finding(request, 'omitted-consumer')] };
    }
    return { status: request.role === 'implement' ? 'candidate' : 'passed', summary: 'Synthetic contract verified', evidence: ['Synthetic scope and native evidence'] };
  } });
  const result = await f.host().run({ commit: true }, signal());
  assert.equal(result.specStatus, 'passed');
  const ledger = f.host().state()!.records[2].corrections!;
  assert.deepEqual(ledger.map(item => item.kind), ['check', 'review']);
  assert.equal(implementations, 2); assert.equal(reviews, 2);
  const record = f.host().state()!.records[2];
  assert.ok(record.tdd!.red, 'Review repair must retain the original certified RED');
  assert.equal(record.attempts.flatMap(attempt => attempt.checks).filter(check => check.expectedRed).length, 1);
});
test('third repair success can be delivered after check review and JSON resume', async () => {
  const f = fixture(1); const agent = f.getAgent(); let revisions = 0, pause = true;
  f.setAgent({ run: async request => {
    if (request.role !== 'implement' && request.issue === 2 && revisions < 3) return { status: 'changes_requested', summary: 'Same located root cause', evidence: ['a.txt'], findings: [finding(request)] };
    if (request.role === 'implement') {
      if (revisions === 2 && pause) { pause = false; return { status: 'blocked', summary: 'Explicit pause before third repair', evidence: ['Synthetic pause'] }; }
      certifySyntheticRed(request);
      if (revisions === 0) {
        request.recordFailure!(request.directory, snapshot(f.cwd), 'Initial normal failure');
        request.requestCorrection!([finding(request)], 'Fix the located consumer', 'Same approved contract');
      }
      request.beforeWrite!();
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `revision-${++revisions}`);
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
      return { status: 'candidate', summary: 'Repair verified', evidence: ['Current source GREEN'] };
    }
    return agent.run(request);
  } });
  await f.host().run({}, signal());
  assert.equal(f.host().state()!.records[2].corrections?.length ?? 0, 3, 'Check and review must share exactly three rounds');
  const result = await f.host().run({ resume: true }, signal());
  assert.equal(result.counts.awaiting_commit, 1, 'The third repair must be allowed to succeed after resume');
  assert.equal(f.host().state()!.records[2].corrections!.at(-1)!.outcome, 'passed');
});
test('third normal check failure requires attribution then stops writes without charging a fourth round', async () => {
  const f = fixture(1);
  f.setAgent({ run: async request => {
    certifySyntheticRed(request);
    const diagnose = () => request.requestCorrection!([finding(request)], 'Fix the same located consumer', 'Approved contract unchanged');
    request.recordFailure!(request.directory, snapshot(f.cwd), 'Initial normal failure');
    for (let round = 1; round <= 3; round++) {
      diagnose(); request.beforeWrite!();
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `failed-repair-${round}`);
      request.recordCheck({ project: 'synthetic Unit', directory: `${request.directory}/check-${round}`, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 1 });
      request.recordFailure!(`${request.directory}/check-${round}`, snapshot(f.cwd), 'Same normal failure requires diagnosis');
      assert.throws(() => request.beforeWrite!(), /request_correction/, 'Unattributed failure never permits resumed writes');
    }
    assert.throws(diagnose, /three repair rounds failed/);
    assert.throws(() => request.beforeWrite!(), /three repair rounds failed/);
    return { status: 'blocked', summary: 'Third same-problem check failure ended execution', evidence: ['All three original check directories'] };
  } });
  await f.host().run({}, signal());
  const record = f.host().state()!.records[2];
  assert.equal(record.corrections!.length, 3);
  assert.deepEqual(record.corrections!.map(item => item.outcome), ['failed', 'failed', 'failed']);
  await assert.rejects(f.host().run({ resume: true }, signal()), /three repair rounds failed/);
});
test('review exhaustion stays blocked across fresh attempts and extras cannot bypass three rounds', async () => {
  const f = fixture(1); const agent = f.getAgent(); let fail = true, revisions = 0;
  f.setAgent({ run: async request => {
    if (request.role === 'standards' && fail) return { status: 'changes_requested', summary: 'Located same-contract repair', evidence: ['Synthetic consumer'], findings: [finding(request)] };
    const report = await agent.run(request);
    if (request.role === 'implement') {
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), `synthetic repair ${++revisions}`);
      request.recordCheck({ project: 'synthetic native', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
    }
    return report;
  } });
  const stopped = await f.host().run({ commit: true }, signal());
  assert.equal(stopped.counts.blocked, 1); assert.equal(f.host().state()!.records[2].corrections!.length, 3);
  const record = f.host().state()!.records[2];
  assert.equal(revisions, 4, 'Initial implementation plus all three repairs must run');
  assert.deepEqual(record.corrections!.map(item => item.outcome), ['failed', 'failed', 'failed'], 'All three failed rounds must retain their validation attribution');
  assert.match(record.attempts.at(-1)!.error!, /three.*failed|failed.*three/i);
  await assert.rejects(f.host().run({}, signal()), /--resume/);
  fail = false;
  await assert.rejects(f.host().run({ resume: true, extraCorrections: 1 }, signal()), /cannot.*three|per.problem/i);
  await assert.rejects(f.host().run({ resume: true }, signal()), /three.*failed|failed.*three/i);
  assert.equal(revisions, 4, 'Fresh resume cannot write after exhaustion');
});
test('parent-only legacy migration is identity-bound complete immutable and preserves unstarted evidence', async () => {
  const f = fixture(1);
  f.setAgent({ run: async () => ({ status: 'blocked', summary: 'Synthetic legacy stop', evidence: ['Legacy original'] }) });
  await f.host().run({}, signal());
  const file = path.join(f.state, 'github-state.json'); const state = f.host().state()!;
  const original = { cause: 'Located old review batch', change: 'Repair two consumers', contract: 'Approved behavior unchanged', evidence: '/original/review', sourceFingerprint: snapshot(f.cwd).fingerprint, kind: 'review' as const, utc: '2026-11-14T00:00:00Z' };
  state.records[2].corrections = [original, { ...original, cause: 'Stopped before writes' }];
  atomicJson(file, state);
  const approval = path.join(f.directory, 'parent-approval.txt'); fs.writeFileSync(approval, 'Explicit synthetic parent mapping approval and no-write original source proof');
  const host = f.host();
  assert.equal(typeof host.migrateCorrections, 'function', 'Parent-only migration entry must exist');
  const input = { issue: 2, stateHash: hash(fs.readFileSync(file)), sourceFingerprint: snapshot(f.cwd).fingerprint, approval: { file: approval, sha256: hash(fs.readFileSync(approval)) }, mappings: [
    { index: 0, findings: [{ problemId: 'consumer-contract', evidenceKey: `${snapshot(f.cwd).fingerprint}:a.txt#consumer-contract`, cause: original.cause, evidence: [approval] }], outcome: 'passed' as const },
    { index: 1, findings: [{ problemId: 'other-contract', evidenceKey: `${snapshot(f.cwd).fingerprint}:b.txt#other-contract`, cause: original.cause, evidence: [approval] }], notStarted: { file: approval, sha256: hash(fs.readFileSync(approval)) } },
  ] };
  assert.throws(() => host.migrateCorrections({ ...input, stateHash: '0'.repeat(64) }), /state identity/i);
  assert.throws(() => host.migrateCorrections({ ...input, sourceFingerprint: '0'.repeat(64) }), /source identity/i);
  assert.throws(() => host.migrateCorrections({ ...input, mappings: input.mappings.slice(0, 1) }), /every legacy/i);
  host.migrateCorrections(input);
  const migrated = host.state()!.records[2];
  assert.deepEqual(migrated.corrections, [original, { ...original, cause: 'Stopped before writes' }]);
  assert.equal(migrated.legacyMigration!.mappings.length, 2);
  requireWritable(migrated);
  const next = { ...original, problemId: 'consumer-contract', evidenceKey: input.mappings[0].findings[0].evidenceKey };
  approveCorrection(migrated, next); approveCorrection(migrated, next);
  assert.throws(() => approveCorrection(migrated, next), /three|exhausted/);
  const unstarted = { ...next, problemId: 'other-contract', evidenceKey: input.mappings[1].findings[0].evidenceKey };
  approveCorrection(migrated, unstarted); approveCorrection(migrated, unstarted); approveCorrection(migrated, unstarted);
  assert.throws(() => approveCorrection(migrated, unstarted), /three|exhausted/);
  assert.throws(() => host.migrateCorrections({ ...input, stateHash: hash(fs.readFileSync(file)) }), /already migrated/i);
});
test('request_correction tool carries structured identity rather than a summary-only diagnosis', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1);
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      const findings = [finding(request)]; let received: unknown;
      const tool = codingTools(sdk, { ...request, requestCorrection: (...args) => { received = args; } }, signal(), error => { throw error; }).find(tool => tool.name === 'request_correction')!;
      await tool.execute('structured-correction', { findings, change: 'Fix the consumer', contract: 'Approved behavior unchanged' }, undefined, undefined, {} as Pi.ExtensionToolContext);
      assert.deepEqual(received, [findings, 'Fix the consumer', 'Approved behavior unchanged'], 'Tool must pass stable finding identity to the host');
    }
    return { status: 'blocked', summary: 'Completed tool boundary probe', evidence: ['Tool schema and callback'] };
  } });
  await f.host().run({}, signal());
  assert.match(f.host().state()!.records[2].attempts[0].error!, /Completed tool boundary probe/, 'Structured correction tool boundary must pass');
});
test('twelve complete-impact files are allowed by real SDK source tools and reviewed, not hard-split', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1); const agent = f.getAgent();
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      certifySyntheticRed(request);
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
test('preflight reports state-derived readiness without GitHub reads, tests or state writes', async () => {
  const f = fixture(1);
  f.setAgent({ run: async request => request.role === 'implement'
    ? { status: 'blocked', summary: 'Synthetic stop before any native check', evidence: ['Synthetic original'] }
    : { status: 'passed', summary: 'Independent synthetic review', evidence: ['Synthetic review'] } });
  await f.host().run({}, signal());
  const host = f.host();
  assert.equal(typeof host.preflight, 'function', 'GitHubHost must expose the read-only preflight method');
  const before = fs.readFileSync(path.join(f.state, 'github-state.json'), 'utf8');
  f.reader.issue = () => { throw new Error('preflight must not read GitHub'); };
  const report = host.preflight();
  assert.equal(fs.readFileSync(path.join(f.state, 'github-state.json'), 'utf8'), before, 'preflight must not modify state');
  assert.equal(report.root, 1); assert.equal(report.integrationHead, f.head);
  assert.equal(report.checkout.head, f.head); assert.equal(report.checkout.dirty, false); assert.equal(report.checkout.matchesIntegrationHead, true);
  assert.deepEqual(report.writerLock, { present: false, owner: 'none', pid: null });
  assert.equal(report.unfinished, 1);
  assert.deepEqual(report.toolWhitelist, { unitTest: true, rangeTest: false, engineChecksAuthorized: false });
  const record = report.records.find(row => row.issue === 2)!;
  assert.equal(record.phase, 'blocked'); assert.equal(record.attempts, 1); assert.equal(record.resumable, true);
  assert.equal(record.pendingFailure, false); assert.equal(record.tdd!.declared, false); assert.equal(record.tdd!.redCertified, false);
  assert.equal(record.greenCheckAtCurrentFingerprint, false, 'no native GREEN bound to the current fingerprint');
  assert.ok(record.blockers.some(text => /no successful native check/i.test(text)), 'the missing current-fingerprint GREEN must be listed as a blocker');
  assert.equal(record.dependencyProofs.length, 0);
});
test('preflight resumability reflects lock ownership, GREEN-at-fingerprint and correction exhaustion', async () => {
  const g = fixture(1);
  g.setAgent({ run: async request => {
    if (request.role === 'implement') {
      certifySyntheticRed(request);
      fs.writeFileSync(path.join(g.cwd, 'a.txt'), 'issue-2');
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(g.cwd).fingerprint, code: 0 });
      return { status: 'blocked', summary: 'Synthetic stop after GREEN', evidence: ['Synthetic original'] };
    }
    return { status: 'passed', summary: 'Independent synthetic review', evidence: ['Synthetic review'] };
  } });
  await g.host().run({}, signal());
  const host = g.host();
  const current = host.preflight().records.find(row => row.issue === 2)!;
  assert.equal(current.greenCheckAtCurrentFingerprint, true);
  assert.equal(current.blockers.some(text => /no successful native check/i.test(text)), false, 'a current-fingerprint GREEN must not be listed as a blocker');
  assert.equal(current.resumable, true);
  const state = host.state()!;
  state.records[2].attempts[0].dependencyProofs.push({ issue: 7, commit: g.head, evidence: 'valid proof' }, { issue: 8, commit: 'z'.repeat(40), evidence: 'non-hex commit' });
  const original = { cause: 'Located synthetic failure', change: 'Repair the consumer', contract: 'Approved contract unchanged', evidence: '/original/synthetic', sourceFingerprint: state.records[2].attempts[0].original.fingerprint, kind: 'check' as const, utc: '2026-11-14T00:00:00Z', problemId: 'consumer-contract', evidenceKey: `${state.records[2].attempts[0].original.fingerprint}:a.txt#consumer-contract`, outcome: 'failed' as const };
  state.records[2].corrections = [original, original, { ...original, outcome: undefined }];
  atomicJson(path.join(g.state, 'github-state.json'), state);
  const diagnosed = host.preflight().records.find(row => row.issue === 2)!;
  assert.deepEqual(diagnosed.dependencyProofs, [
    { issue: 7, commit: g.head, validCommit: true, ancestorOfHead: true, reusable: true },
    { issue: 8, commit: 'z'.repeat(40), validCommit: false, ancestorOfHead: false, reusable: false },
  ]);
  assert.equal(diagnosed.problems[0].problemId, 'consumer-contract');
  assert.equal(diagnosed.problems[0].roundsUsed, 3);
  assert.equal(diagnosed.problems[0].roundsRemaining, 0);
  assert.equal(diagnosed.problems[0].pendingCorrection?.kind, 'check');
  assert.equal(diagnosed.problems[0].pendingCorrection?.sourceChangedSinceFailure, true);
  assert.equal(diagnosed.problems[0].pendingCorrection?.evidenceBindingAvailable, false);
  assert.ok(diagnosed.blockers.some(text => /pending correction for consumer-contract/i.test(text)));
  assert.equal(diagnosed.resumable, true, 'a diagnosed pending correction remains resumable; only lock/exhaustion hard-block');
  state.records[2].corrections = [original, original, original];
  atomicJson(path.join(g.state, 'github-state.json'), state);
  const exhausted = host.preflight().records.find(row => row.issue === 2)!;
  assert.equal(exhausted.resumable, false);
  assert.ok(exhausted.blockers.some(text => /exhausted three repair rounds/i.test(text)));
  const release = acquire(g.cwd, g.state);
  const locked = host.preflight();
  assert.deepEqual(locked.writerLock, { present: true, owner: 'alive', pid: process.pid });
  assert.equal(locked.records.find(row => row.issue === 2)!.resumable, false);
  assert.ok(locked.records.find(row => row.issue === 2)!.blockers.some(text => /live owner/i.test(text)));
  release();
});
test('cli preflight subcommand prints the readiness report and stays option-guarded', async () => {
  const f = fixture(1);
  f.setAgent({ run: async () => ({ status: 'blocked', summary: 'Synthetic stop', evidence: ['Synthetic original'] }) });
  await f.host().run({}, signal());
  await assert.rejects(githubMain(['preflight', '--issue', '1', '--state', f.state, '--cwd', f.cwd, '--commit']), /Inappropriate/);
  assert.equal(await githubMain(['preflight', '--issue', '1', '--state', f.state, '--cwd', f.cwd]), 0);
  assert.equal(await githubMain(['preflight', '--issue', '1', '--state', f.state, '--cwd', f.cwd, '--engine-checks', '--godot-path', f.directory, '--fixed-package-directory', f.directory]), 0, 'engine options only reflect the tool whitelist, never launch anything');
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
  assert.ok((tools.find(tool => tool.name === 'unit_test')!.parameters as { properties: { filter?: unknown } }).properties.filter, 'unit_test must expose the FQN filter');
  const execute = (name: string, params: unknown) => tools.find(tool => tool.name === name)!.execute('synthetic', params, undefined, undefined, {} as Pi.ExtensionToolContext);
  assert.equal(tools.some(tool => ['bash', 'powershell'].includes(tool.name)), false);
  const unverified = await execute('scoped_write', { path: 'a.txt', content: 'denied' });
  assert.equal(unverified.isError, true); assert.match(JSON.stringify(unverified), /verified dependency/);
  request.pendingDependencies.clear();
  request.beforeWrite = () => {}; // Synthetic certified write controller; RED ordering is exercised through GitHubHost above.
  await execute('scoped_write', { path: 'a.txt', content: 'SDK source write' });
  assert.equal(fs.readFileSync(path.join(f.cwd, 'a.txt'), 'utf8'), 'SDK source write');
  const protectedPath = await execute('scoped_read', { path: '.git/config' });
  assert.equal(protectedPath.isError, true); assert.match(JSON.stringify(protectedPath), /Protected/);
  const privateAssets = await execute('scoped_read', { path: 'data/raw/private' });
  assert.equal(privateAssets.isError, true); assert.match(JSON.stringify(privateAssets), /Private/);
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
      ? ['scoped_read', 'scoped_edit', 'scoped_write', 'scoped_list', 'scoped_search', 'inspect_git', 'check_evidence', 'declare_red', 'request_correction', 'record_evidence_resolution', 'verify_dependency', 'unit_test']
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
test('failed check receipts embed verbatim classifier failures for exact re-declaration', async () => {
  const agentModule = await import('./github-agent.ts');
  const failureReceipt = (agentModule as Record<string, unknown>).failureReceipt as ((outcome: { code: number; expectedRed: boolean; summary: string }, directory: string) => string) | undefined;
  assert.ok(typeof failureReceipt === 'function', 'github-agent must compose agent-side failure receipts containing verbatim classifier failures');
  const directory = fs.mkdtempSync(path.join(suite, 'receipt-'));
  const exactFailures = [
    { test: 'Pal.Core.UnitTests.SeamTests.ExactRed identity ("case α")', message: `Expected: exact "verbatim" quote — ${'long diagnostic '.repeat(60)}beyond the summary truncation` },
    { test: 'Pal.Core.UnitTests.SeamTests.Second', message: 'Expected: 2 Actual: 3' },
    { test: 'Pal.Core.UnitTests.SeamTests.Third', message: 'Assert.Equal() failure' },
    { test: 'Pal.Core.UnitTests.SeamTests.FourthBeyondSummaryLimit', message: 'the fourth failure message must survive the summary slice intact' },
  ];
  fs.writeFileSync(path.join(directory, 'failure-classification.json'), JSON.stringify({ status: 'classified', nativeExit: 1, scope: 'unit', trx: path.join(directory, 'trx', 'results.trx'), expectedRed: false, failures: exactFailures }));
  fs.writeFileSync(path.join(directory, 'stdout.log'), 'synthetic stdout tail');
  fs.writeFileSync(path.join(directory, 'stderr.log'), 'synthetic stderr tail');
  const failed = failureReceipt({ code: 1, expectedRed: false, summary: 'Normal assertion/fixture failure: truncated by summary. Evidence: failure-classification.json' }, directory);
  for (const failure of exactFailures) {
    assert.ok(failed.includes(failure.test), `receipt must embed the exact classifier test identity: ${failure.test}`);
    assert.ok(failed.includes(failure.message), `receipt must embed the exact classifier message verbatim: ${failure.message.slice(0, 60)}`);
  }
  assert.ok(failed.includes('failed'), 'failed receipts keep the status marker');
  const expectedRed = failureReceipt({ code: 1, expectedRed: true, summary: 'Exact predeclared RED observed' }, directory);
  assert.ok(expectedRed.includes('expected_red'), 'expected_red receipts keep the status marker');
  assert.ok(expectedRed.includes(exactFailures[3].message), 'expected_red receipts also embed verbatim failures');
});
test('recovered implement prompts carry state-derived protocol facts', async () => {
  const f = fixture(2); f.issues[3].state = 'closed'; f.blockers[2] = [3];
  f.issues[3].comments.push({ body: `Accepted integration ${f.head}` } as IssueContext['comments'][number]);
  const prompts: string[] = []; let implementations = 0;
  f.setAgent({ run: async request => {
    prompts.push(request.prompt);
    if (request.role === 'implement') {
      implementations++;
      request.verifyDependency(3, f.head, `Accepted integration ${f.head}`);
      if (implementations === 1) {
        certifySyntheticRed(request);
        return { status: 'blocked', summary: 'Synthetic stop after verified dependency and certified RED', evidence: ['Synthetic pause'] };
      }
      fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'issue-2');
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
      return { status: 'candidate', summary: 'Recovered implementation completed', evidence: ['Recovered run'] };
    }
    return { status: 'passed', summary: 'Independent synthetic review', evidence: ['Synthetic review'] };
  } });
  await f.host().run({}, signal());
  await f.host().run({ resume: true }, signal());
  assert.equal(implementations, 2, 'resume must start a fresh implement session');
  const recovered = prompts[1];
  assert.ok(recovered.includes('Recovery protocol facts'), 'recovered implement prompt must carry the state-derived protocol facts section');
  assert.ok(recovered.includes(`Accepted integration ${f.head}`), 'protocol facts must show the stored dependency proof evidence string verbatim');
  assert.ok(recovered.includes('do not call request_correction'), 'protocol facts must carry the certified-expectedRed rule sentence');
  assert.ok(recovered.includes('proceed directly to production writes and GREEN'), 'protocol facts must direct certified RED straight to production writes and GREEN');
  assert.ok(recovered.includes('pure JSON'), 'protocol facts must require a pure JSON report');
  assert.ok(recovered.includes(syntheticDeclaration.expectedFailure.test) && recovered.includes(syntheticDeclaration.expectedFailure.message), 'protocol facts must restate the certified RED expectedFailure exactly');
  for (const review of [prompts[2], prompts[3]]) assert.doesNotMatch(review, /Recovery protocol facts/, 'review prompts stay read-only without implement protocol facts');
  assert.equal(f.host().state()!.records[2].attempts.length, 2);
});
test('record_evidence_resolution binds operator external evidence and rejects forged inputs', async () => {
  const f = fixture(1); const agent = f.getAgent(); let implementations = 0;
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      implementations++;
      if (implementations === 1) {
        certifySyntheticRed(request);
        fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'issue-2');
        request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
        return { status: 'candidate', summary: 'Initial candidate delivered', evidence: ['a.txt'] };
      }
      assert.ok(typeof request.recordEvidenceResolution === 'function', 'host must provide the external evidence resolution controller');
      const evidence = path.join(f.directory, 'operator-evidence.txt');
      fs.writeFileSync(evidence, 'operator-reviewed external evidence original');
      const sha256 = hash(fs.readFileSync(evidence));
      assert.throws(() => request.recordEvidenceResolution!('consumer-contract', path.join(f.cwd, 'a.txt'), sha256, 'repository-internal path'), /outside the repository/i, 'repository-internal paths rejected');
      assert.throws(() => request.recordEvidenceResolution!('consumer-contract', evidence, 'f'.repeat(64), 'wrong sha'), /sha256 does not match|mismatch/i, 'wrong sha256 rejected');
      assert.throws(() => request.recordEvidenceResolution!('unknown-problem', evidence, sha256, 'wrong problemId'), /pending review correction/i, 'unknown problemId rejected');
      request.recordEvidenceResolution!('consumer-contract', evidence, sha256, 'Operator resolved this finding with external evidence');
      assert.throws(() => request.recordEvidenceResolution!('consumer-contract', evidence, sha256, 'duplicate binding'), /already/i, 'duplicate binding rejected');
      const unchanged = snapshot(f.cwd);
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: unchanged.fingerprint, code: 0 });
      return { status: 'candidate', summary: 'Review correction resolved by operator external evidence; source deliberately unchanged', evidence: [evidence] };
    }
    if (request.role === 'standards' && implementations === 1) return { status: 'changes_requested', summary: 'Located consumer repair', evidence: ['a.txt'], findings: [finding(request)] };
    return agent.run(request);
  } });
  const result = await f.host().run({ commit: true }, signal());
  assert.equal(result.counts.local_completed, 1, 'source-unchanged review repair must pass the gate after binding');
  const ledger = f.host().state()!.records[2].corrections!;
  assert.equal(ledger.length, 1);
  assert.equal(ledger[0].kind, 'review');
  assert.equal(ledger[0].evidenceResolution?.file, path.join(f.directory, 'operator-evidence.txt'));
  assert.match(ledger[0].evidenceResolution?.sha256 ?? '', /^[0-9a-f]{64}$/);
});
test('record_evidence_resolution accepts an omitted sha256 by binding the host-computed file hash', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const f = fixture(1); const agent = f.getAgent(); let implementations = 0;
  f.setAgent({ run: async request => {
    if (request.role === 'implement') {
      implementations++;
      if (implementations === 1) {
        certifySyntheticRed(request);
        fs.writeFileSync(path.join(f.cwd, 'a.txt'), 'issue-2');
        request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: snapshot(f.cwd).fingerprint, code: 0 });
        return { status: 'candidate', summary: 'Initial candidate delivered', evidence: ['a.txt'] };
      }
      const evidence = path.join(f.directory, 'operator-evidence.txt');
      fs.writeFileSync(evidence, 'operator-reviewed external evidence original');
      const tool = codingTools(sdk, request, signal(), error => { throw error; }).find(item => item.name === 'record_evidence_resolution')!;
      const schema = tool.parameters as { properties: Record<string, unknown>; required?: string[] };
      assert.ok(!schema.required?.includes('sha256'), 'the tool schema must not force a client-side sha256 the agent cannot compute');
      const wrong = await tool.execute('wrong-sha', { problemId: 'consumer-contract', file: evidence, sha256: 'f'.repeat(64), note: 'provided wrong sha' }, undefined, undefined, {} as Pi.ExtensionToolContext);
      assert.equal(wrong.isError, true); assert.match(JSON.stringify(wrong), /sha256 does not match/);
      const resolved = await tool.execute('omitted-sha', { problemId: 'consumer-contract', file: evidence, note: 'operator resolution without client-side hashing' }, undefined, undefined, {} as Pi.ExtensionToolContext);
      assert.equal(resolved.isError, undefined, 'omitting sha256 must resolve instead of failing on the missing parameter');
      const parsed = JSON.parse((resolved.content as { type: 'text'; text: string }[])[0].text);
      assert.equal(parsed.sha256, hash(fs.readFileSync(evidence)));
      const unchanged = snapshot(f.cwd);
      request.recordCheck({ project: 'synthetic Unit', directory: request.directory, sourceFingerprint: unchanged.fingerprint, code: 0 });
      return { status: 'candidate', summary: 'Review correction resolved by operator evidence with a host-computed sha256', evidence: [evidence] };
    }
    if (request.role === 'standards' && implementations === 1) return { status: 'changes_requested', summary: 'Located consumer repair', evidence: ['a.txt'], findings: [finding(request)] };
    return agent.run(request);
  } });
  const result = await f.host().run({ commit: true }, signal());
  assert.equal(result.counts.local_completed, 1, 'source-unchanged review repair must still pass the gate after a host-computed binding');
  const ledger = f.host().state()!.records[2].corrections!;
  assert.equal(ledger[0].evidenceResolution?.sha256, hash(fs.readFileSync(path.join(f.directory, 'operator-evidence.txt'))));
});
test('parent-only advanceIntegration validates ownership and advances a single field', async () => {
  const f = fixture(1);
  f.setAgent({ run: async () => ({ status: 'blocked', summary: 'Synthetic stop', evidence: ['Synthetic original'] }) });
  await f.host().run({}, signal());
  const host = f.host();
  assert.equal(typeof host.advanceIntegration, 'function', 'GitHubHost must expose the parent-only advanceIntegration entry');
  assert.throws(() => host.advanceIntegration({ commit: 'abc' }), /40-hex/);
  assert.throws(() => host.advanceIntegration({ commit: 'a'.repeat(40) }), /current HEAD/);
  const state = host.state()!;
  const head = git(f.cwd, 'rev-parse', 'HEAD').trim();
  state.records[2].phase = 'working';
  atomicJson(path.join(f.state, 'github-state.json'), state);
  assert.throws(() => host.advanceIntegration({ commit: head }), /working/);
  const release = acquire(f.cwd, f.state);
  assert.throws(() => host.advanceIntegration({ commit: head }), /lock/);
  release();
  state.records[2].phase = 'blocked';
  atomicJson(path.join(f.state, 'github-state.json'), state);
  fs.writeFileSync(path.join(f.cwd, 'coordinator.txt'), 'explicit coordinator reconciliation');
  git(f.cwd, 'add', 'coordinator.txt'); git(f.cwd, 'commit', '--quiet', '-m', 'Manual coordinator commit');
  const advanced = git(f.cwd, 'rev-parse', 'HEAD').trim();
  host.advanceIntegration({ commit: advanced });
  const after = host.state()!;
  assert.equal(after.integrationHead, advanced);
  assert.equal(after.records[2].phase, 'blocked', 'only the integration head field advances');
  assert.throws(() => host.advanceIntegration({ commit: advanced }), /already/);
});
test('request_correction description confines the tool to unexpected normal failures', async () => {
  const sdk = await import('@earendil-works/pi-coding-agent'); const g = fixture(1);
  const request: CodingRequest = { cwd: g.cwd, issue: 2, role: 'implement', directory: g.directory, contexts: [], prompt: 'Synthetic', input: snapshot(g.cwd), signal: signal(), emit: () => {}, checks: [], pendingDependencies: new Set(), verifyDependency: () => {}, recordCheck: () => {} };
  const description = codingTools(sdk, request, signal(), () => {}).find(tool => tool.name === 'request_correction')!.description ?? '';
  assert.match(description, /unexpected complete normal failure/i, 'description must confine the tool to unexpected complete normal failures');
  assert.match(description, /certified expectedRed is planned work/i, 'description must state certified expectedRed is planned work');
  assert.match(description, /proceed directly to production writes and GREEN/i, 'description must direct certified RED straight to production writes and GREEN');
});
