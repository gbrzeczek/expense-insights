import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, LOCALE_ID } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { map, Subject } from 'rxjs';
import { CategoriesStore } from '../../core/categories/categories.store';
import { DisplayDatePipe } from '../../shared/format/display-date.pipe';
import { formatMoney, MoneyPipe } from '../../shared/format/money.pipe';
import { CategoryBreakdown, CategoryShare } from '../../shared/ui/category-breakdown';
import { InsightCard } from '../../shared/ui/insight-card';
import { MonthBar, MonthBars } from '../../shared/ui/month-bars';
import { InsightText } from './insight-text';
import { InsightsApi } from './insights.api';
import { UNCLASSIFIED } from './insights.models';
import { loadOn } from './loaded';
import { MonthSwitcher } from './month-switcher';

/**
 * Przegląd: one headline for the month against the personal baseline, the recent months, three or
 * four ranked insight cards, the categories, and upcoming recurring payments. Every number links to
 * the transactions behind it.
 */
@Component({
  selector: 'app-overview-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    MoneyPipe,
    DisplayDatePipe,
    CategoryBreakdown,
    InsightCard,
    MonthBars,
    MonthSwitcher,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './overview-page.html',
  styleUrl: './overview-page.scss',
})
export class OverviewPage {
  private readonly api = inject(InsightsApi);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  protected readonly text = inject(InsightText);
  protected readonly categories = inject(CategoriesStore);
  protected readonly unclassified = UNCLASSIFIED;

  private readonly retry$ = new Subject<void>();
  protected readonly state = loadOn(
    inject(ActivatedRoute).queryParamMap.pipe(map((p) => p.get('month'))),
    this.retry$,
    (month) => this.api.overview(month),
  );
  protected readonly overview = computed(() => {
    const state = this.state();
    return state.status === 'ready' ? state.data : null;
  });

  /** The month's one headline: how it compares with the usual. */
  protected readonly headline = computed(() => {
    const o = this.overview();
    if (!o?.month) {
      return '';
    }
    const params = {
      inMonth: this.text.inMonth(o.month, true),
      day: o.through ? this.text.dayOfMonth(o.through) : '',
      percent: this.text.percent(o.usual?.change ?? null),
    };
    if (!o.usual) {
      return this.transloco.translate('insights.headline.noBaseline', {
        inMonth: this.text.inMonth(o.month),
      });
    }
    const change = o.usual.change ?? 0;
    const direction =
      Math.round(Math.abs(change) * 100) === 0 ? 'same' : change > 0 ? 'up' : 'down';
    const key = o.usual.byDay
      ? `insights.headline.byDay.${direction}`
      : `insights.headline.${direction}`;
    return this.transloco.translate(key, params);
  });

  protected readonly insights = computed(() => {
    const o = this.overview();
    // The category names come from the store; read it so the cards update once it loads.
    this.categories.all();
    return o?.month ? o.insights.map((i) => this.text.describe(i, o.month!, o.currency)) : [];
  });

  protected readonly bars = computed((): MonthBar[] => {
    const o = this.overview();
    if (!o?.month) {
      return [];
    }
    return o.history.map((m) => ({
      label: formatDate(`${m.month}-01`, 'LLL', this.locale).replace('.', ''),
      description: this.transloco.translate(
        m.isComplete ? 'insights.historyBar' : 'insights.historyBarPartial',
        {
          month: this.text.monthName(m.month),
          amount: formatMoney(m.spent, this.locale, o.currency),
        },
      ),
      amount: m.spent,
      selected: m.month === o.month,
      partial: !m.isComplete,
      link: '/overview',
      queryParams: { month: m.month },
    }));
  });

  /** A usual month for the chart's line: only for a complete month (a partial one is compared by day). */
  protected readonly usualMonth = computed(() => {
    const usual = this.overview()?.usual;
    return usual && !usual.byDay ? usual.amount : null;
  });

  protected readonly categoryShares = computed((): CategoryShare[] => {
    const o = this.overview();
    if (!o?.month) {
      return [];
    }
    return o.categories.map((c) =>
      c.categoryId === null
        ? {
            name: this.transloco.translate('insights.unclassified'),
            amount: c.amount,
            colorIndex: 6,
            link: '/overview/transactions',
            queryParams: { month: o.month, category: UNCLASSIFIED },
          }
        : {
            name: this.categories.label(c.categoryId),
            amount: c.amount,
            colorIndex: this.categories.colorIndex(c.categoryId),
            link: `/overview/categories/${c.categoryId}`,
            queryParams: { month: o.month },
          },
    );
  });

  constructor() {
    this.categories.load().catch(() => undefined);
  }

  protected reload(): void {
    this.retry$.next();
  }
}
