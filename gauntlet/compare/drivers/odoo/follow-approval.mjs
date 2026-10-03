import { adminRpc, openApp, signInAs } from './_common.mjs';
import { OdooRpc } from '../../lib/odoo-rpc.mjs';

// Odoo Community's nearest feature to a generic approval flow: the purchase order two-step
// approval (orders above the company's limit, AED 5,000 on the rig, wait for a purchase manager).
export default {
  built: true,
  path: 'Apps menu > Purchase (the newest order, waiting for approval, is the first row) > open it > Approve Order.',
  async setup(ctx) {
    const buyer = await new OdooRpc(ctx.product).login(ctx.product.users.buyer);
    const [vendor] = await buyer.search('res.partner', [['ref', '=', ctx.needles.contact.parent_ref]], { limit: 1 });
    const [product] = await buyer.search('product.product', [['name', '=', 'Office Chair']], { limit: 1 });
    if (!vendor || !product) throw new Error('rig fixtures missing (vendor or product); run tools/odoo-reference/up.sh');
    const id = await buyer.create('purchase.order', { partner_id: vendor, order_line: [[0, 0, { product_id: product, product_qty: 10, price_unit: 750, tax_ids: [[5, 0, 0]] }]] });
    await buyer.call('purchase.order', 'button_confirm', [[id]]);
    const [po] = await buyer.read('purchase.order', [id], ['name', 'state', 'amount_total']);
    if (po.state !== 'to approve') throw new Error(`set-up order is ${po.state}, expected "to approve" (check the two-step approval setting)`);
    ctx.state.po = po;
  },
  async signIn(ctx) { await signInAs(ctx, 'approver'); },
  async run(op, ctx) {
    const { name } = ctx.state.po;
    const page = op.page;
    await openApp(op, 'Purchase');
    await op.waitFor(page.locator('.o_data_row').first(), { label: 'order list' });
    const firstRow = await page.locator('.o_data_row').first().innerText();
    await op.shot('order list');
    await op.click(page.locator('.o_data_row', { hasText: name }).first(), { label: 'open the order waiting for approval' });
    await op.waitFor(page.getByRole('button', { name: 'Approve Order' }), { label: 'order form' });
    await op.click(page.getByRole('button', { name: 'Approve Order' }), { label: 'Approve Order' });
    await op.waitFor(() => {
      const s = document.querySelector('.o_form_view .o_statusbar_status .o_arrow_button_current, .o_form_view .o_statusbar_status [aria-current="step"]');
      return !!s && /Purchase Order/i.test(s.textContent) && !document.querySelector('.o_form_view button[name="button_approve"]');
    }, { label: 'order approved' });
    return { first_row_is_the_order: firstRow.includes(name) };
  },
  async verify(ctx, outcome) {
    const rpc = await adminRpc(ctx);
    const [po] = await rpc.read('purchase.order', [ctx.state.po.id], ['state', 'date_approve', 'name']);
    const approver = (await rpc.search('res.users', [['login', '=', ctx.product.users.approver.login]]))[0];
    const msgs = await rpc.searchRead('mail.message', [['model', '=', 'purchase.order'], ['res_id', '=', ctx.state.po.id], ['author_id.user_ids', 'in', [approver]]], ['id']);
    return {
      verified: po.state === 'purchase' && !!po.date_approve && msgs.length > 0,
      details: { order: po.name, state: po.state, date_approve: po.date_approve, approver_messages: msgs.length, ...outcome },
    };
  },
  async cleanup(ctx) {
    if (!ctx.state.po) return;
    const rpc = await adminRpc(ctx);
    await rpc.call('purchase.order', 'button_cancel', [[ctx.state.po.id]]).catch(() => {});
    await rpc.unlink('purchase.order', [ctx.state.po.id]);
  },
};
