import { type Source } from './contract.ts';

export const defaultCorrections = 2;
export const scopeWarningFiles = 10;
export interface Correction {
  cause: string;
  change: string;
  contract: string;
  evidence: string;
  sourceFingerprint: string;
  kind: 'check' | 'review';
  utc: string;
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
}
export function approveCorrection(ledger: CorrectionLedger, correction: Correction): void {
  for (const value of [correction.cause, correction.change, correction.contract, correction.evidence]) {
    if (!value.trim()) throw new Error('Correction requires cause, substantive change, unchanged contract and original evidence');
  }
  if ((ledger.corrections?.length ?? 0) >= defaultCorrections + (ledger.extraCorrections ?? 0)) {
    throw new Error('Correction budget exhausted; preserve evidence and request explicit additional authority');
  }
  (ledger.corrections ??= []).push(correction);
  delete ledger.pendingFailure;
}
export function requireWritable(ledger: CorrectionLedger): void {
  if (ledger.pendingFailure) throw new Error('Diagnose original failure and call request_correction before writes or another check');
}
export function requireChangedAfterFailure(ledger: CorrectionLedger, current: Source): void {
  requireWritable(ledger);
  const correction = ledger.corrections?.at(-1);
  if (correction && correction.sourceFingerprint === current.fingerprint) {
    throw new Error('No substantive source correction since failure; the same check or review must not be retried');
  }
}
