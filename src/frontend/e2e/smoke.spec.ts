import { expect, test } from '@playwright/test';

test('app shell works on a phone-sized screen', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveURL(/\/overview$/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Przegląd');
  await expect(page).toHaveTitle('Przegląd · Wydatki');

  const nav = page.getByRole('navigation', { name: 'Nawigacja główna' });
  const navBox = await nav.boundingBox();
  expect(navBox?.y).toBeGreaterThan(700); // bottom bar, not a side menu

  const links = nav.getByRole('link');
  await expect(links).toHaveText(['Przegląd', 'Transakcje', 'Kategorie', 'Import']);
  for (const box of await Promise.all((await links.all()).map((l) => l.boundingBox()))) {
    expect(box?.height).toBeGreaterThanOrEqual(44);
  }

  await nav.getByRole('link', { name: 'Transakcje' }).tap();
  await expect(page).toHaveURL(/\/transactions$/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Transakcje');
  await expect(nav.getByRole('link', { name: 'Transakcje' })).toHaveAttribute(
    'aria-current',
    'page',
  );

  const overflows = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(overflows).toBe(false);
});
