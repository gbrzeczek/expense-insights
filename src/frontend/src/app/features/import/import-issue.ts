import { formatDate } from '@angular/common';
import { formatMoney } from '../../shared/format/money.pipe';
import { ImportIssue } from './import.models';

export interface IssueText {
  key: string;
  params: Record<string, string | number>;
}

/** The i18n key and parameters that describe a row error or warning. */
export function describeIssue(
  issue: ImportIssue,
  kind: 'errors' | 'warnings',
  locale: string,
): IssueText {
  const money = (value: number | null) => formatMoney(value, locale);
  const day = (value: string | null) => (value ? formatDate(value, 'dd.MM', locale) : '');
  const params: Record<string, string | number> = {
    line: issue.lineNumber ?? '',
    account: issue.account ?? '',
    value: issue.value ?? '',
    from: day(issue.from),
    to: day(issue.to),
  };

  switch (issue.code) {
    case 'balanceMismatch':
    case 'totalsSumMismatch':
      params['expected'] = money(issue.expected);
      params['actual'] = money(issue.actual);
      break;
    default:
      params['expected'] = issue.expected ?? '';
      params['actual'] = issue.actual ?? '';
  }

  if (issue.code === 'totalsCountMismatch' || issue.code === 'totalsSumMismatch') {
    return {
      key: `import.${kind}.${issue.code}.${issue.value === 'credits' ? 'credits' : 'debits'}`,
      params,
    };
  }
  return { key: `import.${kind}.${issue.code}`, params };
}
