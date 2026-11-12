// Open the Incoterm field, look at its default list, choose FOB by keyboard, save; then a new select field typed in one go.
import { open, log, focused, shot } from './common.mjs';
const { browser, page } = await open('en');
await page.goto('http://localhost:20950/customfields/fields'); await page.locator('main h1').waitFor(); await page.waitForTimeout(1000);
await page.getByRole('row', { name: /Incoterm/ }).first().click(); await page.waitForTimeout(1200);
const def = page.getByLabel('Default for new records');
log('default options', JSON.stringify(await def.evaluate(e => [...e.options].map(o => [o.value, o.text]))), 'current', await def.inputValue());
await def.focus(); await page.keyboard.type('FOB'); log('after typing FOB', await def.inputValue());
await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
log('saved; main says', (await page.locator('[role=alert], .form-message, [role=status]').allInnerTexts()).join(' | ').slice(0, 300));
await shot(page, 'en-incoterm-default');
// fresh select field, default chosen right after typing the options
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type('Payment terms'); await page.keyboard.press('Tab'); await page.keyboard.press('Tab'); await page.keyboard.press('Tab');
await page.keyboard.type('Single'); await page.waitForTimeout(300);
await page.locator('textarea[name=options]').focus(); await page.keyboard.type('Cash'); await page.keyboard.press('Enter'); await page.keyboard.type('Net 30'); await page.keyboard.press('Enter'); await page.keyboard.type('Net 60');
const def2 = page.getByLabel('Default for new records');
log('default options before blur-by-tab', JSON.stringify(await def2.evaluate(e => [...e.options].map(o => [o.value, o.text]))));
await def2.focus(); await page.keyboard.type('Net 3'); log('def2 value', await def2.inputValue());
await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
await browser.close();
