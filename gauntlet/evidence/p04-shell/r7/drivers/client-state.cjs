// Critic p04 r7: what B leaves on a shared device for A (storage, palette recent, history, title).
const { chromium } = require('/home/shan/critic/p04-shell-r7/gauntlet/compare/node_modules/playwright-core');
const BASE = 'http://localhost:20450', PW = 'Demo-Pass-2026';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const ctx = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 768 } });
  const page = await ctx.newPage();
  const signIn = async (email) => { await page.goto(BASE + '/'); await page.locator('input[name="email"]:focus').waitFor(); await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(PW); await page.keyboard.press('Enter'); await page.locator('main').waitFor(); await page.waitForLoadState('networkidle'); await sleep(400); };
  const dump = () => page.evaluate(() => ({ ls: Object.fromEntries(Object.keys(localStorage).map((k) => [k, localStorage.getItem(k).slice(0, 200)])), ss: Object.fromEntries(Object.keys(sessionStorage).map((k) => [k, sessionStorage.getItem(k).slice(0, 200)])), name: window.name.slice(0, 200), title: document.title, histLen: history.length }));
  await signIn('admin@gulfsteel.example');
  // B: use palette to open a user record and switch to Arabic digits, search users.
  await page.keyboard.press('Control+k'); await page.keyboard.type('Mariam'); await sleep(900); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await sleep(1000);
  await page.goto(BASE + '/identity/users?q=gulfsteel'); await sleep(800);
  await page.keyboard.press('Alt+l'); await sleep(600);
  console.log('B state', JSON.stringify(await dump()));
  // Sign out with the button.
  await page.locator('button:has-text("تسجيل الخروج"), button:has-text("Sign out")').first().click(); await page.locator('input[name="email"]').waitFor(); await sleep(400);
  console.log('after B sign-out', JSON.stringify(await dump()), 'dir', await page.evaluate(() => document.documentElement.dir));
  await signIn('admin@alnoor.example');
  console.log('A state', JSON.stringify(await dump()));
  await page.keyboard.press('Control+k'); await sleep(800);
  const pal = await page.evaluate(() => document.querySelector('[role=dialog]')?.innerText || '');
  console.log('A palette mentions gulf?', /gulf/i.test(pal), 'B user ids?');
  await page.keyboard.press('Escape');
  // Back through history.
  const seen = [];
  for (let i = 0; i < 6; i++) { await page.goBack().catch(() => {}); await sleep(500); seen.push(await page.evaluate(() => location.pathname + location.search + ' | ' + (document.querySelector('main')?.innerText || '').slice(0, 80).replace(/\s+/g, ' '))); }
  console.log('A back history', JSON.stringify(seen), 'leak', seen.some((s) => /gulf/i.test(s)));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
