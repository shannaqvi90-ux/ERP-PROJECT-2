import { open, shot, BASE } from './lib.mjs';
const { browser, page, errors } = await open('admin.ar@alnoor.example');
const log = (...a) => console.log(...a);
const fn = () => page.evaluate(() => document.activeElement?.getAttribute('name'));
async function tabTo(name, max = 10) { for (let i = 0; i < max; i++) { if ((await fn()) === name) return true; await page.keyboard.press('Tab'); } return (await fn()) === name; }
const stamp = process.argv[2];
async function create({ en, ar, down, decimals, options, defDown, def }) {
  await page.keyboard.press('Alt+n'); await page.waitForTimeout(700);
  await page.keyboard.type(en); await page.keyboard.press('Tab'); await page.keyboard.type(ar);
  await tabTo('type'); for (let i = 0; i < down; i++) await page.keyboard.press('ArrowDown');
  log(en, 'type =', await page.locator('[name=type]').inputValue());
  if (options) { await tabTo('options'); for (const [k, o] of options.entries()) { if (k) await page.keyboard.press('Enter'); await page.keyboard.type(o); } }
  if (decimals !== undefined) { await tabTo('decimals'); await page.keyboard.press('Control+a'); await page.keyboard.type(String(decimals)); }
  if (defDown) { await tabTo('defaultValue'); for (let i = 0; i < defDown; i++) await page.keyboard.press('ArrowDown'); }
  if (def) { await tabTo('defaultValue'); await page.keyboard.type(def); }
  await page.keyboard.press('Control+s'); await page.waitForTimeout(1800);
  const txt = await page.locator('main').innerText();
  log(en, /تم الحفظ/.test(txt) ? 'SAVED' : 'NOT SAVED ' + txt.slice(-300));
}
await page.goto(BASE + '/customfields/fields'); await page.waitForTimeout(2000);
await create({ en: `Shipping line ${stamp}`, ar: `خط الشحن ${stamp}`, down: 4, options: ['MSC | إم إس سي', 'Maersk | ميرسك'], defDown: 2 });
await create({ en: `Bank guarantee ${stamp}`, ar: `الضمان البنكي ${stamp}`, down: 1, decimals: 2, def: '١٢٠٠٫٥' });
await create({ en: `Guarantee expiry ${stamp}`, ar: `انتهاء الضمان ${stamp}`, down: 2, def: '٢٠٢٧-٠١-٣١' });
await shot(page, 'ar-field-created');
log(errors); await browser.close();
