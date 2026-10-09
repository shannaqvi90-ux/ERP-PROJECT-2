// Critic p03 r7 walk 1: users list among 100,000, open one, the panel, effective permissions, at 1920x1080.
import { open, signIn, BASE, shot, focused } from './pw.mjs';
const { browser, page, errors } = await open(1920, 1080);
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/users'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
console.log('focused on open:', await focused(page));
const status = await page.locator('body').innerText();
console.log('LIST HEAD:\n' + status.slice(0, 900));
await page.keyboard.type('dubai.manager.c89bd1'); await page.waitForTimeout(1500);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
const panel = page.locator('aside, [role=complementary], [role=dialog]').last();
console.log('PANEL box:', JSON.stringify(await panel.boundingBox()));
console.log('PANEL:\n' + (await panel.innerText()).slice(0, 1800));
const tabs = await page.getByRole('tab').allInnerTexts(); console.log('tabs', tabs);
const what = page.getByRole('tab', { name: /what they can do|permissions/i }).first();
if (await what.count()) { await what.click(); await page.waitForTimeout(1200); }
await shot(page, '01-effective-permissions-company-manager-1920-en');
console.log('EFFECTIVE:\n' + (await panel.innerText()).slice(0, 2500));
// clipped cells in the effective-permission table
const clipped = await page.evaluate(() => [...document.querySelectorAll('aside td, aside th, [role=dialog] td, [role=dialog] th')].filter(e => e.scrollWidth > e.clientWidth + 1).map(e => e.textContent.trim().slice(0, 40)).slice(0, 20));
console.log('clipped cells:', clipped.length, JSON.stringify(clipped));
console.log('errors', errors);
await browser.close();
