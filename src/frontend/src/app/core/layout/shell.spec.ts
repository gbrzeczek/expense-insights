import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import pl from '../../../../public/i18n/pl.json';
import { Shell } from './shell';

describe('Shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        Shell,
        TranslocoTestingModule.forRoot({
          langs: { pl },
          translocoConfig: { availableLangs: ['pl'], defaultLang: 'pl' },
          preloadLangs: true,
        }),
      ],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the main sections in Polish', async () => {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    const labels = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.nav__label'),
      (el) => el.textContent?.trim(),
    );
    expect(labels).toEqual(['Przegląd', 'Transakcje', 'Kategorie', 'Import', 'Konto']);
  });

  it('labels the navigation landmark', async () => {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    const nav = (fixture.nativeElement as HTMLElement).querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('Nawigacja główna');
  });
});
