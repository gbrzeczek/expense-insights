import { Page } from '@playwright/test';

export interface FakeUser {
  id: string;
  email: string;
  displayName: string | null;
  role: 'owner' | 'user';
}

export const user: FakeUser = {
  id: '01a1-user',
  email: 'anna@example.test',
  displayName: 'Anna Nowak',
  role: 'user',
};

export const owner: FakeUser = {
  id: '00000000-0000-0000-0000-000000000001',
  email: 'owner@example.test',
  displayName: 'Jan Kowalski',
  role: 'owner',
};

/**
 * Fakes the session check (`GET /api/me`): the e2e tests run against `ng serve` only. A real sign-in
 * against a Development backend goes through `/api/auth/dev-login`.
 */
export async function signIn(page: Page, as: FakeUser | null = user): Promise<void> {
  await page.route('**/api/me', (route) =>
    as
      ? route.fulfill({ json: as })
      : route.fulfill({
          status: 401,
          contentType: 'application/problem+json',
          json: { status: 401 },
        }),
  );
}
