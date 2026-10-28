import { formatPercent } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  LOCALE_ID,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { MoneyPipe } from '../format/money.pipe';

/** One prominent amount with a period-over-period comparison. */
@Component({
  selector: 'app-summary-card',
  imports: [TranslocoDirective, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="card summary" *transloco="let t">
      <p class="overline">{{ label() }}</p>
      <p class="summary__amount numeric">{{ amount() | money: currency() }}</p>
      @if (change(); as c) {
        <p class="summary__change support">
          <svg class="summary__arrow" viewBox="0 0 16 16" aria-hidden="true">
            @switch (c.direction) {
              @case ('up') {
                <path d="M8 3l4.5 5h-3.25v5h-2.5V8H3.5z" />
              }
              @case ('down') {
                <path d="M8 13l-4.5-5h3.25V3h2.5v5h3.25z" />
              }
              @default {
                <path d="M3 6.75h10v2.5H3z" />
              }
            }
          </svg>
          <span>
            {{
              t('summary.change.' + c.direction, { percent: c.percent, period: previousLabel() })
            }}
            <span class="numeric">({{ previousAmount() | money: currency() }})</span>
          </span>
        </p>
      }
    </article>
  `,
  styles: `
    .summary {
      display: grid;
      gap: var(--space-2);
    }
    .summary__amount {
      font-size: var(--text-balance);
      font-weight: var(--weight-medium);
      line-height: 1.1;
      letter-spacing: var(--tracking-balance);
    }
    .summary__change {
      display: flex;
      align-items: baseline;
      gap: var(--space-2);
    }
    .summary__arrow {
      flex: none;
      width: 14px;
      height: 14px;
      fill: var(--color-ink-muted);
      transform: translateY(2px);
    }
  `,
})
export class SummaryCard {
  private readonly locale = inject(LOCALE_ID);

  readonly label = input.required<string>();
  readonly amount = input.required<number>();
  readonly currency = input('PLN');
  readonly previousAmount = input<number | null>(null);
  /** Translated period phrase that completes "… niż ___", e.g. "we wrześniu". */
  readonly previousLabel = input('');

  protected readonly change = computed(() => {
    const previous = this.previousAmount();
    if (previous === null || previous === 0) {
      return null;
    }
    // Compare magnitudes so the wording works for expenses (negative) and income alike.
    const ratio = Math.abs(this.amount()) / Math.abs(previous) - 1;
    const rounded = Math.round(ratio * 100);
    const direction = rounded > 0 ? 'up' : rounded < 0 ? 'down' : 'same';
    return { direction, percent: formatPercent(Math.abs(ratio), this.locale, '1.0-0') };
  });
}
