// Critic p03 r13 walk 3: roles in Arabic by keyboard: new role, matrix search, select all shown, save; copy; delete. Effective permissions.
import { launch, BASE, signIn, shot, logger } from './lib.mjs';
const L = logger('w3-roles-ar');
const b = await launch(); const p = await (await b.newContext({ viewport: { width: 1366, height: 800 } })).newPage();
const errs=[]; p.on('console', m => { if (m.type()==='error') errs.push(m.text()); }); p.on('response', r => { if (r.status() >= 500) errs.push('5xx ' + r.url()); });
await signIn(p, 'admin.ar@alnoor.example'); await p.waitForURL(BASE + '/'); await p.waitForTimeout(800);
L('dir=' + await p.evaluate(() => document.documentElement.dir) + ' lang=' + await p.evaluate(() => document.documentElement.lang));
await p.keyboard.press('g'); await p.keyboard.press('r'); await p.waitForURL(/identity\/roles/); await p.waitForTimeout(1000);
await p.keyboard.press('Alt+n'); await p.waitForTimeout(800);
const stamp = Date.now().toString(36);
L('focused: ' + await p.evaluate(() => document.activeElement?.labels?.[0]?.innerText));
await p.keyboard.type('Viewer ' + stamp); await p.keyboard.press('Tab'); await p.keyboard.type('مشاهد ' + stamp);
const main = await p.locator('main').innerText();
L('form: ' + main.replace(/\n/g, ' | ').slice(main.indexOf('مشاهد') - 200, main.indexOf('مشاهد') + 900));
const search = p.getByRole('searchbox').or(p.getByPlaceholder(/ابحث|بحث/)).last();
L('matrix search boxes: ' + await p.getByPlaceholder(/صلاحي|ابحث/).count());
await p.getByPlaceholder(/صلاحي/).first().fill('عرض').catch(e => L('no matrix search: ' + e.message.slice(0, 80)));
await p.waitForTimeout(600);
const btn = p.getByRole('button', { name: /تحديد كل الظاهر|تحديد الكل/ }).first();
L('select-all-shown button: ' + await btn.count());
await btn.click().catch(()=>{}); await p.waitForTimeout(400);
L('checked: ' + await p.locator('main input[type=checkbox]:checked').count());
await shot(p, '07-new-role-matrix-search-select-all-ar.jpg');
await p.keyboard.press('Control+Enter'); await p.waitForTimeout(1500);
L('after save: ' + (await p.locator('main').innerText()).replace(/\n/g, ' | ').match(/.{0,100}مشاهد.{0,200}/)?.[0]);
// copy and delete through the palette
for (const word of ['نسخ', 'حذف']) {
  await p.keyboard.press('Control+k'); await p.waitForTimeout(400); await p.keyboard.type(word); await p.waitForTimeout(600);
  L(`palette '${word}': ` + (await p.locator('[role=option]').allInnerTexts()).join(' / ').slice(0, 200));
  await p.keyboard.press('Escape');
}
// effective permissions: open viewer's record, What they can do tab
await p.goto(BASE + '/identity/users'); await p.waitForTimeout(1500);
await p.keyboard.type('accountant@alnoor.example'); await p.waitForTimeout(1000); await p.keyboard.press('Enter'); await p.waitForTimeout(1000);
await p.getByRole('tab').nth(1).click(); await p.waitForTimeout(1200);
const eff = await p.locator('main').innerText();
L('what they can do: ' + eff.replace(/\n/g, ' | ').slice(eff.indexOf('ما يمكنه فعله'), eff.indexOf('ما يمكنه فعله') + 900));
await shot(p, '08-what-they-can-do-company-roles-ar.jpg');
L('console errors / 5xx: ' + JSON.stringify(errs));
L.save(); await b.close();
