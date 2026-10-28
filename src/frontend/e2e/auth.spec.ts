import { expect, Page, test } from '@playwright/test';
import { owner, signIn, user } from './session';

async function expectNoHorizontalScroll(page: Page) {
  const overflows = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(overflows).toBe(false);
}

test('signed-out visitors land on a Polish login page', async ({ page }) => {
  await signIn(page, null);

  await page.goto('/import');

  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fimport$/);
  await expect(page).toHaveTitle('Logowanie · Wydatki');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Wydatki');
  await expect(page.getByRole('navigation')).toHaveCount(0);
  const google = page.getByRole('link', { name: 'Zaloguj się przez Google' });
  await expect(google).toHaveAttribute('href', '/api/auth/login/google?returnUrl=%2Fimport');
  expect((await google.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  await expectNoHorizontalScroll(page);
});

test('an uninvited Google account sees "no access"', async ({ page }) => {
  await signIn(page, null);

  await page.goto('/login?error=noAccess');

  await expect(page.getByRole('alert')).toContainText('Brak dostępu');
  await expect(page.getByRole('link', { name: 'Zaloguj się innym kontem Google' })).toBeVisible();
});

test('an expired session goes back to the login page', async ({ page }) => {
  await signIn(page);
  await page.route('**/api/imports', (route) => route.fulfill({ status: 401, json: {} }));

  await page.goto('/import');

  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fimport$/);
});

test('signing out from the account page', async ({ page }) => {
  await signIn(page);
  let loggedOut = false;
  await page.route('**/api/auth/logout', async (route) => {
    loggedOut = route.request().method() === 'POST';
    await route.fulfill({ status: 204 });
  });

  await page.goto('/account');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Konto');
  await expect(page.getByText('anna@example.test')).toBeVisible();
  await expect(page.getByRole('link', { name: /Zaproszenia/ })).toHaveCount(0);
  await page.getByRole('button', { name: 'Wyloguj się' }).tap();

  await expect(page).toHaveURL(/\/login$/);
  expect(loggedOut).toBe(true);
});

test('deleting the account asks first', async ({ page }) => {
  await signIn(page, user);
  let deleted = false;
  await page.route('**/api/me', async (route) => {
    if (route.request().method() === 'DELETE') {
      deleted = true;
      await route.fulfill({ status: 204 });
    } else {
      await route.fulfill(deleted ? { status: 401, json: {} } : { json: user });
    }
  });

  await page.goto('/account');
  await page.getByRole('button', { name: 'Usuń konto' }).tap();
  await expect(page.getByText('Usunąć konto i wszystkie dane na zawsze?')).toBeVisible();
  expect(deleted).toBe(false);
  await page.getByRole('button', { name: 'Tak, usuń wszystko' }).tap();

  await expect(page).toHaveURL(/\/login$/);
  expect(deleted).toBe(true);
});

test('the owner invites someone', async ({ page }) => {
  await signIn(page, owner);
  const invitations = [
    {
      id: 'i-owner',
      email: owner.email,
      createdAt: '2026-10-28T10:00:00Z',
      acceptedAt: '2026-10-28T10:05:00Z',
      isOwner: true,
    },
  ];
  await page.route('**/api/invitations', async (route) => {
    if (route.request().method() === 'POST') {
      const { email } = route.request().postDataJSON() as { email: string };
      const invitation = {
        id: 'i-new',
        email,
        createdAt: '2026-10-28T12:00:00Z',
        acceptedAt: null,
        isOwner: false,
      };
      invitations.push(invitation);
      await route.fulfill({ status: 201, json: invitation });
    } else {
      await route.fulfill({ json: invitations });
    }
  });

  await page.goto('/account');
  await page.getByRole('link', { name: /Zaproszenia/ }).tap();
  await expect(page).toHaveURL(/\/invitations$/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Zaproszenia');

  await page.getByLabel('Adres e-mail').fill('piotr@example.test');
  await page.getByRole('button', { name: 'Zaproś' }).tap();

  await expect(page.getByText('Zaproszono piotr@example.test.')).toBeVisible();
  await expect(page.locator('.list__item')).toHaveCount(2);
  await expect(
    page.getByRole('button', { name: 'Cofnij zaproszenie dla piotr@example.test' }),
  ).toBeVisible();
  await expectNoHorizontalScroll(page);
});

test('invitations are for the owner only', async ({ page }) => {
  await signIn(page, user);

  await page.goto('/invitations');

  await expect(page).toHaveURL(/\/account$/);
});
