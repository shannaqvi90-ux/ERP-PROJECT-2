import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { oursAs } from '../../lib/ours-api.mjs';
import { readFirstSheet } from '../../lib/xlsx.mjs';

// The Tags column's filter lists the tags the workspace already uses; a click on one filters the list at once, so nothing is
// typed and the path does not depend on which rows are on the first screen (the 100,000-contact comparison data has the first
// "Government" row around the 139th by name). The list's own "Print or export" menu writes every row the filter selects.
//
// Shortest expert path: Contacts (navigation) > Tags column options > Filter... > click the tag > Print or export > Excel.
// Round 2's tag button in a row (also still there) only helps when a row carrying the tag is on screen.

export default {
  built: true,
  path: 'Contacts (navigation) > Tags column options > Filter... > click the tag in the list of tags in use (the list is then filtered to it) > Print or export > Excel in English.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const tag = ctx.task.input.tag;
    const found = await api.get(`/api/contacts/contacts?take=1&filter=${encodeURIComponent(`tags contains '${tag}'`)}`);
    if (!found.total) throw new Error(`our product holds no contact tagged ${tag}; start it with ERP_SEED_CONTACTS_CSV=gauntlet/compare/data/out/contacts.csv on a fresh database`);
    ctx.state.expected = found.total;
    ctx.state.dir = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-export-'));
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
    const { tag } = ctx.task.input;
    const page = op.page;
    await op.click(page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first(), { label: 'Contacts' });
    const menuButton = page.getByRole('button', { name: 'Options for the column Tags', exact: true }).first();
    await op.click(menuButton, { label: 'Tags column options' });
    await op.click(page.getByRole('menuitem', { name: 'Filter…', exact: true }), { label: 'Filter...' });
    const choice = page.getByRole('listbox').getByRole('button', { name: tag, exact: true });
    await op.waitFor(choice, { label: 'the tags in use' });
    await op.click(choice, { label: 'the ' + tag + ' tag' });
    await op.waitFor(n => {
      const grid = document.querySelector('table[role=grid]');
      return Number(grid?.getAttribute('aria-rowcount')) === n + 1;
    }, { label: 'filtered list', arg: ctx.state.expected });
    await op.shot('export options chosen');
    await op.click(page.getByRole('button', { name: 'Print or export' }), { label: 'Print or export' });
    const excel = page.getByRole('menuitem', { name: 'Excel in English', exact: true });
    await op.waitFor(excel, { label: 'print menu' });
    await op.clickForDownload(excel, ctx.state.dir, { label: 'Excel in English (download)' });
    return {};
  },
  async verify(ctx) {
    const [download] = ctx.downloads;
    if (!download) return { verified: false, details: { file: null } };
    const rows = readFirstSheet(download.file);
    const header = rows[0] || [];
    const has = re => header.some(h => re.test(String(h)));
    const data = rows.slice(1).filter(r => r.some(c => c !== '' && c != null));
    const ok = has(/^name$/i) && has(/e-?mail/i) && has(/phone/i) && data.length === ctx.state.expected;
    return { verified: ok, details: { file: path.basename(download.file), header, rows: data.length, expected_rows: ctx.state.expected } };
  },
  async cleanup(ctx) { if (ctx.state.dir) fs.rmSync(ctx.state.dir, { recursive: true, force: true }); },
};
