import { open, shot, BASE } from './lib.mjs';
const { browser, page, errors } = await open('admin@alnoor.example');
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
  log(en, /Saved/.test(txt) ? 'SAVED' : 'NOT SAVED ' + txt.slice(-300));
}
await page.goto(BASE + '/customfields/fields'); await page.waitForTimeout(2000);
await create({ en: `Visa expiry ${stamp}`, ar: `انتهاء التأشيرة ${stamp}`, down: 2, def: '2027-01-31' });
log('placeholder/type of default:', await page.locator('[name=defaultValue]').evaluate(e => e.type + ' ' + e.value + ' ph=' + e.placeholder));
await create({ en: `Visa expiry b ${stamp}`, ar: `انتهاء التأشيرة ب ${stamp}`, down: 2, def: '01312027' });
log('placeholder/type of default:', await page.locator('[name=defaultValue]').evaluate(e => e.type + ' ' + e.value + ' ph=' + e.placeholder));
await shot(page, 'x-en-date-default');
log(errors); await browser.close();
