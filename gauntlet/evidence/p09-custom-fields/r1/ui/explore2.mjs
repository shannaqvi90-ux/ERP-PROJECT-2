import { chromium } from '/home/shan/critic/p09-custom-fields-r1/gauntlet/compare/node_modules/playwright-core/index.mjs';
const B = 'http://localhost:20950', OUT = '/home/shan/evidence-staging/p09-custom-fields/r1/ui/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const log = (...a) => console.log(...a);
page.on('pageerror', e => log('PAGEERROR', e.message));
await page.goto(B + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('main h1').waitFor();
if (!process.env.SKIP) { await page.goto(B + '/customfields/fields'); await page.locator('table[role=grid] tbody tr').first().waitFor();
await page.keyboard.press('Alt+n');
await page.locator('input[name="labelEn"]:focus').waitFor();
await page.keyboard.type('Credit limit');
await page.keyboard.press('Tab'); await page.keyboard.type('حد الائتمان');
await page.keyboard.press('Tab'); await page.keyboard.press('Tab');
log('focused', await page.evaluate(() => document.activeElement.name));
await page.keyboard.press('ArrowDown'); // Text -> Number
log('type now', await page.locator('select[name=type]').inputValue());
await page.locator('input[name=decimals]').waitFor({ timeout: 3000 }).catch(() => log('no decimals input'));
await page.keyboard.press('Tab'); 
log('focused2', await page.evaluate(() => document.activeElement.name));
await page.keyboard.press('Control+a'); await page.keyboard.type('2');
await page.keyboard.press('Control+s');
await page.locator('.list-record [role="status"]').waitFor({ timeout: 8000 }).catch(e => log('no saved status'));
log('status', await page.locator('.list-record [role="status"]').innerText().catch(() => '-'));
await page.screenshot({ path: OUT + 'en-field-saved.jpg', type: 'jpeg', quality: 60 }); }
// contact
await page.goto(B + '/contacts/contacts'); 
await page.waitForFunction(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]'));
await page.keyboard.type('Wei Ali Al Ketbi');
await page.waitForFunction(() => [...document.querySelectorAll('table[role=grid] tbody tr')].some(r => r.textContent.includes('Wei Ali Al Ketbi')));
await page.keyboard.press('Enter');
const input = page.locator('.list-record .field', { has: page.locator('label', { hasText: /^Credit limit$/ }) }).locator('input').first();
await input.waitFor();
await input.click(); await page.keyboard.type('25000.5');
await page.keyboard.press('Control+s');
await page.locator('.list-record [role="status"]').waitFor({ timeout: 8000 }).catch(e => log('no saved status (contact)'));
log('contact status', await page.locator('.list-record [role="status"]').innerText().catch(() => '-'), 'value now', await input.inputValue());
await page.screenshot({ path: OUT + 'en-contact-custom-fields.jpg', type: 'jpeg', quality: 60 });
await page.keyboard.press('Escape');
// columns chooser
await page.getByRole('button', { name: 'Columns', exact: true }).click();
await page.waitForTimeout(500);
const dialogText = await page.locator('[role=dialog], .popover, .menu').last().innerText().catch(() => '');
log('columns chooser has Credit limit:', dialogText.includes('Credit limit'), '| Licence:', dialogText.slice(0, 400).replace(/\n/g, ' / '));
await page.screenshot({ path: OUT + 'en-column-chooser.jpg', type: 'jpeg', quality: 60 });
await browser.close();
