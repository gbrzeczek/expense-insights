import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { of, Subject } from 'rxjs';
import { DisplayDatePipe } from '../../shared/format/display-date.pipe';
import { MoneyPipe } from '../../shared/format/money.pipe';
import { InsightsApi } from './insights.api';
import { loadOn } from './loaded';

/** Subskrypcje: detected recurring payments, what they cost a month and a year, and what changed. */
@Component({
  selector: 'app-subscriptions-page',
  imports: [TranslocoDirective, RouterLink, MoneyPipe, DisplayDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './subscriptions-page.html',
  styleUrl: './subscriptions-page.scss',
})
export class SubscriptionsPage {
  private readonly api = inject(InsightsApi);
  private readonly retry$ = new Subject<void>();
  protected readonly state = loadOn(of(null), this.retry$, () => this.api.subscriptions());
  protected readonly data = computed(() => {
    const state = this.state();
    return state.status === 'ready' ? state.data : null;
  });

  protected reload(): void {
    this.retry$.next();
  }
}
