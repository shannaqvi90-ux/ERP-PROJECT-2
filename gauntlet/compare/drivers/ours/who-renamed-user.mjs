import { oursAs } from '../../lib/ours-api.mjs';

// Things are found by role and label (the Users link, the search box, the row), never by layout.
const usersLink = page => page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first();

/** The person who changes users in set-up: an ordinary account whose one role lets it read and change users. */
const EDITOR = { local: 'noor.editor', role: 'User maintenance (comparison)' };

/** The latest change under the record's heading names the old and the new name, the person and a time. */
const latestShows = ([oldName, newName, who]) => {
  const line = document.querySelector('[data-testid="audit-latest"]');
  const text = line ? line.innerText : '';
  return text.includes(oldName) && text.includes(newName) && text.includes(who) && !!line.querySelector('time');
};

/** The name's initials as the users list's search reads them: one letter per word (spaces and hyphens). */
const initialsOf = name => name.toLocaleLowerCase().split(/[\s-]+/).filter(Boolean).map(w => w[0]).join('');

/** The rows the users list shows on its first screen without scrolling (1600 x 900), as the reference's driver counts them. */
const FIRST_SCREEN_ROWS = 15;

/** Every piece of the name of 2 to `longest` characters (no leading or trailing space), shortest first. */
export function namePieces(name, longest = 4) {
  const n = name.toLocaleLowerCase();
  const out = new Set();
  for (let len = 2; len <= Math.min(longest, n.length); len++) {
    for (let i = 0; i + len <= n.length; i++) {
      const piece = n.slice(i, i + len);
      // A piece names the user only with two letters or digits at least (a single letter names nobody).
      if (piece.trim() === piece && piece.replace(/[^\p{L}\p{N}]+/gu, '').length >= 2) out.add(piece);
    }
  }
  return [...out].sort((a, b) => a.length - b.length || a.localeCompare(b));
}

/**
 * The shortest piece of the name whose search puts the user on the list's first screen, found
 * through the list's own search before the run (the reference's driver finds its shortest piece the
 * same way, through its back end); null when none of up to four characters does.
 */
async function shortestPiece(api, name, login, maxProbes = 600) {
  for (const piece of namePieces(name).slice(0, maxProbes)) {
    const page = await api.get(`/api/identity/users?take=${FIRST_SCREEN_ROWS}&search=${encodeURIComponent(piece)}`);
    if (page.items?.some(u => u.email?.toLowerCase() === login.toLowerCase())) return piece;
  }
  return null;
}

// The expert paths: open Users (the search box has the focus), type a piece of the name, open the
// user; the record's heading already shows its latest change: who, when, the old and new name.
function build(how) {
  return async (op, ctx) => {
    const { name } = ctx.needles.user;
    const { newName, changedBy } = ctx.task.input;
    await op.click(usersLink(op.page), { label: 'Users' });
    await op.waitFor(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]'), { label: 'user list ready, search focused' });
    const typed = how === 'initials' ? ctx.state.initials : how === 'shortest' ? ctx.state.shortest ?? newName : newName;
    await op.type(typed, { label: 'a piece of the name' });
    const row = op.page.getByRole('row').filter({ hasText: newName }).first();
    await op.waitFor(row, { label: 'the row of the user' });
    if (how === 'initials' || how === 'shortest') {
      // A click on the user's row (scrolled to first, counted, when it is not on the screen).
      const box = await row.boundingBox();
      const grid = await op.page.getByRole('grid').first().boundingBox().catch(() => null);
      const view = op.page.viewportSize();
      const inside = (b, outer) => !outer || (b.y >= outer.y && b.y + b.height <= outer.y + outer.height);
      if (!(box && inside(box, view ? { y: 0, height: view.height } : null) && inside(box, grid))) await op.scrollTo(row, { label: 'scroll to the user' });
      await op.click(row, { label: 'open the user' });
    } else if (how === 'row') await op.click(row, { label: 'open the user' });
    else await op.press('Enter', { label: 'open the best match' });
    await op.waitFor(latestShows, { label: 'the latest change on the record', arg: [name, newName, changedBy], timeout: 20_000 });
    return {};
  };
}

export default {
  built: true,
  path: 'Users (navigation; the search box has the focus) > a piece of the name > open the user: the heading shows the latest change (who, when, old and new name).',
  async setup(ctx) {
    const admin = await oursAs(ctx.product, 'admin');
    const { login, name, lang } = ctx.needles.user;
    const { newName, changedBy } = ctx.task.input;
    const password = ctx.product.users.admin.password;
    let found = (await admin.get(`/api/identity/users?search=${encodeURIComponent(login)}`)).items.find(u => u.email.toLowerCase() === login.toLowerCase());
    if (!found) {
      // A driver health check (a clean stack without the dataset) creates the one user.
      if (!ctx.health) throw new Error(`our product does not hold the dataset user ${login}; start it with ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv on a fresh database`);
      found = await admin.post('/api/identity/users', { email: login, displayName: name, language: lang === 'ar' ? 'ar' : 'en', password, roleIds: [] });
    }
    // The editor: an ordinary account whose only role reads and changes users.
    // Its address names nothing of the user's sign-in (set-up writes may not carry its words, the
    // demo's own domain included).
    const editorLogin = `${EDITOR.local}@comparison.test`;
    if (!(await admin.get(`/api/identity/users?search=${encodeURIComponent(editorLogin)}`)).items.some(u => u.email.toLowerCase() === editorLogin)) {
      const roles = (await admin.get('/api/identity/roles?take=200')).items;
      const role = roles.find(r => r.nameEn === EDITOR.role) ||
        await admin.post('/api/identity/roles', { nameEn: EDITOR.role, nameAr: 'صيانة المستخدمين (مقارنة)', permissions: ['identity.users.read', 'identity.users.update'] });
      await admin.post('/api/identity/users', { email: editorLogin, displayName: changedBy, language: 'en', password, mustChangePassword: false, roleIds: [role.id] });
    }
    // The change the person looks for, made a moment before the task by the editor: the name only (the
    // Arabic name, language, status and roles sent back as they are).
    const editor = await oursAs(ctx.product, { login: editorLogin, password });
    const record = await editor.get(`/api/identity/users/${found.id}`);
    const rename = displayName => editor.put(`/api/identity/users/${found.id}`, {
      displayName, displayNameAr: record.displayNameAr ?? null, language: record.language, isActive: record.isActive, roleIds: record.roleIds, version: record.version,
    });
    // An earlier run whose clean-up did not happen left the editor's rename as the latest change: it stays
    // (putting the old name back here would send its words in a set-up write).
    if (record.displayName !== newName) await rename(newName);
    ctx.state.userId = found.id;
    ctx.state.initials = initialsOf(newName);
    ctx.state.shortest = await shortestPiece(admin, newName, login);
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
  run: build('initials'),
  variants: {
    initials: { path: 'Users (the search box has the focus) > the initials of the name the user has now ("map") > click the user\'s row: the heading shows the latest change.', run: build('initials') },
    shortest: { path: 'Users (the search box has the focus) > the shortest piece of the name that puts the user on the first screen (found through the list\'s search before the run) > click the user\'s row: the heading shows the latest change.', run: build('shortest') },
    enter: { path: 'Users (the search box has the focus) > the name > Enter: the heading shows the latest change.', run: build('enter') },
    row: { path: 'Users (the search box has the focus) > the name > click the row: the heading shows the latest change.', run: build('row') },
  },
  async verify(ctx) {
    const { name } = ctx.needles.user;
    const { newName, changedBy } = ctx.task.input;
    const shown = await ctx.read(() => {
      const line = document.querySelector('[data-testid="audit-latest"]');
      return { text: line ? line.innerText : '', time: !!line && !!line.querySelector('time') };
    });
    const ok = shown.text.includes(name) && shown.text.includes(newName) && shown.text.includes(changedBy) && shown.time;
    return { verified: ok, details: { latest_change_on_screen: shown.text.replace(/\s+/g, ' ').slice(0, 300) } };
  },
  async cleanup(ctx) {
    if (!ctx.state.userId) return;
    const admin = await oursAs(ctx.product, 'admin');
    const record = await admin.get(`/api/identity/users/${ctx.state.userId}`);
    if (record.displayName !== ctx.needles.user.name) {
      await admin.put(`/api/identity/users/${ctx.state.userId}`, {
        displayName: ctx.needles.user.name, displayNameAr: record.displayNameAr ?? null, language: record.language, isActive: record.isActive, roleIds: record.roleIds, version: record.version,
      });
    }
  },
};
