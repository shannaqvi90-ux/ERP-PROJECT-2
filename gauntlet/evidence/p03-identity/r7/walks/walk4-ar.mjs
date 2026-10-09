// Critic p03 r7 walk 4: Arabic, right to left: users search, user panel, what they can do, sign-in history, new role.
import { open, signIn, BASE, shot, focused } from './pw.mjs';
const { browser, page, errors } = await open(1600, 900);
await signIn(page, 'admin.ar@alnoor.example');
console.log('html dir/lang:', await page.evaluate(() => [document.documentElement.dir, document.documentElement.lang]));
await page.goto(BASE + '/identity/users'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.type('ماجد أنيل'); await page.waitForTimeout(1500);
console.log('AR list:\n' + (await page.locator('main').innerText()).slice(0, 500));
await shot(page, '07-users-search-arabic-ar');
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
const panel = page.locator('aside, [role=complementary]').last();
const tabs = await page.getByRole('tab').allInnerTexts(); console.log('AR tabs', JSON.stringify(tabs));
await page.getByRole('tab').nth(1).click(); await page.waitForTimeout(1000);
console.log('AR effective:\n' + (await panel.innerText()).slice(0, 700));
await page.getByRole('tab').nth(2).click().catch(() => {}); await page.waitForTimeout(1000);
console.log('AR history:\n' + (await panel.innerText()).slice(0, 500));
// English left anywhere in the panel?
const latin = await panel.evaluate(el => (el.innerText.match(/\b[A-Za-z]{4,}\b/g) || []).filter(w => !/alnoor|example|ALN|DXB|AUH|FZE|SHJ/i.test(w)).slice(0, 30));
console.log('Latin words in AR panel:', JSON.stringify(latin));
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1000);
console.log('AR focused after Alt+N:', await focused(page));
await shot(page, '08-new-role-matrix-ar');
const clipped = await page.evaluate(() => [...document.querySelectorAll('aside th, aside label, aside caption')].filter(e => e.scrollWidth > e.clientWidth + 1).map(e => e.textContent.trim().slice(0, 30)));
console.log('AR clipped headers/labels:', JSON.stringify(clipped));
console.log('errors', errors);
await browser.close();
