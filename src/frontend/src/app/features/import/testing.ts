import { ImportBatch } from './import.models';

export { text, translocoTesting } from '../../shared/testing';

export function batch(overrides: Partial<ImportBatch> = {}): ImportBatch {
  return {
    id: '01a1',
    bankId: 'ing-pl',
    fileName: 'Lista_transakcji_nr_0123456789_281026.csv',
    importedAt: '2026-10-28T11:40:00Z',
    periodStart: '2026-05-01',
    periodEnd: '2026-10-28',
    rowCount: 165,
    newCount: 42,
    updatedCount: 3,
    duplicateCount: 120,
    errorCount: 0,
    expiredCount: 0,
    totalsCheck: 'passed',
    balanceCheck: 'passed',
    warnings: [],
    errors: [],
    alreadyImported: false,
    ...overrides,
  };
}

export const issue = {
  lineNumber: null,
  account: null,
  value: null,
  expected: null,
  actual: null,
  from: null,
  to: null,
};
