# Critic p01 r14: tenant A (alnoor admin) against tenant B (gulfsteel) over every documented operation,
# with B's ids in paths, query strings, bodies (company, role, branch ids), export formats and tenant headers.
import json, urllib.request, re
BASE = 'http://localhost:20150'
ERR = []
def req(method, path, body=None, token=None, headers=None):
    h = {'Content-Type': 'application/json', 'X-Erp-Request': '1', **(headers or {})}
    if token: h['Authorization'] = 'Bearer ' + token
    r = urllib.request.Request(BASE + path, method=method, data=None if body is None else json.dumps(body).encode(), headers=h)
    try:
        with urllib.request.urlopen(r, timeout=120) as resp: return resp.status, resp.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e: return e.code, e.read().decode('utf-8', 'replace')
    except Exception as e: ERR.append(f'{method} {path}: {e}'); return 599, ''
def sign_in(email):
    s, b = req('POST', '/api/auth/sign-in', {'email': email, 'password': 'Demo-Pass-2026', 'issueToken': True})
    return json.loads(b)['token']
B = sign_in('admin@gulfsteel.example'); A = sign_in('admin@alnoor.example')
ids = {}
for k, p in [('users', '/api/identity/users?search=gulfsteel'), ('roles', '/api/identity/roles'), ('companies', '/api/tenancy/companies'), ('branches', '/api/tenancy/branches')]:
    s, b = req('GET', p, token=B); d = json.loads(b); items = d['items'] if isinstance(d, dict) and 'items' in d else d
    ids[k] = [i['id'] for i in items][:3]
views = []
for lst in ['tenancy.companies', 'tenancy.branches', 'identity.users', 'identity.roles']:
    s, b = req('POST', f'/api/lists/{lst}/views', {'name': 'GSR14CANARY view', 'search': 'GSR14CANARY', 'columns': []}, token=B)
    try: views.append((lst, json.loads(b)['id']))
    except Exception: pass
s, tb = req('GET', '/api/tenancy/tenant', token=B); btenant = json.loads(tb) if s == 200 else {}
markers = ['gulfsteel', 'gsr14canary', 'gulf steel', 'gscanary']
ops = json.loads(urllib.request.urlopen(BASE + '/api/openapi/v1.json').read())['paths']
allids = sum(ids.values(), []) + [v for _, v in views] + [btenant.get('id', '')]
allids = [i for i in allids if i]
n = leaks = writes = 0; findings = []; ops_n = 0
def check(m, p, s, b):
    global n, leaks, writes
    n += 1
    hit = [x for x in markers if x in b.lower()]
    if hit and s < 400: leaks += 1; findings.append(f'LEAK {m} {p} {s} {hit}')
    if m in ('PUT', 'POST', 'DELETE', 'PATCH') and s < 300 and any(i in p for i in allids): writes += 1; findings.append(f'WRITE {m} {p} {s}')
c0 = ids['companies'][0]; r0 = ids['roles'][0] if ids['roles'] else ''; br0 = ids['branches'][0] if ids['branches'] else ''
for path, item in ops.items():
    for m in [x.upper() for x in item if x in ('get', 'put', 'delete', 'post', 'patch')]:
        ops_n += 1
        if path in ('/api/auth/sign-in', '/api/auth/sign-out'): continue
        if '{' in path:
            for i in allids:
                p = re.sub(r'\{[^}]+\}', i, path)
                body = {'name': 'x', 'displayName': 'x', 'version': 1, 'companyId': c0} if m in ('PUT', 'POST', 'PATCH') else None
                s, b = req(m, p, body, token=A); check(m, p, s, b)
        elif m == 'GET':
            for q in ['', f'?company={c0}', f'?companyId={c0}&format=csv', '?search=gulfsteel', f'?tenantId={btenant.get("id","")}', f'?company={c0}&format=xlsx', f'?company={c0}&format=pdf', f"?filter=companyId%20eq%20'{c0}'"]:
                s, b = req('GET', path + q, token=A); check('GET', path + q, s, b)
            for h in [{'X-Tenant-Id': btenant.get('id', '')}, {'X-Erp-Tenant': 'gulfsteel'}, {'X-Company-Id': c0}, {'Host': 'gulfsteel.localhost'}]:
                s, b = req('GET', path, token=A, headers=h); check('GET', path + ' ' + json.dumps(h), s, b)
        else:
            # A body that names B's company, role and branch.
            body = {'companyId': c0, 'branchId': br0, 'roleIds': [r0], 'email': 'r14attack@alnoor.example', 'displayName': 'r14 attack', 'language': 'en', 'name': 'r14', 'code': 'R14X', 'nameEn': 'r14', 'nameAr': 'r14', 'legalNameEn': 'r14', 'legalNameAr': 'r14', 'baseCurrency': 'AED', 'country': 'AE', 'fiscalYearStartMonth': 1, 'fiscalYearStartDay': 1, 'isActive': True}
            s, b = req(m, path, body, token=A)
            check(m, path + ' (body with B ids)', s, b)
            if s < 300: findings.append(f'NOTE {m} {path} accepted a body naming B ids: {s} {b[:160]}')
# Did B's views and companies survive?
s, b = req('GET', '/api/lists/identity.users/views', token=B)
print(json.dumps({'openapi_operations': ops_n, 'requests': n, 'leaks': leaks, 'writes_on_b_ids_accepted': writes, 'b_ids': ids, 'b_views': views, 'b_views_intact': b.lower().count('gsr14canary')}, indent=1))
for f in findings[:80]: print(f)
print('timeouts/errors:', len(ERR)); [print('  ', t) for t in ERR[:20]]
