import { oursAs } from '../../lib/ours-api.mjs';

// Things are found by role and label (the Users link, the search box, the row with the name),
// never by layout, so the driver keeps working while the users screen changes.
const usersLink = page => page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first();
const searchBox = page => page.getByRole('searchbox').or(page.getByLabel(/search/i)).first();

const recordShows = l => [...document.querySelectorAll('input, textarea, dd, output, [role="dialog"], [role="complementary"], form, aside')]
  .some(el => !el.closest('table, [role="grid"], [role="rowgroup"]') && (el.value === l || (el.children.length === 0 && el.textContent.trim() === l) || el.matches('[role="dialog"], [role="complementary"], form, aside') && el.textContent.includes(l)));

/**
 * The fewest letters an expert of this product types to put the user first: the shortest word
 * prefixes of the name (any words, in the name's order) whose ranked search answers the user as
 * the best match, found through the users list's own search before the measured run (critic p03
 * round 5 found 'm an pi' this way; the Odoo side's shortest fragment was probed the same way).
 * Falls back to the first three letters of each word when no prefix of up to four letters a word
 * ranks the user first.
 */
export function prefixCandidates(name, longest = 4) {
  const words = name.toLocaleLowerCase().split(/\s+/).filter(Boolean);
  let combos = [[]];
  for (const w of words) {
    const next = [];
    for (const c of combos) for (let l = 0; l <= Math.min(longest, w.length); l++) next.push([...c, w.slice(0, l)]);
    combos = next;
  }
  const typed = [...new Set(combos.map(c => c.filter(Boolean).join(' ')).filter(Boolean))];
  return typed.sort((a, b) => a.length - b.length || a.localeCompare(b));
}

async function shortestPrefixes(api, name, login, maxProbes = 400) {
  for (const typed of prefixCandidates(name).slice(0, maxProbes)) {
    const page = await api.get(`/api/identity/users?take=1&search=${encodeURIComponent(typed)}`);
    if (page.items?.[0]?.email?.toLowerCase() === login.toLowerCase()) return typed;
  }
  return name.split(/\s+/).map(w => w.slice(0, 3)).join(' ').toLocaleLowerCase();
}

// The shortest expert paths (critic p05 round 4). The search box has the focus when the users
// screen opens, so the name is typed straight away (no click into the box), and Enter opens the
// best match of a ranked search.
function build(how) {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    if (how === 'palette') {
      await op.press('Control+k', { label: 'command palette' });
      await op.waitFor(() => document.activeElement && document.activeElement.tagName === 'INPUT', { label: 'palette input focused' });
      await op.type('users', { label: 'users' });
      await op.waitFor(() => [...document.querySelectorAll('[role="option"]')].some(o => /users/i.test(o.textContent)), { label: 'users offered' });
      await op.press('Enter', { label: 'open Users' });
    } else {
      await op.click(usersLink(op.page), { label: 'Users' });
    }
    await op.waitFor(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]'), { label: 'user list ready, search focused' });
    // 'prefixes': a generic habit of an expert of this product (best match first, word starts
    // score): the first three letters of each word of the name, then Enter opens the best match.
    // 'shortest': the fewest letters that rank the user first, found in set-up (shortestPrefixes).
    const typed = how === 'critic-row' ? 'm pi' : how === 'shortest' ? ctx.state.shortest : how === 'prefixes' ? name.split(/\s+/).map(w => w.slice(0, 3)).join(' ') : name;
    await op.type(typed, { label: 'user name' });
    const row = op.page.getByRole('row').filter({ hasText: name }).first();
    await op.waitFor(row, { label: 'the row with the name' });
    await op.shot('result list');
    if (how === 'row' || how === 'critic-row') await op.click(row, { label: 'open the user' });
    else await op.press('Enter', { label: 'open the best match' });
    await op.waitFor(recordShows, { label: 'the user\'s record with the sign-in', arg: login, timeout: 20_000 });
    return {};
  };
}

export default {
  built: true,
  path: 'Users (navigation; the search box has the focus) > type the name > Enter opens the best match: the user\'s record shows the sign-in.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { login, name, lang } = ctx.needles.user;
    const found = await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`);
    if (!found.items.some(u => u.email.toLowerCase() === login.toLowerCase())) {
      // A driver health check (./erp verify, a clean stack without the dataset) creates the one user.
      if (ctx.health) await api.post('/api/identity/users', { email: login, displayName: name, language: lang === 'ar' ? 'ar' : 'en', password: ctx.product.users.admin.password, roleIds: [] });
      else throw new Error(`our product does not hold the dataset user ${login}; start it with ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv on a fresh database`);
    }
    ctx.state.shortest = await shortestPrefixes(api, name, login);
  },
  async signIn(ctx) {
    const { login, password } = ctx.product.users.admin;
    const page = ctx.page;
    await page.goto(ctx.product.baseUrl + '/');
    await page.getByLabel('E-mail', { exact: true }).or(page.locator('input[name="email"]')).first().fill(login);
    await page.getByLabel('Password', { exact: true }).or(page.locator('input[name="password"]')).first().fill(password);
    await page.keyboard.press('Enter');
    await usersLink(page).waitFor();
  },
  ready: page => usersLink(page),
  run: build('enter'),
  variants: {
    enter: { path: 'Users (navigation; the search box has the focus) > type the name > Enter opens the best match.', run: build('enter') },
    row: { path: 'Users (navigation; the search box has the focus) > type the name > click the row.', run: build('row') },
    prefixes: { path: 'Users (navigation; the search box has the focus) > the first three letters of each word of the name > Enter opens the best match (verified to be the user).', run: build('prefixes') },
    palette: { path: 'Ctrl+K > "users" > Enter (the search box has the focus) > type the name > Enter opens the best match.', run: build('palette') },
    'critic-row': { path: 'Users (navigation; the search box has the focus) > type "m pi" (1,651 match, best match first; the user is row 17 of the first screen) > click the user.', run: build('critic-row') },
    shortest: { path: 'Users (navigation; the search box has the focus) > the shortest word prefixes of the name that rank the user first (found through the list\'s search before the run, e.g. "m an pi") > Enter opens the best match.', run: build('shortest') },
  },
  async verify(ctx) {
    const { name, login } = ctx.needles.user;
    const shown = await ctx.read(([n, l]) => {
      const outside = [...document.querySelectorAll('[role="dialog"], [role="complementary"], form, aside')].filter(el => !el.closest('table, [role="grid"]'));
      const text = outside.map(el => el.innerText).join('\n') + '\n' + outside.flatMap(el => [...el.querySelectorAll('input, textarea')].map(i => i.value)).join('\n');
      return { name: text.includes(n), login: text.includes(l) };
    }, [name, login]);
    return { verified: shown.name && shown.login, details: { record_shows_name: shown.name, record_shows_login: shown.login, url: ctx.page.url() } };
  },
};
