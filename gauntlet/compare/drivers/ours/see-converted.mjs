import { oursAs } from '../../lib/ours-api.mjs';

// Written by the p08 builder (round 1). Shortest expert path through our screens: Currencies
// (navigation) > the euro's row (its panel opens with the converter below today's rate) > click the
// converter's amount > type the amount: the amount in AED, the rate used and its date show as it is
// typed. Set-up makes today's euro rate the task's rate (the euro put in use first when it is not);
// clean-up removes that rate.

const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Dubai' }).format(new Date());

async function removeTodaysRates(api, code) {
  const filter = encodeURIComponent(`currencyCode eq '${code}' and rateDate eq '${today()}'`);
  for (const r of (await api.get(`/api/currency/rates?filter=${filter}&take=10`)).items) await api.request('DELETE', `/api/currency/rates/${r.id}`);
}

export default {
  built: true,
  path: 'Currencies (navigation) > the euro > the converter\'s amount > type the amount (the amount in AED shows as it is typed).',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { currency, aedPerUnit } = ctx.task.input;
    const row = (await api.get(`/api/currency/currencies?search=${currency}&take=10`)).items.find(c => c.code === currency);
    if (!row) throw new Error(`our product holds no ${currency}`);
    if (!row.isActive) await api.put(`/api/currency/currencies/${row.id}`, { isActive: true, maxRateAgeDays: row.maxRateAgeDays, version: row.version });
    await removeTodaysRates(api, currency);
    await api.post('/api/currency/rates', { currency, rate: aedPerUnit, reference: 'Comparison task see-converted' });
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
    const { currency, amount } = ctx.task.input;
    await op.click('nav a[href="/currency/currencies"] >> nth=0', { label: 'Currencies (navigation)' });
    await op.waitFor(code => [...document.querySelectorAll('table[role=grid] tbody tr td')].some(td => td.textContent.trim() === code), { label: 'currencies list', arg: currency });
    await op.click(op.page.locator('table[role=grid] tbody tr', { has: op.page.locator('td', { hasText: new RegExp(`^${currency}$`) }) }).first(), { label: `the ${currency} row` });
    await op.waitFor('.cur-convert input[name="amount"]', { label: 'the converter' });
    await op.click('.cur-convert input[name="amount"]', { label: 'the amount' });
    await op.type(amount, { label: 'amount' });
    await op.waitFor(a => (document.querySelector('[data-testid="conversion"] .cur-convert-figure')?.textContent || '').replace(/\s/g, ' ').includes(a),
      { label: 'converted', arg: `AED ${ctx.task.input.aed}` });
    await op.shot('converted amount');
    return {};
  },
  async verify(ctx) {
    const shown = await ctx.read(() => (document.querySelector('[data-testid="conversion"]')?.textContent || '').replace(/\s/g, ' '));
    const { amount, currency, aed } = ctx.task.input;
    const api = await oursAs(ctx.product, 'admin');
    const answer = await api.get(`/api/currency/convert?amount=${amount}&currency=${currency}`);
    return {
      verified: shown.includes(`AED ${aed}`) && answer.money.baseAmount === aed.replace(/,/g, '') && answer.rateDate === today(),
      details: { shown, baseAmount: answer.money.baseAmount, rate: answer.money.exchangeRate, rateDate: answer.rateDate },
    };
  },
  async cleanup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    await removeTodaysRates(api, ctx.task.input.currency);
  },
};
