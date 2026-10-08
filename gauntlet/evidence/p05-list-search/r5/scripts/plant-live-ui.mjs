// Critic p05 r5: plant L9 on screen: gulfsteel's users list after alnoor ran the same search; End jumps with skip.
import { chromium } from '/home/shan/critic/p05-list-search-r5/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:20570', H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
const urls = []; page.on('request', r => { if (r.url().includes('/api/identity/users?')) urls.push(r.url().replace(base, '')); });
await page.goto(base + '/'); await page.locator('input[type=email], input[name=email]').first().fill('admin@gulfsteel.example'); await page.locator('input[type=password]').fill('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForLoadState('networkidle'); await page.goto(base + '/identity/users'); await page.waitForTimeout(1500);
await page.keyboard.type('a'); await page.waitForTimeout(1500);
const cnt = async () => ((await page.locator('main').innerText()).match(/(\d[\d,]*) users/) || ['?'])[0];
console.log('gulfsteel typed a:', await cnt());
// alnoor runs the same search (another tenant, another browser, same moment)
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
console.log('alnoor same search total', (await (await fetch(base + '/api/identity/users?take=100&search=a', { headers: { ...H, cookie } })).json()).total);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('End'); await page.waitForTimeout(2500);
console.log('gulfsteel after End:', await cnt(), '| requests', urls.slice(-3).map(decodeURIComponent).join(' ; '));
await page.screenshot({ path: '/home/shan/evidence-staging/p05-list-search/r5/plants/plant-L9-on-screen.jpg', type: 'jpeg', quality: 60 });
await browser.close();
