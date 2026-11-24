import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, stripVTControlCharacters } from 'node:util';
import { git, recoverLock, snapshot } from './io.ts';
import { assertEngineIdle, engineLock } from './checks.ts';
import { githubReader, stateDirectory } from './github.ts';
import { GitHubHost, type CodingEvent, type GitHubStatus } from './github-host.ts';

export const githubHelp = `GitHub-backed coding loop (human plans, fresh Pi sessions):
  node tools/long-running-agent/cli.ts run --issue <parent-number-or-URL> [--commit] [--auto-receipt]
  node tools/long-running-agent/cli.ts status --issue <parent-number-or-URL> [--json | --offline]
  node tools/long-running-agent/cli.ts preflight --issue <parent-number-or-URL>

Uses the current Git root, existing Pi configuration, and an automatic state directory.
preflight prints a read-only JSON readiness report from state+git only (writer lock/PID, reusable
dependency proofs, per-problem repair rounds, current-fingerprint GREEN, blockers, tool whitelist);
no model, GitHub reads, tests or state writes.
--commit explicitly permits local per-ticket commits and continuation; never push/merge/close Issues.
--auto-receipt (run only, off by default) additionally publishes each of this run's own leaf tickets:
a short acceptance comment with the full local commit SHA and evidence directory, signed "LRA auto-receipt",
then closes that issue. Never parent tickets, other tickets or body edits; failures warn and never roll back.
Without --commit, a verified dirty candidate waits for your commit. Run the same command afterward.
Stopped attempts require --resume; interrupted/unknown owners also require --confirm-quiescent.
Optional: --cwd <checkout>, --state <directory>, --max-iterations <count>, --provider/--model,
          --max-turns <count>, --session-timeout-ms <ms>, --json.
Prepare/Game: --engine-checks --godot-path <approved-console> --fixed-package-directory <verified-bundle>.
Corrections: at most three repairs per stable root-cause problem across check/review/resume.
--extra-corrections cannot extend this cap; unmapped legacy ledgers require parent-only migration.
Exit 0 requires the final read-only parent-spec audit, not just leaf queue exhaustion.
Legacy explicit-plan commands with --state remain available; no JSON plan is needed here.`;
export function issueNumber(value: string | undefined): number {
  const match = value?.match(/^(?:https:\/\/github\.com\/fanxiao92\/godot-pal\/issues\/)?(\d+)\/?$/);
  const number = Number(match?.[1]);
  if (!Number.isSafeInteger(number) || number <= 0) throw new Error('--issue must be a positive number or a fanxiao92/godot-pal Issue URL');
  return number;
}
export function formatStatus(status: GitHubStatus): string {
  const lines = [
    `GitHub coding #${status.root}`,
    `Progress: ${status.counts.effectiveDone}/${status.counts.total} leaf tickets accounted for`,
    `  Local completed: ${status.counts.local_completed ?? 0}; GitHub closed: ${status.counts.github_closed ?? 0} (not proof of local integration)`,
    `  Ready: ${status.counts.ready ?? 0}; dependency waiting: ${status.counts.waiting_dependencies ?? 0}; blocked: ${status.counts.blocked ?? 0}; awaiting commit: ${status.counts.awaiting_commit ?? 0}`,
    `  Assigned: ${status.counts.assigned ?? 0}; not approved: ${status.counts.not_approved ?? 0}; working: ${status.counts.working ?? 0}; interrupted/unknown: ${(status.counts.interrupted ?? 0) + (status.counts.owner_unknown ?? 0)}`,
    `GitHub snapshot: ${status.fetchedUtc}${status.stale ? ' [OFFLINE / STALE]' : ' [live]'}`,
    `Checkout: ${status.cwd} (${status.checkout.branch || 'detached'} @ ${status.checkout.head.slice(0, 12)}, ${status.checkout.dirty ? 'dirty' : 'clean'})`,
    `Parent specification: ${status.specStatus} (separate from leaf progress/full-gate/GPU/CI)`,
    `Evidence: ${status.directory}`,
    `Next: ${status.nextAction}`,
  ];
  if (status.specAudit) lines.push(`Parent audit: ${status.specAudit.summary.replaceAll('\n', ' ').slice(0, 600)}`, `Parent evidence: ${status.specAudit.evidenceDirectory}`);
  if (status.activity) lines.push(`Last activity: #${status.activity.issue} ${status.activity.role ?? ''} ${status.activity.tool ?? ''} at ${status.activity.updatedUtc}`, ...(status.activity.sessionFile ? [`Session: ${status.activity.sessionFile}`] : []));
  for (const warning of status.warnings) lines.push(`Warning: ${warning}`);
  lines.push('');
  for (const row of status.rows) {
    lines.push(`#${row.number} [${row.status}] ${row.title}${row.reason ? ` — ${row.reason.replaceAll('\n', ' ').slice(0, 400)}` : ''}${row.prerequisitesToVerify.length && row.status === 'ready' ? ` (verify integration: ${row.prerequisitesToVerify.map(n => `#${n}`).join(', ')})` : ''}`);
    if (row.evidenceDirectory && !['github_closed', 'local_completed'].includes(row.status)) lines.push(`  Evidence: ${row.evidenceDirectory}`);
  }
  return stripVTControlCharacters(lines.join('\n'));
}
export async function githubMain(argv: string[]): Promise<number> {
  const { values, positionals } = parseArgs({ args: argv, allowPositionals: true, strict: true, options: {
    issue: { type: 'string' }, cwd: { type: 'string' }, state: { type: 'string' }, commit: { type: 'boolean' },
    resume: { type: 'boolean' }, 'confirm-quiescent': { type: 'boolean' }, offline: { type: 'boolean' }, json: { type: 'boolean' }, 'auto-receipt': { type: 'boolean' },
    'max-iterations': { type: 'string' }, provider: { type: 'string' }, model: { type: 'string' }, 'max-turns': { type: 'string' }, 'session-timeout-ms': { type: 'string' },
    'engine-checks': { type: 'boolean' }, 'godot-path': { type: 'string' }, 'fixed-package-directory': { type: 'string' }, 'extra-corrections': { type: 'string' },
  } });
  const command = positionals[0];
  if (positionals.length !== 1 || !['run', 'status', 'preflight'].includes(command)) throw new Error(githubHelp);
  const allowed = command === 'status' ? ['issue', 'cwd', 'state', 'offline', 'json'] : command === 'preflight' ? ['issue', 'cwd', 'state', 'engine-checks', 'godot-path', 'fixed-package-directory'] : ['issue', 'cwd', 'state', 'commit', 'auto-receipt', 'resume', 'confirm-quiescent', 'json', 'max-iterations', 'provider', 'model', 'max-turns', 'session-timeout-ms', 'engine-checks', 'godot-path', 'fixed-package-directory', 'extra-corrections'];
  if (Object.keys(values).some(key => !allowed.includes(key))) throw new Error(`Inappropriate option for ${command}`);
  if (values['confirm-quiescent'] && !values.resume) throw new Error('--confirm-quiescent requires --resume');
  if (Boolean(values.provider) !== Boolean(values.model)) throw new Error('Use existing Pi configuration, or specify both --provider and --model');
  const positive = (value: string | undefined) => {
    if (value === undefined) return undefined;
    const number = Number(value); if (!Number.isSafeInteger(number) || number <= 0) throw new Error('Execution limits must be positive integers'); return number;
  };
  const maxIterations = positive(values['max-iterations']), maxTurns = positive(values['max-turns']), timeoutMs = positive(values['session-timeout-ms']);
  const extraCorrections = positive(values['extra-corrections']);
  if (extraCorrections) throw new Error('--extra-corrections cannot extend the per-problem three-round cap');
  if (Boolean(values['engine-checks']) !== Boolean(values['godot-path'] && values['fixed-package-directory']) || (!values['engine-checks'] && (values['godot-path'] || values['fixed-package-directory']))) throw new Error('Engine checks require all of --engine-checks, --godot-path and --fixed-package-directory');
  const engine = values['engine-checks'] ? { godotPath: fs.realpathSync(path.resolve(values['godot-path']!)), fixedPackageDirectory: fs.realpathSync(path.resolve(values['fixed-package-directory']!)) } : undefined;
  const root = issueNumber(values.issue);
  const start = fs.realpathSync(path.resolve(values.cwd ?? process.cwd()));
  const cwd = fs.realpathSync(git(start, 'rev-parse', '--show-toplevel').trim());
  const sdk = await import('@earendil-works/pi-coding-agent');
  if (sdk.VERSION !== '1.5.0') throw new Error('This integration is validated for Pi SDK 1.5.0 only');
  const directory = values.state ? path.resolve(values.state) : stateDirectory(sdk.getAgentDir(), cwd, root);
  const output = (event: CodingEvent) => {
    if (values.json) { console.log(JSON.stringify({ utc: new Date().toISOString(), ...event })); return; }
    if (event.type === 'text') process.stdout.write(stripVTControlCharacters(event.text ?? ''));
    else if (event.type === 'tool_start') console.log(`\n[${event.role}] Tool: ${event.tool}`);
    else if (event.type === 'stage') console.log(`\n=== ${event.role} (fresh session) ===`);
    else if (event.type !== 'tool_end') console.log(`\n${event.text ?? `${event.type}${event.sessionFile ? `: ${event.sessionFile}` : ''}`}`);
  };
  const reader = githubReader(cwd);
  const noAgent = { run: async (): Promise<never> => { throw new Error('Status cannot launch an agent'); } };
  if (command === 'status') {
    const status = new GitHubHost(cwd, root, directory, { reader, source: snapshot, agent: noAgent }).status(values.offline);
    console.log(values.json ? JSON.stringify(status, null, 2) : formatStatus(status));
    return 0;
  }
  if (command === 'preflight') {
    const report = new GitHubHost(cwd, root, directory, { reader, source: snapshot, agent: noAgent }).preflight(Boolean(engine));
    console.log(JSON.stringify(report, null, 2));
    return 0;
  }
  const { GitHubPiExecutor } = await import('./github-agent.ts');
  if (values.resume && values['confirm-quiescent'] && fs.existsSync(engineLock)) {
    assertEngineIdle(); recoverLock(cwd, directory, engineLock);
  }
  const host = new GitHubHost(cwd, root, directory, { reader, source: snapshot, agent: new GitHubPiExecutor(sdk, sdk.getAgentDir(), { provider: values.provider, model: values.model, maxTurns, timeoutMs, engine }) }, output);
  const controller = new AbortController();
  const stop = () => controller.abort(); process.on('SIGINT', stop); process.on('SIGTERM', stop);
  try {
    const status = await host.run({ commit: values.commit, maxIterations, resume: values.resume, confirmQuiescent: values['confirm-quiescent'], extraCorrections, autoReceipt: values['auto-receipt'] }, controller.signal);
    console.log(values.json ? JSON.stringify({ type: 'status', ...status }) : `\n${formatStatus(status)}`);
    if (['blocked', 'interrupted', 'owner_unknown'].includes(status.specStatus) || status.rows.some(row => ['blocked', 'interrupted', 'owner_unknown', 'integration_drift'].includes(row.status))) return 1;
    return status.specStatus === 'passed' && status.rows.every(row => ['github_closed', 'local_completed'].includes(row.status)) ? 0 : 2;
  } finally { process.off('SIGINT', stop); process.off('SIGTERM', stop); }
}
