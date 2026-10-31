import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, LOCALE_ID } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { combineLatest, map, Subject } from 'rxjs';
import { CategoriesStore } from '../../core/categories/categories.store';
import { formatMoney, MoneyPipe } from '../../shared/format/money.pipe';
import { MonthBar, MonthBars } from '../../shared/ui/month-bars';
import { SummaryCard } from '../../shared/ui/summary-card';
import { InsightText } from './insight-text';
import { InsightsApi } from './insights.api';
import { loadOn } from './loaded';
import { MonthSwitcher } from './month-switcher';

/** Kategoria: the month against the usual, the trend, and the merchants behind it. */
@Component({
  selector: 'app-category-page',
  imports: [TranslocoDirective, RouterLink, MoneyPipe, MonthBars, MonthSwitcher, SummaryCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './category-page.html',
  styleUrl: './category-page.scss',
})
export class CategoryPage {
  private readonly api = inject(InsightsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  protected readonly text = inject(InsightText);
  protected readonly categories = inject(CategoriesStore);

  private readonly retry$ = new Subject<void>();
  protected readonly state = loadOn(
    combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(
      map(([params, query]) => ({ id: params.get('id') ?? '', month: query.get('month') })),
    ),
    this.retry$,
    ({ id, month }) => this.api.category(id, month),
  );
  protected readonly detail = computed(() => {
    const state = this.state();
    return state.status === 'ready' ? state.data : null;
  });

  protected readonly name = computed(() => {
    const id = this.detail()?.categoryId ?? this.route.snapshot.paramMap.get('id');
    return this.categories.label(id);
  });

  protected readonly bars = computed((): MonthBar[] => {
    const d = this.detail();
    if (!d?.month) {
      return [];
    }
    return d.trend.map((m) => ({
      label: formatDate(`${m.month}-01`, 'LLL', this.locale).replace('.', ''),
      description: this.transloco.translate(
        m.isComplete ? 'insights.historyBar' : 'insights.historyBarPartial',
        {
          month: this.text.monthName(m.month),
          amount: formatMoney(m.spent, this.locale, d.currency),
        },
      ),
      amount: m.spent,
      selected: m.month === d.month,
      partial: !m.isComplete,
      link: `/overview/categories/${d.categoryId}`,
      queryParams: { month: m.month },
    }));
  });

  constructor() {
    this.categories.load().catch(() => undefined);
  }

  protected payments(count: number): string {
    return this.transloco.translate(`insights.category.payments.${this.text.plural(count)}`, {
      count,
    });
  }

  protected reload(): void {
    this.retry$.next();
  }
}
