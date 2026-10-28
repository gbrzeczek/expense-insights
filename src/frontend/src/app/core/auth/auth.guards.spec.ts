import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authGuard, guestGuard, ownerGuard } from './auth.guards';
import { ME_URL } from './auth.models';
import { me } from './testing';

@Component({ template: '' })
class Blank {}

describe('auth guards', () => {
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'login', component: Blank, canActivate: [guestGuard] },
          { path: 'account', component: Blank, canActivate: [authGuard] },
          { path: 'import', component: Blank, canActivate: [authGuard] },
          { path: 'invitations', component: Blank, canActivate: [authGuard, ownerGuard] },
          { path: '', component: Blank },
        ]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  async function navigate(url: string, user: ReturnType<typeof me> | null): Promise<string> {
    const navigation = router.navigateByUrl(url);
    const request = await vi.waitFor(() => http.expectOne(ME_URL));
    if (user) {
      request.flush(user);
    } else {
      request.flush(null, { status: 401, statusText: 'Unauthorized' });
    }
    await navigation;
    return router.url;
  }

  it('sends signed-out visitors to the login page, remembering where they were going', async () => {
    expect(await navigate('/import?x=1', null)).toBe('/login?returnUrl=%2Fimport%3Fx%3D1');
  });

  it('lets signed-in users in', async () => {
    expect(await navigate('/import', me())).toBe('/import');
  });

  it('sends signed-in users away from the login page', async () => {
    expect(await navigate('/login', me())).toBe('/');
  });

  it('keeps invitations to the owner', async () => {
    expect(await navigate('/invitations', me())).toBe('/account');
  });

  it('opens invitations for the owner', async () => {
    expect(await navigate('/invitations', me({ role: 'owner' }))).toBe('/invitations');
  });
});
