import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ME_URL } from './auth.models';
import { Session } from './session';
import { me } from './testing';
import { unauthorizedInterceptor } from './unauthorized.interceptor';

describe('unauthorizedInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([unauthorizedInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  it('sends the user to the login page when the session has ended', async () => {
    const session = TestBed.inject(Session);
    session.user.set(me());
    vi.spyOn(router, 'url', 'get').mockReturnValue('/import');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const request = firstValueFrom(client.get('/api/imports'));
    http.expectOne('/api/imports').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toBeTruthy();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/import' } });
    expect(session.user()).toBeNull();
  });

  it('leaves the session check itself to its caller', async () => {
    const navigate = vi.spyOn(router, 'navigate');

    const request = firstValueFrom(client.get(ME_URL));
    http.expectOne(ME_URL).flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toBeTruthy();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('ignores other errors', async () => {
    const navigate = vi.spyOn(router, 'navigate');

    const request = firstValueFrom(client.get('/api/imports'));
    http.expectOne('/api/imports').flush(null, { status: 403, statusText: 'Forbidden' });

    await expect(request).rejects.toBeTruthy();
    expect(navigate).not.toHaveBeenCalled();
  });
});
