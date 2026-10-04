import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { oursAs } from '../../lib/ours-api.mjs';

// Written by the p06 builder (report framework). Our product has no purchase orders yet, so the
// document is the nearest printed record it has: the company profile of the working company,
// ALN-DXB, opened at its own address. The user works in English; the document is wanted in Arabic.
//
// Shortest expert path: Print (on the record's toolbar) > PDF in Arabic. The interface stays in
// English; the PDF is Arabic and laid out right to left (no switch of the user's language).

const CODE = 'ALN-DXB';
const PRINT = '.record-header button[aria-haspopup="menu"]';

export default {
  built: true,
  path: 'Print > PDF in Arabic. Stand-in document: the company profile (purchase orders arrive with a later piece).',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const page = await api.get(`/api/tenancy/companies?search=${encodeURIComponent(CODE)}&take=50`);
    const found = page.items.find(c => c.code === CODE);
    if (!found) throw new Error(`our product has no company ${CODE}; start it with the demo data`);
    ctx.state.company = found.id;
    // Start state: the administrator works in English.
    await api.put('/api/identity/me/preferences', { language: 'en', numerals: 'latn' });
    ctx.state.dir = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-report-'));
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
    await page.goto(`${ctx.product.baseUrl}/tenancy/companies/${ctx.state.company}`);
    await page.locator(PRINT).waitFor();
  },
  ready: PRINT,
  async run(op, ctx) {
    await op.click(PRINT, { label: 'Print' });
    const arabic = op.page.getByRole('menuitem', { name: 'PDF in Arabic' });
    await op.waitFor(arabic, { label: 'print menu' });
    // The address of the printed document, read (not counted) from the menu item the user chooses:
    // a download started by a link raises no page request event, so it cannot be observed.
    ctx.state.printUrl = new URL(await arabic.getAttribute('href'), ctx.product.baseUrl).href;
    ctx.state.file = await op.clickForDownload(arabic, ctx.state.dir, { label: 'PDF in Arabic' });
    return {};
  },
  async verify(ctx) {
    const pdf = fs.readFileSync(ctx.state.file);
    // The same document as data, from the address the product printed: its language, direction
    // and text (the PDF's text is font-encoded).
    const url = new URL(ctx.state.printUrl || 'http://invalid/');
    url.searchParams.set('format', 'json');
    const api = await oursAs(ctx.product, 'admin');
    const doc = ctx.state.printUrl ? await api.get(url.pathname + url.search) : null;
    const text = doc ? [doc.title, ...doc.facts.map(f => `${f.label} ${f.text}`), ...doc.columns.map(c => c.label)].join(' ') : '';
    const arabicWords = (text.match(/[؀-ۿ]+/g) || []).length;
    const session = await api.get('/api/auth/session');
    return {
      verified: pdf.subarray(0, 5).toString() === '%PDF-' && pdf.length > 1000 && doc?.language === 'ar' && doc?.direction === 'rtl' && arabicWords >= 5,
      details: {
        file: path.basename(ctx.state.file), bytes: pdf.length, print_language: doc?.language ?? null,
        right_to_left: doc?.direction === 'rtl', arabic_words_in_document: arabicWords, user_language_after: session.user.language,
        document: `company profile ${CODE}`,
      },
    };
  },
  async cleanup(ctx) {
    if (ctx.state.dir) fs.rmSync(ctx.state.dir, { recursive: true, force: true });
  },
};
