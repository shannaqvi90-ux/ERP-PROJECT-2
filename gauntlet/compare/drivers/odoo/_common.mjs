// Shared pieces of the Odoo drivers. Odoo is driven through its own web client the way a
// trained user would; selectors target Odoo's rendered screens and are not copied from its code.
import { OdooRpc } from '../../lib/odoo-rpc.mjs';

/** Sign in outside the measured part of a task: session cookie from the back end, then the landing screen. */
export async function signInAs(ctx, user) {
  const rpc = await new OdooRpc(ctx.product).login(typeof user === 'string' ? ctx.product.users[user] : user);
  await ctx.context.addCookies([rpc.sessionCookie()]);
  await ctx.page.goto(ctx.product.baseUrl + '/odoo');
  await ctx.page.locator('.o_main_navbar button.o_user_menu').waitFor();
  await settle(ctx, ctx.page);
  return rpc;
}

/** Wait until Odoo has no request in flight (its "Loading" indicator is gone) and the DOM is quiet (set-up only). */
export async function settle(ctx, page, ms = 300) {
  await ctx.until(() => !document.querySelector('.o_loading_indicator, .o_blockUI'), { page, timeout: 120_000 });
  await page.waitForTimeout(ms);
}

/** Odoo is ready on a start screen the runner opened: the user menu shows and nothing is loading. */
export const READY = '.o_main_navbar button.o_user_menu';

/** Admin back-end session for fixtures, verification and clean-up. */
export async function adminRpc(ctx) {
  if (!ctx.state.admin) ctx.state.admin = await new OdooRpc(ctx.product).login(ctx.product.users.admin);
  return ctx.state.admin;
}

/** Open an app from the apps menu: two clicks. */
export async function openApp(op, name) {
  await op.click('.o_navbar_apps_menu button', { label: 'apps menu' });
  await op.click(op.page.getByRole('menuitem', { name, exact: true }), { label: `${name} app` });
  // The app's first screen has loaded (its menus answer only then).
  await op.waitFor(() => !document.querySelector('.o_loading_indicator, .o_blockUI') && !!document.querySelector('.o_action_manager .o_view_controller, .o_action_manager .o_action'), { label: `${name} loaded` });
}

/**
 * Open a menu through the command palette, keyboard only: Ctrl+K, type "/" and a few letters of
 * the menu, Enter on the first match (`expected`, the full menu path the palette shows).
 */
export async function paletteMenu(op, typed, expected) {
  await op.press('Control+k', { label: 'command palette' });
  await op.waitFor('.o_command_palette input:focus', { label: 'palette open' });
  await op.type(typed, { label: `menu search ${typed}` });
  await op.waitFor(e => {
    const first = document.querySelector('.o_command_palette .o_command');
    return !!first && first.innerText.split('\n')[0].trim() === e;
  }, { label: 'menu found', arg: expected });
  await op.press('Enter', { label: `open ${expected}` });
  await op.waitFor(() => !document.querySelector('.o_command_palette') && !document.querySelector('.o_loading_indicator, .o_blockUI') &&
    !!document.querySelector('.o_action_manager .o_view_controller, .o_action_manager .o_action'), { label: `${expected} loaded` });
}

/** Rows of the current list or kanban view. */
export const ROWS = '.o_data_row, .o_kanban_record:not(.o_kanban_ghost)';
export function rowCount() {
  return document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length;
}

/**
 * Two expert paths through one form task: `keyboard` uses the form's hotkeys (Alt+C new,
 * Alt+S save) where they replace a click, `pointer` clicks the buttons. Which one is shorter
 * depends on the metric (hotkeys press more keys, clicks need more pointing and hand moves), so
 * a driver offers both and the result counts the better one per metric.
 */
export function expertPaths(build, { keyboard, pointer }) {
  return {
    run: build(true),
    variants: {
      keyboard: { path: keyboard, run: build(true) },
      pointer: { path: pointer, run: build(false) },
    },
  };
}

/** Save the open form: Alt+S, or the Save button. */
export async function saveForm(op, keyboard) {
  if (keyboard) await op.press('Alt+s', { label: 'Save (hotkey)' });
  else await op.click('.o_form_view .o_form_button_save', { label: 'Save' });
  await op.waitFor('.o_form_view .o_form_button_save', { label: 'saved', state: 'hidden' });
}

/** Open a contact's form outside the measured part (a start state "on the record"). */
export async function openRecord(ctx, model, id) {
  const paths = { 'res.partner': 'contacts', 'purchase.order': 'purchase' };
  await ctx.page.goto(`${ctx.product.baseUrl}/odoo/${paths[model] || `action-${model}`}/${id}`);
  await ctx.page.locator('.o_form_view').waitFor();
  await settle(ctx, ctx.page);
}

/**
 * Odoo's technical menus (sequences, scheduled actions, attachments) appear only in developer
 * mode: Apps menu > Settings > scroll to the bottom > Activate the developer mode.
 */
export async function developerMode(op) {
  await openApp(op, 'Settings');
  const link = op.page.getByRole('link', { name: 'Activate the developer mode', exact: true })
    .or(op.page.getByRole('button', { name: 'Activate the developer mode', exact: true })).first();
  await op.waitFor(link, { label: 'settings page', state: 'attached' });
  await op.scrollTo(link, { label: 'scroll to the bottom of the settings' });
  await op.click(link, { label: 'Activate the developer mode' });
  await op.waitFor(() => /[?&]debug=1/.test(location.search) && !!document.querySelector('.o_menu_sections') &&
    [...document.querySelectorAll('.o_menu_sections button')].some(b => b.innerText.trim() === 'Technical'), { label: 'developer mode on' });
}

/** Settings > Technical > an item of the long technical menu (scrolled to). */
export async function technicalMenu(op, item) {
  await op.click(op.page.locator('.o_main_navbar .o_menu_sections button', { hasText: 'Technical' }), { label: 'Technical menu' });
  const entry = op.page.locator('.o-dropdown--menu .dropdown-item', { hasText: new RegExp(`^${item}$`) });
  await op.waitFor(entry, { label: 'technical menu open', state: 'attached' });
  const box = await entry.boundingBox();
  const view = op.page.viewportSize();
  if (!box || box.y < 0 || box.y + box.height > view.height) {
    await op.scrollTo(entry, { label: `scroll the menu to ${item}` });
  }
  await op.click(entry, { label: item });
}

/**
 * API session for an API task, set up outside the measured part. The requests are typed and
 * counted in Odoo's JSON-2 form and carried by its external JSON-RPC endpoint: the harness's
 * "odoo-json2" transport (lib/api-transport.mjs) re-envelopes each one, with the admin sign-in.
 */
export async function odooApi(ctx) {
  const rpc = await adminRpc(ctx);
  return { baseUrl: ctx.product.baseUrl, transport: 'odoo-json2', user: 'admin', uid: rpc.uid };
}
