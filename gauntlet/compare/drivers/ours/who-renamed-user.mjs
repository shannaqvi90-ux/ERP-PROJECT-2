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

/** The shortest run of the sign-in's digits that finds exactly this user (found before the run). */
async function shortestDigits(api, login) {
  const digits = (login.match(/\d+/) || [''])[0];
  for (let len = 3; len <= digits.length; len++) {
    for (let at = digits.length - len; at >= 0; at--) {
      const piece = digits.slice(at, at + len);
      const page = await api.get(`/api/identity/users?take=2&search=${encodeURIComponent(piece)}`);
      if (page.total === 1 && page.items[0]?.email?.toLowerCase() === login.toLowerCase()) return piece;
    }
  }
  return login;
}

// The expert paths: open Users (the search box has the focus), type a piece of the sign-in, open the
// user; the record's heading already shows its latest change: who, when, the old and new name.
function build(how) {
  return async (op, ctx) => {
    const { login, name } = ctx.needles.user;
    const { newName, changedBy } = ctx.task.input;
    await op.click(usersLink(op.page), { label: 'Users' });
    await op.waitFor(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]'), { label: 'user list ready, search focused' });
    const typed = how === 'initials' ? ctx.state.initials : how === 'digits' ? ctx.state.digits : login;
    await op.type(typed, { label: 'a piece of the sign-in' });
    const row = op.page.getByRole('row').filter({ hasText: newName }).first();
    await op.waitFor(row, { label: 'the row of the user' });
    if (how === 'initials' && !ctx.state.initialsFirst) {
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
  path: 'Users (navigation; the search box has the focus) > a piece of the sign-in > open the user: the heading shows the latest change (who, when, old and new name).',
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
    // (putting the old name back here would send the sign-in's words in a set-up write).
    if (record.displayName !== newName) await rename(newName);
    ctx.state.userId = found.id;
    ctx.state.initials = initialsOf(newName);
    ctx.state.digits = await shortestDigits(admin, login);
    const byInitials = await admin.get(`/api/identity/users?take=1&search=${encodeURIComponent(ctx.state.initials)}`);
    ctx.state.initialsFirst = byInitials.items?.[0]?.email?.toLowerCase() === login.toLowerCase();
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
    initials: { path: 'Users (the search box has the focus) > the initials the sign-in starts its words with ("map": majid, pillai) > Enter on the best match, or a click on the user\'s row: the heading shows the latest change.', run: build('initials') },
    digits: { path: 'Users (the search box has the focus) > the shortest run of the sign-in\'s digits that finds only this user (found through the list\'s search before the run) > Enter: the heading shows the latest change.', run: build('digits') },
    enter: { path: 'Users (the search box has the focus) > the whole sign-in > Enter: the heading shows the latest change.', run: build('enter') },
    row: { path: 'Users (the search box has the focus) > the whole sign-in > click the row: the heading shows the latest change.', run: build('row') },
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
