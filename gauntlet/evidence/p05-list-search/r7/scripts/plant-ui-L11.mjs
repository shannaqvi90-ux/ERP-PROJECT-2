import { open, shot } from './pw.mjs';
const base = process.argv[2], outDir = process.argv[3];
// alnoor first sorts its users by name and searches 'khalid'
const a = await open(base, 'admin@alnoor.example');
await a.page.goto(base + '/identity/users?sort=displayName&search=khalid').catch(() => {});
await a.page.waitForTimeout(1500);
await a.browser.close();
const { browser, page } = await open(base, 'admin@gulfsteel.example');
await page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first().click();
await page.waitForTimeout(1000);
// sort by name from the header, then search
await page.getByRole('columnheader', { name: /^Name/ }).first().click();
await page.waitForTimeout(700);
await page.getByRole('searchbox').or(page.getByLabel(/search/i)).first().fill('khalid');
await page.waitForTimeout(2000);
const text = await page.locator('body').innerText();
console.log((text.match(/[\d,]+ users?/gi) || []).join(' | '), '| rows:', await page.getByRole('row').count(), '| url', page.url());
await shot(page, outDir + '/plant-L11-gulfsteel-sorted-search-shows-other-tenant-total.jpg');
await browser.close();
