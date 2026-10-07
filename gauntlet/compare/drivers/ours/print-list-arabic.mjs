import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { oursAs } from '../../lib/ours-api.mjs';

// Our product has no purchase orders yet (a later piece), so the list is the stand-in it has: the
// users list, narrowed by a search for the dataset user's name without the first name
// (53 users of the shared dataset: every word matches the name or e-mail; the reference prints 27 orders). The user works in Arabic; the
// list's own "Print or export" menu prints what the list shows as a PDF report in Arabic.
//
// Shortest expert path: the users list opens with the cursor in its search box > type the name >
// Print or export > PDF in Arabic.

// The product's own words for the menu (its Arabic strings), read from the product's resource file.
const STRINGS = JSON.parse(fs.readFileSync(new URL('../../../../web/src/modules/lists/i18n/ar.json', import.meta.url), 'utf8'));
const PRINT = page => page.locator('button[aria-haspopup="menu"]').filter({ hasText: STRINGS['lists.print.open'] });
const SEARCH = 'input.search[aria-controls$="-grid"]';
const searchText = needles => needles.user.name.split(' ').slice(1).join(' ');

export default {
  built: true,
  path: 'Users list (search focused) > type the name > Print or export > PDF in Arabic. Stand-in list: users (purchase orders arrive with a later piece).',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'adminArabic');
    const search = searchText(ctx.needles);
    const find = () => api.get(`/api/identity/users?search=${encodeURIComponent(search)}&take=200`);
    let found = await find();
    if (!found.items.length) {
      // A driver health check (./erp verify, a clean stack without the dataset) creates the dataset user.
      const { login, name, lang } = ctx.needles.user;
      if (ctx.health) await api.post('/api/identity/users', { email: login, displayName: name, language: lang === 'ar' ? 'ar' : 'en', password: ctx.product.users.admin.password, roleIds: [] });
      else throw new Error(`our product holds no user named "${search}"; start it with ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv on a fresh database`);
      found = await find();
    }
    ctx.state.search = search;
    ctx.state.expected = found.items.length;
    // Start state: the user works in Arabic.
    const session = await api.get('/api/auth/session');
    if (session.user.language !== 'ar') await api.put('/api/identity/me/preferences', { language: 'ar', numerals: session.user.numerals || 'latn' });
    ctx.state.dir = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-list-report-'));
  },
  async signIn(ctx) {
    const page = ctx.page;
    const { login, password } = ctx.product.users.adminArabic;
    await page.goto(ctx.product.baseUrl + '/');
    await page.locator('input[name="email"]:focus').waitFor();
    await page.keyboard.type(login);
    await page.keyboard.press('Tab');
    await page.keyboard.type(password);
    await page.keyboard.press('Enter');
    await page.locator('nav').first().waitFor();
    await page.goto(`${ctx.product.baseUrl}/identity/users`);
    await page.locator(SEARCH).waitFor();
  },
  ready: SEARCH,
  async run(op, ctx) {
    const page = op.page;
    await op.waitFor(`${SEARCH}:focus`, { label: 'users list, search focused' });
    await op.type(ctx.state.search, { label: 'name' });
    await op.waitFor(([text, count]) => {
      const box = document.querySelector('input.search[aria-controls$="-grid"]');
      const grid = box && document.getElementById(box.getAttribute('aria-controls'));
      return box?.value === text && Number(grid?.getAttribute('aria-rowcount')) === count + 1 && grid.querySelectorAll('tbody [role="row"], [role="rowgroup"] [role="row"]').length >= Math.min(count, 1);
    }, { label: 'the narrowed list', arg: [ctx.state.search, ctx.state.expected] });
    await op.shot('list filtered');
    await op.click(PRINT(page), { label: 'Print or export' });
    const arabic = page.getByRole('menuitem', { name: STRINGS['lists.print.pdfArabic'], exact: true });
    await op.waitFor(arabic, { label: 'print menu' });
    // The address of the printed report, read (not counted) from the menu item the user chooses.
    ctx.state.printUrl = new URL(await arabic.getAttribute('href'), ctx.product.baseUrl).href;
    ctx.state.file = await op.clickForDownload(arabic, ctx.state.dir, { label: 'PDF in Arabic' });
    return {};
  },
  async verify(ctx) {
    const pdf = fs.readFileSync(ctx.state.file);
    // The same report as data, from the address the product printed: its language, direction,
    // rows and text (the PDF's text is font-encoded).
    const url = new URL(ctx.state.printUrl || 'http://invalid/');
    url.searchParams.set('format', 'json');
    const api = await oursAs(ctx.product, 'adminArabic');
    const doc = ctx.state.printUrl ? await api.get(url.pathname + url.search) : null;
    const text = doc ? [doc.title, ...doc.columns.map(c => c.label), doc.rowCountText, doc.texts?.printed].join(' ') : '';
    const arabicWords = (text.match(/[\u0600-\u06FF]+/g) || []).length;
    const n = ctx.state.expected;
    return {
      verified: pdf.subarray(0, 5).toString() === '%PDF-' && pdf.length > 1000 && doc?.language === 'ar' && doc?.direction === 'rtl' &&
        doc?.rowCount === n && doc?.matchCount === n && doc?.truncated === false && url.searchParams.get('search') === ctx.state.search && arabicWords >= 3,
      details: {
        file: path.basename(ctx.state.file), bytes: pdf.length, print_language: doc?.language ?? null, right_to_left: doc?.direction === 'rtl',
        rows_printed: doc?.rowCount ?? null, rows_expected: n, search: url.searchParams.get('search'), arabic_words_in_report: arabicWords,
        document: 'users list (stand-in for purchase orders)',
      },
    };
  },
  async cleanup(ctx) {
    if (ctx.state.dir) fs.rmSync(ctx.state.dir, { recursive: true, force: true });
  },
};
