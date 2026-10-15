import { chromium } from '../.local/browser-tools/node_modules/playwright/index.mjs';
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const output = fileURLToPath(new URL('../.local/full-qa-runtime/', import.meta.url));
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
const results = [];
for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }]) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await context.route('**/*', route => {
    const url = new URL(route.request().url());
    return ['localhost', '127.0.0.1'].includes(url.hostname) || ['data:', 'blob:'].includes(url.protocol) ? route.continue() : route.abort();
  });
  const page = await context.newPage();
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  for (const path of ['/', '/login']) {
    const start = Date.now();
    const response = await page.goto(`http://127.0.0.1:5180${path}`, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(path === '/login' ? 15000 : 1200);
    const observation = await page.evaluate(() => ({
      title: document.title,
      horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
      text: document.body.innerText.slice(0, 600),
      controls: [...document.querySelectorAll('button,a,input')].filter(e => e.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true }) && e.getBoundingClientRect().width > 0).map(e => ({ tag: e.tagName, label: e.getAttribute('aria-label') || e.textContent?.trim() || e.getAttribute('placeholder'), width: Math.round(e.getBoundingClientRect().width), height: Math.round(e.getBoundingClientRect().height) })).slice(0, 25),
      loginCardVisible: !!document.querySelector('.card')?.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true }),
      consentKeyboardControl: !!document.querySelector('input[type=checkbox], [role=checkbox][tabindex]'),
    }));
    const name = `guest-${viewport.width}-${path === '/' ? 'landing' : 'login'}`;
    await page.screenshot({ path: `${output}/${name}.png`, fullPage: true });
    results.push({ scenario: name, status: response?.status(), durationMs: Date.now() - start, ...observation, pageErrors: [...errors], scope: 'Guest render only; external requests deliberately blocked; no provider authentication validated.' });
  }
  await context.close();
}
await browser.close();
writeFileSync(`${output}/browser-guest.json`, JSON.stringify(results, null, 2));
console.log(JSON.stringify(results.map(r => ({ scenario: r.scenario, status: r.status, overflow: r.horizontalOverflow, pageErrors: r.pageErrors, visibleControls: r.controls.length })), null, 2));
