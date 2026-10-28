// Renders the PWA icons and favicon.ico from the SVG sources in branding/.
// Run after changing an icon: npm run icons
import { readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import { chromium } from '@playwright/test';

const root = new URL('..', import.meta.url);
const path = (p) => new URL(p, root);

const targets = [
  { source: 'branding/icon.svg', out: 'public/icons/icon-192.png', size: 192 },
  { source: 'branding/icon.svg', out: 'public/icons/icon-512.png', size: 512 },
  { source: 'branding/icon-maskable.svg', out: 'public/icons/icon-maskable-512.png', size: 512 },
  // iOS applies its own rounded mask and ignores transparency, so it gets the full-bleed version.
  { source: 'branding/icon-maskable.svg', out: 'public/icons/apple-touch-icon.png', size: 180 },
];
const icoSizes = [16, 32, 48];

const browser = await chromium.launch();
const page = await browser.newPage();

async function render(source, size) {
  const svg = await readFile(path(source), 'utf8');
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<style>html,body{margin:0;background:transparent}</style>` +
      `<img src="data:image/svg+xml;base64,${Buffer.from(svg).toString('base64')}" width="${size}" height="${size}">`,
  );
  await page.locator('img').evaluate((img) => img.decode());
  return page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
}

/** ICO container with embedded PNGs (supported by every current browser). */
function toIco(images) {
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  images.forEach(({ size, png }, i) => {
    const entry = 6 + 16 * i;
    header.writeUInt8(size % 256, entry);
    header.writeUInt8(size % 256, entry + 1);
    header.writeUInt16LE(1, entry + 4); // colour planes
    header.writeUInt16LE(32, entry + 6); // bits per pixel
    header.writeUInt32LE(png.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += png.length;
  });
  return Buffer.concat([header, ...images.map((i) => i.png)]);
}

await rm(path('public/icons/'), { recursive: true, force: true });
await mkdir(path('public/icons/'), { recursive: true });
for (const { source, out, size } of targets) {
  await writeFile(path(out), await render(source, size));
  console.log(`${out} (${size}px)`);
}
const ico = [];
for (const size of icoSizes) {
  ico.push({ size, png: await render('branding/icon-small.svg', size) });
}
await writeFile(path('public/favicon.ico'), toIco(ico));
console.log(`public/favicon.ico (${icoSizes.join(', ')}px)`);

await browser.close();
