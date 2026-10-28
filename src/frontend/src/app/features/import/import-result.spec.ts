import { TestBed } from '@angular/core/testing';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { ImportBatch } from './import.models';
import { ImportResult } from './import-result';
import { batch, issue, text, translocoTesting } from './testing';

describe('ImportResult', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportResult, translocoTesting],
      providers: [provideLocale()],
    });
  });

  async function render(value: ImportBatch): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(ImportResult);
    fixture.componentRef.setInput('batch', value);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('summarises the counts with Polish plurals', async () => {
    const el = await render(batch());
    expect(text(el.querySelector('.result__headline'))).toBe(
      'Dodano 42, zaktualizowano 3, pominięto 120 duplikatów.',
    );
    expect(text(el.querySelector('.result__meta'))).toContain('okres 01.05.2026–28.10.2026');

    const few = await render(batch({ duplicateCount: 2 }));
    expect(text(few.querySelector('.result__headline'))).toContain('pominięto 2 duplikaty.');
  });

  it('states the checks in words', async () => {
    const el = await render(batch({ balanceCheck: 'failed' }));
    expect(Array.from(el.querySelectorAll('.result__check'), text)).toEqual([
      'Liczba i suma transakcji zgadzają się z podsumowaniem wyciągu.',
      'Salda po niektórych transakcjach się nie zgadzają.',
    ]);
  });

  it('lists warnings, with dates and amounts formatted for Poland', async () => {
    const el = await render(
      batch({
        warnings: [
          { ...issue, code: 'periodGap', from: '2026-09-03', to: '2026-09-10' },
          {
            ...issue,
            code: 'totalsSumMismatch',
            value: 'debits',
            expected: 1234.5,
            actual: 1200,
          },
        ],
      }),
    );
    expect(Array.from(el.querySelectorAll('.result__section li'), text)).toEqual([
      'Brak danych z okresu 03.09–10.09. Wgraj wyciąg obejmujący te dni.',
      // text() folds the non-breaking spaces of the money format.
      'Suma obciążeń: wyciąg podaje 1 234,50 zł, wczytano 1 200,00 zł.',
    ]);
  });

  it('keeps row errors in an expandable list', async () => {
    const el = await render(
      batch({
        errorCount: 2,
        errors: [
          { ...issue, code: 'invalidAmount', lineNumber: 22, value: '-2,0x' },
          { ...issue, code: 'somethingNew', lineNumber: 40 },
        ],
      }),
    );
    const details = el.querySelector('details');
    expect(details?.open).toBe(false);
    expect(text(details?.querySelector('summary'))).toBe('Błędy w 2 wierszach');
    expect(Array.from(details?.querySelectorAll('li') ?? [], text)).toEqual([
      'Wiersz 22: nieprawidłowa kwota „-2,0x”.',
      'Wiersz 40: nie udało się odczytać wiersza.',
    ]);
  });

  it('says when the file was imported before', async () => {
    const el = await render(batch({ alreadyImported: true }));
    expect(text(el.querySelector('.result__headline'))).toBe(
      'Ten plik został już zaimportowany 28.10.2026.',
    );
    expect(el.querySelector('.result__checks')).toBeNull();
  });
});
