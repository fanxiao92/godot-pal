import path from 'node:path';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';
import { Host, defaultServices, initialize } from './host.ts';
import { snapshot } from './io.ts';
import { loadSdk, PiExecutor } from './pi.ts';

export async function main(argv: string[]): Promise<number> {
  if (!argv.length || argv[0] === 'help' || argv.includes('--help')) {
    const { githubHelp } = await import('./github-cli.ts'); console.log(githubHelp); return 0;
  }
  if (['run', 'status', 'preflight'].includes(argv[0]) && argv.some(arg => arg === '--issue' || arg.startsWith('--issue='))) {
    return (await import('./github-cli.ts')).githubMain(argv);
  }
  const { values, positionals } = parseArgs({ args: argv, allowPositionals: true, strict: true, options: {
    state: { type: 'string' }, plan: { type: 'string' }, issue: { type: 'string' }, head: { type: 'string' },
    receipt: { type: 'string' }, fingerprint: { type: 'string' }, reason: { type: 'string' },
    'confirm-quiescent': { type: 'boolean' }, source: { type: 'boolean' },
  } });
  if (positionals.length !== 1 || !values.state || !path.isAbsolute(values.state)) throw new Error('Usage: node cli.ts <init|status|run|accept|retry|recover> --state <absolute-new-or-existing-directory> [options]');
  const directory = path.resolve(values.state), command = positionals[0];
  const permitted: Record<string, string[]> = {
    init: ['state', 'plan'], status: ['state', 'source'], run: ['state'],
    accept: ['state', 'issue', 'head', 'receipt'], retry: ['state', 'issue', 'fingerprint', 'reason'], recover: ['state', 'confirm-quiescent', 'reason'],
  };
  if (!permitted[command] || Object.keys(values).some(key => !permitted[command].includes(key))) throw new Error('Unknown command or inappropriate option');
  if (command === 'init') {
    if (!values.plan) throw new Error('init requires --plan');
    initialize(path.resolve(values.plan), directory);
    console.log(JSON.stringify({ initialized: directory, authority: 'only the supplied plan' })); return 0;
  }
  const noAgent = { run: async (): Promise<never> => { throw new Error('SDK execution unavailable for this command'); } };
  let host = new Host(directory, defaultServices(noAgent));
  const issue = Number(values.issue);
  if (['accept', 'retry'].includes(command) && (!Number.isSafeInteger(issue) || issue <= 0)) throw new Error('--issue must be positive');
  if (command === 'run') {
    const sdk = await loadSdk(host.plan.sdkRoot);
    host = new Host(directory, defaultServices(new PiExecutor(sdk)));
    const controller = new AbortController();
    const stop = () => controller.abort();
    process.on('SIGINT', stop); process.on('SIGTERM', stop);
    try {
      const state = await host.run(controller.signal);
      console.log(JSON.stringify({ tasks: Object.fromEntries(Object.entries(state.tasks).map(([id, task]) => [id, task.phase])) }));
      if (Object.values(state.tasks).some(task => task.phase === 'blocked')) return 1;
      if (Object.values(state.tasks).some(task => task.phase !== 'accepted')) return 2;
    } finally { process.off('SIGINT', stop); process.off('SIGTERM', stop); }
  } else if (command === 'accept') {
    if (!/^[0-9a-f]{40}$/.test(values.head ?? '') || !values.receipt) throw new Error('accept requires --head <full-SHA> and --receipt <operator-evidence-file>');
    host.accept(issue, values.head!, path.resolve(values.receipt));
    console.log(JSON.stringify({ issue, phase: 'accepted', scope: 'local candidate only' }));
  } else if (command === 'retry') {
    if (!/^[0-9a-f]{64}$/.test(values.fingerprint ?? '') || !values.reason) throw new Error('retry requires --fingerprint and --reason');
    host.retry(issue, values.fingerprint!, values.reason); console.log(JSON.stringify({ issue, phase: 'pending', resumedOldSession: false }));
  } else if (command === 'recover') {
    host.recover(values['confirm-quiescent'] === true, values.reason ?? '');
    console.log(JSON.stringify({ recovered: true, executionRestarted: false }));
  } else {
    const state = host.state();
    const source = values.source ? snapshot(host.plan.cwd) : undefined;
    console.log(JSON.stringify({ approval: host.plan.approval, integrationHead: state.integrationHead,
      source: source && { head: source.head, fingerprint: source.fingerprint, dirty: source.dirty },
      tasks: Object.fromEntries(Object.entries(state.tasks).map(([id, task]) => [id, { phase: task.phase, attempts: task.attempts.length, lastAttempt: task.attempts.at(-1)?.id, error: task.attempts.at(-1)?.error, commit: task.commit }])),
    }, null, 2));
  }
  return 0;
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main(process.argv.slice(2)).then(code => { process.exitCode = code; }).catch(error => { console.error(`[long-running-agent] ${error instanceof Error ? error.message : String(error)}`); process.exitCode = 1; });
}
