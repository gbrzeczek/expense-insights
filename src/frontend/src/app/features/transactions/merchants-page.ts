import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  LOCALE_ID,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { CategoriesStore } from '../../core/categories/categories.store';
import { MoneyPipe } from '../../shared/format/money.pipe';
import { CategoryPicker } from '../../shared/ui/category-picker';
import { Snackbar } from '../../shared/ui/snackbar';
import { pluralCategory } from '../import/plural';
import { TransactionsApi } from './transactions.api';
import { MerchantGroup, MerchantReview } from './transactions.models';

/** Global ids of the categories offered first when a group has no guess. */
const SPENDING_DEFAULTS = [
  '0000000c-0000-0000-0000-000000000001', // groceries
  '0000000c-0000-0000-0000-000000000002', // restaurants
  '0000000c-0000-0000-0000-000000000014', // other
];
const INCOME_DEFAULTS = [
  '0000000c-0000-0000-0000-000000000016', // transfers from people
  '0000000c-0000-0000-0000-000000000017', // other income
  '0000000c-0000-0000-0000-000000000019', // refunds
];

/**
 * First-import onboarding: review by merchant, not by transaction. The open merchants are listed
 * biggest first (count × amount); one tap labels a whole group and creates a rule for future imports.
 */
@Component({
  selector: 'app-merchants-page',
  imports: [TranslocoDirective, RouterLink, CategoryPicker, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './merchants-page.html',
  styleUrl: './merchants-page.scss',
})
export class MerchantsPage {
  private readonly api = inject(TransactionsApi);
  private readonly snackbar = inject(Snackbar);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly categories = inject(CategoriesStore);

  protected readonly review = signal<MerchantReview | null>(null);
  protected readonly failed = signal(false);
  protected readonly open = signal<string | null>(null);
  protected readonly saving = signal<string | null>(null);

  /** The progress of the main currency (most spending). */
  protected readonly progress = computed(() => this.review()?.progress[0] ?? null);
  protected readonly percent = computed(() => Math.floor((this.progress()?.share ?? 0) * 100));

  private request: Subscription | undefined;

  constructor() {
    this.categories.load().catch(() => this.failed.set(true));
    this.load();
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
  }

  protected countText(group: MerchantGroup): string {
    const form = pluralCategory(group.count, this.locale);
    return this.transloco.translate(`merchants.count.${form}`, { count: group.count });
  }

  protected suggestions(group: MerchantGroup): string[] {
    const defaults = group.total < 0 ? SPENDING_DEFAULTS : INCOME_DEFAULTS;
    return [...(group.guess ? [group.guess] : []), ...defaults].slice(0, 3);
  }

  protected toggle(group: MerchantGroup): void {
    this.open.update((key) => (key === group.merchantKey ? null : group.merchantKey));
  }

  protected label(group: MerchantGroup, categoryId: string): void {
    if (this.saving()) {
      return;
    }
    this.saving.set(group.merchantKey);
    this.api.labelMerchant(group.merchantKey, categoryId).subscribe({
      next: ({ changed }) => {
        this.saving.set(null);
        this.open.set(null);
        const form = pluralCategory(changed, this.locale);
        this.snackbar.show({
          text: this.transloco.translate(`merchants.labelled.${form}`, {
            count: changed,
            category: this.categories.label(categoryId, true),
          }),
        });
        this.load();
      },
      error: () => {
        this.saving.set(null);
        this.failed.set(true);
      },
    });
  }

  private load(): void {
    this.request?.unsubscribe();
    this.request = this.api.merchantsToReview().subscribe({
      next: (review) => {
        this.failed.set(false);
        this.review.set(review);
      },
      error: () => this.failed.set(true),
    });
  }
}
