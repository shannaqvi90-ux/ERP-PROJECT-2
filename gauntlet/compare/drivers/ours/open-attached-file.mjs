import fs from 'node:fs';
import path from 'node:path';
import { oursAs } from '../../lib/ours-api.mjs';

const ROOT = id => `/api/attachments/contacts.contacts/${id}/files`;
const NAMES = t => [t.input.file, 'delivery-note-critic.txt'];
async function removeCopies(ctx) {
  if (!ctx.state.contactId) return;
  const api = await oursAs(ctx.product, 'admin');
  const list = await api.get(ROOT(ctx.state.contactId));
  for (const file of list.items.filter(f => NAMES(ctx.task).includes(f.name))) await api.request('DELETE', `/api/attachments/contacts.contacts/files/${file.id}`);
}

export default {
  built: true,
  path: 'The file\'s name on the line under the contact\'s heading (the newest files are named there): one click opens it in the viewer.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const found = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(ctx.needles.contact.name)}`);
    let contact = found.items.find(c => c.nameEn === ctx.needles.contact.name);
    if (!contact) {
      // A driver health check (./erp verify, a clean stack without the dataset) creates the one contact.
      const { name, name_ar: nameAr, mobile, email } = ctx.needles.contact;
      if (ctx.health) contact = await api.post('/api/contacts/contacts', { kind: 'person', nameEn: name, nameAr: nameAr ?? '', mobile, email, country: 'AE', defaultCurrency: 'AED', tags: [] });
      else throw new Error(`our product does not hold the dataset contact ${name}; start it with ERP_SEED_CONTACTS_CSV=gauntlet/compare/data/out/contacts.csv on a fresh database`);
    }
    ctx.state.contactId = contact.id;
    await removeCopies(ctx);
    const bytes = fs.readFileSync(path.join(ctx.harnessDir, 'data', 'fixtures', ctx.task.input.file)).toString('base64');
    await api.post(ROOT(contact.id), { file: ctx.task.input.file, data: bytes });
    await api.post(ROOT(contact.id), { file: 'delivery-note-critic.txt', data: Buffer.from('Delivery note\n').toString('base64') });
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
    await page.goto(`${ctx.product.baseUrl}/contacts/contacts/${ctx.state.contactId}`);
    // The screen is whole before the measured part: the file names, and the latest change under the heading (it arrives last).
    await page.locator('[data-testid="attachments-names"]').waitFor();
    await page.locator('[data-testid="audit-latest"]').waitFor();
  },
  ready: '[data-testid="attachments-names"]',
  async run(op, ctx) {
    const name = op.page.locator('[data-testid="attachments-names"]').getByRole('button', { name: `Preview ${ctx.task.input.file}` });
    await op.click(name, { label: 'the file\'s name under the heading' });
    // The viewer says when it holds the file (a PDF's frame loads after the dialog opens).
    await op.waitFor(op.page.locator('[role="dialog"] .attachments-view[data-loaded="true"]'), { label: 'viewer shown' });
    return {};
  },
  async verify(ctx) {
    const shown = await ctx.read(name => ({ dialog: !!document.querySelector('[role="dialog"]'), title: document.querySelector('[role="dialog"]')?.innerText.includes(name) }), ctx.task.input.file);
    return { verified: shown.dialog && shown.title, details: shown };
  },
  async cleanup(ctx) { await removeCopies(ctx); },
};
