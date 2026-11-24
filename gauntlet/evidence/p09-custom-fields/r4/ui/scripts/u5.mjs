// The audit log screen for one contact: custom select, date and number values as the person reads them.
import { open, shot, text, BASE } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en', id = process.argv[4];
const { browser, page, errors } = await open(email);
await page.goto(`${BASE}/audit/log?filter=${encodeURIComponent(`recordId eq '${id}'`)}`); await page.waitForTimeout(3500);
await shot(page, `${tag}-audit-log-custom-values`);
console.log((await text(page)).split('\n').filter(l => /R4/.test(l) && l.length < 200).slice(0, 14).join('\n'));
console.log('errors', errors); await browser.close();
