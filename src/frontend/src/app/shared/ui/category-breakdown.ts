import { formatPercent, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  LOCALE_ID,
} from '@angular/core';
import { Params, RouterLink } from '@angular/router';
import { MoneyPipe } from '../format/money.pipe';

export interface CategoryShare {
  readonly name: string;
  /** Positive magnitude of spending in the category. */
  readonly amount: number;
  /** 1–6, see `--chart-n` tokens. */
  readonly colorIndex: number;
  /** Makes the row a link, e.g. to the transactions behind the amount. */
  readonly link?: string;
  readonly queryParams?: Params;
}

/** Directly labelled horizontal bars; shares are computed here from the amounts. */
@Component({
  selector: 'app-category-breakdown',
  imports: [MoneyPipe, NgTemplateOutlet, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="breakdown">
      @for (row of rows(); track row.name) {
        <li>
          @if (row.link) {
            <a
              class="breakdown__row breakdown__row--link"
              [routerLink]="row.link"
              [queryParams]="row.queryParams"
            >
              <ng-container *ngTemplateOutlet="content; context: { $implicit: row }" />
            </a>
          } @else {
            <div class="breakdown__row">
              <ng-container *ngTemplateOutlet="content; context: { $implicit: row }" />
            </div>
          }
        </li>
      }
    </ul>
    <ng-template #content let-row>
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
    </ng-template>
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
    .breakdown__row--link {
      min-height: var(--touch-target);
      margin: calc(-1 * var(--space-1)) calc(-1 * var(--space-2));
      padding: var(--space-1) var(--space-2);
      border-radius: var(--radius-control);
      color: inherit;
      text-decoration: none;
      transition: background-color var(--duration-fast) var(--ease);

      &:hover {
        background: var(--color-bg);
      }
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
