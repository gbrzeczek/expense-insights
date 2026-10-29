import {
  provideHttpClient,
  withInterceptors,
  withXhr,
  withXsrfConfiguration,
} from '@angular/common/http';
import {
  ApplicationConfig,
  EnvironmentProviders,
  isDevMode,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, TitleStrategy, withComponentInputBinding } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { routes } from './app.routes';
import { unauthorizedInterceptor } from './core/auth/unauthorized.interceptor';
import { provideI18n } from './core/i18n/i18n.providers';
import { TranslatedTitleStrategy } from './core/i18n/translated-title-strategy';

/**
 * HttpClient as the app uses it. XHR rather than fetch: only XHR reports upload progress (the
 * statement import). Angular's XSRF support sends the server's `XSRF-TOKEN` cookie back as the
 * `X-XSRF-TOKEN` header on every state-changing same-origin request.
 */
export function provideApiClient(): EnvironmentProviders {
  return provideHttpClient(
    withXhr(),
    withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    withInterceptors([unauthorizedInterceptor]),
  );
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideApiClient(),
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    provideI18n(),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
