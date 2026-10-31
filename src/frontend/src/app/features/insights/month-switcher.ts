import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { InsightText } from './insight-text';

/** Previous / next month links around the month's name; the month is the `month` query parameter. */
@Component({
  selector: 'app-month-switcher',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="switcher" *transloco="let t" [attr.aria-label]="t('insights.switcher.label')">
      @if (previous(); as p) {
        <a
          class="switcher__step"
          [routerLink]="[]"
          [queryParams]="{ month: p }"
          [queryParamsHandling]="'merge'"
          [attr.aria-label]="t('insights.switcher.previous')"
        >
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M15.4 7.4 14 6l-6 6 6 6 1.4-1.4-4.6-4.6z" />
          </svg>
        </a>
      } @else {
        <span class="switcher__step switcher__step--off" aria-hidden="true"></span>
      }
      <span class="switcher__month" aria-live="polite">{{ name() }}</span>
      @if (next(); as n) {
        <a
          class="switcher__step"
          [routerLink]="[]"
          [queryParams]="{ month: n }"
          [queryParamsHandling]="'merge'"
          [attr.aria-label]="t('insights.switcher.next')"
        >
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M8.6 16.6 10 18l6-6-6-6-1.4 1.4 4.6 4.6z" />
          </svg>
        </a>
      } @else {
        <span class="switcher__step switcher__step--off" aria-hidden="true"></span>
      }
    </nav>
  `,
  styles: `
    .switcher {
      display: grid;
      grid-template-columns: var(--touch-target) 1fr var(--touch-target);
      align-items: center;
      gap: var(--space-2);
    }
    .switcher__month {
      font-weight: var(--weight-medium);
      text-align: center;
      &::first-letter {
        text-transform: uppercase;
      }
    }
    .switcher__step {
      display: grid;
      place-items: center;
      width: var(--touch-target);
      height: var(--touch-target);
      border: var(--border);
      border-radius: var(--radius-pill);
      color: var(--color-ink);
      transition: background-color var(--duration-fast) var(--ease);

      &:hover {
        background: var(--color-surface);
      }

      svg {
        width: 20px;
        height: 20px;
        fill: currentColor;
      }
    }
    .switcher__step--off {
      visibility: hidden;
    }
  `,
})
export class MonthSwitcher {
  private readonly text = inject(InsightText);

  /** `yyyy-MM`. */
  readonly month = input.required<string>();
  readonly earliest = input.required<string>();
  readonly latest = input.required<string>();

  protected readonly name = computed(() => this.text.monthName(this.month()));
  protected readonly previous = computed(() => {
    const month = shift(this.month(), -1);
    return month >= this.earliest() ? month : null;
  });
  protected readonly next = computed(() => {
    const month = shift(this.month(), 1);
    return month <= this.latest() ? month : null;
  });
}

/** `2026-10` ± months, as `yyyy-MM` (which sorts like the months it names). */
export function shift(month: string, by: number): string {
  const [year, number] = month.split('-').map(Number);
  const index = year * 12 + (number - 1) + by;
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, '0')}`;
}
