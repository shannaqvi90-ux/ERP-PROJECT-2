import { OursApi, oursAs } from '../../lib/ours-api.mjs';

// Written by the p08 builder (round 1). Shortest expert path through our screens: Currencies
// (navigation) > the euro's row (its panel opens with today's rate field focused) > the rate >
// Enter. The rate is dated today in the workspace's time zone by the server and quoted in the
// working company's base currency (AED). Set-up removes any euro rate dated today (and puts the
// euro in use when it is not); clean-up removes the one the task saved.

const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Dubai' }).format(new Date());

async function euro(api, code) {
  const rows = (await api.get(`/api/currency/currencies?search=${code}&take=10`)).items;
  const row = rows.find(c => c.code === code);
  if (!row) throw new Error(`our product holds no ${code}`);
  if (!row.isActive) await api.put(`/api/currency/currencies/${row.id}`, { isActive: true, maxRateAgeDays: row.maxRateAgeDays, version: row.version });
  return row;
}

async function todaysRates(api, code) {
  const filter = encodeURIComponent(`currencyCode eq '${code}' and rateDate eq '${today()}'`);
  return (await api.get(`/api/currency/rates?filter=${filter}&take=10`)).items;
}

async function removeTodaysRates(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  for (const r of await todaysRates(api, ctx.task.input.currency)) await api.request('DELETE', `/api/currency/rates/${r.id}`);
}

export default {
  built: true,
  path: 'Currencies (navigation) > the euro (its panel opens on today\'s rate) > type the rate > Enter.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    await euro(api, ctx.task.input.currency);
    await removeTodaysRates(ctx);
  },
  async signIn(ctx) {
    const page = ctx.page;
    const { login, password } = ctx.product.users.admin;
    await page.goto(ctx.product.baseUrl + '/');
    await page.locator('input[name="email"]:focus').waitFor();
    await page.keyboard.type(login);
    await page.keyboard.press('Tab');
    await page.keyboard.type(password);
    await page.keyboard.press('Enter');
    await page.locator('nav[aria-label="Main navigation"]').waitFor();
    await page.locator('main h1').waitFor();
  },
  async run(op, ctx) {
    const { currency, aedPerUnit } = ctx.task.input;
    await op.click('nav a[href="/currency/currencies"] >> nth=0', { label: 'Currencies (navigation)' });
    await op.waitFor(code => [...document.querySelectorAll('table[role=grid] tbody tr td')].some(td => td.textContent.trim() === code), { label: 'currencies list', arg: currency });
    await op.click(op.page.locator('table[role=grid] tbody tr', { has: op.page.locator('td', { hasText: new RegExp(`^${currency}$`) }) }).first(), { label: `the ${currency} row` });
    await op.waitFor('.cur-quick input[name="rate"]:focus', { label: 'today\'s rate field focused' });
    await op.type(aedPerUnit, { label: 'the rate' });
    await op.shot('rate entered');
    await op.press('Enter', { label: 'save (Enter)' });
    await op.waitFor('.cur-quick [role="status"]', { label: 'saved' });
    return {};
  },
  async verify(ctx) {
    const api = new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies());
    const rows = await todaysRates(api, ctx.task.input.currency);
    return { verified: rows.length === 1 && rows[0].exchangeRate === ctx.task.input.aedPerUnit && rows[0].quoteCurrency === 'AED', details: { date: today(), rates: rows } };
  },
  async cleanup(ctx) { await removeTodaysRates(ctx); },
};
