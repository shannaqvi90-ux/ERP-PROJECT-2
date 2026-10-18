import { launch } from '/home/shan/critic/p02-tenancy-r11/gauntlet/compare/lib/browser.mjs';
const base = 'http://localhost:20250', out = '/home/shan/evidence-staging/p02-tenancy/r11/';
const browser = await launch();
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
const txt = async () => (await page.locator('main').innerText().catch(() => page.locator('body').innerText())).replace(/\n+/g, ' | ').slice(0, 700);
await page.goto(base + '/'); await page.waitForTimeout(800);
await page.keyboard.type('accountant@alnoor.example'); await page.keyboard.press('Enter'); await page.waitForTimeout(400);
await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
// working company is DXB (chip). Go to Companies
await page.keyboard.press('g'); await page.keyboard.press('c'); await page.waitForTimeout(1500);
console.log('companies list:', await txt());
await page.getByText('ALN-FZE', { exact: true }).last().click().catch(e => console.log('click err', e.message));
await page.waitForTimeout(1500);
console.log('FZE form (working in DXB):', await txt());
const editable = await page.locator('input:not([disabled]):not([readonly])').count();
const disabledFs = await page.locator('fieldset[disabled]').count();
console.log('editable inputs', editable, 'disabled fieldsets', disabledFs);
await page.screenshot({ path: out + '01-fze-company-editable-while-working-in-dxb.png' });
await browser.close();
