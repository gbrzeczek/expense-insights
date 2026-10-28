import { DOCUMENT, formatDate, formatPercent } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  LOCALE_ID,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { MoneyPipe } from '../../shared/format/money.pipe';
import { CategoryBreakdown, CategoryShare } from '../../shared/ui/category-breakdown';
import { InsightCard } from '../../shared/ui/insight-card';
import { SummaryCard } from '../../shared/ui/summary-card';
import { TransactionRow } from '../../shared/ui/transaction-row';
import { SAMPLE_CATEGORIES, SAMPLE_MONTH, SAMPLE_TRANSACTIONS } from './sample-data';

type Theme = 'system' | 'light' | 'dark';

const sum = (values: number[]) => values.reduce((a, b) => a + b, 0);

/** Development-only page for reviewing the design system on sample data. */
@Component({
  selector: 'app-styleguide-page',
  imports: [
    TranslocoDirective,
    SummaryCard,
    InsightCard,
    TransactionRow,
    CategoryBreakdown,
    MoneyPipe,
  ],
  providers: [MoneyPipe],
  templateUrl: './styleguide-page.html',
  styleUrl: './styleguide-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StyleguidePage {
  private readonly locale = inject(LOCALE_ID);
  private readonly document = inject(DOCUMENT);
  private readonly money = inject(MoneyPipe);

  protected readonly themes: readonly Theme[] = ['system', 'light', 'dark'];
  protected readonly theme = signal<Theme>(
    (this.document.documentElement.dataset['theme'] as Theme | undefined) ?? 'system',
  );

  protected readonly month = formatDate(SAMPLE_MONTH, 'LLLL yyyy', this.locale);
  protected readonly spent = sum(SAMPLE_CATEGORIES.map((c) => c.currentGrosze)) / 100;
  protected readonly spentPrevious = sum(SAMPLE_CATEGORIES.map((c) => c.previousGrosze)) / 100;

  protected readonly categories: readonly CategoryShare[] = SAMPLE_CATEGORIES.map((c) => ({
    name: c.name,
    colorIndex: c.colorIndex,
    amount: c.currentGrosze / 100,
  }));

  protected readonly transactions = SAMPLE_TRANSACTIONS.map((t) => ({
    ...t,
    amount: t.amountGrosze / 100,
  }));

  /** Facts for the insight, computed here and only put into words by the template. */
  protected readonly insight = computed(() => {
    const restaurants = SAMPLE_CATEGORIES.find((c) => c.name === 'Restauracje')!;
    const ratio = 1 - restaurants.currentGrosze / restaurants.previousGrosze;
    return {
      percent: formatPercent(ratio, this.locale, '1.0-0'),
      current: this.money.transform(restaurants.currentGrosze / 100),
      previous: this.money.transform(restaurants.previousGrosze / 100),
    };
  });

  protected readonly colorTokens = [
    'bg',
    'surface',
    'surface-raised',
    'ink',
    'ink-muted',
    'ink-subtle',
    'border',
    'accent',
    'accent-hover',
    'accent-soft',
    'accent-text',
    'focus',
  ].map((name) => `--color-${name}`);

  protected readonly chartTokens = [1, 2, 3, 4, 5, 6].map((n) => `--chart-${n}`);

  protected setTheme(theme: Theme): void {
    this.theme.set(theme);
    const root = this.document.documentElement;
    if (theme === 'system') {
      delete root.dataset['theme'];
    } else {
      root.dataset['theme'] = theme;
    }
  }
}
