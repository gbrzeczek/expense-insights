import { ChangeDetectionStrategy, Component, computed, input, isDevMode } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DEV_LOGIN_URL, googleLoginUrl, safeReturnUrl } from '../../core/auth/auth.models';

/** Errors the server redirects back with (`/login?error=…`). */
const LOGIN_ERRORS = ['noAccess', 'failed', 'unavailable'] as const;
type LoginError = (typeof LOGIN_ERRORS)[number];

/** Sign in with Google. Uninvited accounts come back here with `error=noAccess`. */
@Component({
  selector: 'app-login-page',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
})
export class LoginPage {
  /** Query parameters, bound by the router. */
  readonly error = input<string>();
  readonly returnUrl = input<string>();

  protected readonly returnTo = computed(() => safeReturnUrl(this.returnUrl()));
  protected readonly googleUrl = computed(() => googleLoginUrl(this.returnUrl()));
  protected readonly failure = computed<LoginError | null>(() => {
    const error = this.error();
    return error ? (LOGIN_ERRORS.find((known) => known === error) ?? 'failed') : null;
  });

  /** The fake login exists only on a Development backend, which only dev builds talk to. */
  protected readonly devLogin = isDevMode();
  protected readonly devLoginUrl = DEV_LOGIN_URL;
}
