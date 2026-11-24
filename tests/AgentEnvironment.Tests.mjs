import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { registerHooks } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sha256 = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
test('installed pi-subagents resolves project worker override and assembles fresh read instructions without private global context', {
  skip: !process.env.PAL_SUBAGENTS_ROOT || !process.env.PAL_PI_PEER_ROOT,
}, async () => {
  const packageRoot = path.resolve(process.env.PAL_SUBAGENTS_ROOT);
  const peerRoot = path.resolve(process.env.PAL_PI_PEER_ROOT);
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'pal-worker-environment-'));
  console.log(`Installed worker context evidence retained at ${directory}`);
  const cwd = path.join(directory, 'project with spaces');
  const agentDir = path.join(directory, 'isolated-agent');
  fs.mkdirSync(agentDir); fs.mkdirSync(path.join(cwd, '.pi'), { recursive: true });
  fs.mkdirSync(path.join(cwd, 'docs/agents'), { recursive: true });
  for (const file of ['.pi/settings.json', 'AGENTS.md', 'docs/agents/execution-environment.md']) fs.copyFileSync(path.join(repository, file), path.join(cwd, file));
  fs.writeFileSync(path.join(cwd, 'context.md'), 'Synthetic context material');
  fs.writeFileSync(path.join(cwd, 'plan.md'), 'Synthetic plan material');
  fs.writeFileSync(path.join(agentDir, 'AGENTS.md'), 'PRIVATE_GLOBAL_CANARY_NOT_FOR_WORKER');
  const previousAgentDir = process.env.PI_CODING_AGENT_DIR;
  process.env.PI_CODING_AGENT_DIR = agentDir;
  const rootUrl = pathToFileURL(packageRoot + path.sep).href;
  // Resolve only this selected installation's peer imports through the supplied
  // Pi host, as the extension host does; never modify installed packages.
  const hook = registerHooks({ resolve(specifier, context, nextResolve) {
    if (context.parentURL?.startsWith(rootUrl) && (specifier === 'typebox' || specifier.startsWith('typebox/') || specifier.startsWith('@earendil-works/'))) {
      return nextResolve(specifier, { ...context, parentURL: pathToFileURL(path.join(peerRoot, 'package.json')).href });
    }
    return nextResolve(specifier, context);
  } });
  try {
    const installed = relative => import(pathToFileURL(path.join(packageRoot, relative)).href);
    const { discoverAgents } = await installed('src/agents/agents.js');
    const { resolveStepBehavior, buildChainInstructions } = await installed('src/shared/settings.js');
    const { rewriteSubagentPrompt } = await installed('src/runs/shared/subagent-prompt-runtime.js');
    const peerMetadata = JSON.parse(fs.readFileSync(path.join(peerRoot, 'package.json'), 'utf8'));
    const sdk = await import(pathToFileURL(path.resolve(peerRoot, peerMetadata.exports['.'].import)).href);
    const discovered = discoverAgents(cwd, 'project', undefined, { globalNpmRoot: null, projectTrusted: true });
    assert.deepEqual(discovered.agentDiagnostics, []);
    const worker = discovered.agents.find(agent => agent.name === 'worker'); assert.ok(worker);
    const baselineCwd = path.join(directory, 'without-override'); fs.mkdirSync(baselineCwd);
    const baseline = discoverAgents(baselineCwd, 'project', undefined, { globalNpmRoot: null, projectTrusted: true }).agents.find(agent => agent.name === 'worker');
    assert.ok(baseline); assert.equal(worker.source, 'builtin');
    assert.equal(worker.inheritProjectContext, true); assert.equal(worker.inheritGlobalContext, false);
    assert.deepEqual(worker.defaultReads, ['context.md', 'plan.md', 'docs/agents/execution-environment.md']);
    for (const field of ['tools', 'model', 'modelProvider', 'thinking', 'defaultContext', 'systemPrompt', 'acceptanceRole']) assert.deepEqual(worker[field], baseline[field], `${field} must retain the bundled default`);
    assert.equal(worker.defaultContext, 'fresh');
    const behavior = resolveStepBehavior(worker, {}, undefined);
    const instructions = buildChainInstructions(behavior, cwd, true);
    const expectedReads = ['context.md', 'plan.md', 'docs/agents/execution-environment.md'].map(file => path.join(cwd, file));
    assert.equal(instructions.prefix, `[Read from: ${expectedReads.join(', ')}]\n\n`);
    const settingsManager = sdk.SettingsManager.inMemory({});
    const loader = new sdk.DefaultResourceLoader({ cwd, agentDir, settingsManager, noExtensions: true, noSkills: true, noThemes: true, noPromptTemplates: true });
    await loader.reload();
    const runtime = await sdk.ModelRuntime.create({ authPath: path.join(agentDir, 'no-auth.json'), modelsPath: null, refreshOnCreate: false, allowModelNetwork: false });
    const model = runtime.getModel('anthropic', 'claude-sonnet-4-5'); assert.ok(model);
    const { session } = await sdk.createAgentSession({ cwd, agentDir, model, modelRuntime: runtime, settingsManager, resourceLoader: loader, noTools: true, sessionManager: sdk.SessionManager.inMemory() });
    try {
      assert.ok(session.systemPrompt.includes('PRIVATE_GLOBAL_CANARY_NOT_FOR_WORKER'));
      const childPrompt = rewriteSubagentPrompt(session.systemPrompt, worker);
      assert.ok(childPrompt.includes('每个 fresh 会话开工前必读'));
      assert.ok(!childPrompt.includes('PRIVATE_GLOBAL_CANARY_NOT_FOR_WORKER'));
      assert.ok(!childPrompt.includes('# Fresh session 执行环境契约'), 'defaultReads is an instruction, not file content injection');
      fs.writeFileSync(path.join(directory, 'child-system-prompt.txt'), childPrompt, 'utf8');
      fs.writeFileSync(path.join(directory, 'child-task-prefix.txt'), instructions.prefix, 'utf8');
      assert.deepEqual(session.messages, []);
    } finally { await session.abort(); session.dispose(); }
    fs.writeFileSync(path.join(directory, 'assembly-report.json'), JSON.stringify({
      projectSettings: { path: path.join(repository, '.pi/settings.json'), sha256: sha256(path.join(repository, '.pi/settings.json')) },
      installation: { root: packageRoot, version: JSON.parse(fs.readFileSync(path.join(packageRoot, 'package.json'), 'utf8')).version,
        agentsSha256: sha256(path.join(packageRoot, 'src/agents/agents.js')), settingsSha256: sha256(path.join(packageRoot, 'src/shared/settings.js')), promptRuntimeSha256: sha256(path.join(packageRoot, 'src/runs/shared/subagent-prompt-runtime.js')) },
      peer: { root: peerRoot, version: peerMetadata.version }, defaultReads: worker.defaultReads,
      inheritedProjectContext: true, inheritedGlobalContext: false, readInstructionsOnly: true, providerRequested: false, childDispatched: false,
    }, null, 2), 'utf8');
  } finally {
    hook.deregister();
    if (previousAgentDir === undefined) delete process.env.PI_CODING_AGENT_DIR;
    else process.env.PI_CODING_AGENT_DIR = previousAgentDir;
  }
});
