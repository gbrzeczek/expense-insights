/** Mirrors `MeResponse` from the API (`GET /api/me`). */
export interface Me {
  id: string;
  email: string | null;
  displayName: string | null;
  role: UserRole;
}

export type UserRole = 'owner' | 'user';

export const ME_URL = '/api/me';
export const LOGOUT_URL = '/api/auth/logout';
export const GOOGLE_LOGIN_URL = '/api/auth/login/google';
export const DEV_LOGIN_URL = '/api/auth/dev-login';

/** Only same-site paths, never `//host`: the server checks too. */
export function safeReturnUrl(url: string | null | undefined): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\') ? url : '/';
}

/** A full-page navigation to Google (the sign-in ends in a redirect back here). */
export function googleLoginUrl(returnUrl: string | null | undefined): string {
  return `${GOOGLE_LOGIN_URL}?returnUrl=${encodeURIComponent(safeReturnUrl(returnUrl))}`;
}
