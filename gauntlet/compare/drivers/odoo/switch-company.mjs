import { adminRpc, signInAs } from './_common.mjs';

export default {
  built: true,
  path: 'Company switcher in the top bar > the company. (Alt+Shift+U opens the switcher too, but then the company must be typed or arrowed to: more steps and keys.)',
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    const { from, to } = ctx.task.input;
    const [fromId] = await rpc.search('res.company', [['name', '=', from]]);
    const [toId] = await rpc.search('res.company', [['name', '=', to]]);
    if (!fromId || !toId) throw new Error('rig fixtures missing (companies); run tools/odoo-reference/up.sh');
    // The admin may work in both; the start company is the user's default.
    await rpc.write('res.users', [rpc.uid], { company_ids: [[4, fromId], [4, toId]], company_id: fromId });
    ctx.state.toId = toId;
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op, ctx) {
    const { to } = ctx.task.input;
    const page = op.page;
    await op.click('.o_main_navbar .o_switch_company_menu', { label: 'company switcher' });
    await op.click(page.locator('.o-dropdown--menu .company_label', { hasText: new RegExp(`^${to}$`) }), { label: to });
    await op.waitFor(name => document.querySelector('.o_main_navbar .o_switch_company_menu')?.textContent.trim() === name && !document.querySelector('.o_loading_indicator'), { label: 'working in the other company', arg: to });
    return {};
  },
  async verify(ctx) {
    const shown = await ctx.read(() => ({
      switcher: document.querySelector('.o_main_navbar .o_switch_company_menu')?.textContent.trim(),
      cids: (document.cookie.match(/(?:^|; )cids=([^;]*)/) || [])[1] || '',
    }));
    const first = Number(decodeURIComponent(shown.cids).split(/[,-]/)[0]);
    return { verified: shown.switcher === ctx.task.input.to && first === ctx.state.toId, details: shown };
  },
  async cleanup(ctx) {
    await ctx.context.clearCookies().catch(() => { });
  },
};
