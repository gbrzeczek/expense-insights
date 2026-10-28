/** Mirrors `ImportBatchResponse` from the API (`POST/GET /api/imports`). */
export interface ImportBatch {
  id: string;
  bankId: string;
  fileName: string;
  importedAt: string;
  periodStart: string | null;
  periodEnd: string | null;
  rowCount: number;
  newCount: number;
  updatedCount: number;
  duplicateCount: number;
  errorCount: number;
  expiredCount: number;
  totalsCheck: CheckResult;
  balanceCheck: CheckResult;
  warnings: ImportIssue[];
  errors: ImportIssue[];
  alreadyImported: boolean;
}

export type CheckResult = 'passed' | 'failed' | 'notAvailable';

/** A row error or warning: a code plus parameters; the wording lives in i18n (`import.issues.*`). */
export interface ImportIssue {
  code: string;
  lineNumber: number | null;
  account: string | null;
  value: string | null;
  expected: number | null;
  actual: number | null;
  from: string | null;
  to: string | null;
}

/** Codes of a failed upload (problem details `code`), see `import.failure.*`. */
export const FAILURE_CODES = [
  'fileMissing',
  'fileTooLarge',
  'unsupportedFormat',
  'headerNotFound',
  'headerMismatch',
] as const;

export type FailureCode = (typeof FAILURE_CODES)[number] | 'unknown';

export const MAX_FILE_SIZE = 5 * 1024 * 1024;
