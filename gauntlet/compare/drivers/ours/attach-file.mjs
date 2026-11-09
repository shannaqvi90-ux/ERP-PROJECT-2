import path from 'node:path';
import { OursApi, oursAs } from '../../lib/ours-api.mjs';

// Written by the p11 builder (round 1). Shortest expert path through our screens: the contact is open
// and its Attachments section is on the form under the fields, with an "Add files" button. One
// action: pick the file at that button (the file dialog and the choice of the file). The file is sent
// at once, listed on the contact when the server has stored it. Odoo needs the paperclip, then "Attach
// files", then the file: ours is one step shorter (docs/decisions/p11-attachments-compare-paths.md).
// Set-up removes any earlier copy of the file from the contact; clean-up removes the one this run attached.

const ROOT = id => `/api/attachments/contacts.contacts/${id}/files`;

async function removeCopies(ctx) {
  if (!ctx.state.contactId) return;
  const api = await oursAs(ctx.product, 'admin');
  const list = await api.get(ROOT(ctx.state.contactId));
  for (const file of list.items.filter(f => f.name === ctx.task.input.file)) await api.request('DELETE', `/api/attachments/contacts.contacts/files/${file.id}`);
}

export default {
  built: true,
  path: 'Pick the file at the "Add files" button of the contact\'s Attachments section (the file dialog and the choice of the file are one step); the file is listed on the contact.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { name, name_ar: nameAr, mobile, email } = ctx.needles.contact;
    const found = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(name)}`);
    let contact = found.items.find(c => c.nameEn === name);
    if (!contact) {
      // A driver health check (./erp verify, a clean stack without the dataset) creates the one contact.
      if (ctx.health) contact = await api.post('/api/contacts/contacts', { kind: 'person', nameEn: name, nameAr: nameAr ?? '', mobile, email, country: 'AE', defaultCurrency: 'AED', tags: [] });
      else throw new Error(`our product does not hold the dataset contact ${name}; start it with ERP_SEED_CONTACTS_CSV=gauntlet/compare/data/out/contacts.csv on a fresh database`);
    }
    ctx.state.contactId = contact.id;
    await removeCopies(ctx);
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
    await page.goto(`${ctx.product.baseUrl}/contacts/contacts?open=${ctx.state.contactId}`);
    await page.locator('[data-testid="attachments"]').waitFor();
  },
  ready: '[data-testid="attachments"]',
  async run(op, ctx) {
    const file = path.join(ctx.harnessDir, 'data', 'fixtures', ctx.task.input.file);
    await op.pickFile(op.page.locator('[data-testid="attachments"]').getByRole('button', { name: 'Add files', exact: true }), file, { label: ctx.task.input.file });
    await op.waitFor(name => [...document.querySelectorAll('[data-testid="attachments"] table tbody tr')].some(r => r.innerText.includes(name))
      && !document.querySelector('[data-testid="attachments"] .attachments-progress'),
    { label: 'file listed on the contact', arg: ctx.task.input.file });
    return {};
  },
  async verify(ctx) {
    const api = new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies());
    const list = await api.get(ROOT(ctx.state.contactId));
    const rows = list.items.filter(f => f.name === ctx.task.input.file);
    const shown = await ctx.read(name => ({
      listed: [...document.querySelectorAll('[data-testid="attachments"] table tbody tr')].filter(r => r.innerText.includes(name)).length,
    }), ctx.task.input.file);
    return { verified: rows.length === 1 && rows[0].sizeBytes > 0 && shown.listed === 1, details: { attachments: rows, shown } };
  },
  async cleanup(ctx) { await removeCopies(ctx); },
};
