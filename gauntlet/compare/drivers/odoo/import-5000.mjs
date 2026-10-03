import path from 'node:path';
import { adminRpc, openApp, signInAs } from './_common.mjs';

// Rows from contacts-import-5000.csv: no internal reference, and e-mails only that file uses.
const IMPORTED = ['&', ['ref', '=', false], '|', ['email', '=like', '%.imp%@mail.example'], ['email', '=like', 'accounts%@import-%.example']];

async function removeImported(ctx) {
  const rpc = await adminRpc(ctx);
  for (;;) {
    const ids = await rpc.search('res.partner', IMPORTED, { limit: 1000 });
    if (!ids.length) return;
    await rpc.unlink('res.partner', ids);
  }
}

export default {
  built: true,
  path: 'Apps menu > Contacts > actions menu (⋮) > Import > Upload (choose the file) > Import. Odoo maps the plain column headers (Name, Email, Phone, Street, City, Country) to contact fields by itself.',
  async setup(ctx) {
    await removeImported(ctx);
    ctx.state.before = await (await adminRpc(ctx)).searchCount('res.partner', []);
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op, ctx) {
    const page = op.page;
    const file = path.join(ctx.dataDir, 'contacts-import-5000.csv');
    await openApp(op, 'Contacts');
    await op.waitFor('.o_data_row', { label: 'contact list' });
    await op.click('.o_control_panel .o_cp_action_menus button', { label: 'actions menu' });
    await op.click(page.locator('.o-dropdown--menu .o-dropdown-item, .o-dropdown--menu .dropdown-item', { hasText: /^\s*Import\s*$/ }), { label: 'Import' });
    await op.waitFor(page.getByRole('button', { name: 'Upload' }).first(), { label: 'import screen' });
    await op.pickFile(page.getByRole('button', { name: 'Upload' }).first(), file, { label: 'contacts-import-5000.csv' });
    await op.waitFor(page.getByRole('button', { name: 'Import', exact: true }).first(), { label: 'column mapping preview' });
    await op.shot('columns matched');
    await op.click(page.getByRole('button', { name: 'Import', exact: true }).first(), { label: 'Import' });
    await op.waitFor(() => /Imported records/.test(document.querySelector('.o_control_panel')?.innerText || '')
      && /\/\s*5,?000\b/.test(document.querySelector('.o_pager')?.innerText || ''), { label: 'import finished, imported records listed', timeout: 900_000 });
    return {};
  },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const imported = await rpc.searchCount('res.partner', IMPORTED);
    const after = await rpc.searchCount('res.partner', []);
    const { first, last } = ctx.needles.import;
    const ends = await rpc.searchCount('res.partner', ['&', ['ref', '=', false], ['name', 'in', [first, last]]]);
    return { verified: imported === 5000 && after - ctx.state.before === 5000 && ends >= 2, details: { imported, added: after - ctx.state.before, first_and_last_found: ends } };
  },
  async cleanup(ctx) { await removeImported(ctx); },
};
