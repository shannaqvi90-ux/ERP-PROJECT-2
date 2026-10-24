import { OursApi, oursAs } from '../../lib/ours-api.mjs';

// Written by the p10 builder (round 1). Shortest expert path through our screens: Document
// numbering (navigation) > the purchase order row (the working company's sequences come first; its
// panel opens with the whole prefix chosen) > type the prefix > Tab > type the digits > Enter. The
// panel previews the next number as it is typed. Set-up puts the working company's purchase order
// sequence back to a start like the reference's default (prefix P, 5 digits, never starting again);
// clean-up gives it back the product's own default (PO-{YYYY}-, 5 digits, a new run each year).

const START = { prefix: 'P', suffix: '', padding: 5, reset: 'never', gapless: false, perBranch: false, nextNumber: null };
const DEFAULT = { prefix: 'PO-{YYYY}-', suffix: '', padding: 5, reset: 'yearly', gapless: false, perBranch: false, nextNumber: null };

async function purchaseOrders(api) {
  const rows = (await api.get('/api/numbering/sequences?take=200')).items;
  const row = rows.find(r => r.documentType === 'purchasing.order' && r.workingCompany);
  if (!row) throw new Error('our product holds no purchase order sequence in the working company');
  return row;
}

async function put(ctx, values) {
  const api = await oursAs(ctx.product, 'admin');
  const row = await purchaseOrders(api);
  const current = await api.get(`/api/numbering/sequences/${row.id}`);
  await api.put(`/api/numbering/sequences/${row.id}`, { ...values, version: current.version });
  return row.id;
}

export default {
  built: true,
  path: 'Document numbering (navigation) > Purchase order (its panel opens on the prefix, chosen) > type the prefix > Tab > type the digits > Enter.',
  async setup(ctx) {
    ctx.state.sequenceId = await put(ctx, START);
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
    const { prefix, digits } = ctx.task.input;
    await op.click('nav a[href="/numbering/sequences"] >> nth=0', { label: 'Document numbering (navigation)' });
    await op.waitFor(() => [...document.querySelectorAll('table[role=grid] tbody tr td')].some(td => td.textContent.trim() === 'Purchase order'), { label: 'sequences list' });
    await op.click(op.page.locator('table[role=grid] tbody tr', { has: op.page.locator('td', { hasText: /^Purchase order$/ }) }).first(), { label: 'the purchase order row' });
    await op.waitFor('.list-record input[name="prefix"]:focus', { label: 'prefix field focused, its text chosen' });
    await op.type(prefix, { label: 'the prefix' });
    await op.press('Tab', { label: 'to the digits' });
    await op.type(String(digits), { label: 'the digits' });
    await op.shot('sequence edited');
    await op.press('Enter', { label: 'save (Enter)' });
    await op.waitFor('.list-record [role="status"]', { label: 'saved' });
    return {};
  },
  async verify(ctx) {
    const api = new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies());
    const s = await api.get(`/api/numbering/sequences/${ctx.state.sequenceId}`);
    const { prefix, digits } = ctx.task.input;
    return {
      verified: s.prefix === prefix && s.padding === digits && new RegExp(`^${prefix}\\d{${digits}}$`).test(s.nextNumber),
      details: { prefix: s.prefix, padding: s.padding, next_number: s.nextNumber },
    };
  },
  async cleanup(ctx) { if (ctx.state.sequenceId) await put(ctx, DEFAULT); },
};
