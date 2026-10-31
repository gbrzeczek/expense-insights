import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CATEGORIES_URL } from '../../core/categories/categories.store';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { INSIGHTS_URL } from './insights.api';
import { Overview } from './insights.models';
import { OverviewPage } from './overview-page';
import {
  categoryList,
  GROCERIES,
  insight,
  overview,
  subscription,
  text,
  translocoTesting,
} from './testing';

const plain = (value: string) => value.replace(/\s/g, ' ');

describe('OverviewPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OverviewPage, translocoTesting],
      providers: [
        provideLocale(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(data: Overview | null) {
    const fixture = TestBed.createComponent(OverviewPage);
    TestBed.tick();
    http.expectOne(CATEGORIES_URL).flush(categoryList());
    const request = http.expectOne(`${INSIGHTS_URL}/overview`);
    if (data) {
      request.flush(data);
    } else {
      request.flush(null, { status: 500, statusText: 'Server Error' });
    }
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('leads with one headline against the usual month, and the amount links to its transactions', async () => {
    const { el } = await render(overview());

    expect(text(el.querySelector('h1'))).toBe('We wrześniu wydatki były o 8% niższe niż zwykle');
    const amount = el.querySelector<HTMLAnchorElement>('.lead__spent')!;
    expect(plain(text(amount))).toBe('4 383,80 zł');
    expect(amount.getAttribute('href')).toBe('/overview/transactions?month=2026-09');
    expect(plain(text(el.querySelector('.lead__notes')))).toContain(
      'Zwykle 4 771,20 zł: mediana 5 poprzednich miesięcy.',
    );
  });

  it('compares a month in progress up to the same day', async () => {
    const { el } = await render(
      overview({
        month: '2026-10',
        isPartial: true,
        through: '2026-10-28',
        usual: { amount: 4788.06, months: 6, change: 0.0431, isUnusual: false, byDay: true },
        projected: 5269.97,
      }),
    );

    expect(text(el.querySelector('h1'))).toBe(
      'Do 28 października wydatki są o 4% wyższe niż zwykle o tej porze miesiąca',
    );
    expect(plain(text(el.querySelector('.lead__notes')))).toContain(
      'Szacunek na cały miesiąc: 5 269,97 zł.',
    );
  });

  it('shows the ranked insight cards, each leading to its numbers', async () => {
    const { el } = await render(
      overview({
        insights: [
          insight({ categoryId: GROCERIES, amount: 2400, usual: 1500, change: 0.6, months: 6 }),
          insight({ type: 'uncategorized', count: 3, amount: 166.08 }),
        ],
      }),
    );

    const cards = el.querySelectorAll('app-insight-card');
    expect(cards).toHaveLength(2);
    expect(text(cards[0].querySelector('h3'))).toBe('Wyższe wydatki niż zwykle');
    expect(text(cards[1].querySelector('h3'))).toBe('3 transakcje czekają na kategorię');
    expect(cards[1].querySelector('a')!.getAttribute('href')).toBe(
      '/overview/transactions?month=2026-09&category=none',
    );
  });

  it('says so when nothing stands out', async () => {
    const { el } = await render(overview());

    expect(text(el.querySelector('#insights-title + p'))).toBe(
      'Nic nietypowego w tym miesiącu: wydatki mieszczą się w zwykłych granicach.',
    );
  });

  it('explains that comparisons need three months of history', async () => {
    const { el } = await render(overview({ usual: null, history: [] }));

    expect(text(el.querySelector('h1'))).toBe('Wydatki we wrześniu');
    expect(text(el.querySelector('.lead__notes'))).toContain(
      'gdy historia obejmie 3 pełne miesiące',
    );
    expect(text(el.querySelector('#insights-title + p'))).toBe(
      'Wnioski pojawią się, gdy historia obejmie 3 pełne miesiące.',
    );
  });

  it('links every category to its page and the unclassified part to its transactions', async () => {
    const { el } = await render(overview());

    const links = Array.from(el.querySelectorAll('a.breakdown__row'), (a) =>
      a.getAttribute('href'),
    );
    expect(links).toEqual([
      `/overview/categories/${GROCERIES}?month=2026-09`,
      '/overview/transactions?month=2026-09&category=none',
    ]);
  });

  it('lists upcoming recurring payments', async () => {
    const { el } = await render(overview({ upcoming: [subscription()] }));

    const row = plain(text(el.querySelector('#upcoming-title + ul li')));
    expect(row).toContain('Netflix');
    expect(row).toContain('05.11.2026');
    expect(row).toContain('49,00 zł');
  });

  it('invites the first import when there is no data', async () => {
    const { el } = await render(overview({ hasData: false, month: null }));

    expect(text(el.querySelector('h1'))).toBe('Przegląd');
    expect(el.querySelector('a.button')!.getAttribute('href')).toBe('/import');
  });

  it('offers a retry when loading fails', async () => {
    const { fixture, el } = await render(null);

    expect(text(el.querySelector('[role="alert"]'))).toContain(
      'Nie udało się wczytać podsumowania',
    );
    el.querySelector<HTMLButtonElement>('button')!.click();
    http.expectOne(`${INSIGHTS_URL}/overview`).flush(overview());
    await fixture.whenStable();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });
});
