import { open, shot, BASE } from './lib.mjs';
const { browser, page, errors } = await open('admin.ar@alnoor.example');
const log = (...a) => console.log(...a);
const focusName = () => page.evaluate(() => document.activeElement?.getAttribute('name'));
const stamp = process.argv[2];
async function tabTo(name, max = 8) { for (let i = 0; i < max; i++) { if ((await focusName()) === name) return true; await page.keyboard.press('Tab'); } return (await focusName()) === name; }
async function create({ en, ar, type, decimals, required, def, target }) {
  await page.keyboard.press('Alt+n'); await page.waitForTimeout(700);
  await page.keyboard.type(en); await page.keyboard.press('Tab'); await page.keyboard.type(ar);
  await tabTo('type'); await page.keyboard.type(type); await page.waitForTimeout(200);
  if (type === 'اختيار') { await tabTo('options'); await page.keyboard.type('MSC | إم إس سي'); await page.keyboard.press('Enter'); await page.keyboard.type('Maersk | ميرسك'); }
  if (decimals !== undefined) { await tabTo('decimals'); await page.keyboard.press('Control+a'); await page.keyboard.type(String(decimals)); }
  if (target) { await tabTo('lookupTarget'); await page.keyboard.type(target); }
  if (required) { await tabTo('required'); await page.keyboard.press('Space'); }
  if (def !== undefined) { if (await tabTo('defaultValue')) { await page.keyboard.type(def); } else log('no default field reached for', en); }
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1800);
  const txt = await page.locator('main').innerText();
  log(en, (txt.includes('تم الحفظ')||txt.includes('Saved.')) ? 'SAVED' : 'NOT SAVED: ' + txt.slice(-600));
}
await page.goto(BASE + '/customfields/fields'); await page.waitForTimeout(2000);
await create({ en: `Shipping line ${stamp}`, ar: `خط الشحن ${stamp}`, type: 'اختيار', def: 'م' });
await create({ en: `Bank guarantee ${stamp}`, ar: `الضمان البنكي ${stamp}`, type: 'رقم', decimals: 2, def: '١٢٠٠٫٥' });
await create({ en: `Guarantee expiry ${stamp}`, ar: `انتهاء الضمان ${stamp}`, type: 'تاريخ', def: '٢٠٢٧-٠١-٣١' });
await shot(page, 'ar-fields-created');
log(errors);
await browser.close();
