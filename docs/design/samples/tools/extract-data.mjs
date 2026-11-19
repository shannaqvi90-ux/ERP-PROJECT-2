// Builds data.json (a small demo subset) from the comparison contacts file.
// Usage: node extract-data.mjs [path-to-contacts.csv]
import fs from 'node:fs';
const src = process.argv[2] || '/home/shan/ERP-PROJECT-2/gauntlet/compare/data/out/contacts.csv';
const lines = fs.readFileSync(src, 'utf8').split('\n').filter(Boolean);
const head = lines[0].split(',');
const rows = lines.slice(1, 37).map((l) => {
  const v = l.split(',');
  const o = {};
  head.forEach((h, i) => (o[h] = v[i] || ''));
  return o;
});
const cur = { AE: 'AED', SA: 'SAR', OM: 'OMR', QA: 'QAR', KW: 'KWD', BH: 'BHD' };
const over = {
  C000020: { city: 'Doha', country_code: 'QA' },
  C000021: { city: 'Kuwait City', country_code: 'KW' },
  C000029: { city: 'Manama', country_code: 'BH' },
};
const out = rows.map((r, idx) => {
  Object.assign(r, over[r.ref] || {});
  const n = idx + 1;
  const cents = ((n * 7919 * 131) % 4800000) + 12500;
  const sign = n % 7 === 2 ? -1 : 1;
  return {
    ref: r.ref, kind: r.kind, name: r.name, nameAr: r.name_ar, email: r.email,
    phone: r.phone || r.mobile, city: r.city, area: r.area, street: r.street, cc: r.country_code,
    trn: r.trn, licence: r.trade_licence, tags: r.tags ? r.tags.split(';') : [],
    currency: cur[r.country_code] || 'USD',
    creditLimit: r.credit_limit ? Number(r.credit_limit) : 0,
    balanceCents: n % 11 === 0 ? 0 : sign * cents,
    status: n % 9 === 5 ? 'hold' : n % 13 === 4 ? 'blocked' : 'active',
    parent: r.parent_ref,
  };
});
fs.writeFileSync(new URL('./data.json', import.meta.url), JSON.stringify(out, null, 1));
console.log(out.length, 'rows');
