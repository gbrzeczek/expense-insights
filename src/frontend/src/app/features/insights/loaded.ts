import { Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, map, Observable, of, startWith, switchMap } from 'rxjs';

export type Loaded<T> = { status: 'loading' } | { status: 'error' } | { status: 'ready'; data: T };

/**
 * Loads again whenever the route parameters change (or on retry), dropping a stale request. Call
 * it in an injection context (a field initializer).
 */
export function loadOn<P, T>(
  params: Observable<P>,
  retry: Observable<unknown>,
  load: (params: P) => Observable<T>,
): Signal<Loaded<T>> {
  return toSignal(
    combineLatest([params, retry.pipe(startWith(null))]).pipe(
      switchMap(([p]) =>
        load(p).pipe(
          map((data): Loaded<T> => ({ status: 'ready', data })),
          catchError(() => of<Loaded<T>>({ status: 'error' })),
          startWith<Loaded<T>>({ status: 'loading' }),
        ),
      ),
    ),
    { initialValue: { status: 'loading' } },
  );
}
