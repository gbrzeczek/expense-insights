import { inject, LOCALE_ID, Pipe, PipeTransform } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { describeIssue } from './import-issue';
import { ImportBatch, ImportIssue } from './import.models';
import { pluralCategory } from './plural';

/** "Dodano 42, zaktualizowano 3, pominięto 120 duplikatów." */
@Pipe({ name: 'importSummary' })
export class ImportSummaryPipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  transform(batch: ImportBatch): string {
    const count = batch.duplicateCount;
    const duplicates = this.transloco.translate(
      `import.result.duplicates.${pluralCategory(count, this.locale)}`,
      { count },
    );
    return this.transloco.translate('import.result.summary', {
      added: batch.newCount,
      updated: batch.updatedCount,
      duplicates,
    });
  }
}

/** One row error or warning in words; unknown codes get a generic sentence. */
@Pipe({ name: 'importIssue' })
export class ImportIssuePipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  transform(issue: ImportIssue, kind: 'errors' | 'warnings'): string {
    const { key, params } = describeIssue(issue, kind, this.locale);
    const text = this.transloco.translate(key, params);
    return text === key ? this.transloco.translate(`import.${kind}.other`, params) : text;
  }
}

/** Picks the plural form of an i18n key: `import.result.errors` + 3 → `import.result.errors.few`. */
@Pipe({ name: 'pluralKey' })
export class PluralKeyPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(base: string, count: number): string {
    return `${base}.${pluralCategory(count, this.locale)}`;
  }
}
