import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { IMPORTS_URL } from './import.api';
import { ImportPage } from './import-page';
import { batch, text, translocoTesting } from './testing';

describe('ImportPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportPage, translocoTesting],
      providers: [provideLocale(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(ImportPage);
    // Not whenStable(): it would wait for the history request, which the test flushes.
    TestBed.tick();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  }

  function choose(el: HTMLElement, file: File) {
    const input = el.querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
  }

  it('shows earlier imports', async () => {
    const { fixture, el } = render();
    http.expectOne(IMPORTS_URL).flush([batch()]);
    await fixture.whenStable();

    expect(text(el.querySelector('.history__item'))).toContain(
      'Dodano 42, zaktualizowano 3, pominięto 120 duplikatów.',
    );
  });

  it('uploads the chosen file, shows progress, then the result', async () => {
    const { fixture, el } = render();
    http.expectOne(IMPORTS_URL).flush([]);
    await fixture.whenStable();
    expect(text(el.querySelector('.history'))).toContain('Nie ma jeszcze żadnych importów.');

    choose(el, new File(['x'], 'lista.csv', { type: 'text/csv' }));
    const upload = http.expectOne({ method: 'POST', url: IMPORTS_URL });
    expect(upload.request.body).toBeInstanceOf(FormData);
    upload.event({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
    TestBed.tick();
    expect(text(el.querySelector('.upload__name'))).toBe('Wysyłanie pliku lista.csv');
    expect(el.querySelector('progress')?.value).toBe(40);
    expect(el.querySelector('input')?.disabled).toBe(true);

    upload.flush(batch({ newCount: 1, updatedCount: 0, duplicateCount: 0 }));
    TestBed.tick();
    http.expectOne(IMPORTS_URL).flush([batch()]);
    await fixture.whenStable();
    expect(text(el.querySelector('app-import-result .result__headline'))).toBe(
      'Dodano 1, zaktualizowano 0, pominięto 0 duplikatów.',
    );
    expect(text(el.querySelector('.upload__pick'))).toBe('Wybierz kolejny plik');
  });

  it('explains a rejected file', async () => {
    const { fixture, el } = render();
    http.expectOne(IMPORTS_URL).flush([]);

    choose(el, new File(['x'], 'other.csv'));
    http
      .expectOne({ method: 'POST', url: IMPORTS_URL })
      .flush({ code: 'unsupportedFormat' }, { status: 422, statusText: 'Unprocessable Entity' });
    await fixture.whenStable();

    expect(text(el.querySelector('[role=alert]'))).toBe(
      'Nie rozpoznano pliku. Wgraj plik CSV wyeksportowany z ING Banku Śląskiego.',
    );
  });

  it('rejects files over 5 MB without uploading', async () => {
    const { fixture, el } = render();
    http.expectOne(IMPORTS_URL).flush([]);

    choose(el, new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'big.csv'));
    await fixture.whenStable();

    expect(text(el.querySelector('[role=alert]'))).toContain('Plik jest większy niż 5 MB.');
  });
});
