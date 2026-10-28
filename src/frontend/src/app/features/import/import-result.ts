import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DisplayDatePipe } from '../../shared/format/display-date.pipe';
import { ImportBatch } from './import.models';
import { ImportIssuePipe, ImportSummaryPipe, PluralKeyPipe } from './import.pipes';

/** What one import did: counts, integrity checks, warnings and expandable row errors. */
@Component({
  selector: 'app-import-result',
  imports: [
    TranslocoDirective,
    DatePipe,
    DisplayDatePipe,
    ImportIssuePipe,
    ImportSummaryPipe,
    PluralKeyPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let b = batch();
    <article class="card result" *transloco="let t" aria-live="polite">
      <p class="overline">{{ t('import.result.overline') }}</p>
      @if (b.alreadyImported) {
        <p class="result__headline">
          {{ t('import.result.alreadyImported', { date: b.importedAt | date: 'dd.MM.yyyy' }) }}
        </p>
        <p class="support">{{ t('import.result.alreadyImportedHint') }}</p>
      } @else {
        <p class="result__headline numeric">{{ b | importSummary }}</p>
        <p class="support result__meta">
          <span class="result__file">{{ b.fileName }}</span>
          @if (b.periodStart && b.periodEnd) {
            <span class="numeric">{{
              t('import.result.period', {
                from: b.periodStart | displayDate,
                to: b.periodEnd | displayDate,
              })
            }}</span>
          }
        </p>
        @if (b.expiredCount > 0) {
          <p class="support numeric">
            {{ t('import.result.expired' | pluralKey: b.expiredCount, { count: b.expiredCount }) }}
          </p>
        }

        <ul class="result__checks">
          @for (check of checks(); track check.name) {
            <li class="result__check">
              <svg class="result__icon" viewBox="0 0 16 16" aria-hidden="true">
                @switch (check.result) {
                  @case ('passed') {
                    <path d="M6.5 11.2 3.3 8l-1 1 4.2 4.2 7.2-7.2-1-1z" />
                  }
                  @case ('failed') {
                    <path d="M7 3h2v6H7zm0 8h2v2H7z" />
                  }
                  @default {
                    <path d="M3 7h10v2H3z" />
                  }
                }
              </svg>
              <span>{{ t('import.result.' + check.name + '.' + check.result) }}</span>
            </li>
          }
        </ul>

        @if (b.warnings.length > 0) {
          <section class="result__section">
            <h2 class="result__subhead">{{ t('import.result.warnings') }}</h2>
            <ul class="result__list">
              @for (w of b.warnings; track $index) {
                <li class="numeric">{{ w | importIssue: 'warnings' }}</li>
              }
            </ul>
          </section>
        }

        @if (b.errors.length > 0) {
          <details class="result__errors">
            <summary class="result__summary">
              {{ t('import.result.errors' | pluralKey: b.errorCount, { count: b.errorCount }) }}
            </summary>
            <ul class="result__list">
              @for (e of b.errors; track $index) {
                <li class="numeric">{{ e | importIssue: 'errors' }}</li>
              }
            </ul>
          </details>
        }
      }
    </article>
  `,
  styles: `
    .result {
      display: grid;
      gap: var(--space-3);
    }
    .result__headline {
      font-size: var(--text-section);
      font-weight: var(--weight-medium);
      line-height: var(--leading-section);
    }
    .result__meta {
      display: flex;
      flex-wrap: wrap;
      column-gap: var(--space-3);
    }
    .result__file {
      overflow-wrap: anywhere;
    }
    .result__checks,
    .result__list {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .result__checks {
      padding-top: var(--space-3);
      border-top: var(--border);
    }
    .result__check {
      display: flex;
      gap: var(--space-2);
      font-size: var(--text-support);
      line-height: var(--leading-support);
    }
    .result__icon {
      flex: none;
      width: 16px;
      height: 16px;
      margin-top: 2px;
      fill: var(--color-ink-muted);
    }
    .result__section {
      display: grid;
      gap: var(--space-2);
    }
    .result__subhead {
      font-size: var(--text-support);
      font-weight: var(--weight-semibold);
    }
    .result__list {
      font-size: var(--text-support);
      line-height: var(--leading-support);
      color: var(--color-ink-muted);
    }
    .result__errors {
      border-top: var(--border);
      padding-top: var(--space-1);
    }
    .result__summary {
      // Keeps the native disclosure marker; the padding makes a 44 px target.
      padding-block: var(--space-3);
      font-size: var(--text-support);
      font-weight: var(--weight-medium);
      cursor: pointer;
    }
    .result__errors[open] .result__list {
      padding-bottom: var(--space-2);
    }
  `,
})
export class ImportResult {
  readonly batch = input.required<ImportBatch>();

  protected readonly checks = computed(() => [
    { name: 'totals', result: this.batch().totalsCheck },
    { name: 'balance', result: this.batch().balanceCheck },
  ]);
}
