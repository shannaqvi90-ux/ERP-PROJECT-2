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

export default {
  built: true,
  path: 'The sign-in screen focuses the e-mail field: type the e-mail > Tab > type the password > Enter.',
  async setup(ctx) { ctx.state.userId = await ensureUser(ctx); },
  async signIn(ctx) {
    // Start state: signed out, on the bookmarked address, on a device that has not signed in before.
    await ctx.page.goto(ctx.product.baseUrl + '/');
    await ctx.page.locator('input[name="email"]:focus').waitFor();
  },
  async run(op, ctx) {
    const { user, password } = ctx.task.input;
    await op.waitFor('input[name="email"]:focus', { label: 'sign-in screen, e-mail focused' });
    await op.type(user, { label: 'e-mail' });
    await op.press('Tab', { label: 'next field (password)' });
    await op.type(password, { label: 'password', chain: true });
    await op.press('Enter', { label: 'sign in', chain: true });
    await op.waitFor('nav[aria-label="Main navigation"]', { label: 'signed in, working screen ready' });
    return {};
  },
  async verify(ctx) {
    const session = await ctx.page.evaluate(async () => (await fetch('/api/auth/session', { headers: { 'X-Erp-Request': '1' } })).json());
    return {
      verified: session?.authenticated === true && session.user?.id === ctx.state.userId,
      details: { authenticated: session?.authenticated, user: session?.user?.email },
    };
  },
};
