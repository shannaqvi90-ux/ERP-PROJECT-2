import { adminRpc, openApp, paletteMenu, signInAs } from './_common.mjs';

/**
 * Two expert paths to the user list: `menus` (Apps menu > Settings > Manage Users, three clicks)
 * and `palette` (Ctrl+K > "/users" > Enter, keyboard only, and it skips the settings page). The
 * name is searched either with Enter or by clicking the search box's first suggestion, which
 * saves the Enter key (one keystroke fewer; found by the p05 round 4 critic). The result counts
 * the better path per metric.
 */
/**
 * The users list's own search for a typed text ("Search User for: ..."), as the search box's first
 * suggestion applies it: the name, the sign-in or the e-mail holds the text.
 */
const userSearch = text => ['|', '|', ['name', 'ilike', text], ['login', 'ilike', text], ['email', 'ilike', text]];
/** The rows the users list shows on its first screen without scrolling (1600 x 900). */
const FIRST_SCREEN_ROWS = 15;

/** Every piece of the name (no leading or trailing space), shortest first. */
export function nameFragments(name, longest = 8) {
  const n = name.toLocaleLowerCase();
  const out = new Set();
  for (let len = 1; len <= Math.min(longest, n.length); len++) {
    for (let i = 0; i + len <= n.length; i++) {
      const f = n.slice(i, i + len);
      if (f.trim() === f && f.length) out.add(f);
    }
  }
  return [...out].sort((a, b) => a.length - b.length || a.localeCompare(b));
}

/**
 * Round 9 (p05 critic, round 7: "il pi" and the suggestion found the user in 5 keystrokes, against
 * the 17 or more of typing the name): the reference's shortest expert path is the shortest piece
 * of the name whose search puts the user on the list's first screen, in the list's own order. It is
 * found through the back end before the measured run, as ours finds its shortest prefixes.
 */
async function shortestFragment(ctx) {
  const rpc = await adminRpc(ctx);
  const { name, login } = ctx.needles.user;
  const [user] = await rpc.search('res.users', [['login', '=', login]], { limit: 1 });
  if (!user) throw new Error(`the rig holds no user ${login}; run tools/odoo-reference/up.sh`);
  for (const text of nameFragments(name).slice(0, 600)) {
    const ids = await rpc.search('res.users', userSearch(text), { limit: FIRST_SCREEN_ROWS, order: 'name, login' });
    const rank = ids.indexOf(user);
    if (rank >= 0) return { text, rank: rank + 1, shown: Math.min(ids.length, FIRST_SCREEN_ROWS) };
  }
  return { text: name, rank: 1, shown: 1 };
}

async function shortestSetup(ctx) { ctx.state.shortest = await shortestFragment(ctx); }

/** The shortest piece of the name (found in set-up) > the first suggestion or Enter > the user's row. */
function buildShortest(palette, suggestion) {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    const { text } = ctx.state.shortest;
    if (palette) await paletteMenu(op, '/users', 'Settings / Users & Companies / Users');
    else {
      await openApp(op, 'Settings');
      await op.click(op.page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    }
    await op.waitFor('.o_searchview_input:focus', { label: 'user list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 0, { label: 'first page of users' });
    await op.type(text, { label: 'a piece of the name' });
    if (suggestion) {
      const item = op.page.locator('.o_searchview_autocomplete .o-dropdown-item, .o_searchview_autocomplete li').first();
      await op.waitFor(item, { label: 'search suggestions' });
      await op.click(item, { label: 'search the piece' });
    } else {
      await op.press('Enter', { label: 'search' });
    }
    const row = op.page.locator('.o_data_row').filter({ hasText: name }).first();
    await op.waitFor(() => !!document.querySelector('.o_searchview_facet') && !document.querySelector('.o_loading_indicator'), { label: 'search applied' });
    await op.waitFor(row, { label: 'the row with the name' });
    await op.shot('result list');
    await op.click(row, { label: 'open the user' });
    await op.waitFor(m => {
      const form = document.querySelector('.o_form_view');
      return !!form && (form.innerText.includes(m) || [...form.querySelectorAll('input')].some(i => i.value === m));
    }, { label: 'sign-in shown', arg: login });
    return {};
  };
}

function build(palette, suggestion = false) {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    if (palette) await paletteMenu(op, '/users', 'Settings / Users & Companies / Users');
    else {
      await openApp(op, 'Settings');
      await op.click(op.page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    }
    await op.waitFor('.o_searchview_input:focus', { label: 'user list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 0, { label: 'first page of users' });
    await op.type(name, { label: 'user name' });
    if (suggestion) {
      const item = op.page.locator('.o_searchview_autocomplete .o-dropdown-item, .o_searchview_autocomplete li').first();
      await op.waitFor(item, { label: 'search suggestions' });
      await op.click(item, { label: 'search the name' });
    } else {
      await op.press('Enter', { label: 'search' });
    }
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length === 1, { label: 'one result' });
    await op.shot('result list');
    await op.click(op.page.locator('.o_data_row').first(), { label: 'open the result' });
    await op.waitFor(m => {
      const form = document.querySelector('.o_form_view');
      return !!form && (form.innerText.includes(m) || [...form.querySelectorAll('input')].some(i => i.value === m));
    }, { label: 'sign-in shown', arg: login });
    return {};
  };
}

export default {
  built: true,
  run: build(false),
  variants: {
    menus: { path: 'Apps menu > Settings > Manage Users (the search box has focus) > type the name > Enter > open the single result.', run: build(false) },
    palette: { path: 'Ctrl+K > type "/users" > Enter (the search box has focus) > type the name > Enter > open the single result.', run: build(true) },
    'menus-suggestion': { path: 'Apps menu > Settings > Manage Users (the search box has focus) > type the name > click the first search suggestion > open the single result.', run: build(false, true) },
    'shortest-suggestion': { path: 'Apps menu > Settings > Manage Users > the shortest piece of the name that puts the user on the first screen (found through the back end before the run, e.g. "il pi") > click the first search suggestion > click the user\'s row.', run: buildShortest(false, true), setup: shortestSetup },
    'shortest-enter': { path: 'Apps menu > Settings > Manage Users > the shortest piece of the name that puts the user on the first screen > Enter > click the user\'s row.', run: buildShortest(false, false), setup: shortestSetup },
    'palette-shortest': { path: 'Ctrl+K > "/users" > Enter > the shortest piece of the name that puts the user on the first screen > Enter > click the user\'s row.', run: buildShortest(true, false), setup: shortestSetup },
  },
  path: 'Users list by the menus or the command palette > type the name, or the shortest piece of it that puts the user on the first screen > Enter or click the first search suggestion > open the user.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async verify(ctx) {
    const { name, login } = ctx.needles.user;
    const shown = await ctx.read(() => ({
      text: document.querySelector('.o_form_view')?.innerText || '',
      inputs: [...document.querySelectorAll('.o_form_view input, .o_form_view textarea')].map(i => i.value),
    }));
    const has = v => shown.text.includes(v) || shown.inputs.includes(v);
    return { verified: has(name) && has(login), details: { url: ctx.page.url(), name_shown: has(name), login_shown: has(login) } };
  },
};
