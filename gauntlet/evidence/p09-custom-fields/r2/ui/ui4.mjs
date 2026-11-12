// Capture what the screen sends when a select field is added with a default in one go.
import { open, log, shot } from './common.mjs';
const { browser, page } = await open('en');
page.on('request', r => { if (r.url().includes('/api/customfields/fields') && r.method() !== 'GET') log('REQ', r.method(), r.postData()); });
await page.goto('http://localhost:20950/customfields/fields'); await page.locator('main h1').waitFor(); await page.waitForTimeout(1000);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
await page.keyboard.type('Shipping line two'); await page.keyboard.press('Tab'); await page.keyboard.press('Tab'); await page.keyboard.press('Tab');
await page.keyboard.type('Single'); await page.waitForTimeout(300);
await page.keyboard.press('Tab'); log('tab after type ->', await page.evaluate(() => document.activeElement?.name));
await page.keyboard.type('MSC'); await page.keyboard.press('Enter'); await page.keyboard.type('Maersk');
await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Tab'); const order=[]; for (let i=0;i<4;i++){ await page.keyboard.press('Tab'); order.push(await page.evaluate(() => document.activeElement?.name+':'+document.activeElement?.tagName)); } log('order after options', order.join(' ')); await page.getByLabel('Default for new records').focus();
await page.keyboard.type('Maersk'); log('default value', await page.evaluate(() => document.activeElement?.value));
await page.keyboard.press('Control+s'); await page.waitForTimeout(1500);
await shot(page, 'en-select-default-on-create');
await browser.close();
