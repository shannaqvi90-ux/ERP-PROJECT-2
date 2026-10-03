import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { adminRpc, openRecord, signInAs } from './_common.mjs';
import { OdooRpc } from '../../lib/odoo-rpc.mjs';

export default {
  built: true,
  path: 'User menu > My Preferences > Language: Arabic > Update Preferences > Print. Printing straight away gives Arabic text in a left-to-right layout (Odoo lays the page out in the user\'s language and translates it into the vendor\'s), so the user switches to Arabic first.',
  async setup(ctx) {
    const admin = await adminRpc(ctx);
    const [vendor] = await admin.searchRead('res.partner', [['ref', '=', ctx.needles.contact.parent_ref]], ['id', 'lang']);
    const [product] = await admin.search('product.product', [['name', '=', 'Office Chair']], { limit: 1 });
    if (!vendor || !product) throw new Error('rig fixtures missing (vendor or product); run tools/odoo-reference/up.sh');
    ctx.state.vendor = vendor;
    await admin.write('res.users', [admin.uid], { lang: 'en_US' });
    await admin.write('res.partner', [vendor.id], { lang: 'ar_001' });
    const buyer = await new OdooRpc(ctx.product).login(ctx.product.users.buyer);
    const id = await buyer.create('purchase.order', { partner_id: vendor.id, order_line: [[0, 0, { product_id: product, product_qty: 2, price_unit: 750, tax_ids: [[5, 0, 0]] }]] });
    await buyer.call('purchase.order', 'button_confirm', [[id]]);
    ctx.state.po = id;
    // The Print button runs the order's report action (its name is the action's id).
    ctx.state.printAction = await admin.ref('purchase.action_report_purchase_order');
    ctx.state.dir = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-report-'));
  },
  async signIn(ctx) {
    await signInAs(ctx, 'admin');
    await openRecord(ctx, 'purchase.order', ctx.state.po);
    // The context the client sends with the print request: the document is rendered with it.
    // The listener is registered here, outside the measured part; it only reads the request.
    ctx.page.on('request', r => {
      if (r.url().endsWith('/report/download')) {
        const m = /name="context"\r?\n\r?\n(\{[\s\S]*?\})\r?\n--/.exec(r.postData() || '');
        if (m) ctx.state.printContext = JSON.parse(m[1]);
      }
    });
  },
  async run(op, ctx) {
    const page = op.page;
    await op.click('button.o_user_menu', { label: 'user menu' });
    await op.click(page.locator('.o-dropdown--menu .dropdown-item', { hasText: 'My Preferences' }), { label: 'My Preferences' });
    await op.waitFor('.modal .o_field_widget[name="lang"] input', { label: 'preferences dialog' });
    await op.click('.modal .o_field_widget[name="lang"] input', { label: 'language' });
    await op.click(page.locator('.o_select_menu_item', { hasText: 'Arabic' }), { label: 'Arabic' });
    await op.click(page.locator('.modal-footer button', { hasText: 'Update Preferences' }), { label: 'Update Preferences' });
    await op.waitFor(() => !document.querySelector('.modal') && !!document.body?.classList.contains('o_rtl'), { label: 'interface in Arabic' });
    const print = page.locator(`.o_form_view .o_form_statusbar button[name="${ctx.state.printAction}"]`).first();
    await op.waitFor(print, { label: 'order form in Arabic' });
    ctx.state.file = await op.clickForDownload(print, ctx.state.dir, { label: 'Print' });
    return {};
  },
  async verify(ctx) {
    const pdf = fs.readFileSync(ctx.state.file);
    // The same document rendered as HTML in the language of the print request, to read its
    // direction and script (the PDF's text is font-encoded).
    const context = ctx.state.printContext || {};
    const html = await ctx.page.evaluate(async ([id, lang]) =>
      (await fetch(`/report/html/purchase.report_purchaseorder/${id}?context=${encodeURIComponent(JSON.stringify({ lang }))}`)).text(), [ctx.state.po, context.lang || 'en_US']);
    const arabic = (html.replace(/<[^>]+>/g, ' ').match(/[؀-ۿ]+/g) || []).length;
    const rtl = /<body[^>]*dir="rtl"/i.test(html);
    return {
      verified: pdf.subarray(0, 5).toString() === '%PDF-' && pdf.length > 1000 && arabic >= 5 && rtl,
      details: { file: path.basename(ctx.state.file), bytes: pdf.length, print_language: context.lang ?? null, arabic_words_in_document: arabic, right_to_left: rtl },
    };
  },
  async cleanup(ctx) {
    const admin = await adminRpc(ctx);
    await admin.write('res.users', [admin.uid], { lang: 'en_US' });
    if (ctx.state.po) {
      await admin.call('purchase.order', 'button_cancel', [[ctx.state.po]]).catch(() => {});
      await admin.unlink('purchase.order', [ctx.state.po]).catch(() => {});
    }
    if (ctx.state.vendor) await admin.write('res.partner', [ctx.state.vendor.id], { lang: ctx.state.vendor.lang || 'en_US' });
    if (ctx.state.dir) fs.rmSync(ctx.state.dir, { recursive: true, force: true });
  },
};
