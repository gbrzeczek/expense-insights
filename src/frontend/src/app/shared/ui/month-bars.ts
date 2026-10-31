import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  LOCALE_ID,
} from '@angular/core';
import { Params, RouterLink } from '@angular/router';

export interface MonthBar {
  /** Short month name under the bar (`paź`). */
  readonly label: string;
  /** Spoken label, e.g. "październik 2026: 4 210,00 zł". */
  readonly description: string;
  /** Positive spending. */
  readonly amount: number;
  readonly selected?: boolean;
  /** Not imported to its end: drawn lighter. */
  readonly partial?: boolean;
  readonly link?: string;
  readonly queryParams?: Params;
}

/**
 * A small bar chart of monthly spending from a zero baseline, with an optional dashed line at the
 * usual month. Each bar is directly labelled (compact amount) and, with a link, tappable. On a phone
 * only the latest six months are shown.
 */
@Component({
  selector: 'app-month-bars',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bars">
      @if (usualHeight() !== null) {
        <span class="bars__overlay" aria-hidden="true">
          <span class="bars__usual" [style.bottom.%]="usualHeight()"></span>
        </span>
      }
      @for (bar of rows(); track bar.label + bar.description) {
        <a
          class="bars__column"
          [class.bars__column--selected]="bar.selected"
          [class.bars__column--partial]="bar.partial"
          [routerLink]="bar.link ?? null"
          [queryParams]="bar.queryParams"
          [attr.aria-label]="bar.description"
          [attr.aria-current]="bar.selected ? 'date' : null"
        >
          <span class="bars__value numeric" aria-hidden="true">{{ bar.compact }}</span>
          <span class="bars__plot" aria-hidden="true">
            <span class="bars__bar" [style.height.%]="bar.height"></span>
          </span>
          <span class="bars__label" aria-hidden="true">{{ bar.label }}</span>
        </a>
      }
    </div>
  `,
  styles: `
    .bars {
      --plot-height: 96px;
      --label-height: 20px;
      position: relative;
      display: grid;
      grid-auto-columns: minmax(var(--touch-target), 1fr);
      grid-auto-flow: column;
      gap: var(--space-1);
    }
    // A phone fits six tappable bars: show the latest six of a longer series.
    @media (max-width: 639px) {
      .bars__column:nth-last-of-type(n + 7) {
        display: none;
      }
    }
    // Exactly over the plot area: below the value labels, above the month labels.
    .bars__overlay {
      position: absolute;
      top: calc(var(--label-height) + var(--space-1));
      right: 0;
      left: 0;
      height: var(--plot-height);
      pointer-events: none;
    }
    .bars__usual {
      position: absolute;
      right: 0;
      left: 0;
      border-top: 1px dashed var(--color-ink-muted);
    }
    .bars__column {
      display: grid;
      grid-template-rows: var(--label-height) var(--plot-height) var(--label-height);
      gap: var(--space-1);
      justify-items: center;
      min-height: var(--touch-target);
      border-radius: var(--radius-control);
      color: var(--color-ink-muted);
      text-decoration: none;
    }
    .bars__value,
    .bars__label {
      font-size: var(--text-meta);
      line-height: var(--label-height);
      white-space: nowrap;
    }
    .bars__plot {
      display: flex;
      align-items: flex-end;
      justify-content: center;
      width: 100%;
      border-bottom: 1px solid var(--color-border);
    }
    .bars__bar {
      display: block;
      width: 60%;
      max-width: 32px;
      border-radius: var(--radius-control) var(--radius-control) 0 0;
      background: var(--chart-6);
    }
    .bars__column--selected {
      color: var(--color-ink);
      font-weight: var(--weight-medium);

      .bars__bar {
        background: var(--color-accent);
      }
    }
    .bars__column--partial .bars__bar {
      opacity: 0.55;
    }
  `,
})
export class MonthBars {
  private readonly locale = inject(LOCALE_ID);

  readonly bars = input.required<readonly MonthBar[]>();
  /** The usual month: drawn as a dashed line. */
  readonly usual = input<number | null>(null);

  private readonly max = computed(() =>
    Math.max(this.usual() ?? 0, ...this.bars().map((b) => b.amount), 0),
  );

  protected readonly rows = computed(() => {
    const max = this.max();
    // `4,2 tys.` for thousands, whole złoty below.
    const compact = new Intl.NumberFormat(this.locale, {
      notation: 'compact',
      maximumFractionDigits: 1,
    });
    const whole = new Intl.NumberFormat(this.locale, { maximumFractionDigits: 0 });
    return this.bars().map((bar) => ({
      ...bar,
      compact: (Math.abs(bar.amount) >= 1000 ? compact : whole).format(bar.amount),
      height: max > 0 ? (Math.max(bar.amount, 0) / max) * 100 : 0,
    }));
  });

  protected readonly usualHeight = computed(() => {
    const usual = this.usual();
    const max = this.max();
    return usual === null || max <= 0 ? null : (usual / max) * 100;
  });
}
