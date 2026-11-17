import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { Type, type TSchema } from 'typebox';
import type * as Pi from '@earendil-works/pi-coding-agent';
import { canonical, hash, parseReport, type Report } from './contract.ts';
import { atomicJson, git, inside, isIgnored, json, noLinks, QuiescenceUnknownError, snapshot } from './io.ts';
import { executeCheck, gameBatches, unitProjects, type EngineOptions, type Selection } from './checks.ts';
import { workspacePath } from './pi.ts';
import type { CodingExecutor, CodingRequest } from './github-host.ts';

export interface CodingSettings { provider?: string; model?: string; maxTurns?: number; timeoutMs?: number; engine?: EngineOptions }
type Sdk = typeof Pi;
// Preserve each schema's parameter type until the SDK's heterogeneous tool array.
const typedTool = <T extends TSchema>(tool: Pi.ToolDefinition<T>): Pi.ToolDefinition => tool as unknown as Pi.ToolDefinition;
function sourcePath(request: CodingRequest, input: string, write = false): string {
  const file = workspacePath(request.cwd, input);
  const relative = path.relative(request.cwd, file).replaceAll('\\', '/');
  if (relative.split('/').some(segment => ['bin', 'obj', '.godot', '.packages', 'TestResults'].includes(segment))) throw new Error('Build/cache artifacts are outside source tools');
  if (write) {
    if (request.role !== 'implement' || request.pendingDependencies.size) throw new Error('Writes require implementation role and verified dependency integration');
    if (isIgnored(request.cwd, relative)) throw new Error('Cannot modify ignored files');
    if (!request.beforeWrite) throw new Error('RED write controller unavailable');
    request.beforeWrite(relative);
  }
  return file;
}
const result = (value: unknown) => ({ content: [{ type: 'text' as const, text: typeof value === 'string' ? value : JSON.stringify(value) }], details: {} });
// Host-callback protocol rejections (wrong stage, wrong identity, frozen-contract mismatch, missing precondition)
// are session-local tool errors: the model reads the original rejection text and corrects itself within the same
// attempt instead of losing the cold-start session. Quiescence-unknown and an already-signalled session stop stay
// fatal and keep propagating to the attempt boundary; commitStarted failures live host-side and never pass here.
const toolError = (text: string) => ({ content: [{ type: 'text' as const, text }], details: {}, isError: true });
// Agent-side failed-check receipt: the classifier's exact test/message strings are embedded verbatim (raw lines, not JSON-escaped)
// so a RED re-declaration or correction diagnosis is a copy, never inference from source code. The classifier itself stays unchanged.
export function failureReceipt(outcome: { code: number; expectedRed: boolean; summary: string }, directory: string): string {
  const base = JSON.stringify({ status: outcome.expectedRed ? 'expected_red' : 'failed', summary: outcome.summary, directory });
  const classificationFile = path.join(directory, 'failure-classification.json');
  let failures = '';
  if (fs.existsSync(classificationFile)) {
    const exact = (json(classificationFile) as { failures?: { test: string; message: string }[] }).failures ?? [];
    if (exact.length) failures = `\nExact classifier failures (verbatim test identity and message for every failed case; copy these exactly into declare_red/expectedFailure or the failure diagnosis instead of inferring them from source):\n${exact.map(failure => `test: ${failure.test}\nmessage: ${failure.message}`).join('\n')}\n`;
  }
  return `${base}${failures}stdout (tail):\n${fs.readFileSync(path.join(directory, 'stdout.log'), 'utf8').slice(-4000)}\nstderr (tail):\n${fs.readFileSync(path.join(directory, 'stderr.log'), 'utf8').slice(-2000)}`;
}
export function codingTools(sdk: Sdk, request: CodingRequest, signal: AbortSignal, fatal: (error: Error) => void, engine?: EngineOptions, runNativeCheck: typeof executeCheck = executeCheck): Pi.ToolDefinition[] {
  const tools: Pi.ToolDefinition[] = [];
  const reject = (error: unknown) => {
    if (signal.aborted || error instanceof QuiescenceUnknownError) { fatal(error instanceof Error ? error : new Error(String(error))); throw error; }
    return toolError(error instanceof Error ? error.message : String(error));
  };
  // glm-5.3 twice issued same-turn parallel [check, edit] tool batches (#142 attempt 1f42baed unit-G18Rvg,
  // #147 attempt f59851b0): the edit landed inside the check's before/after snapshot window and the resulting
  // QuiescenceUnknownError lost the whole cold-start session. Prompt discipline did not prevent it twice, so
  // the execute layer enforces it: every source-mutating execute (scoped_edit/scoped_write) and every native
  // check execute (unit_test/range_test) shares one promise-chain mutex, serialized within and across both
  // groups. Read-only tools never take the lock; the chain swallows settlement so a rejected holder still
  // releases the mutex while its caller observes the original error unchanged.
  let tail: Promise<unknown> = Promise.resolve();
  const serialize = <T>(operation: () => Promise<T>): Promise<T> => {
    const run = tail.then(operation);
    tail = run.then(() => undefined, () => undefined);
    return run;
  };
  const wrappers = [[sdk.createReadToolDefinition(request.cwd), false], ...(request.role === 'implement' ? [[sdk.createEditToolDefinition(request.cwd), true], [sdk.createWriteToolDefinition(request.cwd), true]] : [])] as [Pi.ToolDefinition, boolean][];
  for (const [tool, write] of wrappers) tools.push({ ...tool, name: `scoped_${tool.name}`, execute: async (id, params, toolSignal, update, context) => {
    if (signal.aborted) throw new Error('Session cancelled');
    const body = async () => {
      try {
        const input = params as { path: string; [key: string]: unknown };
        const file = sourcePath(request, input.path, write);
        return await tool.execute(id, { ...input, path: file }, toolSignal, update, context);
      } catch (error) { return reject(error); }
    };
    return write ? serialize(body) : body();
  } });
  tools.push(typedTool({ name: 'scoped_list', label: 'List source directory', description: 'List immediate safe source entries, including the repository root.', parameters: Type.Object({ path: Type.String() }, { additionalProperties: false }),
    execute: async (_id, params) => {
      const directory = path.resolve(request.cwd, params.path);
      if (directory !== request.cwd) sourcePath(request, params.path);
      noLinks(directory);
      return result(fs.readdirSync(directory, { withFileTypes: true }).filter(entry => {
        if (entry.isSymbolicLink()) return false;
        try { sourcePath(request, path.join(directory, entry.name)); return true; } catch { return false; }
      }).map(entry => ({ name: entry.name, directory: entry.isDirectory() })));
    },
  }));
  tools.push(typedTool({ name: 'scoped_search', label: 'Search source', description: 'Literal text search in safe source files. No regex/shell, maximum 100 hits and 2000 files.', parameters: Type.Object({ query: Type.String({ minLength: 1 }), path: Type.Optional(Type.String()) }, { additionalProperties: false }),
    execute: async (_id, params) => {
      const start = path.resolve(request.cwd, params.path ?? '.');
      if (start !== request.cwd) sourcePath(request, start);
      const hits: { path: string; line: number; text: string }[] = []; let visited = 0;
      const walk = (file: string) => {
        if (signal.aborted) throw new Error('Search cancelled');
        if (hits.length >= 100 || visited >= 2000) return;
        if (file !== request.cwd) { try { sourcePath(request, file); } catch { return; } }
        const stat = fs.lstatSync(file); if (stat.isSymbolicLink()) return;
        if (stat.isDirectory()) { for (const entry of fs.readdirSync(file)) walk(path.join(file, entry)); return; }
        if (!stat.isFile() || stat.size > 512 * 1024 || !/\.(cs|md|json|ps1|psm1|csproj|props|targets|runsettings|ts|mjs|txt|yml|yaml|c|h)$/i.test(file)) return;
        visited++; const text = fs.readFileSync(file, 'utf8'); if (text.includes('\0')) return;
        text.split('\n').forEach((line, index) => { if (hits.length < 100 && line.includes(params.query)) hits.push({ path: path.relative(request.cwd, file), line: index + 1, text: line.slice(0, 500) }); });
      };
      walk(start); return result({ hits, truncated: hits.length >= 100 || visited >= 2000 });
    },
  }));
  tools.push(typedTool({ name: 'inspect_git', label: 'Inspect Git', description: 'Read-only fixed Git status/history/diff/file-list operations; no arbitrary arguments or shell.', parameters: Type.Object({ action: Type.Union([Type.Literal('status'), Type.Literal('history'), Type.Literal('diff'), Type.Literal('files'), Type.Literal('ancestor')]), commit: Type.Optional(Type.String({ pattern: '^[0-9a-f]{40}$' })) }, { additionalProperties: false }),
    execute: async (_id, params) => {
      if (params.action === 'ancestor') {
        if (!params.commit) throw new Error('Ancestor query requires a full commit SHA');
        try { git(request.cwd, 'merge-base', '--is-ancestor', params.commit, 'HEAD'); return result({ ancestor: true, commit: params.commit }); }
        catch { return result({ ancestor: false, commit: params.commit }); }
      }
      const commands = { status: ['status', '--short'], history: ['log', '--format=%H %s', '--max-count=30'], diff: ['diff', '--no-ext-diff', '--no-textconv', request.input.head, '--'], files: ['ls-files'] };
      const output = git(request.cwd, ...commands[params.action]);
      return result(output.length > 8000 ? output.slice(0, 8000) + '\n[truncated; inspect the relevant files directly]' : output);
    },
  }));
  tools.push(typedTool({ name: 'check_evidence', label: 'Read owned check evidence', description: 'Read bounded owned check logs/TRX or host evidence. With check index, file is relative to that check; otherwise relative to this host state. Never arbitrary outside-workspace reads.', parameters: Type.Object({ check: Type.Optional(Type.Integer({ minimum: 0 })), file: Type.String({ minLength: 1 }), offset: Type.Optional(Type.Integer({ minimum: 0 })), limit: Type.Optional(Type.Integer({ minimum: 1, maximum: 8000 })) }, { additionalProperties: false }),
    execute: async (_id, params) => {
      const check = params.check === undefined ? undefined : request.checks[params.check];
      const root = params.check === undefined ? request.stateDirectory : check?.directory;
      if (!root) throw new Error('Unknown owned check/state directory');
      const file = path.resolve(root, params.file); noLinks(file);
      if (!inside(root, file) || !/\.(log|trx|json|txt)$/.test(file)) throw new Error('Only owned log/TRX/JSON/text evidence is readable');
      const text = fs.readFileSync(file, 'utf8'), offset = params.offset ?? 0, end = offset + (params.limit ?? 4000);
      return result({ text: text.slice(offset, end), nextOffset: end < text.length ? end : null });
    },
  }));
  if (request.role === 'implement') {
    const expectedFailure = Type.Optional(Type.Object({ test: Type.String({ minLength: 1 }), message: Type.String({ minLength: 1 }) }, { additionalProperties: false }));
    const perform = async (selection: Selection) => {
      try {
        if (signal.aborted) throw new Error('Session cancelled');
        if (request.pendingDependencies.size) throw new Error('Verify prerequisite integration before running tests');
        const before = snapshot(request.cwd); request.beforeCheck?.(before, selection);
        if (selection.expectedFailure && request.checks.some(check => check.sourceFingerprint === before.fingerprint && check.code !== 0)) throw new Error('Cannot retroactively declare RED for an already observed failure');
        if (selection.scope === 'unit') { sourcePath(request, selection.project!); sourcePath(request, 'tests/unit.runsettings'); }
        else sourcePath(request, 'scripts/Test-Range.ps1');
        const directory = fs.mkdtempSync(path.join(request.directory, `${selection.scope}-`));
        const outcome = await runNativeCheck(request.cwd, directory, request.stateDirectory ?? request.directory, selection, signal, engine);
        const after = snapshot(request.cwd);
        if (canonical(before) !== canonical(after)) throw new QuiescenceUnknownError('Native check changed source identity');
        request.recordCheck({ project: selection.project ?? `${selection.scope}:${selection.batch ?? 'all'}:${selection.filter ?? 'all'}`, scope: selection.scope, directory, sourceFingerprint: after.fingerprint, code: outcome.code, expectedRed: outcome.expectedRed, expectedFailure: selection.expectedFailure });
        if (outcome.code !== 0 && !outcome.expectedRed) {
          if (!request.recordFailure) throw new Error('No bounded correction controller is available');
          request.recordFailure(directory, after, outcome.summary);
        }
        if (outcome.code !== 0) return result(failureReceipt(outcome, directory));
        return result({ status: 'passed', summary: outcome.summary, directory, stdout: fs.readFileSync(path.join(directory, 'stdout.log'), 'utf8').slice(-4000), stderr: fs.readFileSync(path.join(directory, 'stderr.log'), 'utf8').slice(-2000) });
      } catch (error) { return reject(error); }
    };
    tools.push(typedTool({ name: 'declare_red', label: 'Declare test-first stage', description: 'Before writes, freeze the new behavior, public test seam, test/fixture/old-contract migration paths and exact native RED. Stage paths are not a permanent production allowlist. Historical evidence cannot be rewritten.', parameters: Type.Object({ behavior: Type.String({ minLength: 1 }), seam: Type.String({ minLength: 1 }), paths: Type.Array(Type.String({ minLength: 1 }), { minItems: 1 }), expectedFailure: Type.Object({ test: Type.String({ minLength: 1 }), message: Type.String({ minLength: 1 }) }, { additionalProperties: false }) }, { additionalProperties: false }),
      execute: async (_id, params) => {
        if (signal.aborted) throw new Error('Session cancelled');
        try {
          if (!request.declareRed) throw new Error('RED declaration controller unavailable');
          for (const file of params.paths) sourcePath(request, file);
          request.declareRed(params); return result({ declared: true, next: 'Migrate old test contracts, add capability test, run exact native RED before production writes' });
        } catch (error) { return reject(error); }
      },
    }));
    tools.push(typedTool({ name: 'request_correction', label: 'Record problem-scoped correction', description: 'Diagnose a recorded unexpected complete normal failure (compiler/assertion/fixture) that arose during implementation, using stable root-cause problemIds and host original evidence identity:path#contract-anchor. A certified expectedRed is planned work, not a correction: never call request_correction for it; proceed directly to production writes and GREEN. Reuse existing IDs across checks/reviews/sessions; uncertain attribution requires operator decision. At most three repair rounds per problem; no new permissions.', parameters: Type.Object({ findings: Type.Array(Type.Object({ problemId: Type.String({ pattern: '^[a-z0-9][a-z0-9-]*$' }), evidenceKey: Type.String({ minLength: 1 }), cause: Type.String({ minLength: 1 }), evidence: Type.Array(Type.String({ minLength: 1 }), { minItems: 1 }) }, { additionalProperties: false }), { minItems: 1 }), change: Type.String({ minLength: 1 }), contract: Type.String({ minLength: 1 }) }, { additionalProperties: false }),
      execute: async (_id, params) => {
        if (!request.requestCorrection) throw new Error('Correction controller unavailable');
        try { request.requestCorrection(params.findings, params.change, params.contract); return result({ approved: true, scope: 'existing contract/permissions only' }); }
        catch (error) { return reject(error); }
      },
    }));
    tools.push(typedTool({ name: 'record_evidence_resolution', label: 'Bind operator external evidence resolution', description: 'Bind operator-provided external evidence resolving the current pending check or review correction. file must be an absolute path outside the repository; sha256 is optional: provide it to pin the exact content (it must match the file), or omit it and the host computes and records the actual file hash. problemId must equal the pending correction problemId. The binding itself is the substantive change: the source may legitimately stay unchanged. Duplicate, forged or repository-internal inputs stop the session.', parameters: Type.Object({ problemId: Type.String({ pattern: '^[a-z0-9][a-z0-9-]*$' }), file: Type.String({ minLength: 1 }), sha256: Type.Optional(Type.String({ pattern: '^[0-9a-f]{64}$' })), note: Type.String({ minLength: 1 }) }, { additionalProperties: false }),
      execute: async (_id, params) => {
        if (!request.recordEvidenceResolution) throw new Error('Evidence resolution controller unavailable');
        try {
          const sha256 = request.recordEvidenceResolution(params.problemId, params.file, params.sha256, params.note);
          return result({ resolved: true, problemId: params.problemId, file: params.file, sha256 });
        } catch (error) { return reject(error); }
      },
    }));
    tools.push(typedTool({ name: 'verify_dependency', label: 'Verify prerequisite integration', description: 'Bind a direct prerequisite to its GitHub acceptance evidence before edits/tests. evidence must be exactly equal to the acceptance comment/issue URL, or be a verbatim excerpt of that comment/issue body (the host matches URL equality or body inclusion); that comment must contain the dependency integration commit as the full 40-hex SHA or its 7-hex short form. Repeating the same valid issue/commit is idempotent across repair/resume; changed commits require operator reconciliation.', parameters: Type.Object({ issue: Type.Integer({ minimum: 1 }), commit: Type.String({ pattern: '^[0-9a-f]{40}$' }), evidence: Type.String({ minLength: 1 }) }, { additionalProperties: false }),
      execute: async (_id, params) => {
        try { request.verifyDependency(params.issue, params.commit, params.evidence); return result({ verified: params.issue, commit: params.commit }); }
        catch (error) { return reject(error); }
      },
    }));
    tools.push(typedTool({ name: 'unit_test', label: 'Run native Unit project', description: 'Run a complete approved Core/CLI Unit project, optionally narrowed by one FullyQualifiedName= or ~ method/class-prefix filter. Optional exact RED declaration is frozen before launch; abnormal results stop.', parameters: Type.Object({ project: Type.Union([Type.Literal(unitProjects[0]), Type.Literal(unitProjects[1])]), filter: Type.Optional(Type.String()), expectedFailure }, { additionalProperties: false }),
      execute: async (_id, params) => serialize(() => perform({ scope: 'unit', ...params })),
    }));
    if (engine) tools.push(typedTool({ name: 'range_test', label: 'Run approved Prepare/Game range', description: 'Existing Test-Range.ps1 only; Prepare whole scope, Game explicit positive batch and optional one FQN method/class-prefix. Global engine lock and PID/Name guard. No full gate, private data, GPU, arbitrary commands or timeout overrides.', parameters: Type.Object({ scope: Type.Union([Type.Literal('prepare'), Type.Literal('game')]), batch: Type.Optional(Type.Union(gameBatches.map(batch => Type.Literal(batch)))), filter: Type.Optional(Type.String()), expectedFailure }, { additionalProperties: false }),
      execute: async (_id, params) => serialize(() => perform(params)),
    }));
  }
  return tools;
}
// Use the harness's versioned contract, not an unrelated file in the target checkout.
const environmentContract = fileURLToPath(new URL('../../docs/agents/execution-environment.md', import.meta.url));
export async function codingResourceLoader(sdk: Sdk, request: CodingRequest, agentDir: string, settingsManager: Pi.SettingsManager, tools: Pi.ToolDefinition[]): Promise<Pi.DefaultResourceLoader> {
  const contract = fs.readFileSync(environmentContract, 'utf8');
  const environment = {
    capturedUtc: new Date().toISOString(),
    platform: process.platform, release: os.release(), arch: process.arch,
    cwd: request.cwd, node: process.version, nodeExecutable: process.execPath,
    tools: tools.map(tool => tool.name), engineChecksAuthorized: tools.some(tool => tool.name === 'range_test'),
    nativeExecution: { shell: false, arguments: 'argv', textEncoding: 'utf8' },
    contract: { source: environmentContract, sha256: hash(contract) },
  };
  const environmentFile = path.join(request.directory, 'environment.json');
  const contractFile = path.join(request.directory, 'environment-contract.txt');
  atomicJson(environmentFile, environment);
  fs.writeFileSync(contractFile, contract, 'utf8');
  const loader = new sdk.DefaultResourceLoader({
    cwd: request.cwd, agentDir, settingsManager,
    noExtensions: true, noSkills: true, noPromptTemplates: true, noThemes: true,
    agentsFilesOverride: current => ({ agentsFiles: [...current.agentsFiles,
      { path: contractFile, content: contract },
      { path: environmentFile, content: `# Host execution environment (capabilities, not permission)\n${JSON.stringify(environment, null, 2)}` },
    ] }),
  });
  await loader.reload();
  return loader;
}
export class GitHubPiExecutor implements CodingExecutor {
  readonly sdk: Sdk;
  readonly agentDir: string;
  readonly settings: CodingSettings;
  constructor(sdk: Sdk, agentDir: string, settings: CodingSettings = {}) { this.sdk = sdk; this.agentDir = agentDir; this.settings = settings; }
  async run(request: CodingRequest): Promise<Report> {
    const { sdk } = this;
    if (request.signal.aborted) throw new Error('Run cancelled');
    const settingsManager = sdk.SettingsManager.create(request.cwd, this.agentDir);
    settingsManager.applyOverrides({ retry: { enabled: false }, compaction: { enabled: false } });
    const controller = new AbortController();
    const signal = AbortSignal.any([request.signal, controller.signal]);
    let failure: Error | undefined, abortPromise: Promise<void> | undefined;
    let session: Pi.AgentSession | undefined;
    const fail = (error: Error) => {
      failure ??= error; controller.abort();
      if (session) abortPromise ??= session.abort().catch(() => { failure = new QuiescenceUnknownError('SDK abort failed; inspect session and owned processes'); });
    };
    const tools = codingTools(sdk, request, signal, fail, this.settings.engine);
    const resourceLoader = await codingResourceLoader(sdk, request, this.agentDir, settingsManager, tools);
    let model: Pi.CreateAgentSessionOptions['model'];
    let modelRuntime: Pi.CreateAgentSessionOptions['modelRuntime'];
    if (this.settings.model || this.settings.provider) {
      if (!this.settings.model || !this.settings.provider) throw new Error('Specify both --provider and --model, or use existing Pi configuration');
      modelRuntime = await sdk.ModelRuntime.create({ authPath: path.join(this.agentDir, 'auth.json'), modelsPath: path.join(this.agentDir, 'models.json'), allowModelNetwork: false, signal });
      model = modelRuntime.getModel(this.settings.provider, this.settings.model);
      if (!model) throw new Error('Requested provider/model is not configured');
    }
    const timer = setTimeout(() => fail(new Error('Session deadline exceeded; explicit resume required')), this.settings.timeoutMs ?? 1_800_000);
    let unsubscribe = () => {};
    const cancel = () => fail(new Error('Run cancelled'));
    request.signal.addEventListener('abort', cancel, { once: true });
    try {
      if (signal.aborted) throw failure ?? new Error('Run cancelled');
      const created = await sdk.createAgentSession({ cwd: request.cwd, agentDir: this.agentDir, model, modelRuntime, settingsManager, resourceLoader, tools: tools.map(tool => tool.name), customTools: tools, sessionManager: sdk.SessionManager.create(request.cwd, request.directory) });
      session = created.session;
      if (created.modelFallbackMessage || !session.model) throw new Error('Pi has no usable configured model or selected a fallback; configure Pi explicitly');
      if (this.settings.model && (session.model.id !== this.settings.model || session.model.provider !== this.settings.provider)) throw new Error('Requested model changed unexpectedly');
      if (canonical(session.getActiveToolNames().toSorted()) !== canonical(tools.map(tool => tool.name).toSorted())) throw new Error('Unexpected session capability');
      let turns = 0;
      unsubscribe = session.subscribe(event => {
        if (event.type === 'turn_start' && ++turns > (this.settings.maxTurns ?? 100)) fail(new Error('Session turn limit exceeded; explicit resume required'));
        if (event.type === 'tool_execution_start') request.emit({ type: 'tool_start', role: request.role, tool: event.toolName, turns });
        if (event.type === 'tool_execution_end') request.emit({ type: 'tool_end', role: request.role, tool: event.toolName, turns });
        if (event.type === 'message_update' && event.assistantMessageEvent.type === 'text_delta') request.emit({ type: 'text', role: request.role, text: event.assistantMessageEvent.delta });
        if (event.type === 'message_end' && event.message.role === 'assistant' && ['error', 'aborted'].includes(event.message.stopReason)) fail(new Error('Provider error or aborted response; inspect the original session'));
      });
      request.emit({ type: 'session', role: request.role, sessionFile: session.sessionFile });
      if (signal.aborted) throw failure ?? new Error('Run cancelled');
      await session.prompt(request.prompt);
      if (abortPromise) await abortPromise;
      if (failure || signal.aborted) throw failure ?? new Error('Run cancelled');
      const last = session.messages.filter(message => message.role === 'assistant').at(-1);
      if (last?.stopReason !== 'stop') throw new Error('No normally completed assistant response');
      return parseReport(session.getLastAssistantText() ?? '', request.role);
    } finally {
      clearTimeout(timer); request.signal.removeEventListener('abort', cancel); unsubscribe();
      if (session) {
        try { await session.abort(); } catch { session.dispose(); throw new QuiescenceUnknownError('Cannot confirm SDK session idle'); }
        session.dispose();
      }
    }
  }
}
