// Read-only probe of the shared Odoo rig: why the see-converted reference did not use the set-up rate.
const base = 'http://localhost:8069';
const j = async (path, params, cookie) => { const r = await fetch(base + path, { method: 'POST', headers: { 'content-type': 'application/json', ...(cookie ? { cookie } : {}) }, body: JSON.stringify({ jsonrpc: '2.0', method: 'call', params }) }); return { r, d: await r.json() }; };
const { r, d } = await j('/web/session/authenticate', { db: 'reference', login: 'admin', password: 'admin' });
let db = d.result?.db; let cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
if (d.error) { const l = await j('/web/database/list', {}); db = l.d.result[0]; const a = await j('/web/session/authenticate', { db, login: 'admin', password: 'admin' }); cookie = a.r.headers.getSetCookie().map(c => c.split(';')[0]).join('; '); }
const kw = async (model, method, args, kwargs = {}) => (await j('/web/dataset/call_kw', { model, method, args, kwargs }, cookie)).d;
const me = await kw('res.users', 'search_read', [[['login', '=', 'admin']]], { fields: ['company_id', 'company_ids'] });
console.log('admin', JSON.stringify(me.result));
const comps = await kw('res.company', 'search_read', [[]], { fields: ['name', 'currency_id'], order: 'id' });
console.log('companies', JSON.stringify(comps.result));
const eur = await kw('res.currency', 'search_read', [[['name', '=', 'EUR']]], { fields: ['id', 'active'], context: { active_test: false } });
const rates = await kw('res.currency.rate', 'search_read', [[['currency_id', '=', eur.result[0].id], ['name', '>=', '2026-09-25']]], { fields: ['name', 'company_id', 'inverse_company_rate'], order: 'name desc', limit: 10 });
console.log('eur rates', JSON.stringify(rates.result));
// Replay the driver's set-up write, read it back, then remove it (as the driver's clean-up does).
const created = await kw('res.currency.rate', 'create', [{ currency_id: eur.result[0].id, company_id: 1, name: '2026-10-28', inverse_company_rate: 4.2875 }]);
console.log('create', JSON.stringify(created).slice(0, 300));
const back = await kw('res.currency.rate', 'search_read', [[['currency_id', '=', eur.result[0].id], ['name', '=', '2026-10-28']]], { fields: ['name', 'company_id', 'rate', 'company_rate', 'inverse_company_rate'] });
console.log('read back', JSON.stringify(back.result));
const cur = await kw('res.currency', 'read', [[eur.result[0].id], ['rate', 'inverse_rate', 'date', 'rate_ids']], { context: { allowed_company_ids: [1] } });
console.log('currency now', JSON.stringify(cur.result?.map(c => ({ rate: c.rate, inverse_rate: c.inverse_rate, date: c.date }))));
const ids = back.result.map(x => x.id);
console.log('unlink', JSON.stringify((await kw('res.currency.rate', 'unlink', [ids])).result));
