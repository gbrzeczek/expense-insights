import { TranslocoTestingModule } from '@jsverse/transloco';
import pl from '../../../public/i18n/pl.json';

/** Transloco with the real Polish strings, so tests assert on what users see. */
export const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { pl },
  translocoConfig: { availableLangs: ['pl'], defaultLang: 'pl' },
  preloadLangs: true,
});

export function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}
