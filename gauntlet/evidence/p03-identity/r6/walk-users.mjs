import { open, signIn, BASE, shot } from './pw.mjs';
const { browser, page, errors } = await open(1600, 900);
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/users'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
// find the company manager and open "What they can do"
await page.keyboard.type('dubai.manager.64eabe'); await page.waitForTimeout(1200);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
await page.getByRole('tab', { name: /what they can do/i }).or(page.getByRole('button', { name: /what they can do/i })).first().click();
await page.waitForTimeout(1200);
await shot(page, '04-effective-permissions-company-manager-en');
const panel = page.locator('aside, [role=complementary]').last();
console.log('EFFECTIVE:\n' + (await panel.innerText()).slice(0, 2500));
// New user form via Alt+N
await page.keyboard.press('Escape'); await page.waitForTimeout(400);
await page.goto(BASE + '/identity/users'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1500);
await shot(page, '05-new-user-form-en');
console.log('NEW USER FORM:\n' + (await page.locator('aside, [role=complementary]').last().innerText()).slice(0, 2000));
console.log('errors', errors);
await browser.close();
