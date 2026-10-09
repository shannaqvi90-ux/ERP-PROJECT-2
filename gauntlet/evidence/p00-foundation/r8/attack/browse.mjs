// Critic p00 r8: the product in a real browser (Chromium), English and Arabic, by keyboard.
import { chromium } from '/home/shan/critic/p00-foundation-r8/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const OUT = '/home/shan/evidence-staging/p00-foundation/r8/';
const BASE = process.env.BASE ?? 'http://localhost:20050';
const PW = 'Demo-Pass-2026';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const notes = [];
const note = (...a) => { const s = a.join(' '); notes.push(s); console.log(s); };
const shot = (page, name) => page.screenshot({ path: OUT + name, type: 'jpeg', quality: 65 });
const focusName = page => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName.toLowerCase()}${e.getAttribute('name') ? '[name=' + e.getAttribute('name') + ']' : ''}${e.getAttribute('type') ? '[type=' + e.getAttribute('type') + ']' : ''}` : 'none'; });
const NAV = 'nav[aria-label="Main navigation"], nav[aria-label="التنقل الرئيسي"], nav';

async function keyboardSignIn(page, url, email, password) {
  await page.goto(url);
  await page.waitForSelector('input[name="email"]');
  note(`  focus on load (${url.replace(BASE, '')}):`, await focusName(page));
  await page.keyboard.type(email);
  await page.waitForTimeout(150);
  note('  focus after typing whole e-mail:', await focusName(page));
  if ((await focusName(page)).includes('email')) await page.keyboard.press('Enter');
  await page.waitForTimeout(150);
  note('  focus before password:', await focusName(page));
  await page.keyboard.type(password);
  const t0 = Date.now();
  await page.keyboard.press('Enter');
  await page.waitForSelector('#navpane', { state: 'attached', timeout: 20000 });
  note('  signed in, shell nav present after', Date.now() - t0, 'ms');
}

// 1. English sign-in screen, wrong password
if (!process.env.FROM4) {
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  note('1. sign-in html dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  await shot(page, '01-sign-in-en.jpg');
  await page.keyboard.type('admin@alnoor.example');
  await page.keyboard.press('Enter');
  await page.keyboard.type('wrong-password');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  note('   wrong password message:', (await page.locator('[role="alert"]').allInnerTexts()).join(' | '));
  await shot(page, '02-sign-in-error-en.jpg');
  const controls = await page.locator('button, a, select').evaluateAll(els => els.map(e => (e.innerText || e.getAttribute('aria-label') || e.tagName).trim()).filter(Boolean));
  note('   sign-in controls:', JSON.stringify(controls));
  // Switch to Arabic on the sign-in screen
  const ar = page.getByRole('button', { name: /العربية|Arabic/ }).or(page.getByRole('link', { name: /العربية|Arabic/ })).first();
  if (await ar.count()) {
    await ar.click();
    await page.waitForTimeout(500);
    note('   after language switch dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
    note('   Arabic sign-in labels:', JSON.stringify(await page.locator('label, h1, h2, button').allInnerTexts()));
    await shot(page, '03-sign-in-ar.jpg');
  } else note('   no Arabic switch found on sign-in screen');
  await ctx.close();
}

// 2. Keyboard sign-in, English admin, plain address; shell; Tab order
if (!process.env.FROM4) {
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  note('2. English admin by keyboard on the plain address');
  await keyboardSignIn(page, BASE + '/', 'admin@alnoor.example', PW);
  await page.waitForTimeout(800);
  await shot(page, '04-shell-en.jpg');
  note('   shell dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  const seen = [];
  await page.evaluate(() => document.activeElement?.blur());
  for (let i = 0; i < 40; i++) {
    await page.keyboard.press('Tab');
    seen.push(await page.evaluate(() => { const e = document.activeElement; return e === document.body ? 'BODY' : `${e.tagName.toLowerCase()}:${(e.innerText || e.getAttribute('aria-label') || '').trim().slice(0, 25)}`; }));
  }
  note('   Tab order (40 presses):', seen.join(' > '));
  note('   BODY stops in Tab cycle:', seen.filter(s => s === 'BODY').length);
  // Sign out by keyboard: find the sign-out control
  const so = page.getByRole('button', { name: /Sign out/i }).or(page.getByRole('menuitem', { name: /Sign out/i }));
  note('   sign-out control visible at once:', await so.first().isVisible().catch(() => false));
  await ctx.close();
}

// 3. Arabic admin by keyboard (team address), Arabic shell
if (!process.env.FROM4) {
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'ar-AE' });
  const page = await ctx.newPage();
  note('3. Arabic admin by keyboard on the team address');
  await keyboardSignIn(page, BASE + '/?domain=alnoor.example', 'admin.ar', PW);
  await page.waitForTimeout(800);
  note('   shell dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  const latin = await page.evaluate(() => { const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); const out = new Set(); let n; while ((n = w.nextNode())) { const t = n.textContent.trim(); if (/[A-Za-z]{2,}/.test(t) && n.parentElement && n.parentElement.offsetParent !== null) out.add(t.slice(0, 40)); } return [...out]; });
  note('   visible Latin text in Arabic shell:', JSON.stringify(latin));
  await shot(page, '05-shell-ar.jpg');
  await ctx.close();
}

// 4. User with no roles
{
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  note('4. no-roles user');
  await keyboardSignIn(page, BASE + '/', 'noaccess@alnoor.example', PW);
  await page.waitForTimeout(800);
  note('   main text:', (await page.locator('main').innerText().catch(() => '')).slice(0, 300).replace(/\n/g, ' / '));
  note('   nav links:', JSON.stringify(await page.locator('nav a').allInnerTexts()));
  await shot(page, '06-no-roles-user.jpg');
  const api = await page.evaluate(async () => { const r = await fetch('/api/identity/users?take=1', { headers: { 'X-Erp-Request': '1' } }); return r.status; });
  note('   no-roles GET /api/identity/users:', api);
  await ctx.close();
}

// 5. Passkey: add one on My account (virtual authenticator), sign out, fresh visit to the plain address.
{
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', { options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true } });
  note('5. passkey');
  await keyboardSignIn(page, BASE + '/', 'viewer@alnoor.example', PW);
  await page.goto(BASE + '/identity/me');
  const add = page.getByRole('button', { name: 'Add a passkey', exact: true });
  await add.waitFor({ timeout: 10000 });
  await add.focus();
  await page.keyboard.press('Enter');
  await page.getByText(/added/).first().waitFor({ timeout: 10000 });
  note('   passkey added; credentials on device:', (await cdp.send('WebAuthn.getCredentials', { authenticatorId })).credentials.length);
  await shot(page, '07-my-account-passkey.jpg');
  const storage = await ctx.storageState();
  const creds = (await cdp.send('WebAuthn.getCredentials', { authenticatorId })).credentials;
  await page.evaluate(async () => fetch('/api/auth/sign-out', { method: 'POST', headers: { 'X-Erp-Request': '1' } }));
  await ctx.close();
  // A fresh browser on the same device (storage and passkey carried), plain address.
  const ctx2 = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US', storageState: storage });
  const p2 = await ctx2.newPage();
  const cdp2 = await ctx2.newCDPSession(p2);
  await cdp2.send('WebAuthn.enable');
  const a2 = await cdp2.send('WebAuthn.addVirtualAuthenticator', { options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true } });
  for (const c of creds) await cdp2.send('WebAuthn.addCredential', { authenticatorId: a2.authenticatorId, credential: c });
  const t0 = Date.now();
  await p2.goto(BASE + '/');
  await p2.waitForSelector('#navpane', { state: 'attached', timeout: 20000 });
  note('   plain address, device with passkey: in the shell without a key after', Date.now() - t0, 'ms');
  await shot(p2, '08-passkey-signed-in.jpg');
  await ctx2.close();
}

fs.writeFileSync(OUT + (process.env.FROM4 ? 'browser-notes-2.txt' : 'browser-notes.txt'), notes.join('\n') + '\n');
await b.close();
