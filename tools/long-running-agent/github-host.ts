import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { canonical, changedPaths, hash, parseReport, type Report, type Role, type Source } from './contract.ts';
import { acquire, atomicJson, git, inside, json, lockFile, noLinks, QuiescenceUnknownError, recoverLock, snapshot } from './io.ts';
import { approved, contextHash, contextsFor, loadGraph, type GitHubReader, type IssueContext, type IssueGraph } from './github.ts';
import { approveCorrection, defaultCorrections, requireChangedAfterFailure, requireWritable, scopeWarningFiles, type Correction, type CorrectionLedger } from './policy.ts';

export interface UnitEvidence { project: string; directory: string; sourceFingerprint: string; code: number; scope?: string; expectedRed?: boolean }
export interface CodingEvent { type: string; role?: Role; tool?: string; text?: string; sessionFile?: string; turns?: number }
export interface CodingRequest {
  cwd: string; issue: number; role: Role; directory: string; contexts: IssueContext[]; prompt: string;
  input: Source; signal: AbortSignal; emit: (event: CodingEvent) => void;
  checks: UnitEvidence[]; pendingDependencies: Set<number>;
  verifyDependency: (issue: number, commit: string, evidence: string) => void;
  recordCheck: (check: UnitEvidence) => void;
  stateDirectory?: string;
  beforeWrite?: () => void;
  beforeCheck?: (source: Source) => void;
  recordFailure?: (directory: string, source: Source, summary: string) => void;
  requestCorrection?: (cause: string, change: string, contract: string) => void;
}
export interface CodingExecutor { run(request: CodingRequest): Promise<Report> }
export interface GitHubServices { reader: GitHubReader; agent: CodingExecutor; source: typeof snapshot }
export interface RunOptions { commit?: boolean; maxIterations?: number; resume?: boolean; confirmQuiescent?: boolean; extraCorrections?: number }
type SourceBinding = Pick<Source, 'head' | 'fingerprint' | 'indexFingerprint' | 'dirty'>;
const binding = ({ head, fingerprint, indexFingerprint, dirty }: Source): SourceBinding => ({ head, fingerprint, indexFingerprint, dirty });
export interface GitHubAttempt {
  id: string; directory: string; startedUtc: string; endedUtc?: string; input: SourceBinding; original: SourceBinding; inputHash?: string;
  contextsHash: string; candidate?: SourceBinding; gateHash?: string; error?: string; summary?: string;
  sessionFiles?: Partial<Record<Role, string>>;
  checks: UnitEvidence[]; dependencyProofs: { issue: number; commit: string; evidence: string }[];
  unknownQuiescence?: boolean; commitStarted?: boolean;
}
export interface GitHubRecord extends CorrectionLedger {
  phase: 'working' | 'blocked' | 'awaiting_commit' | 'completed'; attempts: GitHubAttempt[]; commit?: string;
}
export interface GitHubState {
  version: 1; kind: 'github_issue_execution_index'; root: number; cwd: string; integrationHead: string;
  records: Record<string, GitHubRecord>; updatedUtc: string;
  activity?: { issue: number; role?: Role; tool?: string; sessionFile?: string; turns?: number; updatedUtc: string };
  parentAudit?: { fingerprint: string; head: string; contextsHash: string; graphHash: string; directory: string; reportHash: string; status: 'passed' | 'blocked'; summary: string; interrupted?: boolean; unknownQuiescence?: boolean };
  extraApprovals?: { issue: number; count: number; sourceFingerprint: string; utc: string }[];
}
export interface StatusRow { number: number; title: string; status: string; reason?: string; commit?: string; prerequisitesToVerify: number[]; evidenceDirectory?: string; sessionFiles?: Partial<Record<Role, string>>; lastSummary?: string }
export interface GitHubStatus {
  root: number; cwd: string; directory: string; fetchedUtc: string; stale: boolean; warnings: string[];
  counts: Record<string, number>; activity?: GitHubState['activity']; rows: StatusRow[]; integrationHead?: string; nextAction: string;
  checkout: { head: string; branch: string; dirty: boolean };
  specStatus: 'not_checked' | 'working' | 'interrupted' | 'owner_unknown' | 'passed' | 'blocked' | 'stale';
  specAudit?: { summary: string; evidenceDirectory: string };
}
const graphIdentity = (graph: IssueGraph): string => hash(canonical(Object.values(graph.nodes).map(node => [node.issue.number, node.children, node.parents, node.blockers.map(blocker => blocker.number)])));
function readState(directory: string, cwd: string, root: number): GitHubState | undefined {
  const file = path.join(directory, 'github-state.json');
  if (!fs.existsSync(file)) return;
  noLinks(directory);
  const state = json(file) as GitHubState;
  if (state.version !== 1 || state.kind !== 'github_issue_execution_index' || state.cwd !== cwd || state.root !== root || !/^[0-9a-f]{40}$/.test(state.integrationHead)) throw new Error('GitHub run identity mismatch');
  for (const [id, record] of Object.entries(state.records)) {
    if (!/^\d+$/.test(id) || !['working', 'blocked', 'awaiting_commit', 'completed'].includes(record.phase) || !record.attempts.length) throw new Error('Invalid GitHub task state');
    for (const attempt of record.attempts) if (!/^[0-9a-f-]{36}$/.test(attempt.id) || attempt.directory !== path.join('attempts', `${id}-${attempt.id}`)) throw new Error('Invalid attempt path');
    if (record.phase === 'completed' && !/^[0-9a-f]{40}$/.test(record.commit ?? '')) throw new Error('Missing local integration identity');
  }
  return state;
}
function ancestor(cwd: string, commit: string): boolean {
  try { git(cwd, 'merge-base', '--is-ancestor', commit, 'HEAD'); return true; } catch { return false; }
}
function validGate(directory: string, record: GitHubRecord): boolean {
  const attempt = record.attempts.at(-1)!;
  try {
    const file = path.join(directory, attempt.directory, 'gate.json'); noLinks(file);
    return Boolean(attempt.gateHash && hash(fs.readFileSync(file)) === attempt.gateHash);
  } catch { return false; }
}
function ownerAlive(cwd: string, directory: string): boolean | undefined {
  if (!fs.existsSync(lockFile(cwd))) return false;
  const lock = json(lockFile(cwd)) as { pid: number; stateDirectory: string };
  if (lock.stateDirectory !== directory || !Number.isSafeInteger(lock.pid) || lock.pid <= 0) return undefined;
  try { process.kill(lock.pid, 0); return true; }
  catch (error) { return (error as NodeJS.ErrnoException).code === 'ESRCH' ? false : undefined; }
}
export class GitHubHost {
  readonly cwd: string;
  readonly root: number;
  readonly directory: string;
  readonly services: GitHubServices;
  readonly output: (event: CodingEvent) => void;
  constructor(cwd: string, root: number, directory: string, services: GitHubServices, output: (event: CodingEvent) => void = () => {}) {
    this.cwd = cwd; this.root = root; this.directory = directory; this.services = services; this.output = output;
  }
  state(): GitHubState | undefined { return readState(this.directory, this.cwd, this.root); }
  private save(state: GitHubState): void {
    state.updatedUtc = new Date().toISOString();
    atomicJson(path.join(this.directory, 'github-state.json'), state);
    const lines = ['# GitHub coding progress', '', `Root: #${this.root}`, `Updated: ${state.updatedUtc}`, `Integration: ${state.integrationHead}`, '', 'Local execution only; GitHub remains the specification/task source.', ''];
    for (const [number, record] of Object.entries(state.records)) {
      const last = record.attempts.at(-1)!;
      lines.push(`- #${number}: ${record.phase}${record.commit ? ` @ ${record.commit}` : ''}`, `  - ${last.summary ?? last.error ?? 'In progress'}`, `  - Evidence: ${last.directory}`);
    }
    const temp = path.join(this.directory, `progress-${randomUUID()}.tmp`);
    fs.writeFileSync(temp, lines.join('\n') + '\n'); fs.renameSync(temp, path.join(this.directory, 'progress.md'));
  }
  status(offline = false): GitHubStatus {
    let graph: IssueGraph, stale = offline;
    if (offline) {
      const file = path.join(this.directory, 'github-snapshot.json');
      if (!fs.existsSync(file)) throw new Error('No saved GitHub snapshot; start a run first, or use online status');
      graph = json(file) as IssueGraph;
    } else graph = loadGraph(this.services.reader, this.root);
    if (graph.root !== this.root) throw new Error('Saved graph root mismatch');
    const state = this.state();
    const checkout = { head: git(this.cwd, 'rev-parse', 'HEAD').trim(), branch: git(this.cwd, 'branch', '--show-current').trim(), dirty: Boolean(git(this.cwd, 'status', '--porcelain=v1', '--untracked-files=all').trim()) };
    const complete = new Set(Object.entries(state?.records ?? {}).filter(([, record]) => record.phase === 'completed' && ancestor(this.cwd, record.commit!) && validGate(this.directory, record)).map(([id]) => Number(id)));
    const rows: StatusRow[] = [];
    for (const node of Object.values(graph.nodes).sort((a, b) => a.issue.number - b.issue.number)) {
      if (node.children.length) continue;
      const issue = node.issue, record = state?.records[issue.number], last = record?.attempts.at(-1);
      let status: string, reason: string | undefined;
      if (complete.has(issue.number)) status = 'local_completed';
      else if (record?.phase === 'completed') { status = 'integration_drift'; reason = 'Local integration commit or frozen gate evidence is missing/changed'; }
      else if (record?.phase === 'working') {
        const alive = ownerAlive(this.cwd, this.directory);
        status = alive === true ? 'working' : alive === false ? 'interrupted' : 'owner_unknown';
        reason = alive === true ? undefined : 'Inspect owned processes and side effects; explicit --resume --confirm-quiescent required';
      } else if (record?.phase === 'blocked' || record?.phase === 'awaiting_commit') { status = record.phase; reason = last?.error ?? last?.summary; }
      else if (issue.state === 'closed') status = 'github_closed';
      else if (!approved(issue)) status = 'not_approved';
      else if (issue.assignees.length) { status = 'assigned'; reason = issue.assignees.map(a => a.login).join(', '); }
      else {
        const blocked = node.blockers.filter(b => b.state !== 'closed' && !complete.has(b.number));
        status = blocked.length ? 'waiting_dependencies' : 'ready';
        if (blocked.length) reason = blocked.map(b => `#${b.number}`).join(', ');
      }
      rows.push({ number: issue.number, title: issue.title, status, reason, commit: record?.commit, prerequisitesToVerify: node.blockers.filter(b => !complete.has(b.number)).map(b => b.number), evidenceDirectory: last && path.join(this.directory, last.directory), sessionFiles: last?.sessionFiles, lastSummary: last?.summary });
    }
    if (graph.nodes[this.root].issue.state === 'closed') {
      for (const row of rows) if (row.status === 'ready') { row.status = 'scope_closed'; row.reason = 'Selected parent is closed'; }
    }
    const counts: Record<string, number> = { total: rows.length, effectiveDone: rows.filter(row => ['github_closed', 'local_completed'].includes(row.status)).length };
    for (const row of rows) counts[row.status] = (counts[row.status] ?? 0) + 1;
    if (offline) stale = true;
    const nextAction = rows.some(row => ['interrupted', 'owner_unknown'].includes(row.status)) ? 'Inspect owned processes and side effects; explicitly run --resume --confirm-quiescent'
      : rows.some(row => row.status === 'blocked') ? 'Inspect last attempt and fix the blocker; explicitly run --resume (unknown quiescence additionally requires --confirm-quiescent)'
      : rows.some(row => row.status === 'integration_drift') ? 'Reconcile the actual checkout and original gate evidence; do not repeat previous side effects'
      : rows.some(row => row.status === 'working') ? 'Observe the active session; do not start another writer'
      : rows.some(row => row.status === 'awaiting_commit') ? 'Review and commit the exact candidate, then rerun the same command; or explicitly supply --commit'
      : checkout.dirty || (state && checkout.head !== state.integrationHead) ? 'Preserve and reconcile checkout changes against the approved integration baseline before running'
      : rows.some(row => row.status === 'ready') ? 'Run the same root command to continue approved leaves'
      : 'No executable approved leaf; inspect GitHub dependencies/approval or perform separate milestone acceptance';
    let specStatus: GitHubStatus['specStatus'] = 'not_checked';
    if (state?.parentAudit) {
      const audit = state.parentAudit;
      try {
        if (offline) throw new Error('Offline cannot refresh parent audit authority');
        const contexts = this.auditContexts(graph);
        const current = this.services.source(this.cwd);
        const file = path.join(this.directory, audit.directory, 'report.json'); noLinks(file);
        specStatus = audit.head === current.head && audit.fingerprint === current.fingerprint && audit.contextsHash === contextHash(contexts) && audit.graphHash === graphIdentity(graph) && hash(fs.readFileSync(file)) === audit.reportHash ? audit.status : 'stale';
        if (specStatus !== 'stale' && audit.interrupted) {
          const owner = ownerAlive(this.cwd, this.directory);
          specStatus = owner === undefined ? 'owner_unknown' : owner && state.activity?.issue === this.root && state.activity.role === 'spec' ? 'working' : 'interrupted';
        }
      } catch { specStatus = 'stale'; }
    }
    return { root: this.root, cwd: this.cwd, directory: this.directory, fetchedUtc: graph.fetchedUtc, stale, warnings: graph.warnings, counts, rows, activity: state?.activity, integrationHead: state?.integrationHead, nextAction: specStatus === 'working' ? 'Observe the active read-only parent audit; do not launch another writer' : ['interrupted', 'owner_unknown'].includes(specStatus) ? 'Inspect the owned parent session, process/source side effects; explicit --resume and unknown-quiescence --confirm-quiescent required' : specStatus === 'blocked' ? 'Read the parent-spec audit gap; publish/approve a real leaf or resolve the business decision, not an automatic requirement rewrite' : specStatus === 'passed' ? 'Parent specification locally audited; GitHub unchanged, no full-gate/GPU/CI claim' : specStatus === 'stale' ? 'Inspect source/spec/evidence drift; previous parent pass is invalid. Explicit --resume permits a fresh audit when original evidence is missing/changed' : nextAction, checkout, specStatus, specAudit: state?.parentAudit ? { summary: state.parentAudit.summary, evidenceDirectory: path.join(this.directory, state.parentAudit.directory) } : undefined };
  }
  async run(options: RunOptions, signal: AbortSignal): Promise<GitHubStatus> {
    noLinks(this.cwd); noLinks(this.directory);
    if (inside(this.cwd, this.directory)) throw new Error('Runtime evidence must be outside the working tree');
    if (path.resolve(this.cwd) !== fs.realpathSync(this.cwd)) throw new Error('Use the canonical repository root as --cwd');
    if (git(this.cwd, 'rev-parse', '--show-toplevel').trim().replaceAll('\\', '/') !== this.cwd.replaceAll('\\', '/')) throw new Error('Run from the repository root');
    const prior = this.state();
    if (!prior && fs.existsSync(this.directory) && fs.readdirSync(this.directory).length) throw new Error('Existing directory has no matching GitHub state; preserve it and choose a new --state');
    if (fs.existsSync(lockFile(this.cwd))) {
      if (options.resume && options.confirmQuiescent) recoverLock(this.cwd, this.directory);
      else if (ownerAlive(this.cwd, this.directory) === false) throw new Error('Previous owner exited: inspect status/processes, then run --resume --confirm-quiescent');
      else throw new Error('A writer is active or its ownership is unknown; inspect status before continuing');
    }
    const release = acquire(this.cwd, this.directory);
    let state = prior;
    try {
      if (!state) {
        const source = this.services.source(this.cwd);
        if (source.dirty) throw new Error('First run needs a clean approved integration checkout; existing changes are never stashed or committed automatically');
        fs.mkdirSync(path.join(this.directory, 'attempts'), { recursive: true });
        state = { version: 1, kind: 'github_issue_execution_index', root: this.root, cwd: this.cwd, integrationHead: source.head, records: {}, updatedUtc: new Date().toISOString() };
        this.save(state);
      }
      if (options.extraCorrections !== undefined) {
        if (!options.resume || !Number.isSafeInteger(options.extraCorrections) || options.extraCorrections <= 0) throw new Error('Additional correction authority requires --resume and a positive count');
        const entry = Object.entries(state.records).find(([, record]) => record.phase === 'blocked');
        if (!entry) throw new Error('Additional corrections must bind an existing blocked ticket');
        entry[1].extraCorrections = (entry[1].extraCorrections ?? 0) + options.extraCorrections;
        (state.extraApprovals ??= []).push({ issue: Number(entry[0]), count: options.extraCorrections, sourceFingerprint: this.services.source(this.cwd).fingerprint, utc: new Date().toISOString() });
        this.save(state);
      }
      let iterations = 0;
      for (;;) {
        if (signal.aborted) throw new Error('Run cancelled');
        const graph = loadGraph(this.services.reader, this.root);
        atomicJson(path.join(this.directory, 'github-snapshot.json'), graph);
        this.output({ type: 'progress', text: this.summary(graph, state) });
        const unfinished = Object.entries(state.records).find(([, record]) => record.phase !== 'completed');
        if (unfinished) {
          const [id, record] = unfinished;
          const last = record.attempts.at(-1)!;
          if (last.gateHash && last.candidate && (record.phase === 'awaiting_commit' || (options.resume && options.confirmQuiescent))) {
            const fresh = this.services.source(this.cwd);
            if (!fresh.dirty && fresh.fingerprint === last.candidate.fingerprint && fresh.head !== last.input.head) {
              this.assertFresh(graph, Number(id), last.contextsHash);
              this.finish(state, Number(id), fresh.head); continue;
            }
            if (record.phase === 'awaiting_commit' && options.commit && fresh.fingerprint === last.candidate.fingerprint && fresh.head === last.input.head) {
              this.assertFresh(graph, Number(id), last.contextsHash);
              this.commit(state, Number(id)); continue;
            }
            if (record.phase === 'awaiting_commit') {
              if (fresh.fingerprint !== last.candidate.fingerprint || fresh.head !== last.input.head) {
                record.phase = 'blocked'; last.error = 'Frozen candidate changed; explicit --resume must revalidate it'; this.save(state);
              }
              return this.status();
            }
          }
          if (!options.resume || (record.phase === 'working' && !options.confirmQuiescent) || (last.unknownQuiescence && !options.confirmQuiescent)) throw new Error(`Issue #${id} stopped: inspect status, then explicitly run --resume${record.phase === 'working' || last.unknownQuiescence ? ' --confirm-quiescent' : ''}`);
          if (!graph.nodes[id] || graph.nodes[id].issue.state !== 'open' || !approved(graph.nodes[id].issue) || graph.nodes[id].children.length || graph.nodes[id].issue.assignees.length) throw new Error('Interrupted issue is no longer an approved, unassigned open leaf');
          last.unknownQuiescence = false;
          last.endedUtc ??= new Date().toISOString();
          await this.execute(graph, state, Number(id), options, signal);
          iterations++;
        } else {
          const current = this.services.source(this.cwd);
          if (current.dirty || current.head !== state.integrationHead) throw new Error('Integration source drifted; inspect it before continuing');
          const next = this.rows(graph, state).find(row => row.status === 'ready');
          if (!next) {
            const status = this.status();
            if (status.rows.every(row => ['github_closed', 'local_completed'].includes(row.status))) await this.auditParent(graph, state, signal, options);
            return this.status();
          }
          if (options.maxIterations !== undefined && iterations >= options.maxIterations) return this.status();
          await this.execute(graph, state, next.number, options, signal);
          iterations++;
        }
        if (Object.values(state.records).some(record => record.phase !== 'completed')) return this.status();
        if (options.maxIterations !== undefined && iterations >= options.maxIterations) return this.status();
      }
    } finally {
      if (!state?.parentAudit?.unknownQuiescence && !Object.values(state?.records ?? {}).some(record => record.attempts.at(-1)?.unknownQuiescence)) release();
    }
  }
  private rows(graph: IssueGraph, state: GitHubState): StatusRow[] {
    const completed = new Set(Object.entries(state.records).filter(([, record]) => record.phase === 'completed' && ancestor(this.cwd, record.commit!) && validGate(this.directory, record)).map(([id]) => Number(id)));
    if (Object.values(state.records).some(record => record.phase === 'completed' && (!ancestor(this.cwd, record.commit!) || !validGate(this.directory, record)))) throw new Error('A previously accepted integration commit is missing');
    return Object.values(graph.nodes).filter(node => !node.children.length && node.issue.state === 'open' && approved(node.issue) && !node.issue.assignees.length && graph.nodes[this.root].issue.state === 'open').map(node => ({
      number: node.issue.number, title: node.issue.title,
      status: completed.has(node.issue.number) ? 'local_completed' : node.blockers.every(blocker => blocker.state === 'closed' || completed.has(blocker.number)) ? 'ready' : 'waiting_dependencies',
      prerequisitesToVerify: node.blockers.filter(blocker => !completed.has(blocker.number)).map(blocker => blocker.number),
    })).sort((a, b) => a.number - b.number);
  }
  private summary(graph: IssueGraph, state: GitHubState): string {
    const leaves = Object.values(graph.nodes).filter(node => !node.children.length);
    const local = leaves.filter(node => state.records[node.issue.number]?.phase === 'completed').length;
    const remote = leaves.filter(node => node.issue.state === 'closed' && state.records[node.issue.number]?.phase !== 'completed').length;
    return `#${this.root}: ${local + remote}/${leaves.length} leaves accounted for (${local} locally completed, ${remote} GitHub closed; closed does not prove local integration)`;
  }
  private assertFresh(graph: IssueGraph, issue: number, expected: string): void {
    const liveBlockers = this.services.reader.blockers(issue);
    const freshGraph = { ...graph, nodes: { ...graph.nodes, [issue]: { ...graph.nodes[issue], blockers: liveBlockers } } };
    if (contextHash(contextsFor(this.services.reader, freshGraph, issue)) !== expected || canonical(liveBlockers.map(b => b.number).sort()) !== canonical(graph.nodes[issue].blockers.map(b => b.number).sort())) throw new Error('Issue, parent specification or dependency changed; stop and re-read GitHub');
  }
  private async execute(graph: IssueGraph, state: GitHubState, issue: number, options: RunOptions, signal: AbortSignal): Promise<void> {
    const input = this.services.source(this.cwd);
    if (input.head !== state.integrationHead) throw new Error('Integration HEAD changed before dispatch');
    const contexts = contextsFor(this.services.reader, graph, issue), id = randomUUID();
    const selected = contexts.find(context => context.number === issue)!;
    if (selected.state !== 'open' || !approved(selected) || selected.assignees.length || (selected.sub_issues_summary?.total ?? 0) > 0 || contexts.find(context => context.number === this.root)?.state !== 'open') throw new Error('Issue scope changed before dispatch');
    if (graph.nodes[issue].blockers.some(blocker => state.records[blocker.number]?.phase !== 'completed' && contexts.find(context => context.number === blocker.number)?.state !== 'closed')) throw new Error('Dependency reopened before dispatch');
    this.assertFresh(graph, issue, contextHash(contexts));
    const existing = state.records[issue];
    const original = existing ? this.originalSource(existing.attempts[0]) : input;
    const attempt: GitHubAttempt = { id, directory: path.join('attempts', `${issue}-${id}`), startedUtc: new Date().toISOString(), input: binding(input), original: binding(original), contextsHash: contextHash(contexts), checks: [], dependencyProofs: [] };
    const directory = path.join(this.directory, attempt.directory); fs.mkdirSync(directory);
    const record: GitHubRecord = existing ?? { phase: 'working', attempts: [] };
    record.phase = 'working'; record.attempts.push(attempt); state.records[issue] = record;
    state.activity = { issue, updatedUtc: new Date().toISOString() };
    const inputFile = path.join(directory, 'input.json');
    atomicJson(inputFile, { contexts, source: input, original, corrections: record.corrections ?? [], correctionLimit: defaultCorrections + (record.extraCorrections ?? 0), commandApproval: { commit: options.commit === true, resume: options.resume === true, confirmQuiescent: options.confirmQuiescent === true } });
    attempt.inputHash = hash(fs.readFileSync(inputFile));
    this.save(state);
    const pending = new Set(graph.nodes[issue].blockers.filter(blocker => state.records[blocker.number]?.phase !== 'completed').map(blocker => blocker.number));
    const emit = (event: CodingEvent) => {
      if (event.type === 'text') { this.output(event); return; }
      if (event.role && event.sessionFile) (attempt.sessionFiles ??= {})[event.role] = event.sessionFile;
      fs.appendFileSync(path.join(directory, 'events.jsonl'), JSON.stringify({ utc: new Date().toISOString(), ...event, text: undefined }) + '\n');
      if (event.type !== 'text') {
        state.activity = { ...state.activity!, role: event.role ?? state.activity?.role, tool: event.tool, sessionFile: event.sessionFile ?? state.activity?.sessionFile, turns: event.turns, updatedUtc: new Date().toISOString() };
        this.save(state);
      }
      this.output(event);
    };
    const reports: Partial<Record<Role, Report>> = {};
    const checkHistory = record.attempts.flatMap(item => item.checks);
    let candidate: Source | undefined;
    let cycle = 0;
    try {
      for (;;) {
        this.assertFresh(graph, issue, attempt.contextsHash);
        const cycleDirectory = cycle === 0 ? directory : path.join(directory, `repair-${cycle}`);
        if (cycle) fs.mkdirSync(cycleDirectory);
        for (const role of ['implement', 'standards', 'spec'] as const) {
          if (signal.aborted) throw new Error('Run cancelled');
          const roleDirectory = path.join(cycleDirectory, role); fs.mkdirSync(roleDirectory);
          const prompt = [
            `You are a fresh ${role} agent for ${repositoryName()} issue #${issue}. This is NOT a planning session. Humans maintain the plan in GitHub.`,
            'Read the issue, latest comments, parent specifications and direct dependencies below. Follow all applicable AGENTS.md and domain/testing rules. Work only on this approved leaf. Never modify requirements, claim other issues, publish, push, merge, delegate, clean worktrees or read/upload private assets.',
            `GitHub context:\n${JSON.stringify(contexts)}`,
            `Recent local progress:\n${Object.entries(state.records).filter(([, value]) => value.phase === 'completed').slice(-5).map(([number, value]) => `#${number} @ ${value.commit}`).join('\n')}`,
            `Previous attempt: ${existing?.attempts.at(-2)?.error ?? 'none'}. Current source may contain explicitly retained partial work; inspect it rather than starting over.`,
            role === 'implement' ? 'First inspect Git status/history and verify actual prerequisite integration using GitHub acceptance evidence and verify_dependency. No writes/tests before all dependencies are verified. Complete one independently verifiable behavior, including all producers/consumers/fixtures/docs. Select the appropriate complete Unit project or approved Prepare/Game range; never substitute Unit for engine evidence. Read docs/agents/task-execution.md. A predeclared exact RED is planned work. For a complete normal compiler/assertion failure, diagnose original evidence and use request_correction before writes/recheck. Crashes, timeouts, unknown side effects, provider failures and missing/incomplete results STOP. New business decisions, GPU/desktop/private/full-gate authority require operator action.' : `Independent ${role} review: ${role === 'standards' ? 'check repository rules and affected contracts, complete impact set and single responsibility' : 'check approved issue behavior, omissions and normal/error/edge paths; Unit cannot replace missing Game/Prepare evidence'}. Read production/test files and the diff. No writes/tests. Do not infer a pass from another verdict. Use changes_requested only for a located repair within the existing contract/permissions; new decisions or missing run authority are blocked.`,
            `File count is advisory (risk signal above ${scopeWarningFiles}), never a stop or a reason to omit consumers/group unrelated tests. Explain atomicity and legal intermediate states; split only independently deliverable behavior.`,
            `Shared correction ledger: ${JSON.stringify(record.corrections ?? [])}. Limit: ${defaultCorrections + (record.extraCorrections ?? 0)}. Pending failure: ${JSON.stringify(record.pendingFailure ?? null)}. Fresh sessions do not reset it.`,
            `Native evidence (each scope/source is separate, no synthetic full-suite pass):\n${JSON.stringify(checkHistory)}`,
            `Prerequisite integration proofs:\n${JSON.stringify(attempt.dependencyProofs)}`,
            'Return ONLY JSON {"status":"candidate|blocked" for implementation or "passed|changes_requested|blocked" for review,"summary":"what was completed, verified and what is next","evidence":["specific source/issue/check references"]}. No local commit or Issue closure: the host handles only explicitly approved local commits.',
          ].join('\n\n');
          fs.writeFileSync(path.join(roleDirectory, 'brief.txt'), prompt, { flag: 'wx' });
          emit({ type: 'stage', role });
          const report = parseReport(await this.services.agent.run({ cwd: this.cwd, issue, role, directory: roleDirectory, contexts, prompt, input: original, signal, emit, checks: checkHistory, pendingDependencies: pending,
            verifyDependency: (dependency, commit, evidence) => {
              const context = contexts.find(item => item.number === dependency);
              if (!pending.has(dependency) || !context || !/^[0-9a-f]{40}$/.test(commit) || !evidence.trim() || ![context.body, ...context.comments.map(comment => comment.body)].some(body => body.includes(commit) || body.includes(commit.slice(0, 7))) || !ancestor(this.cwd, commit)) throw new Error('Dependency needs GitHub acceptance evidence and an actual ancestor commit');
              attempt.dependencyProofs.push({ issue: dependency, commit, evidence }); pending.delete(dependency); this.save(state);
            },
            recordCheck: check => { attempt.checks.push(check); checkHistory.push(check); this.save(state); },
            stateDirectory: this.directory,
            beforeWrite: () => requireWritable(record),
            beforeCheck: source => requireChangedAfterFailure(record, source),
            recordFailure: (directory, source, summary) => { record.pendingFailure = { directory, sourceFingerprint: source.fingerprint, summary }; this.save(state); },
            requestCorrection: (cause, change, contract) => {
              if (!record.pendingFailure) throw new Error('No recorded normal failure to correct');
              approveCorrection(record, { cause, change, contract, evidence: record.pendingFailure.directory, sourceFingerprint: record.pendingFailure.sourceFingerprint, kind: 'check', utc: new Date().toISOString() });
              this.save(state);
            },
          }), role);
          atomicJson(path.join(roleDirectory, 'report.json'), report); reports[role] = report;
          if (report.status === 'blocked' || (role === 'implement' && report.status !== 'candidate')) throw new Error(`${role}: ${report.status}: ${report.summary}`);
          const current = this.services.source(this.cwd);
          if (role === 'implement') {
            if (pending.size) throw new Error('Unverified dependency integration');
            requireChangedAfterFailure(record, current);
            if (current.head !== input.head || current.indexFingerprint !== input.indexFingerprint) throw new Error('Implementation changed HEAD/index');
            const paths = changedPaths(original, current);
            if (paths.length > scopeWarningFiles) emit({ type: 'scope_warning', text: `${paths.length} files: review single responsibility and complete impact set, not a hard limit` });
            if (!checkHistory.some(check => check.code === 0 && check.sourceFingerprint === current.fingerprint)) throw new Error('No successful current-source native evidence');
            candidate = current; attempt.candidate = binding(current);
          } else if (canonical(binding(current)) !== canonical(attempt.candidate)) throw new QuiescenceUnknownError('Frozen candidate changed during independent review; confirm writer ownership');
        }
        const repairs = (['standards', 'spec'] as const).filter(role => reports[role]?.status === 'changes_requested');
        if (!repairs.length) break;
        const cause = repairs.map(role => `${role}: ${reports[role]!.summary}`).join('\n');
        approveCorrection(record, { cause, change: 'Repair the located review findings and run affected native checks', contract: 'Existing approved ticket and independent expectations unchanged; no new business decision or run permission', evidence: cycleDirectory, sourceFingerprint: candidate!.fingerprint, kind: 'review', utc: new Date().toISOString() });
        this.save(state);
        cycle++;
      }
      this.assertFresh(graph, issue, attempt.contextsHash);
      if (signal.aborted) throw new Error('Run cancelled');
      atomicJson(path.join(directory, 'gate.json'), { candidate, reports, checks: checkHistory, corrections: record.corrections ?? [], dependencyProofs: attempt.dependencyProofs });
      attempt.gateHash = hash(fs.readFileSync(path.join(directory, 'gate.json'))); attempt.summary = reports.implement!.summary;
      attempt.endedUtc = new Date().toISOString(); record.phase = 'awaiting_commit'; delete state.activity; this.save(state);
      if (!this.services.source(this.cwd).dirty) this.finish(state, issue, input.head);
      else if (options.commit) this.commit(state, issue);
    } catch (error) {
      record.phase = 'blocked'; attempt.error = error instanceof Error ? error.message : String(error); attempt.endedUtc = new Date().toISOString();
      if (error instanceof QuiescenceUnknownError || attempt.commitStarted) attempt.unknownQuiescence = true;
      delete state.activity; this.save(state); this.output({ type: 'blocked', text: `#${issue}: ${attempt.error}` });
    }
  }
  private auditContexts(graph: IssueGraph): IssueContext[] {
    const ids = new Set(Object.values(graph.nodes).flatMap(node => [node.issue.number, ...node.blockers.map(blocker => blocker.number)]));
    return [...ids].sort((a, b) => a - b).map(id => this.services.reader.context(id));
  }
  private async auditParent(graph: IssueGraph, state: GitHubState, signal: AbortSignal, options: RunOptions): Promise<void> {
    const current = this.services.source(this.cwd), contexts = this.auditContexts(graph), contextsHash = contextHash(contexts), graphHash = graphIdentity(graph);
    const previous = state.parentAudit;
    if (previous?.interrupted && (!options.resume || (previous.unknownQuiescence && !options.confirmQuiescent))) throw new Error('Parent audit interrupted: explicit --resume and, for unknown quiescence, --confirm-quiescent required');
    if (previous && !previous.interrupted && previous.head === current.head && previous.fingerprint === current.fingerprint && previous.contextsHash === contextsHash && previous.graphHash === graphHash) {
      const file = path.join(this.directory, previous.directory, 'report.json'); noLinks(file);
      const intact = fs.existsSync(file) && hash(fs.readFileSync(file)) === previous.reportHash;
      if (intact) return;
      if (!options.resume) throw new Error('Parent audit evidence changed; preserve it and use explicit --resume for a fresh audit');
    }
    const relative = path.join('attempts', `parent-${randomUUID()}`), directory = path.join(this.directory, relative);
    fs.mkdirSync(directory);
    const checks = Object.values(state.records).flatMap(record => record.attempts.flatMap(attempt => attempt.checks));
    const prompt = [
      `Read-only final parent-spec audit for #${this.root}. Queue exhaustion and GitHub closed are NOT specification acceptance.`,
      'Read the parent and all nested contracts/comments, actual source/tests, actual ancestor commits and cited native evidence. Account for every parent clause including cross-leaf integration, consumers, samples, negative controls and lifecycle. Check existing acceptance evidence against its exact scope/version; missing Game/Prepare/full-gate/GPU/desktop proof cannot be replaced by this review or Unit. Do not invent mandatory layers absent from the approved specification.',
      'No writes, tests, requirements changes, new Issue publication or automatic approval. Return passed only if the entire local business contract is supported; otherwise blocked with exact uncovered clauses and source/evidence gaps. A passed audit is not a commit, GitHub closure, main merge or arbitrary full-suite pass.',
      `Current source: ${JSON.stringify(binding(current))}`,
      `Leaf integration evidence: ${JSON.stringify(Object.entries(state.records).map(([issue, record]) => ({ issue, commit: record.commit, gate: record.attempts.at(-1)?.directory })))}`,
      `Native checks: ${JSON.stringify(checks)}`,
      `Live contracts: ${JSON.stringify(contexts)}`,
      'Return ONLY JSON {"status":"passed|blocked","summary":"whole-parent coverage conclusion and specific remaining gaps","evidence":["clause/source/native evidence references"]}.',
    ].join('\n\n');
    atomicJson(path.join(directory, 'input.json'), { source: current, contexts, checks });
    fs.writeFileSync(path.join(directory, 'brief.txt'), prompt, { flag: 'wx', encoding: 'utf8' });
    const intentReport = { status: 'blocked', summary: 'Parent audit running or interrupted; not yet accepted', evidence: [directory] };
    atomicJson(path.join(directory, 'intent.json'), intentReport);
    atomicJson(path.join(directory, 'report.json'), intentReport);
    state.parentAudit = { fingerprint: current.fingerprint, head: current.head, contextsHash, graphHash, directory: relative, reportHash: hash(fs.readFileSync(path.join(directory, 'report.json'))), status: 'blocked', summary: intentReport.summary, interrupted: true, unknownQuiescence: true };
    state.activity = { issue: this.root, role: 'spec', updatedUtc: new Date().toISOString() }; this.save(state);
    this.output({ type: 'stage', role: 'spec', text: `Parent #${this.root} final contract audit (read-only)` });
    try {
      const report = parseReport(await this.services.agent.run({ cwd: this.cwd, issue: this.root, role: 'spec', directory, contexts, prompt, input: current, signal, stateDirectory: this.directory,
        emit: event => {
          if (event.type !== 'text') {
            fs.appendFileSync(path.join(directory, 'events.jsonl'), JSON.stringify({ utc: new Date().toISOString(), ...event }) + '\n', 'utf8');
            state.activity = { issue: this.root, role: 'spec', tool: event.tool, sessionFile: event.sessionFile ?? state.activity?.sessionFile, turns: event.turns, updatedUtc: new Date().toISOString() };
            this.save(state);
          }
          this.output(event);
        }, checks, pendingDependencies: new Set(), verifyDependency: () => { throw new Error('Parent audit is read-only'); }, recordCheck: () => { throw new Error('Parent audit cannot run checks'); } }), 'spec');
      if (signal.aborted) throw new Error('Parent audit cancelled');
      if (canonical(this.services.source(this.cwd)) !== canonical(current)) throw new QuiescenceUnknownError('Source changed during parent-spec audit; confirm writer ownership');
      const liveGraph = loadGraph(this.services.reader, this.root);
      if (graphIdentity(liveGraph) !== graphHash || contextHash(this.auditContexts(liveGraph)) !== contextsHash) throw new Error('Parent specification/graph changed during final audit');
      atomicJson(path.join(directory, 'report.json'), report);
      state.parentAudit = { fingerprint: current.fingerprint, head: current.head, contextsHash, graphHash, directory: relative, reportHash: hash(fs.readFileSync(path.join(directory, 'report.json'))), status: report.status === 'passed' ? 'passed' : 'blocked', summary: report.summary };
      this.save(state);
    } catch (error) {
      const stopped = { status: 'blocked', summary: error instanceof Error ? error.message : String(error), evidence: [directory] };
      atomicJson(path.join(directory, 'report.json'), stopped);
      state.parentAudit = { fingerprint: current.fingerprint, head: current.head, contextsHash, graphHash, directory: relative, reportHash: hash(fs.readFileSync(path.join(directory, 'report.json'))), status: 'blocked', summary: stopped.summary, interrupted: true, unknownQuiescence: error instanceof QuiescenceUnknownError };
      throw error;
    } finally { delete state.activity; this.save(state); }
  }
  private originalSource(attempt: GitHubAttempt): Source {
    const file = path.join(this.directory, attempt.directory, 'input.json'); noLinks(file);
    if (!attempt.inputHash || hash(fs.readFileSync(file)) !== attempt.inputHash) throw new Error('Original input artifact changed');
    const original = (json(file) as { original: Source }).original;
    if (original.fingerprint !== attempt.original.fingerprint || original.fingerprint !== hash(canonical({ files: original.files, modes: original.modes }))) throw new Error('Original source identity mismatch');
    return original;
  }
  private commit(state: GitHubState, issue: number): void {
    const record = state.records[issue], attempt = record.attempts.at(-1)!;
    const current = this.services.source(this.cwd);
    if (!attempt.candidate || current.head !== attempt.input.head || current.fingerprint !== attempt.candidate.fingerprint || current.indexFingerprint !== attempt.candidate.indexFingerprint) throw new Error('Candidate changed before local commit');
    if (git(this.cwd, 'diff', '--cached', '--name-only').trim()) throw new Error('Index contains existing work; reconcile it rather than overwriting');
    const files = changedPaths(this.originalSource(attempt), current);
    attempt.commitStarted = true; this.save(state);
    try {
      git(this.cwd, 'add', '-A', '--', ...files); git(this.cwd, 'commit', '-m', `Implement #${issue}`);
      const after = this.services.source(this.cwd);
      if (after.dirty || after.fingerprint !== attempt.candidate.fingerprint) throw new Error('Commit hooks changed the frozen candidate');
      this.finish(state, issue, after.head);
    } catch (error) { attempt.unknownQuiescence = true; record.phase = 'blocked'; attempt.error = `Local commit outcome needs reconciliation: ${error instanceof Error ? error.message : String(error)}`; this.save(state); throw error; }
  }
  private finish(state: GitHubState, issue: number, commit: string): void {
    const record = state.records[issue], attempt = record.attempts.at(-1)!;
    const gate = path.join(this.directory, attempt.directory, 'gate.json');
    if (!attempt.gateHash || hash(fs.readFileSync(gate)) !== attempt.gateHash || !ancestor(this.cwd, attempt.input.head)) throw new Error('Gate/integration identity changed');
    record.phase = 'completed'; record.commit = commit; attempt.unknownQuiescence = false;
    state.integrationHead = commit; delete state.activity; this.save(state);
    this.output({ type: 'completed', text: `#${issue} locally verified @ ${commit.slice(0, 12)} (GitHub unchanged)` });
  }
}
const repositoryName = () => 'fanxiao92/godot-pal';
