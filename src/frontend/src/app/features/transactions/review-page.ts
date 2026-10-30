import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { CategoriesStore } from '../../core/categories/categories.store';
import { DisplayDatePipe } from '../../shared/format/display-date.pipe';
import { MoneyPipe } from '../../shared/format/money.pipe';
import { CategoryPicker } from '../../shared/ui/category-picker';
import { CorrectionFeedback } from './correction-feedback';
import { TransactionsApi } from './transactions.api';
import { TransactionDetail, TransactionItem } from './transactions.models';

export const REVIEW_BATCH = 200;

/**
 * "Do sprawdzenia": the rules' guesses, one at a time, with large buttons. Confirming keeps the
 * guess as the user's own choice; a chip picks another category; "Pomiń" leaves it for later.
 */
@Component({
  selector: 'app-review-page',
  imports: [TranslocoDirective, RouterLink, CategoryPicker, DisplayDatePipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './review-page.html',
  styleUrl: './review-page.scss',
})
export class ReviewPage {
  private readonly api = inject(TransactionsApi);
  private readonly feedback = inject(CorrectionFeedback);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly categories = inject(CategoriesStore);

  protected readonly queue = signal<TransactionItem[]>([]);
  protected readonly position = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly detail = signal<TransactionDetail | null>(null);

  protected readonly current = computed(() => this.queue()[this.position()] ?? null);
  protected readonly done = computed(() => !this.loading() && this.current() === null);

  private detailRequest: Subscription | undefined;

  constructor() {
    this.categories.load().catch(() => this.failed.set(true));
    const list = this.api
      .list({ filter: 'review', q: '', offset: 0, limit: REVIEW_BATCH })
      .subscribe({
        next: (page) => {
          this.queue.set(page.items);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        },
      });

    // The suggestions come with the detail of the item on screen.
    effect(() => {
      const item = this.current();
      untracked(() => {
        this.detailRequest?.unsubscribe();
        this.detail.set(null);
        if (item) {
          this.detailRequest = this.api.detail(item.id).subscribe({
            next: (detail) => this.detail.set(detail),
            error: () => this.detail.set(null),
          });
        }
      });
    });

    this.destroyRef.onDestroy(() => {
      list.unsubscribe();
      this.detailRequest?.unsubscribe();
    });
  }

  protected confirm(): void {
    const item = this.current();
    if (item?.categoryId) {
      this.pick(item.categoryId);
    }
  }

  protected pick(categoryId: string): void {
    const item = this.current();
    if (!item || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.api.correct(item.id, categoryId).subscribe({
      next: (response) => {
        this.saving.set(false);
        // Apply-to-all may settle later items too: drop the ones it changed.
        this.feedback.announce(response, () => this.dropSettled(response.transaction.merchantKey));
        this.next();
      },
      error: () => {
        this.saving.set(false);
        this.failed.set(true);
      },
    });
  }

  protected next(): void {
    this.position.update((p) => p + 1);
  }

  private dropSettled(merchantKey: string): void {
    const seen = this.queue().slice(0, this.position());
    const rest = this.queue()
      .slice(this.position())
      .filter((i) => i.merchantKey !== merchantKey);
    this.queue.set([...seen, ...rest]);
  }
}
