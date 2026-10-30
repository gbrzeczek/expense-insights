import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CATEGORIES_URL } from '../../core/categories/categories.store';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { Snackbar } from '../../shared/ui/snackbar';
import { SEARCH_DEBOUNCE_MS, TransactionsPage } from './transactions-page';
import { TRANSACTIONS_URL } from './transactions.api';
import {
  categoryList,
  detail,
  GROCERIES,
  item,
  RESTAURANTS,
  text,
  translocoTesting,
} from './testing';

describe('TransactionsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TransactionsPage, translocoTesting],
      providers: [
        provideLocale(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  async function render(items = [item()], nextOffset: number | null = null) {
    const fixture = TestBed.createComponent(TransactionsPage);
    TestBed.tick();
    http.expectOne(CATEGORIES_URL).flush(categoryList());
    http
      .expectOne((r) => r.url === TRANSACTIONS_URL && r.params.get('offset') === '0')
      .flush({ items, nextOffset });
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  }

  it('groups the transactions by day, with category and signed amount', async () => {
    const { el } = await render([
      item({
        id: 'a',
        date: '2026-10-28',
        merchant: 'pyszne.pl',
        categoryId: RESTAURANTS,
        amount: -85.48,
      }),
      item({ id: 'b', date: '2026-10-27' }),
      item({ id: 'c', date: '2026-10-27', categoryId: null, merchant: '9186 KRAKOW ZAKOPIAN' }),
      item({ id: 'd', date: '2026-10-27', needsReview: true, merchant: 'MEDIAEXPET' }),
    ]);

    const days = [...el.querySelectorAll('.day')];
    expect(days.map((d) => text(d.querySelector('.day__date')))).toEqual([
      '28.10.2026',
      '27.10.2026',
    ]);
    expect(text(days[0])).toContain('Restauracje i jedzenie na wynos');
    expect(text(days[0])).toContain('−85,48 zł');
    expect(text(days[1])).toContain('Bez kategorii');
    expect(text(days[1])).toContain('Spożywcze · do sprawdzenia');
  });

  it('filters with the chips and searches after a pause', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const { fixture, el } = await render();

    const chips = [...el.querySelectorAll<HTMLButtonElement>('.chips .chip')];
    expect(chips.map(text)).toEqual(['Wszystkie', 'Nieskategoryzowane', 'Do sprawdzenia']);
    chips[1].click();
    TestBed.tick();
    http
      .expectOne((r) => r.params.get('filter') === 'unclassified')
      .flush({ items: [], nextOffset: null });
    await fixture.whenStable();
    expect(text(el.querySelector('.empty'))).toBe('Wszystkie transakcje mają kategorię.');

    const search = el.querySelector<HTMLInputElement>('#transaction-search')!;
    search.value = 'biedr';
    search.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    TestBed.tick();
    http
      .expectOne((r) => r.params.get('q') === 'biedr' && r.params.get('filter') === 'unclassified')
      .flush({ items: [item()], nextOffset: null });
  });

  it('loads the next page on "Pokaż więcej"', async () => {
    const { fixture, el } = await render([item({ id: 'a' })], 50);

    el.querySelector<HTMLButtonElement>('.more button')!.click();
    http
      .expectOne((r) => r.params.get('offset') === '50')
      .flush({ items: [item({ id: 'b' })], nextOffset: null });
    await fixture.whenStable();

    expect(el.querySelectorAll('button.tx')).toHaveLength(2);
    expect(el.querySelector('.more')).toBeNull();
  });

  it('corrects a category in two taps and offers to apply it to the merchant', async () => {
    const { fixture, el } = await render();

    // Tap 1: the row opens the sheet with the suggestions.
    el.querySelector<HTMLButtonElement>('button.tx')!.click();
    await fixture.whenStable();
    http.expectOne(`${TRANSACTIONS_URL}/tx-1`).flush(detail());
    await fixture.whenStable();
    expect(text(el.querySelector('.sheet .sheet__current'))).toBe('Spożywcze');

    // Tap 2: a chip saves the correction.
    const chip = [...el.querySelectorAll<HTMLButtonElement>('.sheet .chip')].find(
      (c) => text(c) === 'Restauracje i jedzenie na wynos',
    )!;
    chip.click();
    const put = http.expectOne({ method: 'PUT', url: `${TRANSACTIONS_URL}/tx-1/category` });
    expect(put.request.body).toEqual({ categoryId: RESTAURANTS });
    put.flush({
      transaction: item({ categoryId: RESTAURANTS, source: 'userCorrection', reason: null }),
      applyToAll: { matchType: 'exact', label: 'biedronka', count: 23 },
    });
    await fixture.whenStable();

    expect(el.querySelector('app-transaction-sheet')).toBeNull();
    expect(text(el.querySelector('.day'))).toContain('Restauracje i jedzenie na wynos');
    const snackbar = TestBed.inject(Snackbar);
    expect(snackbar.message()?.actionLabel).toBe('Zastosuj do wszystkich: „biedronka” (23)');

    snackbar.act();
    http
      .expectOne({ method: 'POST', url: `${TRANSACTIONS_URL}/tx-1/apply-to-all` })
      .flush({ rule: {}, changed: 23 });
    http
      .expectOne((r) => r.url === TRANSACTIONS_URL && r.params.get('offset') === '0')
      .flush({
        items: [item({ categoryId: RESTAURANTS })],
        nextOffset: null,
      });
    expect(snackbar.message()?.text).toBe('Zmieniono kategorie 23 transakcji.');
  });

  it('marks incoming money as a reimbursement', async () => {
    const incoming = item({ amount: 22, categoryId: null, kind: 'blikP2P' });
    const { fixture, el } = await render([incoming]);
    el.querySelector<HTMLButtonElement>('button.tx')!.click();
    await fixture.whenStable();
    http.expectOne(`${TRANSACTIONS_URL}/tx-1`).flush(detail({ transaction: incoming }));
    await fixture.whenStable();

    const select = el.querySelector<HTMLSelectElement>('#offsets')!;
    select.value = GROCERIES;
    select.dispatchEvent(new Event('change'));

    const put = http.expectOne({ method: 'PUT', url: `${TRANSACTIONS_URL}/tx-1/offsets` });
    expect(put.request.body).toEqual({ categoryId: GROCERIES });
    put.flush({ ...incoming, offsetsCategoryId: GROCERIES });
  });
});
