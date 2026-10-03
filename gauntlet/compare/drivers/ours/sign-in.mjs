import { oursAs } from '../../lib/ours-api.mjs';

/** The task's user: an ordinary user (the read-only role) with the task's e-mail and password. */
async function ensureUser(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  const { user, password, name } = ctx.task.input;
  const found = await api.get(`/api/identity/users?search=${encodeURIComponent(user)}`);
  const existing = found.items.find(u => u.email.toLowerCase() === user.toLowerCase());
  if (existing) return existing.id;
  const roles = await api.get('/api/identity/roles');
  const readOnly = (Array.isArray(roles) ? roles : roles.items).find(r => !r.isSystem);
  const created = await api.post('/api/identity/users', { email: user, displayName: name, language: 'en', password, roleIds: readOnly ? [readOnly.id] : [] });
  return created.id;
}

// Fields are found by their labels (E-mail, Password) with the field names as a fallback, so the
// driver keeps working while the sign-in screen's layout changes.
const emailField = page => page.getByLabel('E-mail', { exact: true }).or(page.locator('input[name="email"]')).first();
const passwordField = page => page.getByLabel('Password', { exact: true }).or(page.locator('input[name="password"]')).first();

/**
 * Two start states, each the user's shortest path from it. `new-device`: the first sign-in on this
 * browser. `returning`: this browser has signed in and out before (set up outside the measured
 * part); our sign-in screen then remembers the e-mail and puts the focus on the password.
 */
function variant(returning) {
  return {
    path: returning
      ? 'A browser that signed in before: the e-mail is remembered and the password has focus: type the password > Enter.'
      : 'The sign-in screen focuses the e-mail field: type the e-mail > Tab > type the password > Enter.',
    async signIn(ctx) {
      const { user, password } = ctx.task.input;
      const page = ctx.page;
      if (returning) {
        await page.goto(ctx.product.baseUrl + '/');
        await emailField(page).fill(user);
        await passwordField(page).fill(password);
        await passwordField(page).press('Enter');
        await page.getByRole('navigation').first().waitFor();
        await page.evaluate(async () => { await fetch('/api/auth/sign-out', { method: 'POST', headers: { 'X-Erp-Request': '1' } }); });
      }
      // Start state: signed out, on the bookmarked address.
      await page.goto(ctx.product.baseUrl + '/');
      await page.locator('input:focus').waitFor();
    },
    async run(op, ctx) {
      const { user, password } = ctx.task.input;
      await op.waitFor(() => !!document.activeElement && document.activeElement.tagName === 'INPUT', { label: 'sign-in screen, a field focused' });
      const remembered = (await emailField(op.page).inputValue()) === user
        && (await op.page.locator('input:focus').getAttribute('type')) === 'password';
      if (!remembered) {
        await op.type(user, { label: 'e-mail' });
        await op.press('Tab', { label: 'next field (password)' });
      }
      await op.type(password, { label: 'password', chain: !remembered });
      await op.press('Enter', { label: 'sign in', chain: true });
      await op.waitFor(op.page.getByRole('navigation', { name: 'Main navigation' }).or(op.page.locator('nav[aria-label="Main navigation"]')), { label: 'signed in, working screen ready' });
      return { remembered };
    },
  };
}

export default {
  built: true,
  path: 'Sign-in screen: e-mail > Tab > password > Enter; on a returning browser the e-mail is remembered: password > Enter.',
  run: variant(false).run,
  variants: { 'new-device': variant(false), returning: variant(true) },
  async setup(ctx) { ctx.state.userId = await ensureUser(ctx); },
  async verify(ctx) {
    const session = await ctx.page.evaluate(async () => (await fetch('/api/auth/session', { headers: { 'X-Erp-Request': '1' } })).json());
    return {
      verified: session?.authenticated === true && session.user?.id === ctx.state.userId,
      details: { authenticated: session?.authenticated, user: session?.user?.email },
    };
  },
};
