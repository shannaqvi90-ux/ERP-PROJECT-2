import { oursAs } from '../../lib/ours-api.mjs';

// First written by the p02 critic (round 2), owned by p02 since round 3. Shortest expert path
// through our screens: Companies (navigation) > Alt+N (English legal name focused) > the company
// name > Ctrl+S (the new company's branch line takes the focus) > the branch name > Enter. The
// company code and the branch code are optional (made from the names); the branch starts in the
// company's emirate.
//
// Companies cannot be deleted (only deactivated), so set-up and clean-up retire any earlier copy:
// renamed and deactivated, outside the measured part, as the Odoo driver does when a delete fails.

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

async function findCompanies(api, name) {
  const page = await api.get(`/api/tenancy/companies?search=${encodeURIComponent(name.split(' ')[0])}&take=200`);
  return page.items.filter(c => c.legalNameEn === name);
}

async function retire(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  const { company } = ctx.task.input;
  for (const c of await findCompanies(api, company)) {
    const full = await api.get(`/api/tenancy/companies/${c.id}`);
    await api.put(`/api/tenancy/companies/${c.id}`, body(full, { legalNameEn: `${company} (retired ${Date.now()})`, isActive: false }));
  }
}

function build(keyboard) { return async (op, ctx) => {
  const { company, branch } = ctx.task.input;
  await op.click('nav a[href="/tenancy/companies"] >> nth=0', { label: 'Companies (navigation)' });
  await op.waitFor('table[role=grid] tbody tr', { label: 'companies list' });
  if (keyboard) await op.press('Alt+n', { label: 'New company (Alt+N)' });
  else await op.click('main button.button.primary:has-text("New")', { label: 'New' });
  await op.waitFor('[data-field="legalNameEn"] input:focus', { label: 'new company form, name focused' });
  await op.type(company, { label: 'company name' });
  if (keyboard) await op.press('Control+s', { label: 'Save (Ctrl+S)' });
  else await op.click('.record-form button[type=submit]', { label: 'Save' });
  // Saving a new company moves the focus to its branch line: no click to reach it.
  await op.waitFor('input[name="branchNameEn"]:focus', { label: 'saved: branch line focused' });
  await op.type(branch, { label: 'branch name' });
  await op.shot('branch filled in');
  if (keyboard) await op.press('Enter', { label: 'Add branch (Enter)' });
  else await op.click('form.quick-add button[type=submit]', { label: 'Add branch' });
  await op.waitFor(name => [...document.querySelectorAll('.record-section table tbody tr')].some(r => r.textContent.includes(name)),
    { label: 'branch saved under the company', arg: branch });
  return {};
}; }

export default {
  built: true,
  path: 'Companies (navigation) > New > company name > Save > branch name (focused) > Add. Two expert variants; the result counts the better one per metric.',
  run: build(true),
  variants: {
    keyboard: { path: 'Companies > Alt+N > company name > Ctrl+S > branch name > Enter', run: build(true) },
    pointer: { path: 'Companies > New > company name > Save > branch name > Add', run: build(false) },
  },
  async setup(ctx) { await retire(ctx); },
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
  async verify(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { company, branch } = ctx.task.input;
    const found = await findCompanies(api, company);
    if (found.length !== 1) return { verified: false, details: { companies: found.length } };
    const filter = encodeURIComponent(`companyId eq '${found[0].id}'`);
    const branches = (await api.get(`/api/tenancy/branches?filter=${filter}&take=50`)).items;
    return {
      verified: found[0].isActive && branches.length === 1 && branches[0].nameEn === branch,
      details: { company: found[0], branches },
    };
  },
  async cleanup(ctx) { await retire(ctx); },
};
