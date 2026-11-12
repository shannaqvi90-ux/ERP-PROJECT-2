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
  path: 'Scroll to the file\'s row, then the Preview button on the file\'s row in the contact\'s Attachments section.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const found = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(ctx.needles.contact.name)}`);
    const contact = found.items.find(c => c.nameEn === ctx.needles.contact.name);
    if (!contact) throw new Error('dataset contact missing');
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
    await page.locator('[data-testid="attachments"] table tbody tr').first().waitFor();
  },
  ready: '[data-testid="attachments"] table tbody tr',
  async run(op, ctx) {
    const btn = op.page.locator('[data-testid="attachments"]').getByRole('button', { name: `Preview ${ctx.task.input.file}` }).last();
    await op.scrollTo(btn, { label: 'scroll to the file' });
    await op.click(btn, { label: 'Preview' });
    await op.waitFor(op.page.locator('[role="dialog"]'), { label: 'viewer shown' });
    return {};
  },
  async verify(ctx) {
    const shown = await ctx.read(name => ({ dialog: !!document.querySelector('[role="dialog"]'), title: document.querySelector('[role="dialog"]')?.innerText.includes(name) }), ctx.task.input.file);
    return { verified: shown.dialog && shown.title, details: shown };
  },
  async cleanup(ctx) { await removeCopies(ctx); },
};
