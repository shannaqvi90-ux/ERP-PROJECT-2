// Critic p04 r13 (from r12 critic), plant CL3 / CL3b at run time: A (Al Noor) opens a user's record, presses Alt+L, signs out;
// B (Gulf Steel) signs in on the same tab. What does B's tab show?
import { launch, save } from './pw.mjs';
const BASE = process.env.BASE || 'http://localhost:20470';
const b = await launch();
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
async function signIn(email) {
  await p.goto(BASE + '/'); await p.locator('input[name="email"]').waitFor();
  await p.locator('input[name="email"]').fill(email); await p.keyboard.press('Tab'); await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
  await p.locator('nav.navpane a').first().waitFor({ timeout: 30000 }); await p.waitForTimeout(800);
}
const res = {};
await signIn('admin@alnoor.example');
await p.keyboard.press('Control+k'); await p.locator('[role=dialog] input[role=combobox]').waitFor(); await p.keyboard.type('accountant'); await p.waitForTimeout(1500); await p.keyboard.press('Enter'); await p.waitForTimeout(2000);
res.aOnRecord = p.url();
await p.keyboard.press('Alt+l'); await p.waitForTimeout(1500);
res.aDeviceLanguageKey = await p.evaluate(() => localStorage.getItem('erp.language'));
await p.locator('button', { hasText: /Sign out|تسجيل الخروج/ }).first().click();
await p.locator('input[name="email"]').waitFor(); await p.waitForTimeout(800);
res.afterSignOutKey = await p.evaluate(() => localStorage.getItem('erp.language'));
// B on the same tab: type on the sign-in screen shown
await p.locator('input[name="email"]').fill('admin@gulfsteel.example'); await p.keyboard.press('Tab'); await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
await p.locator('nav.navpane a').first().waitFor({ timeout: 30000 }); await p.waitForTimeout(2500);
res.bUrl = p.url();
res.bSearchBox = await p.evaluate(() => document.querySelector('main input.search')?.value ?? null);
res.leaksAlNoor = /alnoor/i.test(decodeURIComponent(res.bUrl)) || /alnoor/i.test(res.bSearchBox ?? '');
await p.screenshot({ path: '/home/shan/evidence-staging/p04-shell/r13/dbg-shared-device-product.jpg', type: 'jpeg', quality: 60 });
save('shared-device-product.json', res);
console.log(JSON.stringify(res, null, 1));
await b.close();
