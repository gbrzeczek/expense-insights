import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CATEGORIES_URL } from '../../core/categories/categories.store';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { item } from '../transactions/testing';
import { CategoryPage } from './category-page';
import { INSIGHTS_URL } from './insights.api';
import { shift } from './month-switcher';
import { SpendingTransactionsPage } from './spending-transactions-page';
import { SubscriptionsPage } from './subscriptions-page';
import { categoryList, GROCERIES, subscription, text, translocoTesting } from './testing';

const plain = (value: string) => value.replace(/\s/g, ' ');

describe('drill-down screens', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [translocoTesting],
      providers: [
        provideLocale(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'overview/categories/:id', component: CategoryPage },
          { path: 'overview/transactions', component: SpendingTransactionsPage },
          { path: 'overview/subscriptions', component: SubscriptionsPage },
        ]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function open(url: string) {
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl(url);
    (await vi.waitFor(() => http.expectOne(CATEGORIES_URL))).flush(categoryList());
    return { harness, navigation };
  }

  it('category: the month against the usual, the trend, and merchants leading to transactions', async () => {
    const { harness, navigation } = await open(`/overview/categories/${GROCERIES}?month=2026-09`);
    (
      await vi.waitFor(() =>
        http.expectOne(
          (r) =>
            r.url === `${INSIGHTS_URL}/categories/${GROCERIES}` &&
            r.params.get('month') === '2026-09',
        ),
      )
    ).flush({
      categoryId: GROCERIES,
      month: '2026-09',
      currency: 'PLN',
      earliestMonth: '2026-04',
      latestMonth: '2026-10',
      isPartial: false,
      spent: 1515.51,
      usual: 1750.49,
      trend: [
        { month: '2026-08', spent: 1600, isComplete: true },
        { month: '2026-09', spent: 1515.51, isComplete: true },
      ],
      merchants: [{ merchantKey: 'biedronka', merchant: 'Biedronka', amount: 800, count: 12 }],
    });
    await navigation;
    await harness.fixture.whenStable();
    const el = harness.routeNativeElement!;

    expect(text(el.querySelector('h1'))).toBe('Spożywcze');
    expect(plain(text(el.querySelector('.summary__change')))).toBe(
      'o 13% mniej niż zwykle (1 750,49 zł)',
    );
    expect(el.querySelectorAll('.bars__column')).toHaveLength(2);
    const merchant = el.querySelector<HTMLAnchorElement>('.list__row')!;
    expect(plain(text(merchant))).toBe('Biedronka12 płatności800,00 zł');
    expect(merchant.getAttribute('href')).toBe(
      `/overview/transactions?month=2026-09&category=${GROCERIES}&merchant=biedronka`,
    );
  });

  it('transactions: the total behind the number and the rows', async () => {
    const { harness, navigation } = await open(
      `/overview/transactions?month=2026-09&category=${GROCERIES}&merchant=biedronka`,
    );
    (
      await vi.waitFor(() =>
        http.expectOne(
          (r) =>
            r.url === `${INSIGHTS_URL}/transactions` &&
            r.params.get('category') === GROCERIES &&
            r.params.get('merchant') === 'biedronka',
        ),
      )
    ).flush({
      month: '2026-09',
      currency: 'PLN',
      total: 33.5,
      items: [
        { transaction: item({ id: 'a', merchant: 'Biedronka', amount: -23.5 }), counted: 23.5 },
        { transaction: item({ id: 'b', merchant: 'Biedronka', amount: -10 }), counted: 10 },
      ],
    });
    await navigation;
    await harness.fixture.whenStable();
    const el = harness.routeNativeElement!;

    expect(text(el.querySelector('h1'))).toBe('Biedronka · Spożywcze');
    expect(text(el.querySelector('.overline'))).toBe('wrzesień 2026');
    expect(plain(text(el.querySelector('.total__amount')))).toBe('33,50 zł');
    expect(el.querySelectorAll('app-transaction-row')).toHaveLength(2);
    expect(el.querySelector('.back')!.getAttribute('href')).toBe(
      `/overview/categories/${GROCERIES}?month=2026-09`,
    );
  });

  it('subscriptions: totals, badges and the previous price', async () => {
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/overview/subscriptions');
    (await vi.waitFor(() => http.expectOne(`${INSIGHTS_URL}/subscriptions`))).flush({
      currency: 'PLN',
      monthlyTotal: 104.99,
      yearlyTotal: 1259.88,
      items: [
        subscription({
          merchant: 'Netflix',
          latestAmount: 55,
          previousAmount: 49,
          priceChanged: true,
        }),
        subscription({ merchantKey: 'spotify', merchant: 'Spotify', isNew: true }),
        subscription({ merchantKey: 'gym', merchant: 'Siłownia', isActive: false }),
      ],
    });
    await navigation;
    await harness.fixture.whenStable();
    const el = harness.routeNativeElement!;

    expect(plain(text(el.querySelector('.totals')))).toBe('Miesięcznie104,99 złRocznie1 259,88 zł');
    const rows = Array.from(el.querySelectorAll('.list__row'), (r) => plain(text(r)));
    expect(rows[0]).toContain('Zmiana ceny');
    expect(rows[0]).toContain('wcześniej 49,00 zł');
    expect(rows[1]).toContain('Nowa');
    expect(rows[2]).toContain('Nieaktywna');
    expect(rows[2]).toContain('Ostatnia: 05.10.2026');
    expect(TestBed.inject(Router).url).toBe('/overview/subscriptions');
  });

  it('shifts months across years', () => {
    expect(shift('2026-01', -1)).toBe('2025-12');
    expect(shift('2025-12', 1)).toBe('2026-01');
    expect(shift('2026-10', -12)).toBe('2025-10');
  });
});
