import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DisplayDatePipe } from '../format/display-date.pipe';
import { MoneyPipe } from '../format/money.pipe';

/** Merchant, date · category, right-aligned signed amount. Use inside a list. */
@Component({
  selector: 'app-transaction-row',
  imports: [DisplayDatePipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="row__dot" [style.background]="dotColor()" aria-hidden="true"></span>
    <span class="row__main">
      <span class="row__merchant">{{ merchant() }}</span>
      <span class="row__meta support">
        <span class="numeric">{{ date() | displayDate }}</span> · {{ category() }}
      </span>
    </span>
    <span class="row__amount numeric" [class.row__amount--income]="amount() > 0">
      {{ amount() | money: currency() : 'always' }}
    </span>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--space-3);
      min-height: 64px;
      padding: var(--space-3) 0;
      border-bottom: var(--border);
    }
    :host(:last-child) {
      border-bottom: 0;
    }
    .row__dot {
      width: 10px;
      height: 10px;
      border-radius: var(--radius-pill);
    }
    .row__main {
      display: grid;
      min-width: 0;
    }
    .row__merchant {
      overflow: hidden;
      font-weight: var(--weight-medium);
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .row__amount {
      font-size: var(--text-amount);
      font-weight: var(--weight-medium);
      text-align: right;
      white-space: nowrap;
    }
    .row__amount--income {
      color: var(--color-accent-text);
    }
  `,
})
export class TransactionRow {
  readonly merchant = input.required<string>();
  /** `yyyy-MM-dd` */
  readonly date = input.required<string>();
  readonly category = input.required<string>();
  /** 1–6, see `--chart-n` tokens. */
  readonly colorIndex = input(6);
  readonly amount = input.required<number>();
  readonly currency = input('PLN');

  protected readonly dotColor = computed(() => `var(--chart-${this.colorIndex()})`);
}
