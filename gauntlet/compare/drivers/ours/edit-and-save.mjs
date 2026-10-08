import { oursAs } from '../../lib/ours-api.mjs';

// Written by the p06 builder (form framework). Our product has no contacts yet (p16), so the
// record is the nearest one it holds with a phone number: the working company, ALN-DXB, opened at
// its own address (/tenancy/companies/<id>). The edit is the same: replace the phone number, save.
//
// Shortest expert path: click the phone field (the first click selects the whole number, as Tab
// would, so typing replaces it) > type the number > Ctrl+S (or click Save).

const CODE = 'ALN-DXB';

function body(c, overrides) {
  return {
    code: c.code, legalNameEn: c.legalNameEn, legalNameAr: c.legalNameAr,
    tradeLicenceNumber: c.tradeLicenceNumber, tradeLicenceAuthority: c.tradeLicenceAuthority,
    taxRegistrationNumber: c.taxRegistrationNumber, baseCurrency: c.baseCurrency,
    fiscalYearStartMonth: c.fiscalYearStartMonth, fiscalYearStartDay: c.fiscalYearStartDay,
    addressLine1: c.addressLine1, addressLine2: c.addressLine2, city: c.city, emirate: c.emirate,
    poBox: c.poBox, country: c.country, addressAr: c.addressAr, phone: c.phone, email: c.email,
    website: c.website, isActive: c.isActive, version: c.version, ...overrides,
  };
}

async function company(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  const page = await api.get(`/api/tenancy/companies?search=${encodeURIComponent(CODE)}&take=50`);
  const found = page.items.find(c => c.code === CODE);
  if (!found) throw new Error(`our product has no company ${CODE}; start it with the demo data`);
  return { api, record: await api.get(`/api/tenancy/companies/${found.id}`) };
}

const PHONE = '[data-field="phone"] input';

function build(keyboard) {
  return async (op, ctx) => {
    await op.click(PHONE, { label: 'phone field' });
    await op.type(ctx.task.input.phone, { label: 'new number' });
    if (keyboard) await op.press('Control+s', { label: 'Save (Ctrl+S)' });
    else await op.click('.record-form button[type=submit]', { label: 'Save' });
    await op.waitFor(() => document.querySelector('.record-form .notice')?.getAttribute('role') === 'status', { label: 'saved' });
    return {};
  };
}

export default {
  built: true,
  path: 'Click the phone field (its number is selected) > type the number > Save. Stand-in record: a company (contacts arrive with p16).',
  run: build(true),
  variants: {
    keyboard: { path: 'Click the phone field > type the number > Ctrl+S', run: build(true) },
    pointer: { path: 'Click the phone field > type the number > click Save', run: build(false) },
  },
  async setup(ctx) {
    const { api, record } = await company(ctx);
    ctx.state.company = { id: record.id, phone: record.phone };
    // Start state: a number that is not the task's.
    if (record.phone === ctx.task.input.phone) await api.put(`/api/tenancy/companies/${record.id}`, body(record, { phone: '+971 4 555 0100' }));
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
    await page.goto(`${ctx.product.baseUrl}/tenancy/companies/${ctx.state.company.id}`);
    await page.locator(PHONE).waitFor();
  },
  ready: PHONE,
  async verify(ctx) {
    const { record } = await company(ctx);
    const shown = await ctx.page.locator(PHONE).inputValue();
    const unsaved = await ctx.page.locator('.record-header').textContent();
    return {
      verified: record.phone === ctx.task.input.phone && shown === ctx.task.input.phone && !/Unsaved/.test(unsaved || ''),
      details: { saved: record.phone, shown, record: `company ${CODE}` },
    };
  },
  async cleanup(ctx) {
    if (!ctx.state.company) return;
    const { api, record } = await company(ctx);
    if (record.phone !== ctx.state.company.phone) await api.put(`/api/tenancy/companies/${record.id}`, body(record, { phone: ctx.state.company.phone }));
  },
};
