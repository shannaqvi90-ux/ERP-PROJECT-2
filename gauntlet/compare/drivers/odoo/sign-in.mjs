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

const LOGIN_URL = ctx => `${ctx.product.baseUrl}/web/login?db=${encodeURIComponent(ctx.product.db)}`;

/**
 * Two start states, each the user's shortest path from it. `new-device`: the first sign-in on this
 * browser. `returning`: this browser has signed in and out before (set up outside the measured
 * part), so whatever the product remembers for a returning user is used; if the e-mail is already
 * filled in and the password has focus, the path is the password and Enter.
 */
function variant(returning) {
  return {
    path: returning
      ? 'A browser that signed in before: if the e-mail is remembered, type the password > Enter; otherwise type the e-mail > Tab > password > Enter.'
      : 'The sign-in screen focuses the e-mail field: type the e-mail > Tab > type the password > Enter.',
    async signIn(ctx) {
      // Start state: signed out, on the bookmarked sign-in address. Odoo serves several databases
      // on the rig's port, so the bookmark names the database.
      const { user, password } = ctx.task.input;
      if (returning) {
        await ctx.page.goto(LOGIN_URL(ctx));
        await ctx.page.locator('input[name="login"]').fill(user);
        await ctx.page.locator('input[name="password"]').fill(password);
        await ctx.page.locator('input[name="password"]').press('Enter');
        await ctx.page.locator('.o_main_navbar button.o_user_menu').waitFor();
        await ctx.page.goto(`${ctx.product.baseUrl}/web/session/logout`);
      }
      // The runner opens the start (the bookmarked sign-in address) in a fresh browser that keeps
      // this browser's cookies and local storage.
    },
    ready: 'input[name="login"]:focus, input[name="password"]:focus',
    async run(op, ctx) {
      const { user, password } = ctx.task.input;
      const remembered = (await op.page.locator('input[name="login"]').inputValue()) === user
        && (await op.page.locator('input[name="password"]:focus').count()) === 1;
      if (!remembered) {
        await op.type(user, { label: 'e-mail' });
        await op.press('Tab', { label: 'next field (password)' });
      }
      await op.type(password, { label: 'password' });
      await op.press('Enter', { label: 'sign in' });
      await op.waitFor('.o_main_navbar button.o_user_menu', { label: 'signed in' });
      await op.waitFor('.o_action_manager :is(.o_kanban_view, .o_list_view) :is(.o_kanban_record:not(.o_kanban_ghost), .o_data_row)', { label: 'working screen ready' });
      return { remembered };
    },
  };
}

export default {
  built: true,
  path: 'Sign-in screen: e-mail > Tab > password > Enter (a returning browser keeps whatever the product remembers).',
  run: variant(false).run,
  variants: { 'new-device': variant(false), returning: variant(true) },
  async setup(ctx) { ctx.state.uid = await ensureUser(ctx); },
  async verify(ctx) {
    // The browser's own session, read through the back end with its cookie.
    const info = await new OdooRpc(ctx.product).withBrowserSession(await ctx.context.cookies()).post('/web/session/get_session_info', {}).catch(() => null);
    return { verified: info?.uid === ctx.state.uid, details: { uid: info?.uid, expected_uid: ctx.state.uid, login: info?.username } };
  },
  async cleanup(ctx) {
    // Sign the session out so the next run starts signed out (the user is kept for the next run).
    await ctx.page.goto(`${ctx.product.baseUrl}/web/session/logout`).catch(() => { });
  },
};
