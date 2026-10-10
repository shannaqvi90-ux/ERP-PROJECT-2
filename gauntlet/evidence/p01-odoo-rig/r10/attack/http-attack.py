# Critic p01 r10 (adapted from the r9 critic tool): tenant A (alnoor admin) against tenant B (gulfsteel) over every documented operation.
import json, urllib.request, re, sys
BASE = 'http://localhost:20150'
def req(method, path, body=None, token=None, headers=None):
    h = {'Content-Type': 'application/json', 'X-Erp-Request': '1', **(headers or {})}
    if token: h['Authorization'] = 'Bearer ' + token
    r = urllib.request.Request(BASE + path, method=method, data=None if body is None else json.dumps(body).encode(), headers=h)
    try:
        with urllib.request.urlopen(r, timeout=30) as resp: return resp.status, resp.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e: return e.code, e.read().decode('utf-8', 'replace')
def sign_in(email):
    s, b = req('POST', '/api/auth/sign-in', {'email': email, 'password': 'Demo-Pass-2026', 'issueToken': True})
    return json.loads(b)['token']
B = sign_in('admin@gulfsteel.example'); A = sign_in('admin@alnoor.example')
ids = {}
for k, p in [('users', '/api/identity/users?search=gulfsteel'), ('roles', '/api/identity/roles'), ('companies', '/api/tenancy/companies'), ('branches', '/api/tenancy/branches')]:
    s, b = req('GET', p, token=B); d = json.loads(b); items = d['items'] if isinstance(d, dict) and 'items' in d else d
    ids[k] = [i['id'] for i in items][:3]
# B creates a private view as a canary on each list
views = []
for lst in ['tenancy.companies', 'tenancy.branches', 'tenancy.access', 'identity.users', 'identity.roles']:
    s, b = req('POST', f'/api/lists/{lst}/views', {'name': 'GSCANARY view', 'state': {}}, token=B)
    try: views.append((lst, json.loads(b)['id']))
    except Exception: pass
s, tb = req('GET', '/api/tenancy/tenant', token=B); btenant = json.loads(tb)
markers = ['gulfsteel', 'GSCANARY', 'Gulf Steel'] 
ops = json.loads(urllib.request.urlopen(BASE + '/api/openapi/v1.json').read())['paths']
allids = sum(ids.values(), []) + [v for _, v in views] + [btenant.get('id', '')]
n = leaks = writes = 0; findings = []
def check(m, p, s, b):
    global n, leaks, writes
    n += 1
    low = b.lower()
    hit = [x for x in markers if x.lower() in low]
    if hit and s < 400: leaks += 1; findings.append(f'LEAK {m} {p} {s} {hit}')
    if m in ('PUT', 'POST', 'DELETE') and s < 300 and any(i and i in p for i in allids): writes += 1; findings.append(f'WRITE {m} {p} {s}')
for path, item in ops.items():
    for m in [x.upper() for x in item if x in ('get', 'put', 'delete', 'post')]:
        if path in ('/api/auth/sign-in', '/api/auth/sign-out'): continue
        if '{' in path:
            for i in allids:
                p = re.sub(r'\{[^}]+\}', i, path)
                if m in ('PUT', 'POST'): s, b = req(m, p, {'name': 'x', 'displayName': 'x', 'version': 1}, token=A)
                elif m == 'DELETE': s, b = req(m, p, token=A)
                else: s, b = req(m, p, token=A)
                check(m, p, s, b)
        elif m == 'GET':
            for q in ['', f'?company={ids["companies"][0]}', f'?companyId={ids["companies"][0]}&format=csv', f'?search=gulfsteel', f'?tenantId={btenant.get("id")}', f'?company={ids["companies"][0]}&format=xlsx', f'?company={ids["companies"][0]}&format=pdf']:
                s, b = req('GET', path + q, token=A); check('GET', path + q, s, b)
            for h in [{'X-Tenant-Id': btenant.get('id', '')}, {'X-Erp-Tenant': 'gulfsteel'}, {'X-Company-Id': ids['companies'][0]}]:
                s, b = req('GET', path, token=A, headers=h); check('GET', path + ' ' + json.dumps(h), s, b)
print(json.dumps({'requests': n, 'leaks': leaks, 'writes_on_b_ids_accepted': writes, 'b_ids': ids, 'b_views': views}, indent=1))
for f in findings[:50]: print(f)
