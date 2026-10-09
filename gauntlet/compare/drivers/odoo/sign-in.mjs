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
 * Sign out the way the user does (user menu > Log out), outside the measured part. Odoo refuses a
 * plain GET of its logout address (HTTP 405), so the round-3 'returning' set-up never signed out:
 * the pre-clock verify of instrument 4 caught it.
 */
async function signOut(page) {
  await page.locator('.o_main_navbar button.o_user_menu').click();
  await page.locator('.o-dropdown--menu [data-menu="logout"]').click();
  await page.locator('input[name="login"]').waitFor();
}

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
        // Round 9 (critic p01 r8: one repeat timed out waiting for the e-mail field): a browser the
        // rig still holds a session for lands signed in; sign it out and open the sign-in screen again.
        const form = ctx.page.locator('input[name="login"]');
        const menu = ctx.page.locator('.o_main_navbar button.o_user_menu');
        await form.or(menu).first().waitFor();
        if (await menu.isVisible()) { await signOut(ctx.page); await ctx.page.goto(LOGIN_URL(ctx)); }
        await ctx.page.locator('input[name="login"]').fill(user);
        await ctx.page.locator('input[name="password"]').fill(password);
        await ctx.page.locator('input[name="password"]').press('Enter');
        await ctx.page.locator('.o_main_navbar button.o_user_menu').waitFor();
        await signOut(ctx.page);
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

/**
 * `passkey`: the reference signs in with a passkey too (its auth_passkey module, installed on the
 * rig). Set-up adds one the way its user does (user menu > Preferences > Security > Add Passkey >
 * confirm the password > name it > Create; the device confirms at once in set-up) and signs out.
 * Its sign-in screen asks the device only when the person picks "Use a Passkey"; then the person
 * confirms on the device and the screen signs in.
 */
const passkey = {
  path: 'The sign-in screen: click "Use a Passkey" > confirm on the device.',
  async signIn(ctx) {
    const { user, password } = ctx.task.input;
    const page = ctx.page;
    // A device of the user's from an earlier run is gone: set-up starts from a user with none.
    await removeHarnessPasskeys(ctx);
    await page.goto(LOGIN_URL(ctx));
    await page.locator('input[name="login"]').fill(user);
    await page.locator('input[name="password"]').fill(password);
    await page.locator('input[name="password"]').press('Enter');
    await page.locator('.o_main_navbar button.o_user_menu').waitFor();
    await page.locator('.o_main_navbar button.o_user_menu').click();
    await page.locator('.o-dropdown--menu [data-menu="preferences"], .o-dropdown--menu [data-menu="settings"]').first().click();
    await page.locator('button[role=tab]:has-text("Security"), a[role=tab]:has-text("Security")').first().click();
    await page.locator('button:has-text("Add Passkey")').click();
    await page.locator('.modal [name=password] input, .modal input[type=password]').first().fill(password);
    await page.locator('.modal button:has-text("Confirm Password")').click();
    await page.locator('.modal .o_field_char input').first().fill('Harness device');
    await page.locator('.modal button:has-text("Create")').first().click();
    await page.locator('.modal').waitFor({ state: 'detached' });
    await page.keyboard.press('Escape');
    await signOut(page);
  },
  ready: 'input[name="login"]:focus, input[name="password"]:focus',
  async run(op) {
    await op.click(op.page.locator('a.passkey_login_link'), { label: 'Use a Passkey' });
    await op.confirmOnDevice({ label: 'confirm on the device' });
    await op.waitFor('.o_main_navbar button.o_user_menu', { label: 'signed in' });
    await op.waitFor('.o_action_manager :is(.o_kanban_view, .o_list_view) :is(.o_kanban_record:not(.o_kanban_ghost), .o_data_row)', { label: 'working screen ready' });
    return { remembered: false, passkey: true };
  },
  async cleanup(ctx) {
    if (await ctx.page.locator('.o_main_navbar button.o_user_menu').isVisible().catch(() => false)) await signOut(ctx.page).catch(() => { });
    // The passkey set-up added for the task's user is removed again.
    await removeHarnessPasskeys(ctx).catch(() => { });
  },
};

/** The passkeys of the task's user that set-up added (named "Harness device"), removed as the administrator. */
async function removeHarnessPasskeys(ctx) {
  const rpc = await adminRpc(ctx);
  const keys = await rpc.call('auth.passkey.key', 'search', [[['create_uid', '=', ctx.state.uid], ['name', '=', 'Harness device']]]);
  if (keys.length) await rpc.unlink('auth.passkey.key', keys);
}

export default {
  built: true,
  path: 'Sign-in screen: e-mail > Tab > password > Enter (a returning browser keeps whatever the product remembers); with a passkey: Use a Passkey > confirm on the device.',
  run: variant(false).run,
  variants: { 'new-device': variant(false), returning: variant(true), passkey },
  async setup(ctx) { ctx.state.uid = await ensureUser(ctx); },
  async verify(ctx) {
    // The browser's own session, read through the back end with its cookie.
    const info = await new OdooRpc(ctx.product).withBrowserSession(await ctx.context.cookies()).post('/web/session/get_session_info', {}).catch(() => null);
    return { verified: info?.uid === ctx.state.uid, details: { uid: info?.uid, expected_uid: ctx.state.uid, login: info?.username } };
  },
  async cleanup(ctx) {
    // Sign the session out so the next run starts signed out (the user is kept for the next run).
    if (await ctx.page.locator('.o_main_navbar button.o_user_menu').isVisible().catch(() => false)) await signOut(ctx.page).catch(() => { });
  },
};
