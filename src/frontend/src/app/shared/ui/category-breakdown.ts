import { formatPercent } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  LOCALE_ID,
} from '@angular/core';
import { MoneyPipe } from '../format/money.pipe';

export interface CategoryShare {
  readonly name: string;
  /** Positive magnitude of spending in the category. */
  readonly amount: number;
  /** 1–6, see `--chart-n` tokens. */
  readonly colorIndex: number;
}

/** Directly labelled horizontal bars; shares are computed here from the amounts. */
@Component({
  selector: 'app-category-breakdown',
  imports: [MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="breakdown">
      @for (row of rows(); track row.name) {
        <li class="breakdown__row">
          <span class="breakdown__name">{{ row.name }}</span>
          <span class="breakdown__amount numeric">{{ row.amount | money: currency() }}</span>
          <span class="breakdown__share numeric support">{{ row.share }}</span>
          <span class="breakdown__track" aria-hidden="true">
            <span
              class="breakdown__bar"
              [style.width.%]="row.width"
              [style.background]="'var(--chart-' + row.colorIndex + ')'"
            ></span>
          </span>
        </li>
      }
    </ul>
  `,
  styles: `
    .breakdown {
      display: grid;
      gap: var(--space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .breakdown__row {
      display: grid;
      grid-template-columns: 1fr auto 3.5ch;
      align-items: baseline;
      column-gap: var(--space-3);
      row-gap: var(--space-2);
    }
    .breakdown__name {
      font-size: var(--text-support);
      font-weight: var(--weight-medium);
    }
    .breakdown__amount {
      font-size: var(--text-support);
      font-weight: var(--weight-medium);
      text-align: right;
    }
    .breakdown__share {
      text-align: right;
    }
    .breakdown__track {
      grid-column: 1 / -1;
      height: 6px;
      border-radius: var(--radius-pill);
      background: var(--chart-grid);
      overflow: hidden;
    }
    .breakdown__bar {
      display: block;
      height: 100%;
      border-radius: var(--radius-pill);
    }
  `,
})
export class CategoryBreakdown {
  private readonly locale = inject(LOCALE_ID);

  readonly categories = input.required<readonly CategoryShare[]>();
  readonly currency = input('PLN');

  protected readonly rows = computed(() => {
    const items = [...this.categories()].sort((a, b) => b.amount - a.amount);
    const total = items.reduce((sum, c) => sum + c.amount, 0);
    const max = items[0]?.amount ?? 0;
    return items.map((c) => ({
      ...c,
      share: total ? formatPercent(c.amount / total, this.locale, '1.0-0') : '',
      // Bars are scaled to the largest category, starting from zero.
      width: max ? (c.amount / max) * 100 : 0,
    }));
  });
}
