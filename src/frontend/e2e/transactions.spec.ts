import { expect, test } from '@playwright/test';
import { signIn } from './session';

const GROCERIES = '0000000c-0000-0000-0000-000000000001';
const RESTAURANTS = '0000000c-0000-0000-0000-000000000002';
const OTHER = '0000000c-0000-0000-0000-000000000014';

const categories = [
  { id: GROCERIES, key: 'groceries', sortOrder: 10 },
  { id: RESTAURANTS, key: 'restaurants', sortOrder: 20 },
  { id: OTHER, key: 'other', sortOrder: 140 },
].map((c) => ({ ...c, name: null, parentId: null, kind: 'spending', hidden: false, own: false }));

const zabka = {
  id: 'tx-1',
  date: '2026-10-28',
  status: 'booked',
  amount: -10.33,
  currency: 'PLN',
  kind: 'card',
  merchant: 'ZABKA Z5017 K.1 KRAKOW POL',
  merchantKey: 'zabka',
  categoryId: GROCERIES,
  source: 'globalRule',
  confidence: 0.95,
  needsReview: false,
  reason: 'merchantExact',
  reasonValue: 'zabka',
  offsetsCategoryId: null,
};

test('corrects a category in two taps on a phone and applies it to the merchant', async ({
  page,
}) => {
  await signIn(page);
  let corrected = false;
  let appliedToAll = false;
  await page.route('**/api/categories', (route) => route.fulfill({ json: categories }));
  await page.route('**/api/transactions?**', (route) =>
    route.fulfill({
      json: {
        items: [corrected ? { ...zabka, categoryId: RESTAURANTS } : zabka],
        nextOffset: null,
      },
    }),
  );
  await page.route('**/api/transactions/tx-1', (route) =>
    route.fulfill({
      json: {
        transaction: zabka,
        title: 'Płatność kartą 28.10.2026 Nr karty 4111xx1234',
        counterpartyRaw: ' ZABKA Z5017 K.1  KRAKOW  POL ',
        counterpartyAccount: null,
        counterpartyBank: null,
        accountName: 'KONTO Mobi pow.26',
        bookingDate: '2026-10-28',
        purchaseDate: '2026-10-28',
        originalAmount: null,
        originalCurrency: null,
        raw: ['2026-10-28'],
        suggestions: [RESTAURANTS, OTHER],
        applyToAll: null,
      },
    }),
  );
  await page.route('**/api/transactions/tx-1/category', (route) => {
    corrected = true;
    return route.fulfill({
      json: {
        transaction: { ...zabka, categoryId: RESTAURANTS, source: 'userCorrection', reason: null },
        applyToAll: { matchType: 'exact', label: 'zabka', count: 34 },
      },
    });
  });
  await page.route('**/api/transactions/tx-1/apply-to-all', (route) => {
    appliedToAll = true;
    return route.fulfill({ json: { rule: {}, changed: 34 } });
  });

  await page.goto('/transactions');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Transakcje');
  const row = page.getByRole('button', { name: /ZABKA/ });
  await expect(row).toContainText('Spożywcze');

  // Tap 1 opens the sheet; tap 2 is the right category's chip.
  await row.tap();
  const sheet = page.getByRole('dialog');
  const chip = sheet.getByRole('button', { name: 'Restauracje i jedzenie na wynos', exact: true });
  expect((await chip.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  await chip.tap();

  await expect(sheet).toBeHidden();
  await expect(row).toContainText('Restauracje i jedzenie na wynos');
  const apply = page.getByRole('button', { name: 'Zastosuj do wszystkich: „zabka” (34)' });
  await apply.tap();
  await expect(page.getByText('Zmieniono kategorie 34 transakcji.')).toBeVisible();
  expect(appliedToAll).toBe(true);
});
