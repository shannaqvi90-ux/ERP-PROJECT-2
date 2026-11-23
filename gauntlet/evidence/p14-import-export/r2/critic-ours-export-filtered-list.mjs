import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { oursAs } from '../../lib/ours-api.mjs';
import { readFirstSheet } from '../../lib/xlsx.mjs';

// The contacts list shows each tag of a contact as a button (the tags are one text, "VIP; Government"); a click on a tag lists
// the contacts whose tags contain it, so nothing is typed. The list's own "Print or export" menu writes every row the filter
// selects, not the page on screen.
//
// Shortest expert path: Contacts (navigation) > click the tag in a row > Print or export > Excel in English.
// (Round 1's path, the column's Filter... popover with the tag typed, took 8 steps and 11 keystrokes and is still there for a
// column that has no tag buttons.)

const waitFiltered = (op, ctx) => op.waitFor(n => {
  const grid = document.querySelector('table[role=grid]');
  return Number(grid?.getAttribute('aria-rowcount')) === n + 1;
}, { label: 'filtered list', arg: ctx.state.expected });

async function exportExcel(op, ctx) {
  const page = op.page;
  await op.shot('export options chosen');
  await op.click(page.getByRole('button', { name: 'Print or export' }), { label: 'Print or export' });
  const excel = page.getByRole('menuitem', { name: 'Excel in English', exact: true });
  await op.waitFor(excel, { label: 'print menu' });
  await op.clickForDownload(excel, ctx.state.dir, { label: 'Excel in English (download)' });
  return {};
}

function tagButton(scroll) {
  return async (op, ctx) => {
    const { tag } = ctx.task.input;
    const page = op.page;
    await op.click(page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first(), { label: 'Contacts' });
    const chip = page.locator('button.ct-tag', { hasText: new RegExp('^' + tag + '$') }).first();
    if (scroll) {
      await op.waitFor(page.locator('table[role=grid] tbody tr').first(), { label: 'contact list' });
      for (let i = 0; i < 40 && (await chip.count()) === 0; i++) {
        const before = await page.locator('table[role=grid] tbody tr').last().getAttribute('aria-rowindex');
        await op.scrollTo(page.locator('table[role=grid] tbody tr').last(), { label: 'scroll the list' });
        await op.waitFor(prev => document.querySelector('table[role=grid] tbody tr:last-child')?.getAttribute('aria-rowindex') !== prev, { label: 'more rows', arg: before });
      }
      if (await chip.count()) await op.scrollTo(chip, { label: 'scroll the tag into view' });
    }
    await op.waitFor(chip, { label: 'contact list with a ' + tag + ' tag' });
    await op.click(chip, { label: 'the ' + tag + ' tag' });
    await waitFiltered(op, ctx);
    return exportExcel(op, ctx);
  };
}

async function filterPopover(op, ctx) {
  const { tag } = ctx.task.input;
  const page = op.page;
  const value = page.locator('.list-filter input:not([type="checkbox"])').first();
  await op.click(page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first(), { label: 'Contacts' });
  const options = page.getByRole('button', { name: 'Options for the column Tags' });
  await op.waitFor(options, { label: 'contact list' });
  await op.click(options, { label: 'Tags column options' });
  const filter = page.getByRole('menuitem', { name: 'Filter…' });
  await op.waitFor(filter, { label: 'column menu' });
  await op.click(filter, { label: 'Filter…' });
  await op.waitFor(value, { label: 'filter popover' });
  await op.click(value, { label: 'value field' });
  await op.type(tag, { label: 'tag' });
  await op.click(page.locator('.list-filter').getByRole('button', { name: 'Apply', exact: true }), { label: 'Apply' });
  await waitFiltered(op, ctx);
  return exportExcel(op, ctx);
}

export default {
  built: true,
  path: 'Contacts (navigation) > click the tag in a row (the list is then filtered to the contacts holding it) > Print or export > Excel in English.',
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
  run: tagButton(false),
  // Critic, p14 round 2: the builder's path needs a row with the tag on the first screen; on the shared dataset (sorted by name)
  // the first such row is about the 139th, so the critic measures three whole paths.
  variants: {
    'tag-button': { path: "Builder's path: Contacts > click the tag in a row on the first screen > Print or export > Excel in English", run: tagButton(false) },
    // A scroll variant (tagButton(true)) was tried and dropped: the list renders 32 rows and detaches them while it scrolls, and
    // the first row with the tag is about seven screens down (probe-tags.out: PageDown x7), so that path takes at least 11 steps.
    'filter-popover': { path: 'Contacts > Tags column options > Filter... > the value field > type the tag > Apply > Print or export > Excel in English (round 1 path)', run: filterPopover },
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
