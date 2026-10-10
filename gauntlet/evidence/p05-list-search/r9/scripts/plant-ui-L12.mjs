import { open, shot } from './pw.mjs';
const base = process.argv[2], outDir = process.argv[3];
const { browser, page } = await open(base, 'admin@gulfsteel.example');
await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
await page.waitForTimeout(2500);
const text = await page.locator('body').innerText();
console.log((text.match(/[\d,]+ users?/gi) || []).join(' | '), '| url', page.url());
await shot(page, outDir + '/plant-L12-gulfsteel-users-shows-all-tenants-total.jpg');
await browser.close();
