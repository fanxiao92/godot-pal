import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { spawnSync, spawn } from 'node:child_process';
import { canonical, hash, type Source, type Issue, type Check } from './contract.ts';

export function json(file: string): unknown { return JSON.parse(fs.readFileSync(file, 'utf8')); }
export function atomicJson(file: string, value: unknown): void {
  const temp = `${file}.${randomUUID()}.tmp`;
  const fd = fs.openSync(temp, 'wx');
  try { fs.writeFileSync(fd, JSON.stringify(value, null, 2) + '\n'); fs.fsyncSync(fd); }
  finally { fs.closeSync(fd); }
  // Never delete the old destination first. A failed replace leaves recovery evidence.
  fs.renameSync(temp, file);
}
export function native(cwd: string, executable: string, args: string[]): string {
  const result = spawnSync(executable, args, { cwd, shell: false, encoding: 'utf8', windowsHide: true, timeout: 30_000, maxBuffer: 16 * 1024 * 1024 });
  if (result.error || result.status !== 0 || result.signal) throw new Error(`${path.basename(executable)} ${args[0]} failed: ${result.error?.message ?? result.stderr.trim() ?? result.signal}`);
  return result.stdout;
}
export const git = (cwd: string, ...args: string[]): string => native(cwd, 'git', args);
export function isIgnored(cwd: string, file: string): boolean {
  const result = spawnSync('git', ['check-ignore', '--no-index', '-q', '--', file], { cwd, shell: false, windowsHide: true, timeout: 30_000 });
  if (result.error || (result.status !== 0 && result.status !== 1)) throw new Error('Cannot check ignored path');
  return result.status === 0;
}
export class QuiescenceUnknownError extends Error {}
// Only a normal, known native exit may be inspected for bounded business correction.
export class NativeCheckFailure extends Error {
  readonly code: number;
  constructor(code: number) { super('Native check failed; see original logs'); this.code = code; }
}
export function inside(root: string, target: string): boolean {
  const relative = path.relative(root, target);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
}
export function noLinks(target: string): void {
  let current = path.resolve(target);
  for (;;) {
    try { if (fs.lstatSync(current).isSymbolicLink()) throw new Error(`Symlink/reparse link not allowed: ${current}`); }
    catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error; }
    const parent = path.dirname(current);
    if (parent === current) break;
    current = parent;
  }
}
export function snapshot(cwd: string): Source {
  const read = () => {
    const head = git(cwd, 'rev-parse', 'HEAD').trim();
    const flags = git(cwd, 'ls-files', '-v', '-z').split('\0').filter(Boolean);
    if (flags.some(entry => /^[a-zS]/.test(entry))) throw new Error('assume-unchanged/skip-worktree entries are not supported');
    const index = git(cwd, 'ls-files', '--stage', '-z');
    if (index.split('\0').filter(Boolean).some(entry => !/^[0-7]+ [0-9a-f]+ 0\t/.test(entry))) throw new Error('Unmerged index entries are not supported');
    const names = git(cwd, 'ls-files', '-z', '--cached', '--others', '--exclude-standard').split('\0').filter(Boolean);
    const files: Record<string, string> = Object.create(null);
    const modes: Record<string, number> = Object.create(null);
    for (const name of [...new Set(names)].sort()) {
      const file = path.resolve(cwd, name);
      if (!inside(cwd, file)) throw new Error('Git returned an escaping path');
      noLinks(file);
      if (fs.existsSync(file)) {
        if (!fs.statSync(file).isFile()) throw new Error(`Unsupported source entry: ${name}`);
        const normalized = name.replaceAll('\\', '/');
        files[normalized] = hash(fs.readFileSync(file));
        modes[normalized] = fs.statSync(file).mode & 0o111;
      }
    }
    return { head, files, modes, indexFingerprint: hash(index), fingerprint: hash(canonical({ files, modes })), dirty: Boolean(git(cwd, 'status', '--porcelain=v1', '--untracked-files=all').trim()) };
  };
  const first = read(), second = read();
  if (canonical(first) !== canonical(second)) throw new Error('Source changed while capturing identity');
  return second;
}
export function readIssue(cwd: string, issue: number): { issue: Issue; blockers: number[] } {
  const body = JSON.parse(native(cwd, 'gh', ['issue', 'view', String(issue), '--repo', 'fanxiao92/godot-pal', '--json', 'number,title,body,state,labels,comments'])) as Issue;
  const pages = JSON.parse(native(cwd, 'gh', ['api', '--paginate', '--slurp', `repos/fanxiao92/godot-pal/issues/${issue}/dependencies/blocked_by`])) as { number: number }[][];
  if (body.number !== issue || !Array.isArray(body.comments) || !Array.isArray(body.labels) || !Array.isArray(pages) || pages.some(page => !Array.isArray(page))) throw new Error('Invalid GitHub response');
  const blockers = pages.flat().map(item => item.number);
  if (blockers.some(n => !Number.isSafeInteger(n))) throw new Error('Invalid GitHub blocker identity');
  return { issue: body, blockers };
}
export interface Lock { pid: number; token: string; stateDirectory: string; acquiredUtc: string }
export function lockFile(cwd: string): string {
  return path.join(path.resolve(cwd, git(cwd, 'rev-parse', '--git-common-dir').trim()), 'pal-long-running-agent.lock');
}
export function acquire(cwd: string, directory: string, file = lockFile(cwd)): () => void {
  const lock: Lock = { pid: process.pid, token: randomUUID(), stateDirectory: directory, acquiredUtc: new Date().toISOString() };
  const fd = fs.openSync(file, 'wx');
  try { fs.writeFileSync(fd, JSON.stringify(lock)); fs.fsyncSync(fd); } finally { fs.closeSync(fd); }
  return () => {
    const current = json(file) as Lock;
    if (current.token !== lock.token) throw new Error('Writer lock identity changed; refusing release');
    fs.unlinkSync(file);
  };
}
export function recoverLock(cwd: string, directory: string, file = lockFile(cwd)): void {
  if (!fs.existsSync(file)) return;
  const lock = json(file) as Lock;
  if (!Number.isSafeInteger(lock.pid) || lock.pid <= 0 || !lock.token || lock.stateDirectory !== directory) throw new Error('Unknown writer lock; refusing recovery');
  try { process.kill(lock.pid, 0); }
  catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'ESRCH') throw new Error('Cannot prove writer owner exited');
    if (canonical(json(file)) !== canonical(lock)) throw new Error('Writer lock changed during recovery');
    fs.unlinkSync(file);
    return;
  }
  throw new Error(`Writer PID ${lock.pid} still exists; refusing recovery`);
}
export async function runCheck(cwd: string, check: Check, directory: string, signal: AbortSignal): Promise<void> {
  const stdout = fs.openSync(path.join(directory, 'stdout.log'), 'wx');
  const stderr = fs.openSync(path.join(directory, 'stderr.log'), 'wx');
  const startedUtc = new Date().toISOString();
  atomicJson(path.join(directory, 'intent.json'), { ...check, cwd, startedUtc });
  let timedOut = false, interrupted = false;
  try {
    await new Promise<void>((resolve, reject) => {
      if (signal.aborted) { interrupted = true; reject(new Error('Check cancelled before launch')); return; }
      const child = spawn(check.executable, check.args, { cwd, shell: false, windowsHide: true, stdio: ['ignore', stdout, stderr] });
      let launchError: Error | undefined;
      const stop = () => { interrupted = true; child.kill(); };
      signal.addEventListener('abort', stop, { once: true });
      const timer = setTimeout(() => { timedOut = true; child.kill(); }, check.timeoutMs);
      child.once('error', error => { launchError = error; });
      child.once('spawn', () => {
        try { atomicJson(path.join(directory, 'started.json'), { pid: child.pid, startedUtc, cwd }); }
        catch (error) { launchError = error as Error; interrupted = true; child.kill(); }
      });
      child.once('close', (code, exitSignal) => {
        clearTimeout(timer); signal.removeEventListener('abort', stop);
        try {
          atomicJson(path.join(directory, 'result.json'), { ...check, cwd, pid: child.pid ?? null, startedUtc, endedUtc: new Date().toISOString(), code, signal: exitSignal, timedOut, interrupted, launchError: launchError?.message ?? null });
          if (timedOut || interrupted || exitSignal || (code === null && !launchError)) reject(new QuiescenceUnknownError('Native check interrupted; descendant quiescence requires operator confirmation'));
          else if (launchError) reject(launchError);
          else if (code !== 0) reject(new NativeCheckFailure(code!));
          else resolve();
        } catch (error) { reject(error); }
      });
    });
  } finally { fs.closeSync(stdout); fs.closeSync(stderr); }
}
