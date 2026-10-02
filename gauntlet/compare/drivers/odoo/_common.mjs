// Shared pieces of the Odoo drivers. Odoo is driven through its own web client the way a
// trained user would; selectors target Odoo's rendered screens and are not copied from its code.
import { OdooRpc } from '../../lib/odoo-rpc.mjs';

/** Sign in outside the measured part of a task: session cookie from the back end, then the landing screen. */
export async function signInAs(ctx, user) {
  const rpc = await new OdooRpc(ctx.product).login(typeof user === 'string' ? ctx.product.users[user] : user);
  await ctx.context.addCookies([rpc.sessionCookie()]);
  await ctx.page.goto(ctx.product.baseUrl + '/odoo');
  await ctx.page.locator('.o_main_navbar button.o_user_menu').waitFor();
  await settle(ctx.page);
  return rpc;
}

/** Wait until Odoo has no request in flight (its "Loading" indicator is gone) and the DOM is quiet. */
export async function settle(page, ms = 300) {
  await page.waitForFunction(() => !document.querySelector('.o_loading_indicator, .o_blockUI'), null, { timeout: 120_000 });
  await page.waitForTimeout(ms);
}

/** Admin back-end session for fixtures, verification and clean-up. */
export async function adminRpc(ctx) {
  if (!ctx.state.admin) ctx.state.admin = await new OdooRpc(ctx.product).login(ctx.product.users.admin);
  return ctx.state.admin;
}

/** Open an app from the apps menu: two clicks. */
export async function openApp(op, name) {
  await op.click('.o_navbar_apps_menu button', { label: 'apps menu' });
  await op.click(op.page.getByRole('menuitem', { name, exact: true }), { label: `${name} app` });
}

/** Rows of the current list or kanban view. */
export const ROWS = '.o_data_row, .o_kanban_record:not(.o_kanban_ghost)';
export function rowCount() {
  return document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length;
}
