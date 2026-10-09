// Critic p03 r7 walk 3b: delete a role (the copy made in walk 3).
import { open, signIn, BASE, shot, focused } from './pw.mjs';
const name = process.argv[2];
const { browser, page, errors } = await open(1600, 900);
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.type(name); await page.waitForTimeout(1000); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
await page.getByRole('button', { name: /delete role/i }).first().click(); await page.waitForTimeout(800);
console.log('focused after Delete role:', await focused(page));
const body = await page.locator('body').innerText();
const i = body.indexOf('Delete'); console.log('text around confirm:\n' + body.slice(Math.max(0, body.length - 1200)));
await shot(page, '06-delete-role-confirm-en');
const dialogs = await page.locator('[role=dialog],[role=alertdialog],dialog[open]').count(); console.log('dialogs', dialogs);
const confirm = page.locator("aside, [role=complementary]").last().getByRole("button", { name: /^delete$/i });
console.log('confirm buttons', await confirm.count());
if (await confirm.count()) { await confirm.first().click(); await page.waitForTimeout(1200); }
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.type(name); await page.waitForTimeout(1000);
console.log('after delete:\n' + (await page.locator('main').innerText()).slice(0, 400));
console.log('errors', errors);
await browser.close();
