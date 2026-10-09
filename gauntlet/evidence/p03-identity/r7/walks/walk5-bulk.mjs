// Critic p03 r7 walk 5: deactivate chosen users from the keyboard: the app's own confirm, then the result.
import { open, signIn, BASE, shot, focused } from './pw.mjs';
const { browser, page, errors } = await open(1600, 900);
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/users'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.type('lock.7f7152'); await page.waitForTimeout(1200);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Space'); await page.waitForTimeout(500);
console.log('toolbar buttons:', JSON.stringify(await page.locator('main').getByRole('button').allInnerTexts()));
const deact = page.getByRole('button', { name: /^deactivate/i }).first();
console.log('deactivate buttons:', await deact.count());
if (await deact.count()) { await deact.click(); await page.waitForTimeout(700); }
console.log('focused after Deactivate:', await focused(page));
const dlg = page.locator('[role=dialog],[role=alertdialog],dialog[open]').last();
console.log('dialog:', await dlg.count() ? (await dlg.innerText()).slice(0, 300) : 'none');
await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
console.log('after confirm:\n' + (await page.locator('main').innerText()).slice(0, 700));
console.log('errors', errors);
await browser.close();
