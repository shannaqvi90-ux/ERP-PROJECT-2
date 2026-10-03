// Driver for our product, written by the p03 critic (round 1).
// Our product has no contacts module yet (p16), so no role can grant "view and create contacts".
// The nearest restricted role that exists is the seeded "Read-only" role (no administration, no
// writes). The measured path is the shortest expert path to a saved user holding only that role.
// Users cannot be deleted through the product, so set-up/clean-up remove the task's user directly
// in the demo database (outside the measurement); COMPARE_OURS_DB_CONTAINER names its container.
import { execFileSync } from 'node:child_process';
import { oursAs } from '../../lib/ours-api.mjs';

const DB = process.env.COMPARE_OURS_DB_CONTAINER || 'erp-db-1';
const ADMIN_PERMS = ['identity.users.create', 'identity.users.update', 'identity.users.resetPassword', 'identity.roles.create',
  'identity.roles.update', 'identity.roles.delete', 'tenancy.tenant.update'];

function removeUser(login) {
  const sql = `SELECT set_config('app.actor_kind','migration',false);
    DELETE FROM identity.users WHERE email_normalized = '${login.toLowerCase().replace(/'/g, "''")}';`;
  execFileSync('docker', ['exec', '-i', DB, 'psql', '-U', 'postgres', '-d', 'erp', '-q', '-v', 'ON_ERROR_STOP=1'], { input: sql });
}

async function signIn(ctx) {
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
}

function pathRun(keyboardRole) {
  return async (op, ctx) => {
    const { login } = ctx.task.input;
    await op.click('nav a[href="/identity/users"] >> nth=0', { label: 'Users (navigation)' });
    await op.waitFor('main table tbody tr', { label: 'users list' });
    await op.press('n', { label: 'New user (n)' });
    await op.waitFor('input[name="email"]:focus', { label: 'new user form, e-mail focused' });
    await op.type(login, { label: 'e-mail', chain: true });
    if (keyboardRole) {
      // The name is suggested from the e-mail ("hessa.clerk" -> "Hessa Clerk"); Tab passes name and language.
      await op.press('Tab', { label: 'next field (name)' });
      await op.press('Tab', { label: 'next field (language)' });
      await op.press('Tab', { label: 'next field (find a role)' });
      await op.type('read', { label: 'find a role', chain: true });
      await op.press('Enter', { label: 'tick the first role shown (Read-only)', chain: true });
    } else {
      await op.click('aside label:has-text("Read-only") input[type=checkbox]', { label: 'Read-only role' });
    }
    await op.shot('user filled in');
    await op.press('Control+Enter', { label: 'Create (Ctrl+Enter)' });
    await op.waitFor('[data-testid="setup-code"]', { label: 'saved: set-up code shown' });
    return {};
  };
}

export default {
  built: true,
  path: 'Users (navigation) > n > e-mail > Read-only role > Ctrl+Enter. The name is filled from the e-mail. Two expert variants; the result counts the better one per metric.',
  run: pathRun(false),
  variants: {
    pointer: { path: 'Users > n (e-mail focused) > type e-mail > click Read-only > Ctrl+Enter', run: pathRun(false) },
    keyboard: { path: 'Users > n > type e-mail > Tab Tab Tab > "read" > Enter > Ctrl+Enter', run: pathRun(true) },
  },
  async setup(ctx) { removeUser(ctx.task.input.login); },
  signIn,
  async verify(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { name, login } = ctx.task.input;
    const found = (await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`)).items.filter(u => u.email.toLowerCase() === login);
    if (found.length !== 1) return { verified: false, details: { found: found.length } };
    const access = await api.get(`/api/identity/users/${found[0].id}/access`);
    const held = access.permissions.map(p => p.key);
    const extra = held.filter(p => ADMIN_PERMS.includes(p));
    return {
      verified: found[0].displayName === name && extra.length === 0 && access.roles.length === 1 && !access.roles[0].isSystem,
      details: { displayName: found[0].displayName, roles: access.roles.map(r => r.nameEn), permissions: held, unexpected: extra,
        note: 'no contacts permission exists in our product yet; nearest restricted role used' },
    };
  },
  async cleanup(ctx) { removeUser(ctx.task.input.login); },
};
