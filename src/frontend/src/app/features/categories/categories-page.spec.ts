import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CATEGORIES_URL } from '../../core/categories/categories.store';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { Snackbar } from '../../shared/ui/snackbar';
import { categoryList, OTHER, RESTAURANTS, text, translocoTesting } from '../transactions/testing';
import { RULES_URL } from '../transactions/transactions.api';
import { CategoriesPage } from './categories-page';

describe('CategoriesPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CategoriesPage, translocoTesting],
      providers: [provideLocale(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(CategoriesPage);
    http.expectOne(CATEGORIES_URL).flush(categoryList());
    TestBed.tick();
    http.expectOne(RULES_URL).flush([
      {
        id: 'r1',
        matchType: 'exact',
        pattern: 'zabka',
        label: 'zabka',
        categoryId: RESTAURANTS,
        createdAt: '',
      },
    ]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const row = (name: string) =>
      [...el.querySelectorAll('.row')].find((r) =>
        text(r.querySelector('.row__name')).startsWith(name),
      )!;
    const button = (scope: Element, label: string) =>
      [...scope.querySelectorAll<HTMLButtonElement>('button')].find((b) => text(b) === label)!;
    return { fixture, el, row, button };
  }

  it('lists top-level categories with their subcategories, and the rules', async () => {
    const { el } = await render();

    expect(text(el.querySelector('.children'))).toContain('Kawa');
    expect(text(el.querySelector('.rules__list'))).toBe(
      'zabka → Restauracje i jedzenie na wynos Usuń',
    );
  });

  it('renames a global category; an empty name brings back the default', async () => {
    const { fixture, row, button, el } = await render();

    button(row('Inne'), 'Zmień nazwę').click();
    await fixture.whenStable();
    const input = el.querySelector<HTMLInputElement>(`#rename-${OTHER}`)!;
    expect(input.value).toBe('Inne');
    input.value = '  Różne ';
    input.form!.requestSubmit();
    const put = http.expectOne({ method: 'PUT', url: `${CATEGORIES_URL}/${OTHER}` });
    expect(put.request.body).toEqual({ name: 'Różne', hidden: false });
    put.flush({ ...categoryList()[3], name: 'Różne' });
    await fixture.whenStable();

    expect(row('Różne')).toBeDefined();
  });

  it('hides a category and adds a subcategory', async () => {
    const { fixture, row, button, el } = await render();

    button(row('Inne'), 'Ukryj').click();
    http
      .expectOne({ method: 'PUT', url: `${CATEGORIES_URL}/${OTHER}` })
      .flush({ ...categoryList()[3], hidden: true });
    await fixture.whenStable();
    expect(text(row('Inne'))).toContain('ukryta');

    button(row('Restauracje'), 'Dodaj podkategorię').click();
    await fixture.whenStable();
    const input = el.querySelector<HTMLInputElement>(`#add-${RESTAURANTS}`)!;
    input.value = 'Lunch';
    input.form!.requestSubmit();
    const post = http.expectOne({ method: 'POST', url: CATEGORIES_URL });
    expect(post.request.body).toEqual({ parentId: RESTAURANTS, name: 'Lunch' });
    post.flush({ ...categoryList()[5], id: 'lunch', name: 'Lunch' });
    await fixture.whenStable();
    expect(text(el.querySelector('.children'))).toContain('Lunch');
  });

  it('deletes a rule and says how many transactions changed', async () => {
    const { fixture, el, button } = await render();

    button(el.querySelector('.rules__list')!, 'Usuń').click();
    http.expectOne({ method: 'DELETE', url: `${RULES_URL}/r1` }).flush({ changed: 5 });
    TestBed.tick();
    http.expectOne(RULES_URL).flush([]);
    await fixture.whenStable();

    expect(TestBed.inject(Snackbar).message()?.text).toBe(
      'Usunięto regułę. Zmieniono 5 transakcji.',
    );
    expect(text(el.querySelector('.rules'))).toContain('Nie masz jeszcze reguł.');
  });
});
