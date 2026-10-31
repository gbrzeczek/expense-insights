import { expect, Page, test } from '@playwright/test';
import { signIn } from './session';

const GROCERIES = '0000000c-0000-0000-0000-000000000001';
const RESTAURANTS = '0000000c-0000-0000-0000-000000000002';
const BILLS = '0000000c-0000-0000-0000-000000000011';

const categories = [
  { id: GROCERIES, key: 'groceries', sortOrder: 10 },
  { id: RESTAURANTS, key: 'restaurants', sortOrder: 20 },
  { id: BILLS, key: 'bills', sortOrder: 110 },
].map((c) => ({ ...c, name: null, parentId: null, kind: 'spending', hidden: false, own: false }));

const blank = {
  merchantKey: null,
  merchant: null,
  transactionId: null,
  amount: null,
  usual: null,
  change: null,
  months: null,
  lastYear: null,
  soFar: null,
  count: null,
  usualCount: null,
  cadenceDays: null,
  yearly: null,
};

const overview = {
  hasData: true,
  month: '2026-09',
  currency: 'PLN',
  earliestMonth: '2026-03',
  latestMonth: '2026-10',
  dataEnd: '2026-10-28',
  isPartial: false,
  through: '2026-09-30',
  spent: 5383.8,
  income: 9998.25,
  usual: { amount: 4771.2, months: 6, change: 0.1284, isUnusual: true, byDay: false },
  projected: null,
  history: [
    { month: '2026-04', spent: 4612.73, isComplete: true },
    { month: '2026-05', spent: 4753.58, isComplete: true },
    { month: '2026-06', spent: 5067.34, isComplete: true },
    { month: '2026-07', spent: 4621.0, isComplete: true },
    { month: '2026-08', spent: 4837.02, isComplete: true },
    { month: '2026-09', spent: 5383.8, isComplete: true },
  ],
  categories: [
    { categoryId: GROCERIES, amount: 1715.51, usual: 1650.49 },
    { categoryId: RESTAURANTS, amount: 1095.24, usual: 398.86 },
    { categoryId: BILLS, amount: 884.7, usual: 888.74 },
    { categoryId: null, amount: 166.08, usual: null },
  ],
  insights: [
    {
      ...blank,
      type: 'categoryChange',
      score: 45,
      direction: 'up',
      categoryId: RESTAURANTS,
      amount: 1095.24,
      usual: 398.86,
      change: 1.7459,
      months: 6,
    },
    {
      ...blank,
      type: 'frequentSmall',
      score: 20,
      direction: 'up',
      categoryId: GROCERIES,
      merchantKey: 'zabka',
      merchant: 'Żabka',
      amount: 279.23,
      usual: 117.94,
      change: 1.3676,
      months: 6,
      count: 9,
      usualCount: 5,
    },
    {
      ...blank,
      type: 'uncategorized',
      score: 0,
      direction: 'up',
      amount: 166.08,
      count: 3,
    },
  ],
  upcoming: [],
};

const restaurants = {
  categoryId: RESTAURANTS,
  month: '2026-09',
  currency: 'PLN',
  earliestMonth: '2026-03',
  latestMonth: '2026-10',
  isPartial: false,
  spent: 1095.24,
  usual: 398.86,
  trend: overview.history.map((m) => ({
    ...m,
    spent: m.month === '2026-09' ? 1095.24 : 350 + m.spent / 100,
  })),
  merchants: [
    { merchantKey: 'pyszne', merchant: 'pyszne.pl', amount: 846.99, count: 7 },
    { merchantKey: 'starbucks', merchant: 'Starbucks', amount: 248.25, count: 6 },
  ],
};

const pyszne = {
  month: '2026-09',
  currency: 'PLN',
  total: 846.99,
  items: [108.45, 77.39, 61.15].map((amount, i) => ({
    counted: amount,
    transaction: {
      id: `tx-${i}`,
      date: `2026-09-${String(28 - i * 9).padStart(2, '0')}`,
      status: 'booked',
      amount: -amount,
      currency: 'PLN',
      kind: 'blikOnline',
      merchant: 'pyszne.pl',
      merchantKey: 'pyszne',
      categoryId: RESTAURANTS,
      source: 'globalRule',
      confidence: 0.95,
      needsReview: false,
      reason: 'merchantExact',
      reasonValue: 'pyszne',
      offsetsCategoryId: null,
    },
  })),
};

async function mockApi(page: Page): Promise<void> {
  await signIn(page);
  await page.route('**/api/categories', (route) => route.fulfill({ json: categories }));
  await page.route('**/api/insights/overview**', (route) => route.fulfill({ json: overview }));
  await page.route(`**/api/insights/categories/${RESTAURANTS}**`, (route) =>
    route.fulfill({ json: restaurants }),
  );
  await page.route('**/api/insights/transactions**', (route) => route.fulfill({ json: pyszne }));
}

async function noHorizontalScroll(page: Page): Promise<void> {
  const overflows = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(overflows).toBe(false);
}

for (const colorScheme of ['light', 'dark'] as const) {
  test(`summary → category → merchant → transactions on a phone (${colorScheme})`, async ({
    page,
  }, testInfo) => {
    await page.emulateMedia({ colorScheme });
    await mockApi(page);
    const shot = async (name: string) =>
      testInfo.attach(`${name}-${colorScheme}`, {
        body: await page.screenshot({ fullPage: true }),
        contentType: 'image/png',
      });

    await page.goto('/overview?month=2026-09');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      'We wrześniu wydatki były o 13% wyższe niż zwykle',
    );
    await expect(page.locator('app-insight-card')).toHaveCount(3);
    await noHorizontalScroll(page);
    await shot('overview');

    // Every tappable thing is at least 44 px tall.
    for (const link of await page.locator('.breakdown__row--link, .bars__column, .action').all()) {
      expect((await link.boundingBox())?.height).toBeGreaterThanOrEqual(44);
    }

    await page.getByRole('link', { name: /Restauracje i jedzenie na wynos/ }).tap();
    await expect(page).toHaveURL(
      new RegExp(`/overview/categories/${RESTAURANTS}\\?month=2026-09$`),
    );
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      'Restauracje i jedzenie na wynos',
    );
    await noHorizontalScroll(page);
    await shot('category');

    await page.getByRole('link', { name: /pyszne\.pl/ }).tap();
    await expect(page).toHaveURL(/merchant=pyszne/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      'pyszne.pl · Restauracje i jedzenie na wynos',
    );
    await expect(page.locator('app-transaction-row')).toHaveCount(3);
    await noHorizontalScroll(page);
    await shot('transactions');
  });
}
