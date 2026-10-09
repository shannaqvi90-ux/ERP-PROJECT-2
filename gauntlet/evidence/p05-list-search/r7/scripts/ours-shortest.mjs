// Shortest name-derived text typed in our Users search that puts the target among the first VISIBLE rows (or first).
const base = process.argv[2], VISIBLE = Number(process.argv[3] || 20);
const login = 'majid.pillai.068311@staff.example', words = ['majid', 'anil', 'pillai'];
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) });
const { token } = await r.json();
const subs = w => { const s = new Set(); for (let i = 0; i < w.length; i++) for (let l = 1; i + l <= w.length; l++) s.add(w.slice(i, i + l)); return [...s]; };
const cands = new Set();
const orders = [[0], [1], [2], [0, 1], [1, 2], [0, 2], [0, 1, 2], [1, 0], [2, 1], [2, 0]];
for (const o of orders) {
  let combos = [''];
  for (const wi of o) combos = combos.flatMap(c => subs(words[wi]).map(s => (c ? c + ' ' : '') + s)).filter(c => c.length <= 6);
  combos.forEach(c => cands.add(c));
}
const list = [...cands].sort((a, b) => a.length - b.length || a.localeCompare(b));
let found = [];
for (const s of list) {
  if (found.length && s.length > found[0][0] + 1) break;
  const res = await fetch(`${base}/api/identity/users?take=${VISIBLE}&search=${encodeURIComponent(s)}`, { headers: { Authorization: 'Bearer ' + token, 'X-Erp-Request': '1' } });
  const page = await res.json();
  const rank = page.items.findIndex(u => u.email.toLowerCase() === login) + 1;
  if (rank > 0) { found.push([s.length, s, rank, page.total]); console.log(s.length, JSON.stringify(s), 'rank', rank, 'of', page.total, 'ranked', page.ranked); }
}
