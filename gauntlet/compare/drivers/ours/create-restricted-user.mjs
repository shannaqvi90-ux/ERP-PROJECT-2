import { oursAs } from '../../lib/ours-api.mjs';

// The shortest expert path through our users screen: Users > Alt+N (the list arrives with the
// cursor in its search box, where a plain n would be typed; Alt+N works from there and the e-mail
// field takes the focus) > the
// address > the role, found by typing part of its name > Ctrl+Enter. The name is suggested from the
// address ("hessa.clerk" -> "Hessa Clerk") and the set-up code to hand over appears at once.
//
// The task asks for someone who may view and create contacts and nothing else. Roles take any
// module's permissions as they come (tests/Erp.Modules.Identity.Tests/ModulePermissionsTests.cs);
// while no contacts module is installed (p16, wave 2) the catalogue has no contacts permission, so
// set-up makes the nearest restricted role the catalogue allows, "Clerk (restricted)" (own
// preferences only: the seeded Read-only role also reads the workspace settings, which this task
// forbids), and verify says so in its details. Set-up and clean-up remove the task's user through the API (a user who never signed in
// can be deleted), outside the measured part.

const ADMINISTRATION = /^(identity\.(users|roles)\.(create|update|delete|resetPassword)|identity\.signIns\.read|tenancy\.)/;
const CONTACTS = ['contacts.contacts.read', 'contacts.contacts.create'];

async function removeTaskUser(api, login) {
  const found = await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`);
  for (const u of found.items.filter(x => x.email.toLowerCase() === login.toLowerCase())) {
    await api.request('DELETE', `/api/identity/users/${u.id}`, undefined, { allow: [404] });
  }
}

/** The role the task gives: "Contacts clerk" (view and create contacts) when the catalogue has those permissions, else "Clerk (restricted)". */
async function ensureRole(api) {
  const catalogue = new Set((await api.get('/api/identity/permissions')).map(p => p.key));
  const roles = (await api.get('/api/identity/roles?take=200')).items;
  if (CONTACTS.every(k => catalogue.has(k))) {
    const clerk = roles.find(r => r.nameEn === 'Contacts clerk');
    if (!clerk) await api.post('/api/identity/roles', { nameEn: 'Contacts clerk', nameAr: 'كاتب جهات الاتصال', permissions: CONTACTS });
    return { filter: 'clerk', name: 'Contacts clerk', contacts: true };
  }
  const clerk = roles.find(r => r.nameEn === 'Clerk (restricted)');
  if (!clerk) await api.post('/api/identity/roles', { nameEn: 'Clerk (restricted)', nameAr: 'كاتب (مقيد)', permissions: ['identity.profile.update'] });
  return { filter: 'clerk', name: 'Clerk (restricted)', contacts: false };
}

function pathRun(keyboardRole) {
  return async (op, ctx) => {
    const { login } = ctx.task.input;
    await op.click('nav a[href="/identity/users"] >> nth=0', { label: 'Users (navigation)' });
    await op.waitFor('main table tbody tr', { label: 'users list' });
    await op.press('Alt+n', { label: 'New user (Alt+N)' });
    await op.waitFor('input[name="email"]:focus', { label: 'new user form, e-mail focused' });
    await op.type(login, { label: 'e-mail', chain: true });
    if (keyboardRole) {
      // Tab passes the suggested name and the language to the role finder; Enter ticks the first role shown.
      await op.press('Tab', { label: 'next field (name)' });
      await op.press('Tab', { label: 'next field (language)' });
      await op.press('Tab', { label: 'next field (find a role)' });
      await op.type(ctx.state.role.filter, { label: 'find the role', chain: true });
      await op.press('Enter', { label: 'tick the first role shown', chain: true });
    } else {
      await op.click(`aside label:has-text("${ctx.state.role.name}") input[type=checkbox]`, { label: 'the role' });
    }
    await op.shot('user filled in');
    await op.press('Control+Enter', { label: 'Create (Ctrl+Enter)' });
    await op.waitFor('[data-testid="setup-code"]', { label: 'saved: set-up code shown' });
    return {};
  };
}

export default {
  built: true,
  path: 'Users (navigation) > Alt+N > e-mail > the role > Ctrl+Enter. The name is filled from the e-mail. Two expert variants; the result counts the better one per metric.',
  run: pathRun(false),
  variants: {
    pointer: { path: 'Users > Alt+N (e-mail focused) > type the e-mail > click the role > Ctrl+Enter', run: pathRun(false) },
    keyboard: { path: 'Users > Alt+N > type the e-mail > Tab Tab Tab > part of the role name > Enter > Ctrl+Enter', run: pathRun(true) },
  },
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    await removeTaskUser(api, ctx.task.input.login);
    ctx.state.role = await ensureRole(api);
  },
  async signIn(ctx) {
    const page = ctx.page;
    const { login, password } = ctx.product.users.admin;
    await page.goto(ctx.product.baseUrl + '/');
    await page.locator('input[name="email"]:focus').waitFor();
    await page.keyboard.type(login);
    await page.keyboard.press('Tab');
    await page.keyboard.type(password);
    await page.keyboard.press('Enter');
    await page.locator('nav[aria-label="Main navigation"]').waitFor();
    await page.locator('main h1').waitFor();
  },
  async verify(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { name, login } = ctx.task.input;
    const found = (await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`)).items.filter(u => u.email.toLowerCase() === login.toLowerCase());
    if (found.length !== 1) return { verified: false, details: { found: found.length } };
    const access = await api.get(`/api/identity/users/${found[0].id}/access`);
    const held = access.permissions.map(p => p.key);
    const administration = held.filter(p => ADMINISTRATION.test(p));
    const contactsOk = !ctx.state.role.contacts || (CONTACTS.every(k => held.includes(k)) && held.every(k => CONTACTS.includes(k)));
    return {
      verified: found[0].displayName === name && administration.length === 0 && access.roles.length === 1 && !access.roles[0].isSystem && contactsOk,
      details: {
        displayName: found[0].displayName,
        roles: access.roles.map(r => r.nameEn),
        permissions: held,
        administration,
        note: ctx.state.role.contacts ? 'view and create contacts only' : 'no contacts permission exists in our product yet (p16); the nearest restricted role, Clerk (restricted): own preferences only, was used',
      },
    };
  },
  async cleanup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    await removeTaskUser(api, ctx.task.input.login);
  },
};
