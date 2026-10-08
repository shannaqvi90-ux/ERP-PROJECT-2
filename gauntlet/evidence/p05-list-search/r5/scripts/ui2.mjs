// Critic p05 r5: header menus, sort, filter, group, column chooser, saved views by keyboard (English).
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const count = () => page.locator('main').getByText(/\d[\d,]* users?$|one user|No users/).first().innerText().catch(() => '?');
const firstRows = async n => (await page.getByRole('row').allInnerTexts()).slice(1, 1 + n).map(s => s.replace(/\s+/g, ' ').slice(0, 70));
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(2000);
log('focus', await focus(page));
// Tab through toolbar and headers
for (let i = 0; i < 14; i++) { await page.keyboard.press('Tab'); log(' tab', i + 1, await focus(page)); }
await browser.close();
