import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, extname } from 'node:path';
import { pathToFileURL } from 'node:url';
const workspace = resolve(process.argv[2] || '.');
const { chromium } = await import(pathToFileURL(resolve(workspace, 'qa/.local/browser-tools/node_modules/playwright/index.mjs')));
const artifacts = resolve(workspace, 'qa/.local/phone-review-results/pr137');
const build = resolve(artifacts, 'frontend/browser');
const card = (name, id) => ({ userId: id, fullName: name, location: { city: 'QA City' }, bucket: 'BALANCED', profilePhoto: '/qa-avatar.svg', photos: ['/qa-avatar.svg'], highlightedTiles: [], rating: { average: 100, count: 5, show: true }, likedAt: new Date().toISOString(), expiresInHours: 24 });
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
const results = [];
for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }]) {
  const context = await browser.newContext({ viewport, reducedMotion: 'reduce' });
  await context.addInitScript(() => localStorage.setItem('accessToken', 'synthetic-ui-fixture-token'));
  await context.route('**/*', async route => {
    const request = route.request(); const url = new URL(request.url());
    if (url.hostname !== '127.0.0.1') return route.abort();
    if (request.resourceType() === 'document') return route.fulfill({ path: resolve(build, 'index.csr.html'), contentType: 'text/html' });
    if (url.pathname === '/moments') return route.fulfill({ json: { dateUtc: '2026-10-08', count: 1, cards: [card('QA Visible Card', 2)], sparkBalance: 5, budget: { totalCap: 5, totalRemaining: 5, totalUsed: 0 } } });
    if (url.pathname === '/moments/liked-you') return route.fulfill({ json: { count: 1, cards: [card('QA Drawn Card', 3)] } });
    if (url.pathname === '/intake/dynamic/current') return route.fulfill({ json: { cycleId: 'qa-cycle', cycleStartUtc: '2026-10-08T00:00:00Z', answered: true, cycleEndUtc: '2026-12-31T23:59:59Z', questions: [], answers: { d1_battery: 'medium', d2_tone: 'calm', d3_role: 'copilot' } } });
    if (url.pathname === '/me/pending-tasks') return route.fulfill({ json: { tasks: [] } });
    if (url.pathname === '/coaching/current-summary') return route.fulfill({ status: 204 });
    if (url.pathname === '/me/feedback-prompt') return route.fulfill({ json: { hasPendingPrompt: false } });
    if (url.pathname === '/qa-avatar.svg') return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="240"><rect width="200" height="240" fill="#777"/></svg>' });
    const file = resolve(build, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(build + '\\') && !file.startsWith(build + '/')) return route.abort();
    if (['.js', '.css', '.ico', '.png', '.svg', '.mp4'].includes(extname(file))) {
      try { return route.fulfill({ body: readFileSync(file), contentType: ({ '.js': 'application/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.mp4': 'video/mp4' })[extname(file)] || 'application/octet-stream' }); } catch { return route.fulfill({ status: 404 }); }
    }
    return route.fulfill({ status: 200, json: {} });
  });
  const page = await context.newPage(); const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('http://127.0.0.1:5180/moments', { waitUntil: 'domcontentloaded' });
  for (const [tab, text] of [['Deck', 'QA Visible Card'], ['Drawn', 'QA Drawn Card']]) {
    try {
      if (tab === 'Drawn') {
        // Dismiss the real notification prompt through its user action; do not
        // force clicks through an overlay or suppress the prompt in CSS.
        const later = page.locator('.pushNudgeLater');
        try { await later.waitFor({ state: 'visible', timeout: 3000 }); await later.click(); } catch {}
        await page.getByRole('button', { name: 'Drawn', exact: true }).click();
      }
      await page.locator('.card').filter({ hasText: text }).first().waitFor({ state: 'visible', timeout: 20000 });
      const ratingElements = await page.locator('.flagBar').count();
      results.push({ viewport: viewport.width, tab, result: ratingElements === 0 ? 'passed' : 'failed', ratingElements, cardVisible: true });
    } catch (error) { results.push({ viewport: viewport.width, tab, result: 'blocked', reason: error.message.slice(0, 1000) }); }
    await page.screenshot({ path: resolve(artifacts, `mock-ui-${viewport.width}-${tab}.png`), fullPage: true });
  }
  results.push({ viewport: viewport.width, pageErrors: errors });
  await context.close();
}
await browser.close();
writeFileSync(resolve(artifacts, 'browser-results.json'), JSON.stringify({ source: 'PR137 dce8c89 production build; cache disabled for local build recovery', scope: 'Actual built browser UI with intercepted synthetic API payloads, including legacy rating fields. No real API/proxy/provider authentication validated.', results }, null, 2));
console.log(JSON.stringify(results, null, 2));
