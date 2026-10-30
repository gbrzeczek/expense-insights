import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../shared/testing';
import {
  categoryList,
  COFFEE,
  GROCERIES,
  OTHER,
  RESTAURANTS,
} from '../../features/transactions/testing';
import { CATEGORIES_URL, CategoriesStore } from './categories.store';

describe('CategoriesStore', () => {
  let store: CategoriesStore;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [translocoTesting],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(CategoriesStore);
    http = TestBed.inject(HttpTestingController);
    const loading = store.load();
    http.expectOne(CATEGORIES_URL).flush(categoryList());
    await loading;
  });

  afterEach(() => http.verify());

  it('names global categories through i18n and own ones by their name', () => {
    expect(store.label(GROCERIES)).toBe('Spożywcze');
    expect(store.label(COFFEE)).toBe('Kawa');
    expect(store.label(COFFEE, true)).toBe('Restauracje i jedzenie na wynos › Kawa');
    expect(store.label('unknown')).toBe('');
  });

  it("prefers the user's rename of a global category", () => {
    store.upsert({ ...categoryList()[3], name: 'Różne' });

    expect(store.label(OTHER)).toBe('Różne');
  });

  it('loads once', async () => {
    await store.load();
    http.expectNone(CATEGORIES_URL);
  });

  it('groups subcategories under their parent and gives them its colour', () => {
    const restaurants = store.tree().find((g) => g.parent.id === RESTAURANTS);

    expect(restaurants?.children.map((c) => c.id)).toEqual([COFFEE]);
    expect(store.colorIndex(COFFEE)).toBe(store.colorIndex(RESTAURANTS));
    expect(store.colorIndex(null)).toBe(6);
  });
});
