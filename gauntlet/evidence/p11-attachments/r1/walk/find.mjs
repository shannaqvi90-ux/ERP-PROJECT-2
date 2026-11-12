import { createRequire } from 'node:module';
const require = createRequire('/home/shan/critic/p11-attachments-r1/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const B = 'http://localhost:21150', AC = process.env.AC, OUT = '/home/shan/evidence-staging/p11-attachments/r1';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
await page.goto(B + '/'); await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('nav').first().waitFor(); await page.waitForTimeout(1000);
await page.keyboard.press('Control+k'); await page.waitForTimeout(500);
await page.keyboard.type('licence-critic'); await page.waitForTimeout(2500);
console.log('palette for file name:', (await page.locator('[role="dialog"], [role="listbox"]').first().innerText().catch(() => 'none')).slice(0, 300).replace(/\n/g, ' | '));
await page.screenshot({ path: `${OUT}/10-en-global-search-file-name.jpg`, type: 'jpeg', quality: 60 });
await page.keyboard.press('Escape');
await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('attach'); await page.waitForTimeout(1500);
console.log('palette "attach":', (await page.locator('[role="dialog"], [role="listbox"]').first().innerText().catch(() => 'none')).slice(0, 300).replace(/\n/g, ' | '));
await page.keyboard.press('Escape');
// contact history
await page.goto(`${B}/contacts/contacts/${AC}`); await page.locator('[data-testid="attachments"]').waitFor(); await page.waitForTimeout(800);
const hist = page.getByRole('button', { name: /history/i }).or(page.getByRole('tab', { name: /history/i })).or(page.getByRole('link', { name: /history/i }));
console.log('history controls:', await hist.count());
if (await hist.count()) { await hist.first().click(); await page.waitForTimeout(2000);
  const txt = await page.locator('main').innerText(); console.log('history mentions critic files:', /licence-critic|stamp-critic|Attachment/i.test(txt));
  console.log(txt.split('\n').filter(l => /attach|critic|file/i.test(l)).slice(0, 8).join(' | '));
  await page.screenshot({ path: `${OUT}/11-en-contact-history.jpg`, type: 'jpeg', quality: 60 }); }
// contacts list columns: any attachments column?
await page.goto(`${B}/contacts/contacts`); await page.waitForTimeout(1500);
await page.getByRole('button', { name: 'Columns' }).click(); await page.waitForTimeout(800);
console.log('columns:', (await page.locator('[role="dialog"], [role="menu"]').first().innerText().catch(() => '')).replace(/\n/g, ' | ').slice(0, 600));
await browser.close();
