export const B = process.env.BASE || 'http://localhost:20950';
const pw = 'Demo-Pass-2026';
export async function signIn(email) {
  for (;;) {
    const r = await fetch(B + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: pw, issueToken: true }) });
    if (r.status === 429) { await new Promise(s => setTimeout(s, 5000)); continue; }
    const j = await r.json(); if (!j.token) throw new Error('sign-in ' + email + ' ' + r.status + JSON.stringify(j)); return j.token;
  }
}
export async function call(tok, method, path, body, extra = {}, raw) {
  const headers = { Authorization: 'Bearer ' + tok, 'X-Erp-Request': '1', ...extra };
  if (!raw) headers['content-type'] = 'application/json';
  const r = await fetch(B + path, { method, headers, body: raw ?? (body ? JSON.stringify(body) : undefined) });
  const buf = Buffer.from(await r.arrayBuffer()); const t = buf.toString('utf8'); let j; try { j = JSON.parse(t); } catch { j = t; }
  return { s: r.status, j, t, buf, h: r.headers };
}
export const sleep = ms => new Promise(s => setTimeout(s, ms));
