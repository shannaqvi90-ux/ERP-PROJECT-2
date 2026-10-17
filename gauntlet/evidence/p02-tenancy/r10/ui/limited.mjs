// Critic p02 r10: one-company (CL) and one-branch (BL) administrators, and the full administrator's palette actions.
import fs from 'node:fs';
import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p02-tenancy-r10/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = process.argv[2];
const ids = JSON.parse(fs.readFileSync('../attack/scope-attack.log', 'utf8').trim().split('\n').pop());
const signIn = async (email, password) => (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) })).json()).token;
const a0 = await signIn('admin@alnoor.example', 'Demo-Pass-2026');
const pw = 'Critic-R10-Lw-' + Math.random().toString(36).slice(2, 8) + '!A1';
for (const k of ['CL', 'BL']) await fetch(base + '/api/identity/users/' + ids[k].id + '/password', { method: 'POST', headers: { authorization: 'Bearer ' + a0, 'x-erp-request': '1', 'content-type': 'application/json' }, body: JSON.stringify({ password: pw, mustChangePassword: false }) });
const browser = await chromium.launch({ executablePath: `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome` });
const offered = async (page) => page.evaluate(() => ({
  newButtons: [...document.querySelectorAll('main button')].filter((b) => /^(New|جديد)/.test(b.textContent.trim())).length,
  codeDisabled: document.querySelector('[data-field="code"] input')?.disabled ?? null,
  settingDisabled: document.querySelector('[data-field="arabicLegalNameRequired"] input')?.disabled ?? null,
  formOpen: !!document.querySelector('.record-form'),
}));
for (const [who, email, password] of [['CL', ids.CL.email, pw], ['BL', ids.BL.email, pw], ['ADMIN', 'admin@alnoor.example', 'Demo-Pass-2026']]) {
  const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  await page.goto(base + '/'); await page.evaluate(() => localStorage.clear()); await page.goto(base + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(password); await page.keyboard.press('Enter');
  await page.locator('main h1').waitFor(); await page.waitForTimeout(600);
  // go-to keys G then C
  await page.keyboard.press('g'); await page.keyboard.press('c'); await page.waitForTimeout(1200);
  console.log(who, 'G C ->', new URL(page.url()).pathname);
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  console.log(who, 'list', JSON.stringify(await offered(page)));
  await page.keyboard.press('Alt+n'); await page.waitForTimeout(600);
  console.log(who, 'after Alt+N', JSON.stringify(await offered(page)));
  await page.keyboard.press('Escape');
  for (const q of ['new compan', 'new branch', 'print compan']) {
    await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type(q); await page.waitForTimeout(900);
    console.log(who, `palette "${q}":`, JSON.stringify((await page.locator('[role=option]').allTextContents()).slice(0, 4)));
    await page.keyboard.press('Escape'); await page.waitForTimeout(200);
  }
  if (who !== 'ADMIN') {
    await page.goto(base + '/tenancy/companies'); await page.locator('table[role=grid] tbody tr').first().waitFor();
    await page.locator('table[role=grid] tbody tr').first().click(); await page.locator('.record-form').waitFor(); await page.waitForTimeout(800);
    console.log(who, 'record', JSON.stringify(await offered(page)));
  } else {
    // palette "new company" from Home runs the screen's New
    await page.goto(base + '/'); await page.locator('main h1').waitFor();
    await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('new company'); await page.waitForTimeout(900); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
    console.log(who, 'palette Enter on "new company" ->', new URL(page.url()).pathname + new URL(page.url()).search, 'focus', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field')));
  }
  await page.context().close();
}
await browser.close();
