import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LOGOUT_URL, Me, ME_URL } from './auth.models';

/**
 * The signed-in user. The session itself is an HttpOnly cookie that JavaScript never sees; this
 * only knows who `GET /api/me` says is signed in. That call also issues the XSRF token.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly http = inject(HttpClient);
  private loading: Promise<Me | null> | null = null;

  /** `undefined` until the first check, `null` when signed out. */
  readonly user = signal<Me | null | undefined>(undefined);
  readonly isOwner = computed(() => this.user()?.role === 'owner');

  /** The user, checking with the server once; later calls reuse the answer. */
  ensureLoaded(): Promise<Me | null> {
    const known = this.user();
    if (known !== undefined) {
      return Promise.resolve(known);
    }
    this.loading ??= firstValueFrom(this.http.get<Me>(ME_URL))
      .then(
        (me) => me,
        (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 401) {
            return null;
          }
          throw error;
        },
      )
      .then((me) => {
        this.user.set(me);
        return me;
      })
      .finally(() => (this.loading = null));
    return this.loading;
  }

  /** Forgets the user locally, e.g. after a 401 from an expired session. */
  clear(): void {
    this.user.set(null);
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post(LOGOUT_URL, null));
    this.clear();
  }

  /** Deletes the account and all its data; the server ends the session. */
  async deleteAccount(): Promise<void> {
    await firstValueFrom(this.http.delete(ME_URL));
    this.clear();
  }
}
