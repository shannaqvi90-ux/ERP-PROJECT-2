import { adminRpc, odooApi } from './_common.mjs';

const ODOO_LANG = { en: 'en_US', ar: 'ar_001' };

async function userIds(ctx) {
  return (await adminRpc(ctx)).call('res.users', 'search', [[['login', '=', ctx.needles.user.login]]], { context: { active_test: false } });
}

export default {
  built: true,
  path: 'POST /json/2/res.users/search (domain: name; answers the ids) > POST /json/2/res.users/write (ids, vals: lang ar_001).',
  async setup(ctx) {
    ctx.state.ids = await userIds(ctx);
    if (ctx.state.ids.length !== 1) throw new Error(`the rig holds ${ctx.state.ids.length} users with the needle's sign-in; run tools/odoo-reference/up.sh`);
    await (await adminRpc(ctx)).write('res.users', ctx.state.ids, { lang: 'en_US' });
  },
  async signIn(ctx) { ctx.useApi(await odooApi(ctx)); },
  async run(op, ctx) {
    const { name } = ctx.needles.user;
    const found = await op.request('POST', '/json/2/res.users/search', { domain: [['name', '=', name]] }, { label: 'find the user by name' });
    if (found.body?.length !== 1) throw new Error(`search found ${found.body?.length} users`);
    await op.request('POST', '/json/2/res.users/write', { ids: found.body, vals: { lang: 'ar_001' } }, { label: 'set the language' });
    return { id: found.body[0] };
  },
  async verify(ctx) {
    // The back end: the needle user (found in set-up by sign-in, so not by the name the task searches) speaks Arabic.
    const [u] = await (await adminRpc(ctx)).read('res.users', ctx.state.ids, ['lang', 'name']);
    return { verified: u.lang === 'ar_001', details: { id: ctx.state.ids[0], lang: u.lang, name: u.name } };
  },
  async cleanup(ctx) {
    if (ctx.state.ids?.length) await (await adminRpc(ctx)).write('res.users', ctx.state.ids, { lang: ODOO_LANG[ctx.needles.user.lang] || 'en_US' });
  },
};
