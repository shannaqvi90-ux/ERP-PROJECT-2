// Critic p05 r5: the users list in Arabic, by keyboard: RTL, search with spelling variants, header menu, end of list, all-matching confirm text.
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin.ar@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
page.on('dialog', async d => { log('   native dialog:', d.type(), JSON.stringify(d.message())); await d.dismiss(); });
log('dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
await page.keyboard.press('Control+k'); await page.waitForTimeout(300);
const navUsers = await page.locator('nav a').allInnerTexts(); log('nav', navUsers.join(' | '));
await page.keyboard.press('Escape');
await page.locator('nav a').nth(1).click(); await page.waitForTimeout(2000);
log('url', page.url(), 'focus', await focus(page));
log('top', (await main()).split('\n').slice(0, 8).join(' / '));
await shot(page, '13-users-list-ar.jpg');
// Arabic search with variant spelling (alef without hamza, final ya as alef maqsura)
await page.keyboard.type('ماجد انيل بيلاى', { delay: 30 }); await page.waitForTimeout(1500);
log('variant search', (await main()).split('\n').slice(0, 8).join(' / '), '| first row', (await page.getByRole('row').nth(1).innerText()).replace(/\s+/g, ' '));
await shot(page, '14-arabic-variant-search.jpg');
await page.keyboard.press('Enter'); await page.waitForTimeout(1000);
log('Enter -> url', page.url(), 'panel', (await page.locator('[role=dialog], aside, [role=complementary]').first().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 150));
await page.keyboard.press('Escape'); await page.waitForTimeout(400);
// Header menu with keyboard in RTL
const hdr = page.getByRole('button', { name: /اللغة/ }).first();
log('header buttons', (await page.getByRole('columnheader').allInnerTexts()).join(' | '));
// positions of name column vs. e-mail column (RTL: name rightmost)
const xs = await page.getByRole('columnheader').evaluateAll(h => h.map(e => [e.innerText.trim().slice(0, 12), Math.round(e.getBoundingClientRect().x)]));
log('header x', JSON.stringify(xs));
// clear search and select all matching to see confirm text in Arabic
await page.locator('input').first().focus(); await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace');
await page.keyboard.type('خالد', { delay: 30 }); await page.waitForTimeout(1500);
log('خالد', (await main()).split('\n').slice(0, 7).join(' / '));
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Control+a'); await page.waitForTimeout(200); await page.keyboard.press('Control+a'); await page.waitForTimeout(400);
log('selection', ((await main()).match(/[^\n]*(محدد|اختير|مختار|تحديد)[^\n]*/) || ['?'])[0]);
const btns = await page.getByRole('button').allInnerTexts(); log('buttons', btns.slice(0, 30).join(' | '));
await shot(page, '15-ar-all-matching.jpg');
await browser.close();
