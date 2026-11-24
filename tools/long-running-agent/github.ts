import path from 'node:path';
import { canonical, hash } from './contract.ts';
import { native } from './io.ts';

export const repository = 'fanxiao92/godot-pal';
export interface GitHubIssue {
  number: number; title: string; body: string; state: 'open' | 'closed'; html_url: string;
  labels: { name: string }[]; assignees: { login: string }[];
  sub_issues_summary?: { total: number };
}
export interface IssueContext extends GitHubIssue { comments: { body: string; html_url: string }[] }
export interface IssueNode { issue: GitHubIssue; parents: number[]; children: number[]; blockers: GitHubIssue[] }
export interface IssueGraph { root: number; fetchedUtc: string; nodes: Record<string, IssueNode>; warnings: string[] }
export interface GitHubReader {
  issue(number: number): GitHubIssue;
  children(issue: GitHubIssue): { issues: GitHubIssue[]; warning?: string };
  blockers(number: number): GitHubIssue[];
  context(number: number): IssueContext;
}
export interface ReceiptWriter { postReceipt(issue: number, receipt: { commit: string; evidenceDirectory: string }): Promise<void> }
// Default remote channel for --auto-receipt: one short acceptance comment (full commit SHA + evidence
// directory, signed "LRA auto-receipt") then closing that own leaf issue, both via the existing native gh path.
export function nativeReceiptWriter(cwd: string): ReceiptWriter {
  return {
    async postReceipt(issue, { commit, evidenceDirectory }) {
      const body = `LRA auto-receipt\n\nVerified local integration commit: ${commit}\nEvidence directory: ${evidenceDirectory}`;
      native(cwd, 'gh', ['issue', 'comment', String(issue), '--repo', repository, '--body', body]);
      native(cwd, 'gh', ['issue', 'close', String(issue), '--repo', repository]);
    },
  };
}
function validateIssue(input: GitHubIssue): GitHubIssue {
  if (!input || !Number.isSafeInteger(input.number) || !input.title || typeof input.html_url !== 'string' || !['open', 'closed'].includes(input.state) || !Array.isArray(input.labels) || !Array.isArray(input.assignees)) throw new Error('Malformed GitHub issue response');
  if (input.html_url.toLowerCase() !== `https://github.com/${repository}/issues/${input.number}`) throw new Error('Cross-repository Issue relationships are not authorized by this runner');
  return input;
}
export function checklistChildren(body: string): number[] {
  const numbers: number[] = [];
  for (const line of body.split('\n')) {
    const match = line.match(/^\s*[-*]\s+\[[ xX]\]\s+(?:#(\d+)\b|https:\/\/github\.com\/fanxiao92\/godot-pal\/issues\/(\d+)\b|\[[^\]]*\]\(https:\/\/github\.com\/fanxiao92\/godot-pal\/issues\/(\d+)\))/);
    if (match) numbers.push(Number(match[1] ?? match[2] ?? match[3]));
  }
  return [...new Set(numbers)];
}
export function githubReader(cwd: string): GitHubReader {
  const api = (endpoint: string): unknown => JSON.parse(native(cwd, 'gh', ['api', endpoint]));
  const pages = <T>(endpoint: string): T[] => {
    const result = JSON.parse(native(cwd, 'gh', ['api', '--paginate', '--slurp', endpoint])) as T[][];
    if (!Array.isArray(result) || result.some(page => !Array.isArray(page))) throw new Error('Malformed GitHub pagination');
    return result.flat();
  };
  const issue = (number: number) => validateIssue(api(`repos/${repository}/issues/${number}`) as GitHubIssue);
  return {
    issue,
    children(parent) {
      if (parent.sub_issues_summary?.total === 0) {
        const fallback = checklistChildren(parent.body);
        return { issues: fallback.map(issue), warning: fallback.length ? `#${parent.number}: using explicit task-list children (no native children)` : undefined };
      }
      try {
        const children = pages<GitHubIssue>(`repos/${repository}/issues/${parent.number}/sub_issues?per_page=100`).map(validateIssue);
        if (children.length) return { issues: children };
      } catch (error) {
        const fallback = checklistChildren(parent.body);
        if (!fallback.length) throw error;
        return { issues: fallback.map(issue), warning: `#${parent.number}: native children unavailable; using explicit task-list relationships` };
      }
      const fallback = checklistChildren(parent.body);
      return { issues: fallback.map(issue), warning: fallback.length ? `#${parent.number}: using explicit task-list children` : undefined };
    },
    blockers: number => pages<GitHubIssue>(`repos/${repository}/issues/${number}/dependencies/blocked_by?per_page=100`).map(validateIssue),
    context(number) {
      const current = issue(number);
      return { ...current, comments: pages<{ body: string; html_url: string }>(`repos/${repository}/issues/${number}/comments?per_page=100`) };
    },
  };
}
export function loadGraph(reader: GitHubReader, root: number): IssueGraph {
  if (!Number.isSafeInteger(root) || root <= 0) throw new Error('A positive root Issue is required');
  const nodes: IssueGraph['nodes'] = {}, warnings: string[] = [], visiting = new Set<number>();
  const visit = (issue: GitHubIssue, parent?: number) => {
    validateIssue(issue);
    if (visiting.has(issue.number)) throw new Error('Cyclic GitHub child relationships');
    if (nodes[issue.number]) {
      if (parent && !nodes[issue.number].parents.includes(parent)) nodes[issue.number].parents.push(parent);
      return;
    }
    if (Object.keys(nodes).length >= 1000) throw new Error('Issue graph exceeds 1000 nodes; select a narrower parent');
    visiting.add(issue.number);
    const result = reader.children(issue);
    if (result.warning) warnings.push(result.warning);
    const children = [...new Map(result.issues.map(child => [child.number, child])).values()];
    nodes[issue.number] = { issue, parents: parent ? [parent] : [], children: children.map(child => child.number), blockers: [] };
    for (const child of children) visit(child, issue.number);
    visiting.delete(issue.number);
  };
  visit(reader.issue(root));
  for (const node of Object.values(nodes)) {
    if (!node.children.length && node.issue.state === 'open' && approved(node.issue)) node.blockers = reader.blockers(node.issue.number).map(validateIssue);
  }
  return { root, nodes, warnings, fetchedUtc: new Date().toISOString() };
}
export const approved = (issue: GitHubIssue): boolean => issue.labels.some(label => label.name === 'ready-for-agent');
export const contextHash = (contexts: IssueContext[]): string => hash(canonical(contexts));
export function contextsFor(reader: GitHubReader, graph: IssueGraph, number: number): IssueContext[] {
  const ids = new Set<number>([number]);
  const parents = (id: number) => {
    for (const parent of graph.nodes[id]?.parents ?? []) if (!ids.has(parent)) { ids.add(parent); parents(parent); }
  };
  parents(number);
  for (const blocker of graph.nodes[number].blockers) ids.add(blocker.number);
  return [...ids].sort((a, b) => a - b).map(id => reader.context(id));
}
export function stateDirectory(agentDir: string, cwd: string, root: number): string {
  return path.join(agentDir, 'long-running', hash(process.platform === 'win32' ? cwd.toLowerCase() : cwd).slice(0, 20), String(root));
}
