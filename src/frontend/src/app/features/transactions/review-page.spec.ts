import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CATEGORIES_URL } from '../../core/categories/categories.store';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { ReviewPage } from './review-page';
import { TRANSACTIONS_URL } from './transactions.api';
import { categoryList, detail, GROCERIES, item, OTHER, text, translocoTesting } from './testing';

describe('ReviewPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ReviewPage, translocoTesting],
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

  async function render() {
    const fixture = TestBed.createComponent(ReviewPage);
    http.expectOne(CATEGORIES_URL).flush(categoryList());
    http
      .expectOne((r) => r.params.get('filter') === 'review')
      .flush({
        items: [
          item({ id: 'a', needsReview: true, reason: 'merchantSimilar', reasonValue: 'biedronka' }),
          item({
            id: 'b',
            needsReview: true,
            merchant: 'P.H.U. ANNA NOWAK',
            merchantKey: 'anna nowak',
          }),
        ],
        nextOffset: null,
      });
    TestBed.tick();
    http.expectOne(`${TRANSACTIONS_URL}/a`).flush(detail());
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it("confirms the guess as the user's choice, then shows the next one", async () => {
    const { fixture, el } = await render();
    expect(text(el.querySelector('[aria-live]'))).toBe('1 z 2');
    expect(text(el.querySelector('.item__category'))).toBe('Spożywcze');

    el.querySelector<HTMLButtonElement>('.item__confirm')!.click();
    const put = http.expectOne({ method: 'PUT', url: `${TRANSACTIONS_URL}/a/category` });
    expect(put.request.body).toEqual({ categoryId: GROCERIES });
    put.flush({ transaction: item({ id: 'a', source: 'userCorrection' }), applyToAll: null });
    TestBed.tick();
    http.expectOne(`${TRANSACTIONS_URL}/b`).flush(detail());
    await fixture.whenStable();

    expect(text(el.querySelector('[aria-live]'))).toBe('2 z 2');
    expect(text(el.querySelector('.item h2'))).toBe('P.H.U. ANNA NOWAK');
  });

  it('picks another category, or skips, until everything is reviewed', async () => {
    const { fixture, el } = await render();

    const other = [...el.querySelectorAll<HTMLButtonElement>('.chip')].find(
      (c) => text(c) === 'Inne',
    )!;
    other.click();
    http
      .expectOne({ method: 'PUT', url: `${TRANSACTIONS_URL}/a/category` })
      .flush({ transaction: item({ id: 'a', categoryId: OTHER }), applyToAll: null });
    TestBed.tick();
    http.expectOne(`${TRANSACTIONS_URL}/b`).flush(detail());
    await fixture.whenStable();
    el.querySelector<HTMLButtonElement>('.item__skip')!.click();
    await fixture.whenStable();

    expect(text(el.querySelector('.done'))).toContain('Wszystko sprawdzone.');
  });
});
