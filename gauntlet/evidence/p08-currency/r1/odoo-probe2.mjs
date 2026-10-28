// Read-only: EUR rates of every company, and the journal a new customer invoice defaults to.
const base = 'http://localhost:8069';
const j = async (path, params, cookie) => { const r = await fetch(base + path, { method: 'POST', headers: { 'content-type': 'application/json', ...(cookie ? { cookie } : {}) }, body: JSON.stringify({ jsonrpc: '2.0', method: 'call', params }) }); return { r, d: await r.json() }; };
const a = await j('/web/session/authenticate', { db: 'reference', login: 'admin', password: 'admin' });
const cookie = a.r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
const kw = async (model, method, args, kwargs = {}) => (await j('/web/dataset/call_kw', { model, method, args, kwargs }, cookie)).d;
const ctx = { allowed_company_ids: [1, 2, 3, 4, 5] };
const g = await kw('res.currency.rate', 'read_group', [[['currency_id.name', '=', 'EUR']], ['name:max'], ['company_id']], { context: ctx, lazy: false });
console.log('EUR rates by company', JSON.stringify(g.result ?? g.error?.data?.message));
const dflt = await kw('account.move', 'default_get', [['company_id', 'journal_id', 'date', 'invoice_date', 'currency_id']], { context: { default_move_type: 'out_invoice', allowed_company_ids: [1, 2, 3, 4, 5] } });
console.log('default invoice', JSON.stringify(dflt.result ?? dflt.error?.data?.message));
const dflt1 = await kw('account.move', 'default_get', [['company_id', 'journal_id', 'date']], { context: { default_move_type: 'out_invoice', allowed_company_ids: [1] } });
console.log('default invoice (company 1 only)', JSON.stringify(dflt1.result ?? dflt1.error?.data?.message));
