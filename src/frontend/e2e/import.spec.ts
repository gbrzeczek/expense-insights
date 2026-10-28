import { expect, test } from '@playwright/test';
import { signIn } from './session';

const batch = {
  id: '01a1',
  bankId: 'ing-pl',
  fileName: 'Lista_transakcji_nr_0123456789_281026.csv',
  importedAt: '2026-10-28T11:40:00Z',
  periodStart: '2026-05-01',
  periodEnd: '2026-10-28',
  rowCount: 167,
  newCount: 42,
  updatedCount: 3,
  duplicateCount: 120,
  errorCount: 2,
  expiredCount: 0,
  totalsCheck: 'passed',
  balanceCheck: 'passed',
  warnings: [],
  errors: [
    { code: 'invalidAmount', lineNumber: 22, value: '-2,0x' },
    { code: 'unknownAccount', lineNumber: 31, account: 'Konto walutowe' },
  ],
  alreadyImported: false,
};

test('imports a statement on a phone-sized screen', async ({ page }) => {
  await signIn(page);
  let imported = false;
  await page.route('**/api/imports', async (route) => {
    if (route.request().method() === 'POST') {
      imported = true;
      await route.fulfill({ json: batch });
    } else {
      await route.fulfill({ json: imported ? [batch] : [] });
    }
  });

  await page.goto('/import');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Import');
  await expect(page.getByText('Nie ma jeszcze żadnych importów.')).toBeVisible();

  const picker = page.getByText('Wybierz plik CSV');
  expect((await picker.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  await page.locator('input[type=file]').setInputFiles({
    name: 'Lista_transakcji_nr_0123456789_281026.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from('fake'),
  });

  const result = page.locator('app-import-result');
  await expect(result).toContainText('Dodano 42, zaktualizowano 3, pominięto 120 duplikatów.');
  await expect(result).toContainText('Liczba i suma transakcji zgadzają się');

  const errors = result.getByText('Błędy w 2 wierszach');
  await expect(page.getByText('Wiersz 22: nieprawidłowa kwota „-2,0x”.')).toBeHidden();
  await errors.tap();
  await expect(page.getByText('Wiersz 22: nieprawidłowa kwota „-2,0x”.')).toBeVisible();

  await expect(page.locator('.history__item')).toHaveCount(1);

  const overflows = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(overflows).toBe(false);
});
