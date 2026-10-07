import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { adminRpc, settle, signInAs } from './_common.mjs';

// Odoo Community prints a list by selecting its records and printing their own document: every
// selected order in one PDF. The document follows each order's vendor language; the layout follows
// the language it is rendered in. The user already works in Arabic (the task's start), so no
// language switch is part of the path.
const PRINT_MENU = '.o_control_panel button.dropdown-toggle:has([data-icon="print"])';

export default {
  built: true,
  path: 'Purchase orders list (search focused) > type the vendor > Enter (Search Order for: the order, the vendor\'s reference or the vendor and its contacts) > header check box > Print > Purchase Order.',
  async setup(ctx) {
    const admin = await adminRpc(ctx);
    const vendorName = ctx.needles.contact.parent_name;
    const [vendor] = await admin.searchRead('res.partner', [['ref', '=', ctx.needles.contact.parent_ref]], ['id', 'name']);
    if (!vendor || vendor.name !== vendorName) throw new Error('rig fixtures missing (the dataset vendor); run tools/odoo-reference/up.sh');
    // The rig's user who works in Arabic: a purchase administrator (created once, kept).
    const { login, password } = ctx.product.users.arabic;
    let [user] = await admin.searchRead('res.users', [['login', '=', login]], ['id', 'lang']);
    if (!user) {
      const group = await admin.ref('purchase.group_purchase_manager');
      user = { id: await admin.create('res.users', { name: 'Arabic Reporter', login, password, lang: 'ar_001', group_ids: [[4, group]] }), lang: 'ar_001' };
    }
    if (user.lang !== 'ar_001') await admin.write('res.users', [user.id], { lang: 'ar_001' });
    // The orders the list shows once narrowed to the vendor (the search's own rule: the order's
    // reference, the vendor's reference, or a partner of that name and its contacts; the dataset
    // has several companies of the vendor's name).
    ctx.state.expected = await admin.search('purchase.order', ['|', '|', ['name', 'ilike', vendorName], ['partner_ref', 'ilike', vendorName], ['partner_id', 'child_of', vendorName]], { order: 'id' });
    if (ctx.state.expected.length < 2 || ctx.state.expected.length > 80) throw new Error(`the vendor has ${ctx.state.expected.length} orders on the rig; the task needs a list of 2 to 80 (one page)`);
    // Each order prints in its vendor's language: Arabic for every partner of these orders (restored in clean-up).
    const orders = await admin.read('purchase.order', ctx.state.expected, ['partner_id']);
    const partnerIds = [...new Set(orders.map(o => o.partner_id[0]))];
    ctx.state.partners = await admin.read('res.partner', partnerIds, ['lang']);
    await admin.write('res.partner', partnerIds, { lang: 'ar_001' });
    // The menu item the user picks: the order's printed document, by its Arabic name.
    const reportId = await admin.ref('purchase.action_report_purchase_order');
    ctx.state.reportName = (await admin.call('ir.actions.report', 'read', [[reportId], ['name']], { context: { lang: 'ar_001' } }))[0].name;
    ctx.state.dir = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-list-report-'));
  },
  async signIn(ctx) {
    await signInAs(ctx, 'arabic');
    await ctx.page.goto(ctx.product.baseUrl + '/odoo/purchase');
    await ctx.page.locator('.o_list_view .o_data_row').first().waitFor();
    await settle(ctx, ctx.page);
  },
  ready: '.o_list_view .o_data_row',
  observe(ctx) {
    // What the client asks the server to print: the records and the context (read-only listener on
    // the start page, registered before the measured part).
    ctx.page.on('request', r => {
      if (!r.url().endsWith('/report/download')) return;
      const body = r.postData() || '';
      const data = /name="data"\r?\n\r?\n(\[[\s\S]*?\])\r?\n--/.exec(body);
      const context = /name="context"\r?\n\r?\n(\{[\s\S]*?\})\r?\n--/.exec(body);
      if (data) ctx.state.printUrl = JSON.parse(data[1])[0];
      if (context) ctx.state.printContext = JSON.parse(context[1]);
    });
  },
  async run(op, ctx) {
    const page = op.page;
    const n = ctx.state.expected.length;
    await op.waitFor('.o_searchview_input:focus', { label: 'order list, search focused' });
    await op.type(ctx.needles.contact.parent_name, { label: 'vendor' });
    await op.waitFor('.o_searchview_autocomplete .dropdown-item', { label: 'search options' });
    await op.press('Enter', { label: 'search orders' });
    await op.waitFor(count => !document.querySelector('.o_loading_indicator') && document.querySelectorAll('.o_data_row').length === count &&
      Number(document.querySelector('.o_pager_limit')?.textContent.replace(/\D/g, '')) === count, { label: 'the vendor\'s orders', arg: n });
    await op.shot('list filtered');
    await op.click('.o_list_view thead .o_list_record_selector input', { label: 'select every order' });
    await op.waitFor(PRINT_MENU, { label: 'print menu offered' });
    await op.click(PRINT_MENU, { label: 'Print' });
    const item = page.locator('.o-dropdown--menu .dropdown-item').filter({ hasText: new RegExp(`^\\s*${ctx.state.reportName}\\s*$`) });
    ctx.state.file = await op.clickForDownload(item, ctx.state.dir, { label: 'Purchase Order' });
    return {};
  },
  async verify(ctx) {
    const pdf = fs.readFileSync(ctx.state.file);
    const printed = (/\/report\/pdf\/purchase\.report_purchaseorder\/([\d,]+)/.exec(ctx.state.printUrl || '')?.[1] || '').split(',').filter(Boolean).map(Number);
    const expected = ctx.state.expected;
    const same = printed.length === expected.length && [...printed].sort((a, b) => a - b).every((id, i) => id === expected[i]);
    // The same documents rendered as HTML in the print request's language, to read their direction,
    // script and order references (the PDF's text is font-encoded).
    const lang = ctx.state.printContext?.lang || 'en_US';
    const admin = await adminRpc(ctx);
    const html = printed.length ? await admin.getText(`/report/html/purchase.report_purchaseorder/${printed.join(',')}?context=${encodeURIComponent(JSON.stringify({ lang }))}`) : '';
    const text = html.replace(/<[^>]+>/g, ' ');
    const arabic = (text.match(/[؀-ۿ]+/g) || []).length;
    const rtl = /<body[^>]*dir="rtl"/i.test(html) || /<html[^>]*dir="rtl"/i.test(html);
    const pages = (html.match(/class="[^"]*\bpage\b[^"]*"/g) || []).length;
    return {
      verified: pdf.subarray(0, 5).toString() === '%PDF-' && pdf.length > 1000 && same && arabic >= 5 * expected.length && rtl && pages >= expected.length,
      details: {
        file: path.basename(ctx.state.file), bytes: pdf.length, orders_printed: printed.length, orders_expected: expected.length, same_orders: same,
        print_language: ctx.state.printContext?.lang ?? null, arabic_words_in_document: arabic, right_to_left: rtl, documents: pages,
      },
    };
  },
  async cleanup(ctx) {
    const admin = await adminRpc(ctx);
    for (const p of ctx.state.partners || []) await admin.write('res.partner', [p.id], { lang: p.lang || 'en_US' });
    if (ctx.state.dir) fs.rmSync(ctx.state.dir, { recursive: true, force: true });
  },
};
