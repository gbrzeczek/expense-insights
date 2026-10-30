import { inject, Injectable, LOCALE_ID } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { CategoriesStore } from '../../core/categories/categories.store';
import { Snackbar } from '../../shared/ui/snackbar';
import { pluralCategory } from '../import/plural';
import { TransactionsApi } from './transactions.api';
import { CorrectionResponse } from './transactions.models';

/**
 * After a correction: says what changed and, when other transactions match, offers
 * "Zastosuj do wszystkich: Biedronka (23)" in the snackbar.
 */
@Injectable({ providedIn: 'root' })
export class CorrectionFeedback {
  private readonly api = inject(TransactionsApi);
  private readonly snackbar = inject(Snackbar);
  private readonly transloco = inject(TranslocoService);
  private readonly categories = inject(CategoriesStore);
  private readonly locale = inject(LOCALE_ID);

  /** @param applied Called with the number changed after "apply to all" succeeds. */
  announce(response: CorrectionResponse, applied: (changed: number) => void): void {
    const category = this.categories.label(response.transaction.categoryId, true);
    const text = this.transloco.translate('transactions.corrected', { category });
    const offer = response.applyToAll;
    if (!offer || offer.count === 0) {
      this.snackbar.show({ text });
      return;
    }
    this.snackbar.show({
      text,
      actionLabel: this.transloco.translate('transactions.applyToAll', {
        label: offer.label,
        count: offer.count,
      }),
      action: () => this.applyToAll(response.transaction.id, applied),
    });
  }

  private applyToAll(id: string, applied: (changed: number) => void): void {
    this.api.applyToAll(id).subscribe({
      next: ({ changed }) => {
        const form = pluralCategory(changed, this.locale);
        this.snackbar.show({
          text: this.transloco.translate(`transactions.appliedToAll.${form}`, { count: changed }),
        });
        applied(changed);
      },
      error: () =>
        this.snackbar.show({ text: this.transloco.translate('transactions.applyToAllError') }),
    });
  }
}
