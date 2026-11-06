// Critic p16-contacts r2. Contacts are never deleted, so set-up and clean-up rename and archive any
// earlier copy (outside the measured part), so the duplicate warning is the same on every run.
import { oursAs } from '../../lib/ours-api.mjs';

const plain = s => String(s ?? '').replace(/[^\p{L}\p{N}]/gu, '');

function body(c, overrides) {
  return {
    kind: c.kind, code: c.code, nameEn: c.nameEn, nameAr: c.nameAr, organisationId: c.organisationId, jobTitle: c.jobTitle,
    email: c.email, phone: c.phone, mobile: c.mobile, website: c.website, addressLine1: c.addressLine1, addressLine2: c.addressLine2,
    area: c.area, city: c.city, emirate: c.emirate, poBox: c.poBox, country: c.country, addressAr: c.addressAr, tags: c.tags,
    tradeLicenceNumber: c.tradeLicenceNumber, taxRegistrationNumber: c.taxRegistrationNumber, defaultCurrency: c.defaultCurrency,
    creditLimitAmount: c.creditLimit?.amount ?? null, creditLimitCurrency: c.creditLimit?.currency ?? null,
    creditLimitExchangeRate: c.creditLimit?.exchangeRate ?? null, notes: c.notes, version: c.version, ...overrides,
  };
}

async function copies(api, name, email) {
  const out = new Map();
  for (const q of [name, email]) {
    const page = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(q)}&take=50`);
    for (const c of page.items) if (c.nameEn === name || c.email === email) out.set(c.id, c);
  }
  return [...out.values()];
}

async function retire(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  const { name, email } = ctx.task.input;
  for (const c of await copies(api, name, email)) {
    const full = await api.get(`/api/contacts/contacts/${c.id}`);
    await api.put(`/api/contacts/contacts/${c.id}`, body(full, { nameEn: `Retired ${Date.now()}`, email: null, phone: null, mobile: null }));
    if (full.isActive) await api.post(`/api/contacts/contacts/${c.id}/archive`);
  }
}

function build(keyboard) { return async (op, ctx) => {
  const { name, email, phone } = ctx.task.input;
  await op.click('nav a[href="/contacts/contacts"] >> nth=0', { label: 'Contacts (navigation)' });
  await op.waitFor('table[role=grid] tbody tr', { label: 'contacts list' });
  if (keyboard) await op.press('Alt+n', { label: 'New contact (Alt+N)' });
  else await op.click('main button:has-text("New contact")', { label: 'New contact' });
  await op.waitFor('[data-field="nameEn"] input:focus', { label: 'new contact form, name focused' });
  await op.type(name, { label: 'name' });
  await op.click('[data-field="email"] input', { label: 'e-mail field' });
  await op.type(email, { label: 'e-mail' });
  // The field shows +971 once focused; its digits are typed (country code included, as the instrument requires); the field keeps one +971.
  await op.click('[data-field="phone"] input', { label: 'phone field' });
  await op.type(phone.replace(/\D/g, ''), { label: 'phone' });
  await op.shot('contact filled in');
  if (keyboard) await op.press('Enter', { label: 'Save (key)' });
  else await op.click('aside button:has-text("Save")', { label: 'Save' });
  await op.waitFor(() => /\/contacts\/contacts\/[0-9a-f-]{36}/.test(location.pathname) && !document.querySelector('aside')?.innerText.includes('Unsaved changes'), { label: 'saved' });
  return {};
}; }

export default {
  built: true,
  path: 'Contacts (navigation) > New contact > name > e-mail field > e-mail > phone field > number > Save.',
  run: build(true),
  variants: {
    keyboard: { path: 'Contacts > Alt+N > name > click e-mail > e-mail > click phone > digits > Enter', run: build(true) },
    pointer: { path: 'Contacts > New contact > name > click e-mail > e-mail > click phone > digits > Save', run: build(false) },
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
    const { name, email, phone } = ctx.task.input;
    const page = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(name)}&take=50`);
    const found = page.items.filter(c => c.nameEn === name);
    return { verified: found.length === 1 && found[0].email === email && plain(found[0].phone) === plain(phone) && found[0].isActive, details: { found } };
  },
  async cleanup(ctx) { await retire(ctx); },
};
