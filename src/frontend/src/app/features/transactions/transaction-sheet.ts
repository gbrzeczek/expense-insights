import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  input,
  OnInit,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CategoriesStore } from '../../core/categories/categories.store';
import { DisplayDatePipe } from '../../shared/format/display-date.pipe';
import { MoneyPipe } from '../../shared/format/money.pipe';
import { CategoryPicker } from '../../shared/ui/category-picker';
import { TransactionsApi } from './transactions.api';
import { CorrectionResponse, TransactionDetail, TransactionItem } from './transactions.models';

/**
 * The bottom sheet for one transaction: what it is, its category with the likely alternatives as
 * chips (one tap corrects it), reimbursement for money coming in, and the raw bank details.
 */
@Component({
  selector: 'app-transaction-sheet',
  imports: [TranslocoDirective, CategoryPicker, DisplayDatePipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'closed.emit()' },
  templateUrl: './transaction-sheet.html',
  styleUrl: './transaction-sheet.scss',
})
export class TransactionSheet implements OnInit {
  private readonly api = inject(TransactionsApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly categories = inject(CategoriesStore);

  readonly transactionId = input.required<string>();
  /** A correction was saved; carries what "apply to all" would do. */
  readonly corrected = output<CorrectionResponse>();
  /** Something else about the transaction changed (reimbursement). */
  readonly changed = output<TransactionItem>();
  readonly closed = output<void>();

  protected readonly detail = signal<TransactionDetail | null>(null);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly item = computed(() => this.detail()?.transaction ?? null);
  /** Spending categories a reimbursement can pay back. */
  protected readonly spending = computed(() =>
    this.categories.selectable().filter((c) => c.kind === 'spending'),
  );

  ngOnInit(): void {
    const subscription = this.api.detail(this.transactionId()).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        queueMicrotask(() => this.heading()?.nativeElement.focus());
      },
      error: () => this.failed.set(true),
    });
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  protected pick(categoryId: string): void {
    const item = this.item();
    if (!item || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.api.correct(item.id, categoryId).subscribe({
      next: (response) => {
        this.saving.set(false);
        this.corrected.emit(response);
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  protected setOffsets(value: string): void {
    const item = this.item();
    if (!item) {
      return;
    }
    this.saving.set(true);
    this.api.setOffsets(item.id, value || null).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.detail.update((d) => (d ? { ...d, transaction: updated } : d));
        this.changed.emit(updated);
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  private fail(error: unknown): void {
    this.saving.set(false);
    this.failed.set(error instanceof HttpErrorResponse);
  }
}
