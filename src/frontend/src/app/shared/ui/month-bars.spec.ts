import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { MonthBars } from './month-bars';

describe('MonthBars', () => {
  it('scales bars from zero to the largest month or the usual line, with compact labels', async () => {
    TestBed.configureTestingModule({
      imports: [MonthBars],
      providers: [provideLocale(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(MonthBars);
    fixture.componentRef.setInput('bars', [
      {
        label: 'sie',
        description: 'sierpień',
        amount: 2500,
        link: '/overview',
        queryParams: { month: '2026-08' },
      },
      { label: 'wrz', description: 'wrzesień', amount: 820.4, selected: true },
    ]);
    fixture.componentRef.setInput('usual', 5000);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    const heights = Array.from(
      el.querySelectorAll<HTMLElement>('.bars__bar'),
      (b) => b.style.height,
    );
    const values = Array.from(el.querySelectorAll('.bars__value'), (v) =>
      v.textContent?.replace(/\s/g, ' '),
    );
    const columns = el.querySelectorAll('.bars__column');

    expect(heights[0]).toBe('50%');
    expect(el.querySelector<HTMLElement>('.bars__usual')!.style.bottom).toBe('100%');
    expect(values).toEqual(['2,5 tys.', '820']);
    expect(columns[0].getAttribute('aria-label')).toBe('sierpień');
    expect(columns[0].getAttribute('href')).toBe('/overview?month=2026-08');
    expect(columns[1].getAttribute('aria-current')).toBe('date');
  });
});
