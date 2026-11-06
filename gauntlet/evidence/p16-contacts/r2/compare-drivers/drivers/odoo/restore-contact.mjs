// Critic p16-contacts r2.
import { adminRpc, openRecord, signInAs, emojiSettled } from './_common.mjs';

const START_ACTIVE = false;
async function needle(ctx) {
  const rpc = await adminRpc(ctx);
  const [c] = await rpc.searchRead('res.partner', [['ref', '=', ctx.needles.contact.ref]], ['id', 'active'], { context: { active_test: false } });
  if (!c) throw new Error('needle contact missing; run tools/odoo-reference/up.sh');
  return c;
}
const MENU = '.o_control_panel .o_cp_action_menus button';

export default {
  built: true,
  path: START_ACTIVE ? 'Actions (gear) > Archive > confirm Archive.' : 'Actions (gear) > Unarchive.',
  async setup(ctx) {
    const c = await needle(ctx);
    ctx.state.contact = c;
    await (await adminRpc(ctx)).write('res.partner', [c.id], { active: START_ACTIVE });
  },
  async signIn(ctx) {
    await signInAs(ctx, 'admin');
    await openRecord(ctx, 'res.partner', ctx.state.contact.id);
    await ctx.page.locator(MENU).first().waitFor();
  },
  ready: MENU,
  async run(op) {
    const page = op.page;
    await op.click(page.locator(MENU).first(), { label: 'Actions (gear)' });
    if (START_ACTIVE) {
      await op.click(page.locator('.o-dropdown--menu .dropdown-item', { hasText: /^Archive$/ }), { label: 'Archive' });
      await op.waitFor('.modal-footer button.btn-primary', { label: 'confirmation' });
      await op.click('.modal-footer button.btn-primary', { label: 'confirm Archive' });
      await op.waitFor('.modal', { label: 'dialog closed', state: 'hidden' });
    } else {
      await op.click(page.locator('.o-dropdown--menu .dropdown-item', { hasText: /^Unarchive$/ }), { label: 'Unarchive' });
    }
    await op.waitFor(() => !document.querySelector('.o_loading_indicator, .o_blockUI'), { label: 'done' });
    await op.waitFor(emojiSettled, { label: 'the chatter\'s emoji data in' });
    return {};
  },
  async verify(ctx) {
    const [c] = await (await adminRpc(ctx)).read('res.partner', [ctx.state.contact.id], ['active']);
    return { verified: c.active === !START_ACTIVE, details: { active: c.active } };
  },
  async cleanup(ctx) {
    if (ctx.state.contact) await (await adminRpc(ctx)).write('res.partner', [ctx.state.contact.id], { active: true });
  },
};
