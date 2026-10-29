import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { provideApiClient } from './app.config';

describe('provideApiClient', () => {
  let http: HttpTestingController;
  let client: HttpClient;

  beforeEach(() => {
    document.cookie = 'XSRF-TOKEN=token-from-server; path=/';
    TestBed.configureTestingModule({
      providers: [provideApiClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => {
    http.verify();
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  it('echoes the XSRF cookie as a header on state-changing requests, like the upload', () => {
    void firstValueFrom(client.post('/api/imports', new FormData()));
    void firstValueFrom(client.delete('/api/me'));

    for (const request of http.match(() => true)) {
      expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('token-from-server');
      request.flush(null);
    }
  });

  it('does not send it on reads', () => {
    void firstValueFrom(client.get('/api/imports'));

    const request = http.expectOne('/api/imports');
    expect(request.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    request.flush([]);
  });
});
