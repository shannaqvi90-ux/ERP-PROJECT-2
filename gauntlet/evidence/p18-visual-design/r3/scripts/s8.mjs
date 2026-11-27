// s8: the home sparkline's requests: what does the contacts trend ask and get?
import { browser, signIn, BASE } from './lib.mjs';
const b = await browser();
const page = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
const seen = [];
page.on('response', async r => { if (/createdAt/.test(decodeURIComponent(r.url()))) seen.push(r.status() + ' ' + decodeURIComponent(r.url()).replace(BASE, '').slice(0, 160) + ' ' + (await r.text().catch(() => '')).slice(0, 120)); });
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/'); await page.waitForTimeout(4000);
console.log(seen.length); console.log(seen.slice(-3).join('\n'));
console.log('sparklines', await page.locator('svg.sparkline').count());
await b.close();
