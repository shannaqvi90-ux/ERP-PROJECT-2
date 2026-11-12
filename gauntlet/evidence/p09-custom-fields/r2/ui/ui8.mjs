// Arabic: the custom fields screen's new-field form (type names), and a contact's custom date field placeholder.
import { open, log, shot } from './common.mjs';
const { browser, page } = await open('ar');
await page.goto('http://localhost:20950/customfields/fields'); await page.locator('main h1').waitFor(); await page.waitForTimeout(1000);
log('headers', (await page.locator('table[role=grid] thead').innerText()).replace(/\n+/g, ' / '));
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
log('types', JSON.stringify(await page.locator('select[name=type], select').nth(1).evaluate(e => [...e.options].map(o => o.text))));
await shot(page, 'ar-new-field-form');
await page.goto('http://localhost:20950/contacts/contacts?filter=cfInsuranceCover+ge+5000');
await page.locator('table[role=grid] tbody tr').first().waitFor();
await page.locator('table[role=grid] tbody tr').first().click(); await page.waitForTimeout(2000);
const d = page.getByLabel('تاريخ مراجعة الائتمان', { exact: true });
log('date field', await d.count(), await d.getAttribute('placeholder').catch(() => '-'), await d.getAttribute('type').catch(() => '-'));
await shot(page, 'ar-contact-custom-fields');
await browser.close();
