import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { map, Subject } from 'rxjs';
import { CategoriesStore } from '../../core/categories/categories.store';
import { MoneyPipe } from '../../shared/format/money.pipe';
import { TransactionRow } from '../../shared/ui/transaction-row';
import { CorrectionFeedback } from '../transactions/correction-feedback';
import { TransactionSheet } from '../transactions/transaction-sheet';
import { CorrectionResponse, TransactionItem } from '../transactions/transactions.models';
import { InsightText } from './insight-text';
import { InsightsApi } from './insights.api';
import { SpendingFilter, UNCLASSIFIED } from './insights.models';
import { loadOn } from './loaded';

/**
 * The transactions behind a number: a month's spending, narrowed to a category and a merchant.
 * The total is the backend's sum of what each one counts for. Tap one to correct its category.
 */
@Component({
  selector: 'app-spending-transactions-page',
  imports: [TranslocoDirective, RouterLink, MoneyPipe, TransactionRow, TransactionSheet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './spending-transactions-page.html',
  styleUrl: './spending-transactions-page.scss',
})
export class SpendingTransactionsPage {
  private readonly api = inject(InsightsApi);
  private readonly transloco = inject(TranslocoService);
  private readonly feedback = inject(CorrectionFeedback);
  protected readonly text = inject(InsightText);
  protected readonly categories = inject(CategoriesStore);

  private readonly retry$ = new Subject<void>();
  private readonly filter = inject(ActivatedRoute).queryParamMap.pipe(
    map((p): SpendingFilter => ({
      month: p.get('month') ?? '',
      category: p.get('category') ?? undefined,
      merchant: p.get('merchant') ?? undefined,
    })),
  );
  protected readonly state = loadOn(this.filter, this.retry$, (filter) =>
    this.api.transactions(filter).pipe(map((data) => ({ filter, data }))),
  );
  protected readonly result = computed(() => {
    const state = this.state();
    return state.status === 'ready' ? state.data : null;
  });
  protected readonly openId = signal<string | null>(null);

  /** "Spożywcze", "Bez kategorii" or "Wszystkie wydatki"; with the merchant's name when narrowed to one. */
  protected readonly title = computed(() => {
    const result = this.result();
    if (!result) {
      return this.transloco.translate('insights.transactions.title');
    }
    const { category, merchant } = result.filter;
    const scope =
      category === UNCLASSIFIED
        ? this.transloco.translate('insights.unclassified')
        : category
          ? this.categories.label(category)
          : this.transloco.translate('insights.transactions.allSpending');
    const name = merchant ? result.data.items[0]?.transaction.merchant : null;
    return name ? `${name} · ${scope}` : scope;
  });

  /** Back to where the number came from: the category, or the overview. */
  protected readonly back = computed(() => {
    const filter = this.result()?.filter;
    const category = filter?.category;
    return category && category !== UNCLASSIFIED
      ? { link: `/overview/categories/${category}`, label: 'insights.transactions.backToCategory' }
      : { link: '/overview', label: 'insights.category.back' };
  });

  constructor() {
    this.categories.load().catch(() => undefined);
  }

  protected categoryText(item: TransactionItem): string {
    return item.categoryId
      ? this.categories.label(item.categoryId, true)
      : this.transloco.translate('transactions.unclassified');
  }

  protected onCorrected(response: CorrectionResponse): void {
    this.openId.set(null);
    // A new category may move it out of this list: load what the number is now.
    this.reload();
    this.feedback.announce(response, () => this.reload());
  }

  protected reload(): void {
    this.retry$.next();
  }
}
