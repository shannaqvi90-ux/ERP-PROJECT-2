import { adminRpc, openApp, paletteMenu, signInAs, emojiSettled } from './_common.mjs';
import { OdooRpc } from '../../lib/odoo-rpc.mjs';

/** The person who renames users in set-up: an ordinary internal user who may manage access rights. */
const EDITOR = { login: 'noor.editor.users', password: 'noor.editor.users' };

async function ensureEditor(ctx) {
  const rpc = await adminRpc(ctx);
  const groups = [await rpc.ref('base.group_user'), await rpc.ref('base.group_erp_manager')];
  const values = { name: ctx.task.input.changedBy, password: EDITOR.password, active: true, tz: 'Asia/Dubai', group_ids: [[6, 0, groups]] };
  const found = await rpc.call('res.users', 'search', [[['login', '=', EDITOR.login]]], { context: { active_test: false } });
  if (found.length) await rpc.write('res.users', found, values);
  else await rpc.create('res.users', { ...values, login: EDITOR.login, email: 'noor.editor.users@comparison.test' });
  return EDITOR;
}

/** The users list's own search for a typed text, as its search box applies it: name, sign-in or e-mail. */
const userSearch = text => ['|', '|', ['name', 'ilike', text], ['login', 'ilike', text], ['email', 'ilike', text]];
/** The rows the users list shows on its first screen without scrolling (1600 x 900). */
const FIRST_SCREEN_ROWS = 15;

/** Every piece of the sign-in (no leading or trailing space), shortest first. */
export function loginFragments(login, longest = 8) {
  const n = login.toLocaleLowerCase();
  const out = new Set();
  for (let len = 1; len <= Math.min(longest, n.length); len++) {
    for (let i = 0; i + len <= n.length; i++) out.add(n.slice(i, i + len));
  }
  return [...out].sort((a, b) => a.length - b.length || a.localeCompare(b));
}

/** The shortest piece of the sign-in whose search puts the user on the list's first screen (found through the back end). */
async function shortestSetup(ctx) {
  const rpc = await adminRpc(ctx);
  const { login } = ctx.needles.user;
  const [user] = await rpc.search('res.users', [['login', '=', login]], { limit: 1 });
  for (const text of loginFragments(login).slice(0, 600)) {
    const ids = await rpc.search('res.users', userSearch(text), { limit: FIRST_SCREEN_ROWS, order: 'name, login' });
    if (ids.includes(user)) {
      ctx.state.shortest = text;
      return;
    }
  }
  ctx.state.shortest = login;
}

/** The latest message beside the form names the old and new name and the person. */
const latestShows = ([oldName, newName, who]) => {
  const first = document.querySelector('.o-mail-Chatter .o-mail-Message');
  const text = first?.innerText || '';
  return text.includes(oldName) && text.includes(newName) && text.includes(who);
};

function build({ palette = false, suggestion = false, shortest = false }) {
  return async (op, ctx) => {
    const { login, name } = ctx.needles.user;
    const { newName, changedBy } = ctx.task.input;
    if (palette) await paletteMenu(op, '/users', 'Settings / Users & Companies / Users');
    else {
      await openApp(op, 'Settings');
      await op.click(op.page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    }
    await op.waitFor('.o_searchview_input:focus', { label: 'user list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 0, { label: 'first page of users' });
    await op.type(shortest ? ctx.state.shortest : login, { label: 'a piece of the sign-in' });
    if (suggestion) {
      const item = op.page.locator('.o_searchview_autocomplete .o-dropdown-item, .o_searchview_autocomplete li').first();
      await op.waitFor(item, { label: 'search suggestions' });
      await op.click(item, { label: 'search the piece' });
    } else {
      await op.press('Enter', { label: 'search' });
    }
    await op.waitFor(() => !!document.querySelector('.o_searchview_facet') && !document.querySelector('.o_loading_indicator'), { label: 'search applied' });
    const row = op.page.locator('.o_data_row').filter({ hasText: newName }).first();
    await op.waitFor(row, { label: 'the row of the user' });
    await op.click(row, { label: 'open the user' });
    await op.waitFor(latestShows, { label: 'the latest change beside the form', arg: [name, newName, changedBy], timeout: 20_000 });
    await op.waitFor(emojiSettled, { label: 'the chatter\'s emoji data in' });
    return {};
  };
}

const base = {
  built: true,
  path: 'Users list by the menus or the command palette > a piece of the sign-in (the whole of it, or the shortest piece that puts the user on the first screen) > Enter or the first search suggestion > open the user: the change log beside the form shows the latest change first (old > new name, who, when).',
  async setup(ctx) {
    const admin = await adminRpc(ctx);
    const { login } = ctx.needles.user;
    const [user] = await admin.searchRead('res.users', [['login', '=', login]], ['id', 'name']);
    if (!user) throw new Error(`the rig holds no user ${login}; run tools/odoo-reference/up.sh`);
    // The change to find, made a moment before the task by an ordinary user who may manage users. An
    // earlier run whose clean-up did not happen left it as the latest change: it stays (putting the old
    // name back here would send the sign-in's words in a set-up write).
    const editor = await ensureEditor(ctx);
    const asEditor = await new OdooRpc(ctx.product).login(editor);
    if (user.name !== ctx.task.input.newName) await asEditor.write('res.users', [user.id], { name: ctx.task.input.newName });
    ctx.state.user = user;
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  run: build({}),
  variants: {
    menus: { path: 'Apps menu > Settings > Manage Users (the search box has focus) > the sign-in > Enter > open the user.', run: build({}) },
    palette: { path: 'Ctrl+K > "/users" > Enter > the sign-in > Enter > open the user.', run: build({ palette: true }) },
    'menus-suggestion': { path: 'Apps menu > Settings > Manage Users > the sign-in > click the first search suggestion > open the user.', run: build({ suggestion: true }) },
    'shortest-suggestion': { path: 'Apps menu > Settings > Manage Users > the shortest piece of the sign-in that puts the user on the first screen (found through the back end before the run) > click the first search suggestion > open the user.', run: build({ suggestion: true, shortest: true }), setup: async ctx => { await shortestSetup(ctx); await base.setup(ctx); } },
    'shortest-enter': { path: 'Apps menu > Settings > Manage Users > the shortest piece of the sign-in that puts the user on the first screen > Enter > open the user.', run: build({ shortest: true }), setup: async ctx => { await shortestSetup(ctx); await base.setup(ctx); } },
    'palette-shortest': { path: 'Ctrl+K > "/users" > Enter > the shortest piece of the sign-in that puts the user on the first screen > Enter > open the user.', run: build({ palette: true, shortest: true }), setup: async ctx => { await shortestSetup(ctx); await base.setup(ctx); } },
  },
  async verify(ctx) {
    const { name } = ctx.needles.user;
    const { newName, changedBy } = ctx.task.input;
    // The latest change, as the screen showed it when the clock stopped.
    const text = await ctx.read(() => document.querySelector('.o-mail-Chatter .o-mail-Message')?.innerText || '');
    const hasTime = /\d{1,2}:\d{2}|ago|now/i.test(text);
    return {
      verified: text.includes(name) && text.includes(newName) && text.includes(changedBy) && hasTime,
      details: { latest_change_on_screen: text.replace(/\s+/g, ' ').slice(0, 300) },
    };
  },
  async cleanup(ctx) {
    if (ctx.state.user) await (await adminRpc(ctx)).write('res.users', [ctx.state.user.id], { name: ctx.needles.user.name });
  },
};

export default base;
