import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { adminRpc, openApp, signInAs } from './_common.mjs';
import { readFirstSheet } from '../../lib/xlsx.mjs';

export default {
  built: true,
  path: 'Apps menu > Contacts > type the tag > choose "Search Tag for" > header check box > Select all > Actions > Export > Export (the dialog proposes the list\'s columns: name, e-mail, phone and more).',
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    ctx.state.expected = await rpc.searchCount('res.partner', [['category_id.name', '=', ctx.task.input.tag]]);
    ctx.state.dir = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-export-'));
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op, ctx) {
    const { tag } = ctx.task.input;
    const page = op.page;
    await openApp(op, 'Contacts');
    await op.waitFor('.o_searchview_input:focus', { label: 'contact list, search focused' });
    await op.type(tag, { label: 'tag' });
    const option = page.locator('.o_searchview_autocomplete .dropdown-item', { hasText: /Search\s+Tag\s+for/ });
    await op.waitFor(option, { label: 'search options' });
    await op.click(option, { label: 'Search Tag for' });
    await op.waitFor(n => {
      const pager = document.querySelector('.o_pager_limit')?.textContent.replace(/\D/g, '');
      return !document.querySelector('.o_loading_indicator') && Number(pager) === n;
    }, { label: 'filtered list', arg: ctx.state.expected });
    await op.click('.o_list_view thead .o_list_record_selector input', { label: 'select the page' });
    const all = page.locator('button.o_select_domain, .o_list_select_domain');
    await op.waitFor(all, { label: 'select-all offer' });
    await op.click(all, { label: 'Select all' });
    await op.click(page.getByRole('button', { name: 'Actions' }), { label: 'Actions' });
    await op.click(page.locator('.o-dropdown--menu .dropdown-item', { hasText: /^Export$/ }), { label: 'Export' });
    await op.waitFor('.modal .o_export_field, .modal .o_fields_list li', { label: 'export dialog' });
    await op.shot('export dialog');
    ctx.state.file = await op.clickForDownload(page.locator('.modal-footer button', { hasText: /^Export$/ }), ctx.state.dir, { label: 'Export (download)' });
    return {};
  },
  async verify(ctx) {
    const rows = readFirstSheet(ctx.state.file);
    const header = rows[0] || [];
    const has = re => header.some(h => re.test(h));
    const data = rows.slice(1).filter(r => r.some(c => c !== ''));
    const ok = has(/name/i) && has(/e-?mail/i) && has(/phone/i) && data.length === ctx.state.expected;
    return { verified: ok, details: { file: path.basename(ctx.state.file), header, rows: data.length, expected_rows: ctx.state.expected } };
  },
  async cleanup(ctx) { if (ctx.state.dir) fs.rmSync(ctx.state.dir, { recursive: true, force: true }); },
};
