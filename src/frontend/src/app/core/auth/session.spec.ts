import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LOGOUT_URL, ME_URL } from './auth.models';
import { Session } from './session';
import { me } from './testing';

describe('Session', () => {
  let http: HttpTestingController;
  let session: Session;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(Session);
  });

  afterEach(() => http.verify());

  it('asks the server once who is signed in', async () => {
    const first = session.ensureLoaded();
    const second = session.ensureLoaded();
    http.expectOne(ME_URL).flush(me({ role: 'owner' }));

    expect(await first).toEqual(me({ role: 'owner' }));
    expect(await second).toEqual(me({ role: 'owner' }));
    expect(await session.ensureLoaded()).toEqual(me({ role: 'owner' }));
    expect(session.isOwner()).toBe(true);
  });

  it('treats a 401 as signed out', async () => {
    const user = session.ensureLoaded();
    http.expectOne(ME_URL).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await user).toBeNull();
    expect(session.user()).toBeNull();
  });

  it('lets other errors through and asks again next time', async () => {
    const user = session.ensureLoaded();
    http.expectOne(ME_URL).flush(null, { status: 503, statusText: 'Unavailable' });
    await expect(user).rejects.toBeTruthy();
    expect(session.user()).toBeUndefined();

    const retry = session.ensureLoaded();
    http.expectOne(ME_URL).flush(me());
    expect(await retry).toEqual(me());
  });

  it('logs out on the server, then forgets the user', async () => {
    const loaded = session.ensureLoaded();
    http.expectOne(ME_URL).flush(me());
    await loaded;

    const logout = session.logout();
    http
      .expectOne({ method: 'POST', url: LOGOUT_URL })
      .flush(null, { status: 204, statusText: '' });
    await logout;

    expect(session.user()).toBeNull();
  });

  it('deletes the account with DELETE /api/me', async () => {
    const deletion = session.deleteAccount();
    http.expectOne({ method: 'DELETE', url: ME_URL }).flush(null, { status: 204, statusText: '' });
    await deletion;

    expect(session.user()).toBeNull();
  });
});
