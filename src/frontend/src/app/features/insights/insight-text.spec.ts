import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CATEGORIES_URL, CategoriesStore } from '../../core/categories/categories.store';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { InsightText } from './insight-text';
import { categoryList, GROCERIES, insight, RESTAURANTS, translocoTesting } from './testing';

/** Formatted money uses non-breaking spaces. */
const plain = (value: string) => value.replace(/\s/g, ' ');

describe('InsightText', () => {
  let text: InsightText;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [translocoTesting],
      providers: [provideLocale(), provideHttpClient(), provideHttpClientTesting()],
    });
    const loading = TestBed.inject(CategoriesStore).load();
    TestBed.inject(HttpTestingController).expectOne(CATEGORIES_URL).flush(categoryList());
    await loading;
    text = TestBed.inject(InsightText);
  });

  it('words a category spike with its baseline, season and transactions link', () => {
    const view = text.describe(
      insight({
        categoryId: RESTAURANTS,
        amount: 900,
        usual: 400,
        change: 1.25,
        months: 6,
        lastYear: 420,
      }),
      '2026-10',
      'PLN',
    );

    expect(view.overline).toBe('Restauracje i jedzenie na wynos');
    expect(view.headline).toBe('Wyższe wydatki niż zwykle');
    expect(plain(view.evidence)).toBe(
      '900,00 zł wobec zwykłych 400,00 zł (mediana 6 poprzednich miesięcy), o 125% więcej. ' +
        'Więcej także niż w tym miesiącu przed rokiem (420,00 zł).',
    );
    expect(view.link).toBe('/overview/transactions');
    expect(view.queryParams).toEqual({ month: '2026-10', category: RESTAURANTS });
  });

  it('says "usually nothing" when the baseline is zero', () => {
    const view = text.describe(
      insight({ categoryId: GROCERIES, amount: 412.71, usual: 0, change: null, months: 3 }),
      '2026-08',
      'PLN',
    );

    expect(plain(view.evidence)).toBe('412,71 zł, a zwykle nic (mediana 3 poprzednich miesięcy).');
  });

  it('uses the Polish plural forms for the uncategorized reminder', () => {
    const headline = (count: number) =>
      text.describe(insight({ type: 'uncategorized', count, amount: 10 }), '2026-10', 'PLN')
        .headline;

    expect(headline(1)).toBe('1 transakcja czeka na kategorię');
    expect(headline(3)).toBe('3 transakcje czekają na kategorię');
    expect(headline(12)).toBe('12 transakcji czeka na kategorię');
    expect(headline(22)).toBe('22 transakcje czekają na kategorię');
  });

  it('links the uncategorized reminder to the unclassified transactions', () => {
    const view = text.describe(
      insight({ type: 'uncategorized', count: 2, amount: 50 }),
      '2026-10',
      'PLN',
    );

    expect(view.action).toBe('Przypisz kategorie');
    expect(view.queryParams).toEqual({ month: '2026-10', category: 'none' });
  });

  it('words a new subscription with its yearly cost', () => {
    const view = text.describe(
      insight({
        type: 'newSubscription',
        merchant: 'Spotify',
        merchantKey: 'spotify',
        amount: 23.99,
        cadenceDays: 30,
        yearly: 287.88,
      }),
      '2026-10',
      'PLN',
    );

    expect(view.headline).toBe('Nowa płatność cykliczna: Spotify');
    expect(plain(view.evidence)).toBe('23,99 zł co miesiąc, czyli 287,88 zł rocznie.');
    expect(view.link).toBe('/overview/subscriptions');
  });

  it('labels the pace projection as an estimate', () => {
    const view = text.describe(
      insight({ type: 'pace', amount: 6200, usual: 5000, soFar: 4100, months: 6, change: 0.24 }),
      '2026-10',
      'PLN',
    );

    expect(view.headline).toBe('W tym tempie miesiąc wyjdzie drożej niż zwykle');
    expect(plain(view.evidence)).toContain('Szacunek na cały miesiąc: 6 200,00 zł');
  });

  it('names months in the locative case', () => {
    expect(text.inMonth('2026-09', true)).toBe('We wrześniu');
    expect(text.inMonth('2026-10')).toBe('w październiku');
    expect(text.monthName('2026-10')).toBe('październik 2026');
    expect(text.dayOfMonth('2026-10-28')).toBe('28 października');
  });
});
