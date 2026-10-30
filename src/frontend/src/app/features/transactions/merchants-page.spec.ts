import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CATEGORIES_URL } from '../../core/categories/categories.store';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { Snackbar } from '../../shared/ui/snackbar';
import { MerchantsPage } from './merchants-page';
import { MERCHANTS_REVIEW_URL } from './transactions.api';
import { MerchantReview } from './transactions.models';
import { categoryList, GROCERIES, text, translocoTesting } from './testing';

const review: MerchantReview = {
  groups: [
    {
      merchantKey: 'biedronka',
      name: 'JMP S.A. BIEDRONKA',
      count: 412,
      total: -23140,
      currency: 'PLN',
      guess: null,
    },
    {
      merchantKey: 'anna nowak',
      name: 'P.H.U. ANNA NOWAK',
      count: 1,
      total: -63.09,
      currency: 'PLN',
      guess: null,
    },
  ],
  progress: [{ currency: 'PLN', categorized: 23400, total: 30000, share: 0.78 }],
};

describe('MerchantsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MerchantsPage, translocoTesting],
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
    const fixture = TestBed.createComponent(MerchantsPage);
    http.expectOne(CATEGORIES_URL).flush(categoryList());
    http.expectOne(MERCHANTS_REVIEW_URL).flush(review);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('shows the progress by amount and the merchants with their counts', async () => {
    const { el } = await render();

    expect(text(el.querySelector('.progress__headline'))).toBe('78% wydatków skategoryzowanych');
    expect(text(el.querySelector('.progress'))).toContain('23 400,00 zł z 30 000,00 zł');
    const heads = [...el.querySelectorAll('.group__head')].map(text);
    expect(heads[0]).toBe('JMP S.A. BIEDRONKA 412 transakcji · −23 140,00 zł');
    expect(heads[1]).toContain('1 transakcja');
  });

  it('labels a whole merchant with one tap', async () => {
    const { fixture, el } = await render();

    el.querySelector<HTMLButtonElement>('.group__head')!.click();
    await fixture.whenStable();
    [...el.querySelectorAll<HTMLButtonElement>('.chip')]
      .find((c) => text(c) === 'Spożywcze')!
      .click();
    const post = http.expectOne({ method: 'POST', url: '/api/merchants/category' });
    expect(post.request.body).toEqual({ merchantKey: 'biedronka', categoryId: GROCERIES });
    post.flush({ rule: {}, changed: 412 });
    http.expectOne(MERCHANTS_REVIEW_URL).flush({ ...review, groups: review.groups.slice(1) });
    await fixture.whenStable();

    expect(TestBed.inject(Snackbar).message()?.text).toBe(
      'Oznaczono 412 transakcji jako: Spożywcze.',
    );
    expect(el.querySelectorAll('.group')).toHaveLength(1);
  });
});
