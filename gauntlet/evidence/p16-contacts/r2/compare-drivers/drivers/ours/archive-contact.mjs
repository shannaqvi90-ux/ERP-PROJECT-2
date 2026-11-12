// Critic p16-contacts r2.
import { oursAs } from '../../lib/ours-api.mjs';

const START_ACTIVE = true;
async function needle(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  const page = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(ctx.needles.contact.name)}&take=20`);
  const found = page.items.find(c => c.nameEn === ctx.needles.contact.name);
  if (!found) throw new Error('needle contact missing; start the demo with the comparison dataset');
  return { api, record: await api.get(`/api/contacts/contacts/${found.id}`) };
}
const BUTTON = START_ACTIVE ? 'aside button[data-action="archive"]' : 'aside button:has-text("Restore")';

export default {
  built: true,
  path: START_ACTIVE ? 'Archive (the record header).' : 'Restore (the record header).',
  async setup(ctx) {
    const { api, record } = await needle(ctx);
    ctx.state.contact = record;
    if (record.isActive !== START_ACTIVE) await api.post(`/api/contacts/contacts/${record.id}/${START_ACTIVE ? 'restore' : 'archive'}`);
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
    await page.goto(`${ctx.product.baseUrl}/contacts/contacts/${ctx.state.contact.id}`);
    await page.locator(BUTTON).waitFor();
  },
  ready: BUTTON,
  async run(op) {
    await op.click(BUTTON, { label: START_ACTIVE ? 'Archive' : 'Restore' });
    await op.waitFor(t => [...document.querySelectorAll('aside button')].some(b => b.textContent.trim() === t), { label: 'done', arg: START_ACTIVE ? 'Restore' : 'Archive' });
    return {};
  },
  async verify(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const c = await api.get(`/api/contacts/contacts/${ctx.state.contact.id}`);
    return { verified: c.isActive === !START_ACTIVE, details: { isActive: c.isActive } };
  },
  async cleanup(ctx) {
    if (!ctx.state.contact) return;
    const api = await oursAs(ctx.product, 'admin');
    await api.post(`/api/contacts/contacts/${ctx.state.contact.id}/restore`);
  },
};
