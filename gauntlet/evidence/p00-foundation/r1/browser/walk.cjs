// Critic's own browser walk of the p00 scope (Chromium, keyboard first). Run: NODE_PATH=<global node_modules> node walk.cjs
const { chromium } = require('playwright');
const BASE = process.env.BASE || 'http://localhost:20050';
const OUT = process.env.OUT || '.';
const PW = 'Demo-Pass-2026';
const log = (...a) => console.log(...a);
(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const findings = [];
  // 1. English sign-in by keyboard only, fresh device
  let ctx = await browser.newContext({ viewport: { width: 1366, height: 800 }, locale: 'en-US' });
  let page = await ctx.newPage();
  const consoleErrors = [];
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  log('EN first screen: lang=%s dir=%s focused=%s', await page.getAttribute('html', 'lang'), await page.getAttribute('html', 'dir'), await page.evaluate(() => document.activeElement?.getAttribute('name')));
  await page.screenshot({ path: OUT + '/01-signin-en.jpg', type: 'jpeg', quality: 70 });
  let keys = 0; const t0 = Date.now();
  const email = 'admin@alnoor.example';
  await page.keyboard.type(email); keys += email.length;
  await page.keyboard.press('Tab'); keys++;
  await page.keyboard.type(PW); keys += PW.length;
  await page.keyboard.press('Enter'); keys++;
  await page.getByRole('heading', { name: /Welcome/ }).waitFor();
  const signInMs = Date.now() - t0;
  log('EN sign-in: keystrokes=%d steps=4 (type email, Tab, type password, Enter) machine_ms=%d', keys, signInMs);
  await page.screenshot({ path: OUT + '/02-shell-en.jpg', type: 'jpeg', quality: 70 });
  // keyboard navigation: Tab order through top bar
  const order = [];
  await page.keyboard.press('Home');
  for (let i = 0; i < 9; i++) { await page.keyboard.press('Tab'); order.push(await page.evaluate(() => (document.activeElement?.textContent || document.activeElement?.tagName || '').trim().slice(0, 30))); }
  log('Tab order in shell:', JSON.stringify(order));
  // go to Users by keyboard: find link and press Enter via focus
  await page.locator('nav.topnav').getByRole('link', { name: 'Users' }).focus();
  await page.keyboard.press('Enter');
  await page.getByRole('heading', { name: 'Users' }).waitFor();
  await page.waitForSelector('table.grid tbody tr');
  log('Users count text:', await page.locator('.screen-header .muted').textContent());
  // search one record among 100,000
  const needle = 'abdullah.nasser.70155'; const t1 = Date.now();
  await page.locator('input[type=search]').focus();
  await page.keyboard.type(needle);
  await page.waitForFunction(() => document.querySelectorAll('table.grid tbody tr').length >= 1 && document.querySelector('.screen-header .muted')?.textContent?.match(/^1 /));
  log('search one in 100k: ms=%d rows=%d text=%s', Date.now() - t1, await page.locator('table.grid tbody tr').count(), await page.locator('.screen-header .muted').textContent());
  await page.screenshot({ path: OUT + '/03-users-search-en.jpg', type: 'jpeg', quality: 70 });
  // Roles + Workspace screens
  for (const name of ['Roles', 'Workspace']) {
    await page.locator('nav.topnav').getByRole('link', { name }).click();
    await page.waitForTimeout(500);
    log(name, 'screen h1:', await page.locator('main h1').first().textContent());
  }
  await page.screenshot({ path: OUT + '/04-workspace-en.jpg', type: 'jpeg', quality: 70 });
  // switch to Arabic in the shell by keyboard
  await page.locator('.lang-toggle').focus(); await page.keyboard.press('Enter');
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  log('after toggle: lang=%s dir=%s', await page.getAttribute('html', 'lang'), await page.getAttribute('html', 'dir'));
  await page.locator('nav.topnav').getByRole('link', { name: 'المستخدمون' }).click();
  await page.waitForSelector('table.grid tbody tr');
  await page.screenshot({ path: OUT + '/05-users-ar.jpg', type: 'jpeg', quality: 70 });
  // any untranslated English left on the Arabic screen?
  const latin = await page.evaluate(() => { const w = document.createTreeWalker(document.querySelector('.app'), NodeFilter.SHOW_TEXT); const out = []; let n; while ((n = w.nextNode())) { const s = n.textContent.trim(); const p = n.parentElement; if (s && /[A-Za-z]{3,}/.test(s) && !p.closest('td[dir=ltr]') && !p.closest('tbody') && !p.closest('.user-name')) out.push(s); } return out.slice(0, 20); });
  log('Latin text on Arabic users screen (outside data cells):', JSON.stringify(latin));
  // sign out by keyboard
  await page.getByRole('button', { name: 'تسجيل الخروج' }).focus(); await page.keyboard.press('Enter');
  await page.waitForSelector('input[name="password"]');
  log('after sign-out: lang=%s focused=%s', await page.getAttribute('html', 'lang'), await page.evaluate(() => document.activeElement?.getAttribute('name')));
  await page.screenshot({ path: OUT + '/06-signin-ar-after-signout.jpg', type: 'jpeg', quality: 70 });
  // back button after sign-out
  await page.goBack(); await page.waitForTimeout(800);
  log('back after sign-out shows sign-in:', await page.locator('input[name="password"]').count() > 0);
  log('console errors:', JSON.stringify(consoleErrors.slice(0, 5)));
  await ctx.close();

  // 2. Arabic from the first screen + Arabic admin + wrong password message
  ctx = await browser.newContext({ viewport: { width: 1366, height: 800 }, locale: 'ar-AE' });
  page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  log('ar-AE browser first screen: lang=%s dir=%s', await page.getAttribute('html', 'lang'), await page.getAttribute('html', 'dir'));
  await page.screenshot({ path: OUT + '/07-signin-first-screen-ar-browser.jpg', type: 'jpeg', quality: 70 });
  if ((await page.getAttribute('html', 'lang')) !== 'ar') { await page.locator('.lang-toggle').click(); }
  await page.locator('input[name="email"]').fill('admin.ar@alnoor.example');
  await page.locator('input[name="password"]').fill('wrong-password');
  await page.keyboard.press('Enter');
  await page.getByRole('alert').waitFor();
  log('wrong password (ar):', await page.getByRole('alert').textContent());
  await page.screenshot({ path: OUT + '/08-signin-ar-wrong-password.jpg', type: 'jpeg', quality: 70 });
  await page.locator('input[name="password"]').fill(PW);
  await page.keyboard.press('Enter');
  await page.waitForSelector('.topbar');
  await page.waitForTimeout(500);
  log('AR admin shell: dir=%s h1=%s', await page.getAttribute('html', 'dir'), await page.locator('main h1').first().textContent());
  await page.screenshot({ path: OUT + '/09-shell-ar.jpg', type: 'jpeg', quality: 70 });
  await ctx.close();

  // 3. restricted users: viewer and noaccess
  for (const who of ['viewer@alnoor.example', 'noaccess@alnoor.example']) {
    ctx = await browser.newContext({ viewport: { width: 1366, height: 800 } });
    page = await ctx.newPage();
    await page.goto(BASE + '/');
    await page.locator('input[name="email"]').fill(who);
    await page.locator('input[name="password"]').fill(PW);
    await page.keyboard.press('Enter');
    await page.waitForSelector('.topbar');
    await page.waitForTimeout(500);
    const links = await page.locator('nav.topnav a').allTextContents();
    await page.goto(BASE + '/identity/roles'); await page.waitForTimeout(800);
    log('%s menu=%j; /identity/roles shows: %s', who, links, (await page.locator('main h1').first().textContent()));
    if (who.startsWith('noaccess')) await page.screenshot({ path: OUT + '/10-noaccess-direct-url.jpg', type: 'jpeg', quality: 70 });
    await ctx.close();
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
