import { OursApi, oursAs } from '../../lib/ours-api.mjs';

// Written by the p02 critic (round 2). With up to six companies, every other company has its own
// button beside the working-company switcher in the top bar: one click switches. The keyboard
// variant is Alt+C > part of the name > Enter. Set-up makes the two fixture companies (the
// administrator creates them, so works in both) with one branch each and starts the
// administrator in "from".

const CODES = { 'Demo Trading LLC': 'DEMO-TRD', 'Demo Manufacturing FZE': 'DEMO-MFG' };
const ARABIC = { 'Demo Trading LLC': 'ديمو للتجارة ذ.م.م', 'Demo Manufacturing FZE': 'ديمو للتصنيع م.م.ح' };

async function ensureCompany(api, name) {
  const page = await api.get(`/api/tenancy/companies?search=${encodeURIComponent(CODES[name])}&take=50`);
  let company = page.items.find(c => c.code === CODES[name]);
  if (!company) {
    company = await api.post('/api/tenancy/companies', {
      code: CODES[name], legalNameEn: name, legalNameAr: ARABIC[name], baseCurrency: 'AED',
      fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true,
    });
  }
  const filter = encodeURIComponent(`companyId eq '${company.id}'`);
  const branches = (await api.get(`/api/tenancy/branches?filter=${filter}&take=50`)).items;
  if (branches.length === 0) {
    await api.post('/api/tenancy/branches', { companyId: company.id, code: 'MAIN', nameEn: 'Main office', nameAr: 'المكتب الرئيسي', country: 'AE', isActive: true });
  }
  return company;
}

function build(keyboard) {
  return async (op, ctx) => {
    const { to } = ctx.task.input;
    if (keyboard) {
      await op.press('Alt+c', { label: 'company switcher (Alt+C)' });
      await op.waitFor('.workplace-popover input:focus', { label: 'switcher open, filter focused' });
      await op.type('manuf', { label: 'part of the company name' });
      await op.press('Enter', { label: 'switch (Enter)' });
    } else {
      await op.click(`.workplace-chip[data-company="${CODES[to]}"]`, { label: `${to} (one-click button)` });
    }
    await op.waitFor(code => (document.querySelector('[data-testid="workplace"]')?.textContent || '').startsWith(code + ' ')
      && !document.querySelector('.workplace-popover'), { label: 'working in the other company', arg: CODES[to] });
    return {};
  };
}

export default {
  built: true,
  path: 'Click the other company\'s button beside the working-company switcher in the top bar. Two expert variants; the result counts the better one per metric.',
  run: build(false),
  variants: {
    pointer: { path: 'click the company button in the top bar', run: build(false) },
    keyboard: { path: 'Alt+C > type "manuf" > Enter', run: build(true) },
  },
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { from, to } = ctx.task.input;
    const fromCompany = await ensureCompany(api, from);
    ctx.state.to = await ensureCompany(api, to);
    await api.put('/api/tenancy/workplace', { companyId: fromCompany.id, branchId: null });
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
    await page.locator('[data-testid="workplace"]').waitFor();
  },
  ready: '[data-testid="workplace"]',
  async verify(ctx) {
    const shown = await ctx.read(() => ({
      label: document.querySelector('[data-testid="workplace"]')?.textContent?.trim(),
      title: document.querySelector('[data-testid="workplace"]')?.getAttribute('title'),
    }));
    const workplace = await new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies()).get('/api/tenancy/workplace');
    return {
      verified: workplace.companyId === ctx.state.to.id && shown.label.startsWith(CODES[ctx.task.input.to]),
      details: { shown, companyId: workplace.companyId },
    };
  },
  async cleanup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const companies = (await api.get('/api/tenancy/companies?search=ALN-DXB&take=5')).items;
    const home = companies.find(c => c.code === 'ALN-DXB');
    if (home) await api.put('/api/tenancy/workplace', { companyId: home.id, branchId: null });
  },
};
