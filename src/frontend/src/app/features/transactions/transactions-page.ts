import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { CategoriesStore } from '../../core/categories/categories.store';
import { DisplayDatePipe } from '../../shared/format/display-date.pipe';
import { TransactionRow } from '../../shared/ui/transaction-row';
import { CorrectionFeedback } from './correction-feedback';
import { TransactionSheet } from './transaction-sheet';
import { TransactionsApi } from './transactions.api';
import { CorrectionResponse, TransactionFilter, TransactionItem } from './transactions.models';

export const SEARCH_DEBOUNCE_MS = 300;

/** All transactions, newest first, grouped by day; tap one to correct its category. */
@Component({
  selector: 'app-transactions-page',
  imports: [TranslocoDirective, RouterLink, DisplayDatePipe, TransactionRow, TransactionSheet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './transactions-page.html',
  styleUrl: './transactions-page.scss',
})
export class TransactionsPage {
  private readonly api = inject(TransactionsApi);
  private readonly feedback = inject(CorrectionFeedback);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly categories = inject(CategoriesStore);

  protected readonly filters: readonly TransactionFilter[] = ['all', 'unclassified', 'review'];
  protected readonly filter = signal<TransactionFilter>('all');
  protected readonly query = signal('');
  protected readonly items = signal<TransactionItem[]>([]);
  protected readonly nextOffset = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly openId = signal<string | null>(null);

  /** The list grouped by day, keeping the newest-first order. */
  protected readonly days = computed(() => {
    const days: { date: string; items: TransactionItem[] }[] = [];
    for (const item of this.items()) {
      const last = days.at(-1);
      if (last?.date === item.date) {
        last.items.push(item);
      } else {
        days.push({ date: item.date, items: [item] });
      }
    }
    return days;
  });

  private readonly sentinel = viewChild<ElementRef<HTMLElement>>('sentinel');
  private request: Subscription | undefined;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.categories.load().catch(() => this.failed.set(true));

    // A new filter or search starts the list over.
    effect(() => {
      const filter = this.filter();
      const q = this.query();
      untracked(() => this.load(filter, q, 0));
    });

    // Infinite scroll where supported; the "Pokaż więcej" button works everywhere.
    const observer =
      typeof IntersectionObserver === 'undefined'
        ? null
        : new IntersectionObserver((entries) => {
            if (entries.some((e) => e.isIntersecting)) {
              this.more();
            }
          });
    effect(() => {
      const el = this.sentinel()?.nativeElement;
      observer?.disconnect();
      if (el) {
        observer?.observe(el);
      }
    });
    this.destroyRef.onDestroy(() => observer?.disconnect());

    this.destroyRef.onDestroy(() => {
      this.request?.unsubscribe();
      clearTimeout(this.searchTimer);
    });
  }

  protected search(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.query.set(value.trim()), SEARCH_DEBOUNCE_MS);
  }

  protected more(): void {
    const offset = this.nextOffset();
    if (offset !== null && !this.loading()) {
      this.load(this.filter(), this.query(), offset);
    }
  }

  protected categoryText(item: TransactionItem): string {
    const name = item.categoryId
      ? this.categories.label(item.categoryId, true)
      : this.transloco.translate('transactions.unclassified');
    return item.needsReview ? this.transloco.translate('transactions.withReview', { name }) : name;
  }

  protected onCorrected(response: CorrectionResponse): void {
    this.replace(response.transaction);
    this.openId.set(null);
    this.feedback.announce(response, () => this.load(this.filter(), this.query(), 0));
  }

  protected replace(item: TransactionItem): void {
    this.items.update((all) => all.map((i) => (i.id === item.id ? item : i)));
  }

  private load(filter: TransactionFilter, q: string, offset: number): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.failed.set(false);
    this.request = this.api.list({ filter, q, offset }).subscribe({
      next: (page) => {
        this.items.update((all) => (offset === 0 ? page.items : [...all, ...page.items]));
        this.nextOffset.set(page.nextOffset);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }
}
