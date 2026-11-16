import path from 'node:path';
import { createHash } from 'node:crypto';

export type Role = 'implement' | 'standards' | 'spec';
export type Phase = 'pending' | 'running' | 'blocked' | 'awaiting_acceptance' | 'accepted';
export interface Check { kind: 'non_engine' | 'engine'; executable: string; args: string[]; timeoutMs: number }
export interface Task {
  issue: number;
  dependsOn: number[];
  contextIssues: number[];
  allowedWritePaths: string[];
  readPaths: string[];
  checks: Check[];
}
export interface Plan {
  version: 1;
  repository: 'fanxiao92/godot-pal';
  cwd: string;
  baseHead: string;
  sdkRoot: string;
  agentDir: string;
  provider: string;
  model: string;
  approval: { id: string; approvedBy: string; deadlineUtc: string; localCommits: boolean; autoAdvance: boolean };
  limits: { maxTurns: number; timeoutMs: number };
  prerequisites: { issue: number; commit: string; receipt: string }[];
  tasks: Task[];
}
export interface Issue {
  number: number; title: string; body: string; state: string;
  labels: { name: string }[];
  comments: { body: string; url: string }[];
}
export interface Source { head: string; files: Record<string, string>; modes: Record<string, number>; indexFingerprint: string; fingerprint: string; dirty: boolean }
export interface ProblemFinding { problemId: string; evidenceKey: string; cause: string; evidence: string[] }
export interface Report { status: 'candidate' | 'passed' | 'blocked' | 'changes_requested'; summary: string; evidence: string[]; findings?: ProblemFinding[] }
export interface Attempt {
  id: string; startedUtc: string; endedUtc?: string;
  input: Source; issueHash: string; directory: string;
  candidate?: Source; gateHash?: string; error?: string; quiescenceUnknown?: boolean; commitStarted?: boolean;
}
export interface TaskState { phase: Phase; attempts: Attempt[]; retrySource?: Source; commit?: string; receipt?: string }
export interface State {
  version: 1; kind: 'pal_agent_execution'; planHash: string; cwd: string; integrationHead: string;
  tasks: Record<string, TaskState>; prerequisites: Record<string, { commit: string; receiptHash: string }>; updatedUtc: string;
}
export interface AgentRequest {
  plan: Plan; task: Task; role: Role; prompt: string; directory: string;
  signal: AbortSignal; emit: (event: unknown) => void;
}
export interface AgentExecutor { run(request: AgentRequest): Promise<Report> }

export const hash = (value: string | Buffer): string => createHash('sha256').update(value).digest('hex');
export const canonical = (value: unknown): string => {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value !== null && typeof value === 'object') {
    return `{${Object.entries(value).sort(([a], [b]) => a.localeCompare(b, 'en')).map(([k, v]) => `${JSON.stringify(k)}:${canonical(v)}`).join(',')}}`;
  }
  return JSON.stringify(value);
};
export function object(value: unknown, keys: string[], label: string): asserts value is Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value) || Object.keys(value).some(key => !keys.includes(key))) throw new Error(`Invalid ${label} fields`);
}
export function text(value: unknown, label: string): asserts value is string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label} must be nonblank`);
}
function positive(value: unknown, label: string): asserts value is number {
  if (!Number.isSafeInteger(value) || (value as number) <= 0) throw new Error(`${label} must be a positive integer`);
}
export function relativeFile(value: unknown): asserts value is string {
  text(value, 'path');
  if (path.isAbsolute(value) || value.includes('\\') || value.split('/').some(part => !part || part === '.' || part === '..' || part.includes(':') || /[. ]$/.test(part)) || /[\x00-\x1f]/.test(value)) throw new Error(`Not a normalized relative path: ${value}`);
  if (value.split('/').some(part => ['.git', '.pi', 'node_modules'].includes(part.toLowerCase()))) throw new Error(`Protected path: ${value}`);
}
export function parsePlan(input: unknown): Plan {
  object(input, ['version', 'repository', 'cwd', 'baseHead', 'sdkRoot', 'agentDir', 'provider', 'model', 'approval', 'limits', 'prerequisites', 'tasks'], 'plan');
  if (input.version !== 1 || input.repository !== 'fanxiao92/godot-pal') throw new Error('Unsupported version or repository');
  for (const key of ['cwd', 'sdkRoot', 'agentDir']) {
    text(input[key], key);
    if (!path.isAbsolute(input[key] as string)) throw new Error(`${key} must be absolute`);
  }
  if (typeof input.baseHead !== 'string' || !/^[0-9a-f]{40}$/.test(input.baseHead)) throw new Error('baseHead must be a full Git SHA');
  text(input.provider, 'provider'); text(input.model, 'model');
  object(input.approval, ['id', 'approvedBy', 'deadlineUtc', 'localCommits', 'autoAdvance'], 'approval');
  text(input.approval.id, 'approval.id'); text(input.approval.approvedBy, 'approval.approvedBy');
  if (typeof input.approval.deadlineUtc !== 'string' || !/^\d{4}-\d\d-\d\dT.*Z$/.test(input.approval.deadlineUtc) || !Number.isFinite(Date.parse(input.approval.deadlineUtc))) throw new Error('approval.deadlineUtc must be UTC');
  if (typeof input.approval.localCommits !== 'boolean' || typeof input.approval.autoAdvance !== 'boolean') throw new Error('Explicit localCommits and autoAdvance are required');
  object(input.limits, ['maxTurns', 'timeoutMs'], 'limits');
  positive(input.limits.maxTurns, 'maxTurns'); positive(input.limits.timeoutMs, 'timeoutMs');
  if (!Array.isArray(input.tasks) || !input.tasks.length || input.tasks.length > 1000) throw new Error('Expected 1..1000 explicitly selected tasks');
  const ids = new Set<number>();
  for (const task of input.tasks) {
    object(task, ['issue', 'dependsOn', 'contextIssues', 'allowedWritePaths', 'readPaths', 'checks'], 'task');
    positive(task.issue, 'issue');
    if (ids.has(task.issue)) throw new Error('Duplicate issue'); ids.add(task.issue);
    if (!Array.isArray(task.dependsOn) || task.dependsOn.some(id => !Number.isSafeInteger(id) || id <= 0 || id === task.issue) || new Set(task.dependsOn).size !== task.dependsOn.length) throw new Error('Invalid dependsOn');
    if (!Array.isArray(task.contextIssues) || task.contextIssues.some(id => !Number.isSafeInteger(id) || id <= 0 || id === task.issue) || new Set(task.contextIssues).size !== task.contextIssues.length) throw new Error('Explicit contextIssues array required');
    if (!Array.isArray(task.allowedWritePaths) || !task.allowedWritePaths.length) throw new Error('Each ticket requires explicit writable files; file count is advisory');
    if (!Array.isArray(task.readPaths) || !task.readPaths.length) throw new Error('Explicit readPaths required');
    for (const p of [...task.allowedWritePaths, ...task.readPaths]) relativeFile(p);
    if (new Set(task.allowedWritePaths.map(p => p.toLowerCase())).size !== task.allowedWritePaths.length) throw new Error('Duplicate writable path');
    if (!Array.isArray(task.checks) || !task.checks.length) throw new Error('At least one approved native check is required');
    for (const check of task.checks) {
      object(check, ['kind', 'executable', 'args', 'timeoutMs'], 'check'); text(check.executable, 'check.executable');
      if (check.kind !== 'non_engine') throw new Error('This version only schedules explicitly approved non_engine checks');
      if (!path.isAbsolute(check.executable) || /\.(cmd|bat|ps1)$/i.test(check.executable)) throw new Error('Check executable must be an absolute native executable (no shell scripts)');
      if (!Array.isArray(check.args) || check.args.some(arg => typeof arg !== 'string' || arg.includes('\0'))) throw new Error('check.args must be strings');
      positive(check.timeoutMs, 'check.timeoutMs');
    }
  }
  if (!Array.isArray(input.prerequisites)) throw new Error('Explicit prerequisites array required');
  const prerequisiteIds = new Set<number>();
  for (const prerequisite of input.prerequisites) {
    object(prerequisite, ['issue', 'commit', 'receipt'], 'prerequisite');
    positive(prerequisite.issue, 'prerequisite.issue');
    if (ids.has(prerequisite.issue) || prerequisiteIds.has(prerequisite.issue)) throw new Error('Duplicate prerequisite/task identity');
    prerequisiteIds.add(prerequisite.issue);
    if (typeof prerequisite.commit !== 'string' || !/^[0-9a-f]{40}$/.test(prerequisite.commit)) throw new Error('prerequisite.commit must be a full SHA');
    text(prerequisite.receipt, 'prerequisite.receipt');
    if (!path.isAbsolute(prerequisite.receipt)) throw new Error('prerequisite.receipt must be absolute');
  }
  const tasks = input.tasks as unknown as Task[];
  const visiting = new Set<number>(), visited = new Set<number>();
  const visit = (id: number) => {
    if (visiting.has(id)) throw new Error('Dependency cycle');
    if (visited.has(id)) return;
    visiting.add(id);
    for (const dep of tasks.find(t => t.issue === id)!.dependsOn) {
      if (prerequisiteIds.has(dep)) continue;
      if (!ids.has(dep)) throw new Error('All dependencies need an explicit task or prerequisite receipt');
      visit(dep);
    }
    visiting.delete(id); visited.add(id);
  };
  tasks.forEach(t => visit(t.issue));
  return input as unknown as Plan;
}
export function parseReport(input: unknown, role: Role): Report {
  object(input, ['status', 'summary', 'evidence', 'findings'], 'agent result');
  if (!(role === 'implement' ? ['candidate', 'blocked'] : ['passed', 'blocked', 'changes_requested']).includes(String(input.status))) throw new Error(`Invalid ${role} result status`);
  text(input.summary, 'summary');
  if (!Array.isArray(input.evidence) || !input.evidence.length || input.evidence.some(e => typeof e !== 'string' || !e.trim())) throw new Error('Result must include evidence references');
  if (input.findings !== undefined) {
    if (!Array.isArray(input.findings)) throw new Error('findings must be an array');
    for (const finding of input.findings) {
      object(finding, ['problemId', 'evidenceKey', 'cause', 'evidence'], 'problem finding');
      text(finding.problemId, 'problemId');
      if (!/^[a-z0-9][a-z0-9-]*$/.test(finding.problemId)) throw new Error('Invalid stable problemId');
      text(finding.evidenceKey, 'evidenceKey'); text(finding.cause, 'cause');
      if (!Array.isArray(finding.evidence) || !finding.evidence.length || finding.evidence.some(item => typeof item !== 'string' || !item.trim())) throw new Error('Problem finding requires original evidence references');
    }
  }
  return input as unknown as Report;
}
export function changedPaths(before: Source, after: Source): string[] {
  return [...new Set([...Object.keys(before.files), ...Object.keys(after.files)])].filter(p => before.files[p] !== after.files[p] || before.modes[p] !== after.modes[p]).sort();
}
export function requireScope(task: Task, before: Source, after: Source): void {
  const allowed = new Set(task.allowedWritePaths);
  const invalid = changedPaths(before, after).filter(p => !allowed.has(p));
  if (invalid.length) throw new Error(`Out-of-scope source changes: ${invalid.join(', ')}`);
}
