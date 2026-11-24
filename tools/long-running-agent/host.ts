import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { canonical, hash, parsePlan, parseReport, requireScope, changedPaths, type AgentExecutor, type Plan, type State, type Task, type Source, type Issue, type Role } from './contract.ts';
import { acquire, atomicJson, git, inside, isIgnored, json, noLinks, QuiescenceUnknownError, readIssue, recoverLock, runCheck, snapshot } from './io.ts';

export interface Services {
  agent: AgentExecutor;
  issue: (cwd: string, number: number) => { issue: Issue; blockers: number[] };
  source: (cwd: string) => Source;
  check: typeof runCheck;
}
export const defaultServices = (agent: AgentExecutor): Services => ({ agent, issue: readIssue, source: snapshot, check: runCheck });
function authorize(plan: Plan): void {
  if (Date.now() >= Date.parse(plan.approval.deadlineUtc)) throw new Error('Execution approval expired');
}
function ensureAncestor(cwd: string, commit: string): void { git(cwd, 'merge-base', '--is-ancestor', commit, 'HEAD'); }
function evidenceHash(file: string): string { noLinks(file); return hash(fs.readFileSync(file)); }
function checkState(state: State, plan: Plan): void {
  if (state.version !== 1 || state.kind !== 'pal_agent_execution' || state.planHash !== hash(canonical(plan)) || state.cwd !== plan.cwd) throw new Error('State/plan identity mismatch');
  if (!/^[0-9a-f]{40}$/.test(state.integrationHead)) throw new Error('Invalid integration HEAD');
  if (canonical(Object.keys(state.tasks).sort()) !== canonical(plan.tasks.map(t => String(t.issue)).sort())) throw new Error('State task identities mismatch');
  if (canonical(Object.keys(state.prerequisites).sort()) !== canonical(plan.prerequisites.map(t => String(t.issue)).sort())) throw new Error('State prerequisite identities mismatch');
  for (const [id, task] of Object.entries(state.tasks)) {
    if (!['pending', 'running', 'blocked', 'awaiting_acceptance', 'accepted'].includes(task.phase) || !Array.isArray(task.attempts)) throw new Error('Invalid task state');
    if (task.phase !== 'pending' && !task.attempts.length) throw new Error('Missing attempt identity');
    for (const attempt of task.attempts) {
      if (!/^[0-9a-f-]{36}$/.test(attempt.id) || attempt.directory !== path.join('attempts', `${id}-${attempt.id}`)) throw new Error('Invalid attempt path');
    }
    if (task.phase === 'accepted' && (!/^[0-9a-f]{40}$/.test(task.commit ?? '') || !/^[0-9a-f]{64}$/.test(task.receipt ?? ''))) throw new Error('Invalid accepted receipt');
  }
}
export function initialize(planFile: string, directory: string): void {
  const plan = parsePlan(json(planFile));
  if (!path.isAbsolute(directory)) throw new Error('State directory must be absolute');
  authorize(plan);
  noLinks(plan.cwd); noLinks(directory);
  if (fs.realpathSync(plan.cwd) !== path.resolve(plan.cwd)) throw new Error('cwd must be the canonical repository path');
  if (inside(plan.cwd, path.resolve(directory))) throw new Error('Execution evidence must be outside the working tree');
  if (fs.existsSync(directory)) throw new Error('State directory must be new');
  const release = acquire(plan.cwd, directory);
  try {
    const source = snapshot(plan.cwd);
    if (source.dirty || source.head !== plan.baseHead || git(plan.cwd, 'rev-parse', '--show-toplevel').trim().replaceAll('\\', '/') !== plan.cwd.replaceAll('\\', '/')) throw new Error('Initialization requires clean repository root at explicit baseHead');
    for (const task of plan.tasks) {
      for (const file of task.allowedWritePaths) {
        noLinks(path.resolve(plan.cwd, file));
        if (fs.existsSync(path.resolve(plan.cwd, file)) && !fs.statSync(path.resolve(plan.cwd, file)).isFile()) throw new Error(`Writable path must be a file: ${file}`);
        if (isIgnored(plan.cwd, file)) throw new Error(`Writable file is ignored: ${file}`);
      }
    }
    fs.mkdirSync(directory, { recursive: true });
    fs.mkdirSync(path.join(directory, 'attempts')); fs.mkdirSync(path.join(directory, 'receipts'));
    const prerequisites: State['prerequisites'] = {};
    for (const item of plan.prerequisites) {
      ensureAncestor(plan.cwd, item.commit);
      noLinks(item.receipt);
      const bytes = fs.readFileSync(item.receipt);
      if (!bytes.length) throw new Error('Empty prerequisite receipt');
      fs.writeFileSync(path.join(directory, 'receipts', `prerequisite-${item.issue}`), bytes, { flag: 'wx' });
      prerequisites[item.issue] = { commit: item.commit, receiptHash: hash(bytes) };
    }
    atomicJson(path.join(directory, 'plan.json'), plan);
    atomicJson(path.join(directory, 'state.json'), {
      version: 1, kind: 'pal_agent_execution', cwd: plan.cwd, integrationHead: plan.baseHead, planHash: hash(canonical(plan)), prerequisites,
      tasks: Object.fromEntries(plan.tasks.map(task => [task.issue, { phase: 'pending', attempts: [] }])), updatedUtc: new Date().toISOString(),
    } satisfies State);
  } finally { release(); }
}
export class Host {
  readonly directory: string;
  readonly plan: Plan;
  readonly services: Services;
  constructor(directory: string, services: Services) {
    this.directory = path.resolve(directory); noLinks(this.directory);
    this.plan = parsePlan(json(path.join(this.directory, 'plan.json')));
    if (inside(this.plan.cwd, this.directory)) throw new Error('State directory must be outside source');
    this.services = services;
    this.state();
  }
  state(): State {
    const state = json(path.join(this.directory, 'state.json')) as State;
    checkState(state, this.plan); return state;
  }
  private save(state: State): void {
    checkState(state, this.plan);
    state.updatedUtc = new Date().toISOString(); atomicJson(path.join(this.directory, 'state.json'), state);
  }
  private dependencies(state: State, task: Task): boolean {
    for (const item of this.plan.prerequisites) {
      const receipt = state.prerequisites[item.issue];
      if (receipt.commit !== item.commit || evidenceHash(path.join(this.directory, 'receipts', `prerequisite-${item.issue}`)) !== receipt.receiptHash) throw new Error('Prerequisite receipt changed');
      ensureAncestor(this.plan.cwd, item.commit);
    }
    for (const record of Object.values(state.tasks).filter(t => t.phase === 'accepted')) {
      ensureAncestor(this.plan.cwd, record.commit!);
      if (evidenceHash(path.join(this.directory, 'receipts', `${record.commit}.json`)) !== record.receipt) throw new Error('Accepted receipt changed');
    }
    return task.dependsOn.every(id => state.prerequisites[id] || state.tasks[id]?.phase === 'accepted');
  }
  private freshIssue(state: State, task: Task): { issue: Issue; context: Issue[]; fingerprint: string } {
    const result = this.services.issue(this.plan.cwd, task.issue);
    if (result.issue.state !== 'OPEN' || !result.issue.labels.some(l => l.name === 'ready-for-agent')) throw new Error('Issue must be open and ready-for-agent');
    for (const blocker of result.blockers) {
      if (!task.dependsOn.includes(blocker)) throw new Error(`Unbound live dependency #${blocker}; update the approved plan`);
      if (!state.prerequisites[blocker] && state.tasks[blocker]?.phase !== 'accepted') throw new Error(`Dependency #${blocker} has no accepted integration receipt`);
    }
    const context = [...new Set([...task.contextIssues, ...task.dependsOn])].map(number => this.services.issue(this.plan.cwd, number).issue);
    return { issue: result.issue, context, fingerprint: hash(canonical({ ...result, context })) };
  }
  async run(signal: AbortSignal): Promise<State> {
    const release = acquire(this.plan.cwd, this.directory);
    try {
      for (;;) {
        authorize(this.plan);
        if (signal.aborted) throw new Error('Host cancelled');
        const state = this.state();
        if (Object.values(state.tasks).some(t => ['running', 'blocked', 'awaiting_acceptance'].includes(t.phase))) throw new Error('An attempt needs recovery, retry approval, or manual acceptance');
        const task = this.plan.tasks.find(t => state.tasks[t.issue].phase === 'pending' && this.dependencies(state, t));
        if (!task) return state;
        await this.execute(state, task, signal);
        const next = this.state();
        if (!this.plan.approval.autoAdvance || next.tasks[task.issue].phase !== 'accepted') return next;
      }
    } finally {
      // A timed-out native check or uncertain commit may have surviving descendants.
      // Keep the lock as recovery evidence instead of advertising an idle writer.
      if (!Object.values(this.state().tasks).some(t => t.attempts.at(-1)?.quiescenceUnknown)) release();
    }
  }
  private async execute(state: State, task: Task, signal: AbortSignal): Promise<void> {
    const record = state.tasks[task.issue];
    const input = this.services.source(this.plan.cwd);
    if (input.head !== state.integrationHead) throw new Error('Integration HEAD drifted');
    if (record.retrySource) {
      if (canonical(input) !== canonical(record.retrySource)) throw new Error('Approved retry source changed');
      requireScope(task, record.attempts[0].input, input);
    } else if (input.dirty) throw new Error('New ticket requires a clean integration checkout');
    const fresh = this.freshIssue(state, task);
    const id = randomUUID();
    const attempt = { id, directory: path.join('attempts', `${task.issue}-${id}`), input, issueHash: fresh.fingerprint, startedUtc: new Date().toISOString() } as State['tasks'][string]['attempts'][number];
    const directory = path.join(this.directory, attempt.directory);
    fs.mkdirSync(directory);
    atomicJson(path.join(directory, 'input.json'), { issue: fresh.issue, contextIssues: fresh.context, source: input, approval: this.plan.approval });
    record.attempts.push(attempt); record.phase = 'running'; delete record.retrySource;
    this.save(state); // Durable dispatch intent precedes SDK construction.
    const eventFile = fs.openSync(path.join(directory, 'events.jsonl'), 'wx');
    const controller = new AbortController();
    const cancel = () => controller.abort();
    signal.addEventListener('abort', cancel, { once: true });
    if (signal.aborted) cancel();
    const time = Math.min(this.plan.limits.timeoutMs, Date.parse(this.plan.approval.deadlineUtc) - Date.now());
    const deadline = Date.now() + Math.max(0, time);
    const active = () => {
      authorize(this.plan);
      if (controller.signal.aborted || Date.now() >= deadline) throw new Error('Attempt stopped by cancellation or deadline');
    };
    const timer = setTimeout(cancel, Math.max(1, time));
    const emit = (event: unknown) => fs.writeSync(eventFile, JSON.stringify({ utc: new Date().toISOString(), event }) + '\n');
    const reports: Record<string, unknown> = {};
    try {
      const run = async (role: Role) => {
        active();
        const roleDirectory = path.join(directory, role); fs.mkdirSync(roleDirectory);
        const prompt = [
          `Work only on GitHub issue #${task.issue} in ${this.plan.repository}. Role: ${role}.`,
          `Approved task and live comments:\n${JSON.stringify(fresh.issue)}`,
          `Parent specifications / direct dependency issues and comments:\n${JSON.stringify(fresh.context)}`,
          `Writable files (implementation only): ${JSON.stringify(task.allowedWritePaths)}`,
          `Declared native checks: ${JSON.stringify(task.checks)}`,
          'Follow all applicable AGENTS.md instructions. Do not submit, push, merge, run an engine, delegate, or alter requirements. No shell tools are available. Host runs the explicitly approved checks.',
          role === 'implement' ? 'Implement the single ticket, with tests. Do not claim tests ran. Stop and report blocked if requirements or allowed files are insufficient.' : `Read-only independent ${role} review. ${role === 'standards' ? 'Check documented project rules and affected contracts.' : 'Check issue requirements, normal/error/edge paths and omissions.'} Review the actual frozen diff below, not the implementer narrative.`,
          role === 'implement' ? '' : `Changed tracked/nonignored source files: ${JSON.stringify(changedPaths(attempt.input, attempt.candidate!))}\nDiff:\n${git(this.plan.cwd, 'diff', '--no-ext-diff', '--no-textconv', attempt.input.head, '--', ...task.allowedWritePaths).slice(0, 80_000)}\nCheck records: ${JSON.stringify(Object.fromEntries(Object.entries(reports).filter(([key]) => key.startsWith('check-'))))}`,
          'Return ONLY a JSON object {"status":"candidate|blocked" (implementation) or "passed|changes_requested|blocked" (review),"summary":"...","evidence":["specific source/requirement/check references"]}. Reviewers must inspect current files, including new files absent from Git diff. A final response is not integration or milestone acceptance.',
        ].join('\n\n');
        fs.writeFileSync(path.join(roleDirectory, 'brief.txt'), prompt, { flag: 'wx' });
        const report = parseReport(await this.services.agent.run({ plan: this.plan, task, role, prompt, directory: roleDirectory, signal: controller.signal, emit }), role);
        if (controller.signal.aborted) throw new Error('Attempt cancelled');
        atomicJson(path.join(roleDirectory, 'report.json'), report);
        reports[role] = report;
        if (report.status !== (role === 'implement' ? 'candidate' : 'passed')) throw new Error(`${role}: ${report.status}: ${report.summary}`);
      };
      await run('implement');
      attempt.candidate = this.services.source(this.plan.cwd);
      requireScope(task, record.attempts[0].input, attempt.candidate);
      if (attempt.candidate.head !== input.head || attempt.candidate.indexFingerprint !== input.indexFingerprint || !changedPaths(record.attempts[0].input, attempt.candidate).length) throw new Error('Implementation changed HEAD/index or produced no candidate changes');
      for (const [index, check] of task.checks.entries()) {
        const checkDirectory = path.join(directory, `check-${index}`); fs.mkdirSync(checkDirectory);
        active();
        const boundCheck = { ...check, args: check.args.map(arg => arg.replaceAll('{checkDir}', checkDirectory)) };
        await this.services.check(this.plan.cwd, boundCheck, checkDirectory, controller.signal);
        reports[`check-${index}`] = json(path.join(checkDirectory, 'result.json'));
        if (canonical(this.services.source(this.plan.cwd)) !== canonical(attempt.candidate)) throw new Error('Check modified candidate source');
      }
      await run('standards'); await run('spec');
      if (canonical(this.services.source(this.plan.cwd)) !== canonical(attempt.candidate)) throw new Error('Frozen candidate changed during review');
      if (this.freshIssue(state, task).fingerprint !== attempt.issueHash) throw new Error('Issue or dependencies changed during execution');
      active();
      atomicJson(path.join(directory, 'gate.json'), { source: attempt.candidate, reports });
      attempt.gateHash = evidenceHash(path.join(directory, 'gate.json'));
      attempt.endedUtc = new Date().toISOString(); record.phase = 'awaiting_acceptance'; this.save(state);
      if (this.plan.approval.localCommits) {
        const files = changedPaths(record.attempts[0].input, attempt.candidate);
        if (git(this.plan.cwd, 'diff', '--cached', '--name-only').trim()) throw new Error('Index must be empty before approved local commit');
        active();
        attempt.commitStarted = true; this.save(state);
        git(this.plan.cwd, 'add', '-A', '--', ...files);
        active();
        git(this.plan.cwd, 'commit', '-m', `Implement #${task.issue} (${this.plan.approval.id})`);
        active();
        this.acceptCandidate(state, task, 'Approved automatic local checks and independent reviews');
      }
    } catch (error) {
      record.phase = 'blocked'; attempt.error = error instanceof Error ? error.message : String(error);
      if (error instanceof QuiescenceUnknownError || attempt.commitStarted) attempt.quiescenceUnknown = true;
      attempt.endedUtc = new Date().toISOString(); this.save(state);
    } finally { clearTimeout(timer); signal.removeEventListener('abort', cancel); fs.closeSync(eventFile); }
  }
  private acceptCandidate(state: State, task: Task, operatorEvidence: string): void {
    authorize(this.plan);
    const record = state.tasks[task.issue], attempt = record.attempts.at(-1)!;
    if (!['awaiting_acceptance', 'blocked'].includes(record.phase) || !attempt.candidate || !attempt.gateHash) throw new Error('No accepted gate for this candidate');
    if (evidenceHash(path.join(this.directory, attempt.directory, 'gate.json')) !== attempt.gateHash) throw new Error('Gate evidence changed');
    const current = this.services.source(this.plan.cwd);
    if (current.dirty || current.fingerprint !== attempt.candidate.fingerprint || current.head === attempt.input.head) throw new Error('Commit the exact frozen candidate, then accept from its clean HEAD');
    ensureAncestor(this.plan.cwd, attempt.input.head);
    if (this.freshIssue(state, task).fingerprint !== attempt.issueHash) throw new Error('Issue changed before acceptance');
    const receipt = path.join(this.directory, 'receipts', `${current.head}.json`);
    if (fs.existsSync(receipt)) throw new Error('Receipt already exists; reconcile before proceeding');
    atomicJson(receipt, { issue: task.issue, attempt: attempt.id, commit: current.head, sourceFingerprint: current.fingerprint, gateHash: attempt.gateHash, operatorEvidence, approval: this.plan.approval.id, acceptedUtc: new Date().toISOString(), scope: 'local candidate only; not Issue closure, Game/GPU/human/CI or milestone acceptance' });
    record.phase = 'accepted'; record.commit = current.head; record.receipt = evidenceHash(receipt); state.integrationHead = current.head; this.save(state);
  }
  accept(issue: number, expectedHead: string, receiptFile: string): void {
    const release = acquire(this.plan.cwd, this.directory);
    try {
      authorize(this.plan);
      const state = this.state(), task = this.plan.tasks.find(t => t.issue === issue);
      if (!task || this.services.source(this.plan.cwd).head !== expectedHead) throw new Error('Ticket or expected HEAD mismatch');
      noLinks(receiptFile);
      const bytes = fs.readFileSync(receiptFile);
      if (!bytes.length) throw new Error('Manual acceptance requires nonempty operator evidence');
      const target = path.join(this.directory, 'receipts', `operator-${issue}-${randomUUID()}`);
      fs.writeFileSync(target, bytes, { flag: 'wx' });
      this.acceptCandidate(state, task, `${target} sha256=${hash(bytes)}`);
    } finally { release(); }
  }
  retry(issue: number, expectedFingerprint: string, reason: string): void {
    const release = acquire(this.plan.cwd, this.directory);
    try {
      authorize(this.plan);
      const state = this.state(), record = state.tasks[issue];
      if (!reason.trim() || record?.phase !== 'blocked') throw new Error('Retry requires a blocked ticket and explicit reason');
      const source = this.services.source(this.plan.cwd);
      if (source.fingerprint !== expectedFingerprint) throw new Error('Retry source fingerprint mismatch');
      requireScope(this.plan.tasks.find(t => t.issue === issue)!, record.attempts[0].input, source);
      record.phase = 'pending'; record.retrySource = source;
      atomicJson(path.join(this.directory, record.attempts.at(-1)!.directory, `retry-${randomUUID()}.json`), { reason, source, approvedUtc: new Date().toISOString() });
      this.save(state);
    } finally { release(); }
  }
  recover(confirmQuiescent: boolean, reason: string): void {
    if (!confirmQuiescent || !reason.trim()) throw new Error('Recovery requires explicit quiescence confirmation and reason');
    recoverLock(this.plan.cwd, this.directory);
    const release = acquire(this.plan.cwd, this.directory);
    try {
      const state = this.state();
      for (const record of Object.values(state.tasks)) {
        if (record.phase === 'running') {
          record.phase = 'blocked'; record.attempts.at(-1)!.error = `Interrupted; operator confirmed quiescence: ${reason}`;
          record.attempts.at(-1)!.endedUtc = new Date().toISOString();
        }
        if (record.attempts.at(-1)) record.attempts.at(-1)!.quiescenceUnknown = false;
      }
      atomicJson(path.join(this.directory, `recovery-${randomUUID()}.json`), { reason, confirmedQuiescent: true, utc: new Date().toISOString() });
      this.save(state);
    } finally { release(); }
  }
}
