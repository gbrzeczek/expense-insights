import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Session } from './session';

/** Signed-in users only; everyone else goes to the login page and comes back afterwards. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const user = await inject(Session).ensureLoaded();
  return user ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** The login page is for signed-out visitors. */
export const guestGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const user = await inject(Session).ensureLoaded();
  return user ? router.createUrlTree(['/']) : true;
};

/** Owner-only screens (invitations). The API enforces this too. */
export const ownerGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const user = await inject(Session).ensureLoaded();
  return user?.role === 'owner' ? true : router.createUrlTree(['/account']);
};
