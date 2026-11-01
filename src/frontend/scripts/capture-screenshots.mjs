// Captures the README screenshots into docs/screenshots/ from a running dev stack.
// Run after a visible UI change: npm run screenshots
//
// Fake data only. Point the API at a throwaway database, never the one with your own imports:
//
//   docker exec expense-insights-db-1 psql -U expense_insights -d postgres -c 'CREATE DATABASE expense_insights_screenshots'
//   ConnectionStrings__Default='Host=localhost;Port=5432;Database=expense_insights_screenshots;Username=expense_insights;Password=dev_only_password' \
//     Auth__OwnerEmail=anna.kowalska@example.com dotnet run --project src/backend/ExpenseInsights.Api
//   npm --prefix src/frontend start
//
// then sign in with the dev login, import test-data/fake_ing_history.csv on the Import page, and run
// this script. SCREENSHOT_EMAIL, SCREENSHOT_MONTH and BASE_URL override the defaults below.
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from '@playwright/test';

const base = process.env.BASE_URL ?? 'http://localhost:4200';
const email = process.env.SCREENSHOT_EMAIL ?? 'anna.kowalska@example.com';
// The last full month of fake_ing_history.csv, so every comparison has history behind it.
const month = process.env.SCREENSHOT_MONTH ?? '2026-10';
const out = new URL('../../../docs/screenshots/', import.meta.url);

const phone = { width: 390, height: 844 };
const schemes = ['light', 'dark'];
// Behind each phone in the hero image; a step darker than the app's own background.
const backdrop = { light: '#e6dccd', dark: '#15130f' };

/** Each shot opens `route` (or the result of `find` on the overview) and optionally scrolls to `scrollTo`. */
const shots = [
  { name: 'overview', route: `/overview?month=${month}` },
  {
    name: 'categories',
    route: `/overview?month=${month}`,
    scrollTo: (page) => page.getByRole('heading', { name: 'Kategorie', exact: true }),
  },
  { name: 'review', route: '/transactions/review' },
  { name: 'subscriptions', route: `/overview/subscriptions?month=${month}` },
  {
    name: 'category',
    find: (page) => page.locator('a[href*="/overview/categories/"]').first().getAttribute('href'),
  },
  { name: 'transactions', route: '/transactions' },
  { name: 'merchants', route: '/transactions/merchants' },
  { name: 'import', route: '/import' },
];
const hero = ['overview', 'categories', 'review', 'subscriptions'];
// Only the hero has a dark variant; the gallery stays light to keep the repository small.
const gallery = ['category', 'transactions', 'merchants', 'import'];

const browser = await chromium.launch();
await mkdir(out, { recursive: true });

async function settle(page) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(300);
}

const captured = { light: {}, dark: {} };
for (const scheme of schemes) {
  const context = await browser.newContext({
    viewport: phone,
    deviceScaleFactor: 2,
    isMobile: true,
    hasTouch: true,
    locale: 'pl-PL',
    colorScheme: scheme,
    reducedMotion: 'reduce',
  });
  const page = await context.newPage();
  await page.goto(
    `${base}/api/auth/dev-login?email=${encodeURIComponent(email)}&returnUrl=/overview`,
  );
  if (new URL(page.url()).pathname === '/login') {
    throw new Error(`Dev login refused ${email}: set SCREENSHOT_EMAIL to an invited address.`);
  }

  for (const shot of shots) {
    if (scheme === 'dark' && !hero.includes(shot.name)) continue;
    let route = shot.route;
    if (shot.find) {
      await page.goto(`${base}/overview?month=${month}`);
      await settle(page);
      route = await shot.find(page);
    }
    await page.goto(base + route);
    await settle(page);
    if (shot.scrollTo) {
      await shot.scrollTo(page).evaluate((el) => {
        el.scrollIntoView({ block: 'start' });
        window.scrollBy(0, -24);
      });
      await page.waitForTimeout(200);
    }
    captured[scheme][shot.name] = await page.screenshot();
    if (gallery.includes(shot.name)) {
      await writeFile(new URL(`${shot.name}.png`, out), captured[scheme][shot.name]);
      console.log(`docs/screenshots/${shot.name}.png`);
    }
  }
  await context.close();
}

// The hero: the four main screens side by side as phones, one image per colour scheme.
const width = 300;
const height = Math.round((width * phone.height) / phone.width);
const page = await browser.newPage({ deviceScaleFactor: 1.25 });
for (const scheme of schemes) {
  const phones = hero
    .map((name) => `<img src="data:image/png;base64,${captured[scheme][name].toString('base64')}">`)
    .join('');
  await page.setContent(
    `<style>
      html, body { margin: 0; background: transparent; }
      .panel { display: inline-flex; gap: 40px; padding: 56px 64px; border-radius: 28px; background: ${backdrop[scheme]}; }
      img { width: ${width}px; height: ${height}px; border-radius: 28px; box-shadow: 0 12px 32px rgb(0 0 0 / 18%); }
    </style><div class="panel">${phones}</div>`,
  );
  await page.locator('img').evaluateAll((imgs) => Promise.all(imgs.map((img) => img.decode())));
  await writeFile(
    new URL(`hero-${scheme}.png`, out),
    await page.locator('.panel').screenshot({ omitBackground: true }),
  );
  console.log(`docs/screenshots/hero-${scheme}.png`);
}

await browser.close();
