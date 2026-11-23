import path from 'node:path';
import { oursAs } from '../../lib/ours-api.mjs';

// The imported rows are the ones this run made: created after the clock of set-up. Contacts are archived, never
// deleted (documents keep naming them), so clean-up archives what the import added, which also keeps the next
// run's count of contacts honest (an archived contact is not offered; the list's total counts the active ones).
async function archiveImported(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  const since = ctx.state.since;
  if (!since) return;
  // Best effort, after the measured part: a refused archive stops the clean-up, not the run.
  for (let round = 0; round < 40; round++) {
    const page = await api.get(`/api/contacts/contacts?take=200&filter=${encodeURIComponent(`createdAt ge '${since}'`)}&sort=-createdAt`);
    const active = page.items.filter(c => c.isActive !== false);
    if (!active.length) return;
    await Promise.all(active.map(c => api.request('POST', `/api/contacts/contacts/${c.id}/archive`, { version: c.version }, { allow: [404, 409] })));
  }
}

export default {
  built: true,
  path: 'Click Contacts in the navigation > Import (the list\'s toolbar, Alt+I) > choose contacts-import-5000.csv (its plain headers Name, Email, Phone, Street, City, Country are matched to fields by themselves, and a header chosen once is remembered) > Import 5,000 rows. Four steps; the file of up to 5,000 rows is processed while the dialog waits.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const first = await api.get('/api/contacts/contacts?take=1');
    ctx.state.before = first.total;
    ctx.state.since = new Date(Date.now() - 1000).toISOString();
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
  },
  async run(op, ctx) {
    const page = op.page;
    const file = path.join(ctx.dataDir, 'contacts-import-5000.csv');
    await op.click(page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first(), { label: 'Contacts' });
    await op.waitFor('[data-testid="import-open"]', { label: 'contact list with its Import button' });
    await op.click('[data-testid="import-open"]', { label: 'Import' });
    await op.pickFile('[data-testid="import-file"]', file, { label: 'contacts-import-5000.csv' });
    await op.waitFor('[data-testid="import-mapping"]', { label: 'column mapping preview' });
    await op.shot('columns matched');
    await op.click('[data-testid="import-run"]', { label: 'Import 5,000 rows' });
    await op.waitFor('[data-testid="import-done"]', { label: 'import finished', timeout: 900_000 });
    return {};
  },
  async verify(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const after = (await api.get('/api/contacts/contacts?take=1')).total;
    const { first, last } = ctx.needles.import;
    const found = async name => (await api.get(`/api/contacts/contacts?take=5&search=${encodeURIComponent(name)}`)).items.some(c => c.nameEn === name);
    const ends = (await found(first)) && (await found(last));
    return { verified: after - ctx.state.before === 5000 && ends, details: { added: after - ctx.state.before, first_and_last_found: ends } };
  },
  async cleanup(ctx) { await archiveImported(ctx); },
};
