// Critic p05 r5: the read-only user and the no-roles user on the users list (screen and API).
import { open, focus, shot } from './pw.mjs';
const log = (...a) => console.log(...a);
for (const who of ['viewer@alnoor.example', 'noaccess@alnoor.example']) {
  const { browser, page } = await open(who);
  await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(2000);
  const txt = (await page.locator('main').innerText()).replace(/\s+/g, ' ');
  log(who, '| main:', txt.slice(0, 160));
  log('   buttons', (await page.locator('main').getByRole('button').allInnerTexts()).map(s => s.trim()).filter(Boolean).slice(0, 14).join(' | '));
  if (/viewer/.test(who)) {
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Control+a'); await page.keyboard.press('Control+a'); await page.waitForTimeout(300);
    log('   bulk buttons after select-all', (await page.locator('main').getByRole('button').allInnerTexts()).filter(b => /Copy|Activate|Deactivate|Clear/.test(b)).join(' | '));
    await page.getByRole('button', { name: /View:/ }).click(); await page.waitForTimeout(300);
    log('   view menu', (await page.getByRole('menu').innerText().catch(() => '')).replace(/\s+/g, ' '));
    await page.keyboard.press('Escape');
    const t = await page.getByRole('button', { name: /View:/ }); await t.click(); await page.getByRole('menuitem', { name: /Save as/ }).click().catch(() => {}); await page.waitForTimeout(400);
    log('   save dialog', (await page.locator('[role=dialog]').innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 200));
  }
  // API with this session
  const res = await page.evaluate(async () => {
    const H = { 'X-Erp-Request': '1', 'content-type': 'application/json' };
    const r1 = await fetch('/api/identity/users?take=1', { headers: H });
    const r2 = await fetch('/api/lists/identity.users/definition', { headers: H });
    const r3 = await fetch('/api/lists/identity.users/shared-views', { method: 'POST', headers: H, body: JSON.stringify({ name: 'x', columns: ['displayName'] }) });
    const r4 = await fetch('/api/identity/users/matching/active', { method: 'POST', headers: H, body: JSON.stringify({ active: false, search: 'khalid', expectedCount: 3382 }) });
    const r5 = await fetch('/api/reports/lists/identity.users?format=csv&search=khalid', { headers: H });
    return [r1.status, r2.status, r3.status, r4.status, r5.status];
  });
  log('   API users/definition/shared-view POST/matching POST/export:', res.join(' '));
  await browser.close();
}
