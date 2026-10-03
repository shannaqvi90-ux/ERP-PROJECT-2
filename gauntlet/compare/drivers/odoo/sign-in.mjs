import { adminRpc } from './_common.mjs';
import { OdooRpc } from '../../lib/odoo-rpc.mjs';

/** The task's user: an ordinary internal user who works in Contacts, with an e-mail sign-in. */
async function ensureUser(ctx) {
  const rpc = await adminRpc(ctx);
  const { user, password, name } = ctx.task.input;
  const groups = [await rpc.ref('base.group_user'), await rpc.ref('base.group_partner_manager')];
  const values = { name, password, active: true, lang: 'en_US', tz: 'Asia/Dubai', group_ids: [[6, 0, groups]], action_id: await rpc.ref('contacts.action_contacts') };
  const found = await rpc.call('res.users', 'search', [[['login', '=', user]]], { context: { active_test: false } });
  if (found.length) await rpc.write('res.users', found, values);
  else await rpc.create('res.users', { ...values, login: user, email: user });
  return (await rpc.search('res.users', [['login', '=', user]]))[0];
}

export default {
  built: true,
  path: 'The sign-in screen focuses the e-mail field: type the e-mail > Tab > type the password > Enter.',
  async setup(ctx) { ctx.state.uid = await ensureUser(ctx); },
  async signIn(ctx) {
    // Start state: signed out, on the bookmarked sign-in address. Odoo serves several databases
    // on the rig's port, so the bookmark names the database.
    await ctx.page.goto(`${ctx.product.baseUrl}/web/login?db=${encodeURIComponent(ctx.product.db)}`);
    await ctx.page.locator('input[name="login"]:focus').waitFor();
  },
  async run(op, ctx) {
    const { user, password } = ctx.task.input;
    await op.waitFor('input[name="login"]:focus', { label: 'sign-in screen, e-mail focused' });
    await op.type(user, { label: 'e-mail' });
    await op.press('Tab', { label: 'next field (password)' });
    await op.type(password, { label: 'password', chain: true });
    await op.press('Enter', { label: 'sign in', chain: true });
    await op.waitFor('.o_main_navbar button.o_user_menu', { label: 'signed in' });
    await op.waitFor('.o_action_manager :is(.o_kanban_view, .o_list_view) :is(.o_kanban_record:not(.o_kanban_ghost), .o_data_row)', { label: 'working screen ready' });
    return {};
  },
  async verify(ctx) {
    const info = await ctx.page.evaluate(async () => {
      const res = await fetch('/web/session/get_session_info', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', method: 'call', params: {} }) });
      return (await res.json()).result;
    });
    return { verified: info?.uid === ctx.state.uid, details: { uid: info?.uid, expected_uid: ctx.state.uid, login: info?.username } };
  },
  async cleanup(ctx) {
    // Sign the session out so the next run starts signed out (the user is kept for the next run).
    await ctx.page.goto(`${ctx.product.baseUrl}/web/session/logout`).catch(() => {});
  },
};
