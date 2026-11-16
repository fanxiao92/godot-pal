import { canonical, hash, type ProblemFinding, type Source } from './contract.ts';

export const maxProblemCorrections = 3;
export const scopeWarningFiles = 10;
export interface Correction {
  problemId?: string;
  evidenceKey?: string;
  outcome?: 'passed' | 'failed';
  validation?: string;
  cause: string;
  change: string;
  contract: string;
  evidence: string;
  sourceFingerprint: string;
  kind: 'check' | 'review';
  utc: string;
  evidenceResolution?: { file: string; sha256: string; note: string; utc: string };
}
export interface CheckFailure {
  directory: string;
  sourceFingerprint: string;
  summary: string;
}
export interface CorrectionLedger {
  corrections?: Correction[];
  extraCorrections?: number;
  pendingFailure?: CheckFailure;
  problemEvidence?: Record<string, string>;
  legacyMigration?: { ledgerHash: string; stateHash: string; approval: { file: string; sha256: string }; mappings: { index: number; problemIds: string[]; evidence: string[]; outcome?: 'passed' | 'failed'; notStarted?: { sourceFingerprint: string; file: string; sha256: string } }[]; utc: string };
}
function requireMappedLegacy(ledger: CorrectionLedger): void {
  const legacy = ledger.corrections?.filter(item => !item.problemId) ?? [];
  if (legacy.length && !ledger.legacyMigration) throw new Error('Legacy corrections require explicit parent-only migration; preserve original entries');
  if (ledger.legacyMigration && hash(canonical(legacy)) !== ledger.legacyMigration.ledgerHash) throw new Error('Legacy migration original ledger identity changed; stop for parent reconciliation');
}
export function correctionRounds(ledger: CorrectionLedger, problemId: string): Correction[] {
  requireMappedLegacy(ledger);
  const migrated = (ledger.legacyMigration?.mappings ?? []).filter(item => item.problemIds.includes(problemId) && !item.notStarted).map(item => ({ ...ledger.corrections![item.index], problemId, outcome: item.outcome, validation: item.evidence.join('\n') }));
  return [...migrated, ...(ledger.corrections?.filter(item => item.problemId === problemId) ?? [])];
}
export function bindProblemEvidence(ledger: CorrectionLedger, findings: Pick<ProblemFinding, 'problemId' | 'evidenceKey'>[]): void {
  const bindings = { ...ledger.problemEvidence };
  for (const item of [...(ledger.corrections ?? []), ...findings]) {
    if (!item.problemId || !item.evidenceKey) continue;
    if (bindings[item.evidenceKey] && bindings[item.evidenceKey] !== item.problemId) throw new Error('Same evidence cannot acquire a different problem identity; normalize diagnosis first');
    bindings[item.evidenceKey] = item.problemId;
  }
  ledger.problemEvidence = bindings;
}
export function requireUnexhausted(ledger: CorrectionLedger): void {
  requireMappedLegacy(ledger);
  const ids = [...(ledger.corrections?.flatMap(item => item.problemId ? [item.problemId] : []) ?? []), ...(ledger.legacyMigration?.mappings.flatMap(item => item.problemIds) ?? [])];
  for (const problemId of new Set(ids)) {
    const rounds = correctionRounds(ledger, problemId);
    if (rounds.length >= maxProblemCorrections && rounds.at(-1)?.outcome === 'failed') throw new Error(`Problem ${problemId}: three repair rounds failed; end this execution and preserve all rounds: ${JSON.stringify(rounds)}`);
  }
}
// The diagnoser/independent reviewers attribute root causes; native failure alone cannot identify one.
export function attributeValidation(ledger: CorrectionLedger, findings: ProblemFinding[], validation: string, resolveOthers: boolean): void {
  bindProblemEvidence(ledger, findings);
  const failed = new Set(findings.map(item => item.problemId));
  for (const correction of ledger.corrections ?? []) {
    if (correction.problemId && !correction.outcome && (failed.has(correction.problemId) || resolveOthers)) {
      correction.outcome = failed.has(correction.problemId!) ? 'failed' : 'passed';
      correction.validation = validation;
    }
  }
  requireUnexhausted(ledger);
}
export function approveCorrection(ledger: CorrectionLedger, correction: Correction): void {
  requireMappedLegacy(ledger);
  if (!correction.problemId || !/^[a-z0-9][a-z0-9-]*$/.test(correction.problemId) || !correction.evidenceKey?.trim()) throw new Error('Correction requires stable problemId and original evidenceKey');
  bindProblemEvidence(ledger, [correction as Required<Pick<Correction, 'problemId' | 'evidenceKey'>>]);
  requireUnexhausted(ledger);
  for (const value of [correction.cause, correction.change, correction.contract, correction.evidence]) {
    if (!value.trim()) throw new Error('Correction requires cause, substantive change, unchanged contract and original evidence');
  }
  if (correctionRounds(ledger, correction.problemId).length >= maxProblemCorrections) {
    throw new Error('Same problem exhausted three repair rounds; preserve all originals and end this execution');
  }
  (ledger.corrections ??= []).push(correction);
  delete ledger.pendingFailure;
}
// An operator-provided original artifact outside the repository resolves a pending review correction; the file predates
// the session (the agent has no write access outside the workspace), so existence plus an exact content hash is operator
// intent — its sufficiency stays an independent-review judgement, not a host guarantee.
export function bindEvidenceResolution(ledger: CorrectionLedger, resolution: { problemId: string; file: string; sha256: string; note: string; utc: string }): void {
  const correction = ledger.corrections?.at(-1);
  if (!correction || correction.kind !== 'review' || correction.problemId !== resolution.problemId) throw new Error('External evidence resolution must target the current pending review correction problemId');
  if (correction.evidenceResolution) throw new Error('Review correction already carries an external evidence resolution; a second binding is not allowed');
  if (!resolution.file.trim() || !/^[0-9a-f]{64}$/.test(resolution.sha256) || !resolution.note.trim()) throw new Error('External evidence resolution needs a file, an exact sha256 and a note');
  correction.evidenceResolution = { file: resolution.file, sha256: resolution.sha256, note: resolution.note, utc: resolution.utc };
}
export function requireWritable(ledger: CorrectionLedger): void {
  requireUnexhausted(ledger);
  if (ledger.pendingFailure) throw new Error('Diagnose original failure and call request_correction before writes or another check');
}
export function requireChangedAfterFailure(ledger: CorrectionLedger, current: Source): void {
  requireWritable(ledger);
  const correction = ledger.corrections?.at(-1);
  if (correction && correction.sourceFingerprint === current.fingerprint) {
    // The operator's external evidence binding is itself the substantive change of this correction round.
    if (correction.evidenceResolution) return;
    throw new Error('No substantive source correction since failure; the same check or review must not be retried');
  }
}
