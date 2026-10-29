import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ME_URL } from './auth.models';
import { Session } from './session';

/**
 * A 401 from the API means the session ended (expired, logged out elsewhere, account deleted):
 * back to the login page, returning to the current page afterwards. `GET /api/me` is the session
 * check itself, so its 401 is handled by the caller.
 */
export const unauthorizedInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const session = inject(Session);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        request.url.startsWith('/api/') &&
        !(request.url === ME_URL && request.method === 'GET')
      ) {
        session.clear();
        const returnUrl = router.url.startsWith('/login') ? null : router.url;
        void router.navigate(['/login'], { queryParams: returnUrl ? { returnUrl } : {} });
      }
      return throwError(() => error);
    }),
  );
};
