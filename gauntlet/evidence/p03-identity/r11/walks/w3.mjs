import { open, signIn, focused, EV, BASE } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
for (const [who, lang] of [['admin@alnoor.example', 'en'], ['admin.ar@alnoor.example', 'ar']]) {
  for (const width of [1280, 1366, 1600, 1920, 2560]) {
    const { browser, page, errors } = await open({ width, height: 900 });
    await signIn(page, who);
    await page.goto(BASE + '/identity/roles'); await page.waitForTimeout(2000);
    await page.locator('main [role=row]').filter({ hasText: lang === 'en' ? 'Administrator' : 'مدير' }).first().click(); await page.waitForTimeout(1500);
    const sizes = await page.locator('aside input[type=checkbox]').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { w: +r.width.toFixed(1), h: +r.height.toFixed(1), label: (e.getAttribute('aria-label') || e.closest('label')?.innerText || '').slice(0, 40), other: !!e.closest('.id-other') }; }).filter((x) => x.w > 0));
    const small = sizes.filter((s) => s.w < 12 || s.h < 12);
    const others = sizes.filter((s) => s.other);
    L(`${lang} ${width}px: ${sizes.length} visible checkboxes, ${small.length} under 12px; Other-column widths ${JSON.stringify(others.map((o) => o.w + 'x' + o.h + ' ' + o.label))}`);
    if (width === 1600) await page.screenshot({ path: `${EV}/${lang === 'en' ? '03' : '10'}-role-matrix-other-column-${lang}.jpg`, quality: 70, type: 'jpeg' });
    if (errors.length) L('errors ' + JSON.stringify(errors));
    await browser.close();
  }
}
fs.writeFileSync(EV + '/walks/w3-matrix-widths.txt', log.join('\n'));
