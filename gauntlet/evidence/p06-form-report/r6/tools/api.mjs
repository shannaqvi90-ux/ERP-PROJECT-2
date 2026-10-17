// Minimal API client for the critic's probes (bearer token from /api/auth/sign-in).
export async function signIn(base, email, password = 'Demo-Pass-2026') {
  const res = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password }) });
  const text = await res.text();
  if (!res.ok) throw new Error(`sign-in ${email}: ${res.status} ${text.slice(0, 200)}`);
  const body = JSON.parse(text);
  const token = body.token || body.accessToken || body.bearer;
  const cookie = res.headers.getSetCookie?.().map(c => c.split(';')[0]).join('; ');
  const h = { 'X-Erp-Request': '1', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(cookie ? { Cookie: cookie } : {}) };
  const call = async (method, path, json, extra = {}) => {
    const r = await fetch(base + path, { method, headers: { ...h, ...extra, ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}) }, body: json === undefined ? undefined : JSON.stringify(json) });
    const buf = Buffer.from(await r.arrayBuffer());
    return { status: r.status, headers: r.headers, buf, text: buf.toString('utf8'), json() { return JSON.parse(this.text); } };
  };
  return { body, get: (p, extra) => call('GET', p, undefined, extra), post: (p, j) => call('POST', p, j), put: (p, j) => call('PUT', p, j), del: p => call('DELETE', p) };
}
