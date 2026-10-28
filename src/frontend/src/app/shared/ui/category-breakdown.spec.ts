import { TestBed } from '@angular/core/testing';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { CategoryBreakdown } from './category-breakdown';

describe('CategoryBreakdown', () => {
  it('sorts by amount, labels shares and scales bars to the largest category', async () => {
    TestBed.configureTestingModule({ imports: [CategoryBreakdown], providers: [provideLocale()] });
    const fixture = TestBed.createComponent(CategoryBreakdown);
    fixture.componentRef.setInput('categories', [
      { name: 'B', amount: 25, colorIndex: 2 },
      { name: 'A', amount: 75, colorIndex: 1 },
    ]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    const names = Array.from(el.querySelectorAll('.breakdown__name'), (n) => n.textContent);
    const shares = Array.from(el.querySelectorAll('.breakdown__share'), (n) => n.textContent);
    const widths = Array.from(
      el.querySelectorAll<HTMLElement>('.breakdown__bar'),
      (n) => n.style.width,
    );

    expect(names).toEqual(['A', 'B']);
    expect(shares).toEqual(['75%', '25%']);
    expect(widths[0]).toBe('100%');
    expect(parseFloat(widths[1])).toBeCloseTo(33.33, 2);
  });
});
