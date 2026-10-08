// Critic p05 r5: admin save-view dialog (share option), saved shared default view reopened, OR filters in the UI, DOM rows.
import { open, focus, shot } from './pw.mjs';
const { browser, page } = await open('admin@alnoor.example');
const log = (...a) => console.log(...a);
const main = () => page.locator('main').innerText();
await page.goto('http://localhost:20550/identity/users?q=khalid+tariq&filter=' + encodeURIComponent("language eq 'ar'")); await page.waitForTimeout(2000);
log('DOM rows at 100k? (filtered now)', await page.getByRole('row').count());
await page.getByRole('button', { name: /View:/ }).click(); await page.waitForTimeout(300);
await page.getByRole('menuitem', { name: /Save as/ }).click(); await page.waitForTimeout(400);
const dlg = page.locator('[role=dialog]');
log('save dialog', (await dlg.innerText()).replace(/\s+/g, ' '));
log('focus', await focus(page));
await page.keyboard.type('Critic r5 Arabic Khalid Tariq');
const boxes = dlg.getByRole('checkbox'); const n = await boxes.count();
for (let i = 0; i < n; i++) { const l = await boxes.nth(i).evaluate(e => e.closest('label')?.innerText || e.getAttribute('aria-label')); log('  checkbox', l); await boxes.nth(i).check(); }
await shot(page, '16-save-shared-default-view.jpg');
await dlg.getByRole('button', { name: /^Save/ }).click(); await page.waitForTimeout(1200);
log('after save', (await page.getByRole('button', { name: /View:/ }).innerText()).replace(/\s+/g, ' '));
await page.goto('http://localhost:20550/identity/users'); await page.waitForTimeout(2000);
log('reopen', (await page.getByRole('button', { name: /View:/ }).innerText()).replace(/\s+/g, ' '), ((await main()).match(/(\d[\d,]*) users|one user/) || ['?'])[0], decodeURIComponent(page.url()));
// The viewer sees the shared default?
const v = await open('viewer@alnoor.example'); await v.page.goto('http://localhost:20550/identity/users'); await v.page.waitForTimeout(2000);
log('viewer opens', (await v.page.getByRole('button', { name: /View:/ }).innerText()).replace(/\s+/g, ' '), ((await v.page.locator('main').innerText()).match(/(\d[\d,]*) users|one user/) || ['?'])[0]);
await v.browser.close();
// OR filter in the UI: open a header filter on Name and look for 'match any'
await page.goto('http://localhost:20550/identity/users?filter=' + encodeURIComponent("language eq 'ar'")); await page.waitForTimeout(1500);
await page.getByRole('button', { name: 'Options for the column Name' }).click(); await page.getByRole('menuitem', { name: /Filter/ }).click(); await page.waitForTimeout(400);
log('name filter editor', (await page.locator('[role=dialog]').last().innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300));
log('chips area', (await main()).split('\n').slice(0, 12).join(' / '));
await shot(page, '17-filter-editor-and-or.jpg');
// cleanup: delete the shared view through the API
const res = await page.evaluate(async () => { const H = { 'X-Erp-Request': '1' }; const j = await (await fetch('/api/lists/identity.users/views', { headers: H })).json(); const v = j.items.filter(x => /Critic r5/.test(x.name)); for (const x of v) await fetch(`/api/lists/identity.users/${x.isShared ? 'shared-views' : 'views'}/${x.id}`, { method: 'DELETE', headers: H }); return v.map(x => x.name + (x.isShared ? ' (shared)' : '')); });
log('cleanup deleted', res);
await browser.close();
