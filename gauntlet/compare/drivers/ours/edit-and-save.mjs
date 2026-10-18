import { oursAs } from '../../lib/ours-api.mjs';

// Written by the p06 builder (form framework). Our product has no contacts yet (p16), so the
// record is the nearest one it holds with a phone number: the working company, ALN-DXB, opened at
// its own address (/tenancy/companies/<id>). The edit is the same: replace the phone number, save.
//
// Shortest expert path (round 7): click the phone field (the click selects the number after the
// +971 the field keeps, so typing replaces it) > type the number without its spaces (the field
// groups the digits itself). Typing the last digit of a whole UAE number saves the record when it
// is the record's only change (the form kernel's completed entry; Alt+Z undoes it), so no save key
// is pressed. Round 13 (p01, lead decision on critic p06 r7's question): the field keeps +971 as its
// own (the product's HOME_PREFIX, for every user and record), so the person types only the number
// after it (505550199); the task declares that prefix for this field, the instrument checks the field
// shows it as the clock starts, and the back end must hold the whole international number.

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

/** The number as an expert types it into our field: no spaces ('+971 50 555 0199' is +971505550199). */
function compact(phone) {
  return phone.replace(/\s+/g, '');
}

/** The number after the +971 the field keeps, without spaces ('+971 50 555 0199' is 505550199). */
function national(phone) {
  return compact(phone).replace(/^\+971/, '');
}

function build(how) {
  return async (op, ctx) => {
    await op.click(PHONE, { label: 'phone field (selects the number after +971)' });
    const number = how === 'spaced' ? ctx.task.input.phone : how === 'national' ? national(ctx.task.input.phone) : compact(ctx.task.input.phone);
    await op.type(number, { label: 'new number (its last digit saves)' });
    await op.waitFor(() => document.querySelector('.record-form .notice')?.getAttribute('role') === 'status', { label: 'saved' });
    return {};
  };
}

export default {
  built: true,
  path: 'Click the phone field (the number after +971 is selected) > type the number after +971; its last digit saves the record. Stand-in record: a company (contacts arrive with p16).',
  run: build('national'),
  variants: {
    national: { path: 'Click the phone field > type 505550199, the number after the +971 the field keeps (the last digit saves)', run: build('national') },
    compact: { path: 'Click the phone field > type +971505550199 (the field groups it; the last digit saves)', run: build('compact') },
    spaced: { path: 'Click the phone field > type +971 50 555 0199 as written (the last digit saves)', run: build('spaced') },
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
