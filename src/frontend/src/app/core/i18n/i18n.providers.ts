import { registerLocaleData } from '@angular/common';
import localePl from '@angular/common/locales/pl';
import {
  DEFAULT_CURRENCY_CODE,
  EnvironmentProviders,
  inject,
  isDevMode,
  LOCALE_ID,
  Provider,
  provideAppInitializer,
} from '@angular/core';
import { provideTransloco, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { TranslocoHttpLoader } from './transloco-loader';

export const DEFAULT_LANG = 'pl';
export const APP_LOCALE = 'pl-PL';

registerLocaleData(localePl, APP_LOCALE);

/** Locale for number, currency and date formatting. */
export function provideLocale(): Provider[] {
  return [
    { provide: LOCALE_ID, useValue: APP_LOCALE },
    { provide: DEFAULT_CURRENCY_CODE, useValue: 'PLN' },
  ];
}

export function provideI18n(): (Provider | EnvironmentProviders)[] {
  return [
    provideLocale(),
    provideTransloco({
      config: {
        availableLangs: ['pl', 'en'],
        defaultLang: DEFAULT_LANG,
        fallbackLang: DEFAULT_LANG,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
        missingHandler: { logMissingKey: isDevMode() },
      },
      loader: TranslocoHttpLoader,
    }),
    // Load the default language before the first render so nothing flashes untranslated.
    provideAppInitializer(() => firstValueFrom(inject(TranslocoService).load(DEFAULT_LANG))),
  ];
}
