export const BASE = process.env.BASE || 'http://localhost:20250';
export async function signIn(email, password = 'Demo-Pass-2026', workspace = null) {
  for (let i = 0; i < 10; i++) {
    const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' },
      body: JSON.stringify({ email, password, workspace, issueToken: true }) });
    if (r.status === 429) { await new Promise(r => setTimeout(r, 5000)); continue; }
    const j = await r.json();
    if (!r.ok) throw new Error('sign-in ' + email + ' ' + r.status + ' ' + JSON.stringify(j));
    return client(j.token, j);
  }
  throw new Error('rate limited');
}
export function client(token, session) {
  const call = async (method, path, body, extraHeaders = {}) => {
    const r = await fetch(BASE + path, { method, headers: { authorization: 'Bearer ' + token, 'x-erp-request': '1', ...(body !== undefined ? { 'content-type': 'application/json' } : {}), ...extraHeaders },
      body: body !== undefined ? JSON.stringify(body) : undefined });
    const text = await r.text();
    let json; try { json = JSON.parse(text); } catch { json = null; }
    return { status: r.status, text, json, headers: r.headers };
  };
  return { token, session, call, get: (p, h) => call('GET', p, undefined, h), put: (p, b, h) => call('PUT', p, b, h), post: (p, b, h) => call('POST', p, b, h), del: (p, h) => call('DELETE', p, undefined, h) };
}
