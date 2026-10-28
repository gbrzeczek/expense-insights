import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import pl from '../../../../public/i18n/pl.json';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { SummaryCard } from './summary-card';

describe('SummaryCard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        SummaryCard,
        TranslocoTestingModule.forRoot({
          langs: { pl },
          translocoConfig: { availableLangs: ['pl'], defaultLang: 'pl' },
          preloadLangs: true,
        }),
      ],
      providers: [provideLocale()],
    });
  });

  async function render(amount: number, previousAmount: number | null) {
    const fixture = TestBed.createComponent(SummaryCard);
    fixture.componentRef.setInput('label', 'Wydatki');
    fixture.componentRef.setInput('amount', amount);
    fixture.componentRef.setInput('previousAmount', previousAmount);
    fixture.componentRef.setInput('previousLabel', 'we wrześniu');
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    return el.querySelector('.summary__change')?.textContent?.replace(/\s+/g, ' ').trim();
  }

  it('describes a decrease in neutral words with the previous amount', async () => {
    expect(await render(4285.6, 4658.2)).toBe('o 8% mniej niż we wrześniu (4 658,20 zł)');
  });

  it('describes an increase', async () => {
    expect(await render(110, 100)).toBe('o 10% więcej niż we wrześniu (100,00 zł)');
  });

  it('says "the same" when the rounded change is zero', async () => {
    expect(await render(100.2, 100)).toMatch(/^tyle samo co we wrześniu/);
  });

  it('hides the comparison without a previous period', async () => {
    expect(await render(100, null)).toBeUndefined();
  });
});
