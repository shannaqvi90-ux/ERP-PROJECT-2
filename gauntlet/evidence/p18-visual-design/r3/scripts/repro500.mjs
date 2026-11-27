// repro500: sign in, and leave the home dashboard at once for the contacts list (as a fast user or the comparison harness does);
// count 5xx answers. Each round is a fresh context (a fresh sign-in).
import { browser, BASE } from './lib.mjs';
const b = await browser();
const rounds = +(process.env.ROUNDS || 20), leaveMs = +(process.env.LEAVE || 50);
let fivexx = [], aborted = 0;
for (let i = 0; i < rounds; i++) {
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  page.on('response', r => { if (r.status() >= 500) fivexx.push(`${i} ${r.status()} ${r.request().method()} ${r.url().replace(BASE, '').slice(0, 120)}`); });
  page.on('requestfailed', () => aborted++);
  await page.goto(BASE + '/'); await page.waitForSelector('input[name=email]');
  await page.fill('input[name=email]', 'admin@alnoor.example'); await page.keyboard.press('Enter');
  await page.waitForSelector('input[name=password]'); await page.fill('input[name=password]', 'Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForFunction(() => !document.querySelector('input[name=password]'), null, { timeout: 30000 });
  await page.waitForTimeout(leaveMs);
  await page.keyboard.press('g'); await page.keyboard.press('o');
  await page.waitForTimeout(2500);
  const msg = await page.evaluate(() => document.querySelector('main')?.innerText.match(/could not be loaded[^\n]*/)?.[0] || '');
  if (msg) fivexx.push(`${i} screen: ${msg}`);
  await ctx.close();
  await new Promise(r => setTimeout(r, 2600)); // stay under the 30 sign-ins a minute
}
console.log('rounds', rounds, 'aborted requests', aborted, '5xx', fivexx.length); console.log(fivexx.join('\n'));
await b.close();
