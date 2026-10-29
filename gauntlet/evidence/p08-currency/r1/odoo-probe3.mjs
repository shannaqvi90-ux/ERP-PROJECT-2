// Replays the see-converted set-up and the invoice the path makes, through RPC, then removes both (as the driver's clean-up does).
const base = 'http://localhost:8069';
const j = async (path, params, cookie) => { const r = await fetch(base + path, { method: 'POST', headers: { 'content-type': 'application/json', ...(cookie ? { cookie } : {}) }, body: JSON.stringify({ jsonrpc: '2.0', method: 'call', params }) }); return { r, d: await r.json() }; };
const a = await j('/web/session/authenticate', { db: 'reference', login: 'admin', password: 'admin' });
const cookie = a.r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
const kw = async (model, method, args, kwargs = {}) => { const d = (await j('/web/dataset/call_kw', { model, method, args, kwargs }, cookie)).d; if (d.error) console.log('ERR', model, method, d.error.data?.message?.slice(0, 200)); return d.result; };
const [eur] = await kw('res.currency', 'search', [[['name', '=', 'EUR']]], { context: { active_test: false } });
const rate = await kw('res.currency.rate', 'create', [{ currency_id: eur, company_id: 1, name: '2026-10-28', inverse_company_rate: 4.2875 }]);
const move = await kw('account.move', 'create', [{ move_type: 'out_invoice', currency_id: eur, invoice_line_ids: [[0, 0, { name: 'x', price_unit: 1250, tax_ids: [[6, 0, []]] }]] }]);
const [m] = await kw('account.move', 'read', [[move], ['company_id', 'date', 'invoice_date', 'invoice_currency_rate', 'amount_untaxed_signed', 'journal_id']]);
console.log('draft invoice', JSON.stringify(m));
const cr = await kw('res.currency', '_get_conversion_rate', [await kw('res.currency', 'search', [[['name', '=', 'AED']]]), eur, 1, '2026-10-28']).catch(() => null);
await kw('account.move', 'unlink', [[move]]);
await kw('res.currency.rate', 'unlink', [[rate]]);
