import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseReport, relativeFile, type AgentExecutor, type AgentRequest, type Report } from './contract.ts';
import { inside, noLinks } from './io.ts';

// A small runtime seam keeps offline host tests independent of an installed SDK.
// The installed SDK is selected explicitly, version-checked, and smoke-tested.
interface NativeTool {
  name: string; label: string; description: string; parameters: unknown;
  execute: (id: string, params: Record<string, unknown>, signal?: AbortSignal, onUpdate?: unknown, context?: unknown) => Promise<unknown>;
}
interface Session {
  model?: { id: string; provider: string };
  sessionFile?: string;
  messages: { role: string; stopReason?: string; errorMessage?: string }[];
  prompt: (prompt: string) => Promise<void>;
  subscribe: (listener: (event: Record<string, unknown>) => void) => () => void;
  getLastAssistantText: () => string | undefined;
  getActiveToolNames: () => string[];
  abort: () => Promise<void>;
  dispose: () => void;
}
export interface PiSdk {
  createAgentSession: (options: Record<string, unknown>) => Promise<{ session: Session }>;
  createReadToolDefinition: (cwd: string) => NativeTool;
  createEditToolDefinition: (cwd: string) => NativeTool;
  createWriteToolDefinition: (cwd: string) => NativeTool;
  DefaultResourceLoader: new (options: Record<string, unknown>) => { reload: () => Promise<void> };
  SettingsManager: { inMemory: (settings: Record<string, unknown>) => unknown };
  SessionManager: { create: (cwd: string, directory: string) => unknown };
  ModelRuntime: { create: (options: Record<string, unknown>) => Promise<{ getModel: (provider: string, id: string) => unknown }> };
}
export async function loadSdk(root: string): Promise<PiSdk> {
  if (!path.isAbsolute(root)) throw new Error('SDK root must be absolute');
  const metadata = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
  if (metadata.name !== '@earendil-works/pi-coding-agent' || metadata.version !== '1.5.0') throw new Error('This adapter is validated only for pi-coding-agent 1.5.0; no automatic upgrade/fallback');
  // Pi exposes an import-only entry. CJS require.resolve cannot resolve it.
  const entry = metadata.exports?.['.']?.import;
  if (typeof entry !== 'string' || !entry.startsWith('./') || !inside(root, path.resolve(root, entry))) throw new Error('Invalid SDK import export');
  const loaded = await import(pathToFileURL(path.resolve(root, entry)).href);
  for (const name of ['createAgentSession', 'createReadToolDefinition', 'createEditToolDefinition', 'createWriteToolDefinition', 'DefaultResourceLoader', 'SettingsManager', 'SessionManager', 'ModelRuntime']) {
    if (typeof loaded[name] !== 'function') throw new Error(`SDK missing ${name}`);
  }
  return loaded as PiSdk;
}
export function workspacePath(root: string, input: unknown): string {
  if (typeof input !== 'string' || !input.trim() || input.includes('\0')) throw new Error('Tool path required');
  const file = path.resolve(root, input);
  if (!inside(root, file) || file === root) throw new Error('Tool path escapes approved workspace');
  noLinks(file);
  const relative = path.relative(root, file).replaceAll('\\', '/');
  relativeFile(relative);
  let existing = file;
  while (!fs.existsSync(existing)) existing = path.dirname(existing);
  const normalized = (value: string) => process.platform === 'win32' ? value.toLowerCase() : value;
  if (normalized(fs.realpathSync(existing)) !== normalized(existing)) throw new Error('Noncanonical filesystem alias');
  const segments = relative.toLowerCase().split('/');
  if (segments.some(s => ['.git', '.pi', 'node_modules'].includes(s)) || segments.some(s => s === '.env' || s.startsWith('.env.')) || /(^|\/)auth\.json$/i.test(relative)) throw new Error('Protected metadata/credential path');
  if (/^data\/(raw|extracted)(\/|$)/i.test(relative) || /^src\/Pal\.Game\/Assets(\/|$)/i.test(relative)) throw new Error('Private assets cannot be sent to model tools');
  return file;
}
export function scopedPath(request: AgentRequest, input: unknown, write: boolean): string {
  const file = workspacePath(request.plan.cwd, input);
  const relative = path.relative(request.plan.cwd, file).replaceAll('\\', '/');
  const exactWrites = request.task.allowedWritePaths.includes(relative);
  if (write && (request.role !== 'implement' || !exactWrites)) throw new Error('Write denied outside explicit implementation files');
  if (!write && !exactWrites && !request.task.readPaths.some(p => relative === p || relative.startsWith(`${p}/`))) throw new Error('Read denied outside explicit readPaths');
  return file;
}
export function scopedTools(sdk: PiSdk, request: AgentRequest): NativeTool[] {
  const tools = [[sdk.createReadToolDefinition(request.plan.cwd), false]] as [NativeTool, boolean][];
  if (request.role === 'implement') tools.push([sdk.createEditToolDefinition(request.plan.cwd), true], [sdk.createWriteToolDefinition(request.plan.cwd), true]);
  const scoped: NativeTool[] = tools.map(([original, write]) => ({
    ...original, name: `scoped_${original.name}`, label: `Scoped ${original.label}`,
    execute: async (id, params, signal, update, context) => {
      if (request.signal.aborted) throw new Error('Attempt cancelled');
      const file = scopedPath(request, params.path, write);
      return original.execute(id, { ...params, path: file }, signal, update, context);
    },
  }));
  const read = tools[0][0];
  scoped.push({ ...read, name: 'scoped_list', label: 'Scoped directory listing', description: 'List immediate approved directory entries; no recursion or symlink traversal.',
    execute: async (_id, params) => {
      const directory = scopedPath(request, params.path, false);
      const entries = fs.readdirSync(directory, { withFileTypes: true }).filter(entry => {
        if (entry.isSymbolicLink()) return false;
        try { scopedPath(request, path.join(directory, entry.name), false); return true; } catch { return false; }
      }).map(entry => ({ name: entry.name, directory: entry.isDirectory() }));
      return { content: [{ type: 'text', text: JSON.stringify(entries) }], details: {} };
    },
  });
  return scoped;
}
export class PiExecutor implements AgentExecutor {
  private readonly sdk: PiSdk;
  constructor(sdk: PiSdk) { this.sdk = sdk; }
  async run(request: AgentRequest): Promise<Report> {
    const { sdk } = this;
    if (request.signal.aborted) throw new Error('Attempt already cancelled');
    const modelRuntime = await sdk.ModelRuntime.create({ authPath: path.join(request.plan.agentDir, 'auth.json'), modelsPath: path.join(request.plan.agentDir, 'models.json'), allowModelNetwork: false, signal: request.signal });
    const model = modelRuntime.getModel(request.plan.provider, request.plan.model);
    if (!model) throw new Error('Explicit provider/model is not configured; refusing fallback');
    const settingsManager = sdk.SettingsManager.inMemory({ retry: { enabled: false }, compaction: { enabled: false } });
    // Do not inherit global/project extensions, MCP, schedules, or child runners.
    // Standard AGENTS.md discovery remains enabled.
    const resourceLoader = new sdk.DefaultResourceLoader({ cwd: request.plan.cwd, agentDir: request.plan.agentDir, settingsManager, noExtensions: true, noSkills: true, noPromptTemplates: true, noThemes: true });
    await resourceLoader.reload();
    if (request.signal.aborted) throw new Error('Cancelled during SDK initialization');
    const tools = scopedTools(sdk, request);
    const { session } = await sdk.createAgentSession({
      cwd: request.plan.cwd, agentDir: request.plan.agentDir, modelRuntime, model, settingsManager, resourceLoader,
      tools: tools.map(t => t.name), customTools: tools,
      sessionManager: sdk.SessionManager.create(request.plan.cwd, request.directory),
    });
    let turns = 0, failure: string | undefined;
    let abortPromise: Promise<void> | undefined;
    const abort = () => { abortPromise ??= session.abort().catch(() => { failure ??= 'SDK abort failed; inspect original session'; }); };
    const cancel = () => { failure ??= 'Attempt cancelled'; abort(); };
    const unsubscribe = session.subscribe(event => {
      // Do not duplicate transcripts, tool arguments, or provider errors in telemetry.
      if (['agent_start', 'agent_settled', 'turn_start', 'turn_end', 'tool_execution_start', 'tool_execution_end'].includes(String(event.type))) request.emit({ type: event.type, role: request.role, turns });
      if (event.type === 'turn_start' && ++turns > request.plan.limits.maxTurns) { failure = 'Turn limit exhausted'; abort(); }
      if (event.type === 'message_end') {
        const message = event.message as { role?: string; stopReason?: string } | undefined;
        if (message?.role === 'assistant' && ['error', 'aborted'].includes(message.stopReason ?? '')) failure ??= 'Provider error or aborted response; inspect original session';
      }
    });
    request.signal.addEventListener('abort', cancel, { once: true });
    try {
      if (request.signal.aborted) cancel();
      if (session.model?.id !== request.plan.model || session.model.provider !== request.plan.provider) throw new Error('SDK model fallback detected');
      if (JSON.stringify(session.getActiveToolNames().toSorted()) !== JSON.stringify(tools.map(t => t.name).toSorted())) throw new Error('Unexpected SDK tool capability');
      if (failure) throw new Error(failure);
      request.emit({ type: 'session_created', role: request.role, sessionFile: session.sessionFile });
      await session.prompt(request.prompt);
      if (abortPromise) await abortPromise;
      if (failure || request.signal.aborted) throw new Error(failure ?? 'Attempt cancelled');
      const last = session.messages.filter(m => m.role === 'assistant').at(-1);
      if (!last || last.stopReason !== 'stop') throw new Error('No normally completed assistant response');
      return parseReport(session.getLastAssistantText() ?? '', request.role);
    } finally {
      request.signal.removeEventListener('abort', cancel); unsubscribe();
      try { await session.abort(); } finally { session.dispose(); }
    }
  }
}
