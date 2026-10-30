import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CategoriesStore } from '../../core/categories/categories.store';
import {
  categoryList,
  COFFEE,
  GROCERIES,
  HEALTH,
  OTHER,
  RESTAURANTS,
} from '../../features/transactions/testing';
import { text, translocoTesting } from '../testing';
import { CategoryPicker } from './category-picker';

describe('CategoryPicker', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CategoryPicker, translocoTesting],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const store = TestBed.inject(CategoriesStore);
    categoryList().forEach((c) => store.upsert(c));
  });

  async function render(suggestions: string[], current: string | null = null) {
    const fixture = TestBed.createComponent(CategoryPicker);
    fixture.componentRef.setInput('suggestions', suggestions);
    fixture.componentRef.setInput('current', current);
    const picked: string[] = [];
    fixture.componentInstance.picked.subscribe((id) => picked.push(id));
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const chips = () => [...el.querySelectorAll<HTMLButtonElement>('.chip')];
    return { fixture, el, chips, picked };
  }

  it('shows the suggestions as chips; one tap picks', async () => {
    const { chips, picked } = await render([RESTAURANTS, OTHER, HEALTH]);

    expect(chips().map(text)).toEqual([
      'Restauracje i jedzenie na wynos',
      'Inne',
      'Zdrowie',
      'Więcej…',
    ]);
    chips()[1].click();
    expect(picked).toEqual([OTHER]);
  });

  it('marks the current category as the first chip', async () => {
    const { chips } = await render([RESTAURANTS], GROCERIES);

    expect(text(chips()[0])).toBe('Spożywcze');
    expect(chips()[0].getAttribute('aria-pressed')).toBe('true');
  });

  it('lists every category under "Więcej…", subcategories under their parent', async () => {
    const { fixture, el, chips, picked } = await render([]);

    chips().at(-1)!.click();
    await fixture.whenStable();

    const options = [...el.querySelectorAll<HTMLButtonElement>('.option')];
    expect(options.map(text)).toContain('Kawa');
    expect(options.findIndex((o) => text(o) === 'Kawa')).toBe(
      options.findIndex((o) => text(o) === 'Restauracje i jedzenie na wynos') + 1,
    );
    options.find((o) => text(o) === 'Kawa')!.click();
    expect(picked).toEqual([COFFEE]);
  });
});
