# Critic p04 r10: tenant A (alnoor) attacks tenant B (gulfsteel) through every documented route on the demo.
import json, re, urllib.request, http.cookiejar, sys, uuid
B = 'http://localhost:20470'
def client(email):
    cj = http.cookiejar.CookieJar(); op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    def req(method, path, body=None, headers=None):
        h = {'X-Erp-Request': '1', 'Accept': 'application/json'}; h.update(headers or {})
        data = None
        if body is not None: data = json.dumps(body).encode(); h['Content-Type'] = 'application/json'
        r = urllib.request.Request(B + path, data=data, method=method, headers=h)
        try:
            resp = op.open(r, timeout=60); return resp.status, dict(resp.headers), resp.read()
        except urllib.error.HTTPError as e:
            return e.code, dict(e.headers), e.read()
    s, h, b = req('POST', '/api/auth/sign-in', {'email': email, 'password': 'Demo-Pass-2026'})
    assert s == 200, (s, b[:300])
    return req, json.loads(b)
reqA, sesA = client('admin@alnoor.example')
reqB, sesB = client('admin@gulfsteel.example')
spec = json.loads(reqA('GET', '/api/openapi/v1.json')[2])
# B's ids and markers, from B's own answers
idsB = {sesB['user']['id'], sesB['tenant']['id']}
markers = {sesB['user']['email'], sesB['tenant']['code'], sesB['tenant']['nameEn'], sesB['tenant']['nameAr'], sesB['user']['displayName'], sesB['user']['id'], sesB['tenant']['id']}
for p in ['/api/tenancy/companies', '/api/tenancy/branches', '/api/identity/roles', '/api/identity/users?take=20']:
    s, h, b = reqB('GET', p)
    if s == 200:
        text = b.decode()
        for m in re.findall(r'"id":"([0-9a-f-]{36})"', text): idsB.add(m)
        for m in re.findall(r'"(?:code|nameEn|email)":"([^"]{4,})"', text): markers.add(m)
ownA = set()
for p in ['/api/auth/session', '/api/tenancy/companies', '/api/tenancy/branches', '/api/identity/roles', '/api/identity/users?take=200']:
    s, h, b = reqA('GET', p)
    ownA.add(b.decode().lower())
ownText = '\n'.join(ownA)
# Only what is B's own: a value A's own answers also hold (seeded role names, a shared display name) is no marker.
markers = {m for m in markers if m and len(m) >= 4 and m.lower() not in ownText}
print('markers', sorted(markers))
markersLower = [m.lower() for m in markers]
print('B ids', len(idsB), 'B markers', len(markers))
results = []; leaks = []
def check(label, s, h, b):
    text = (b.decode(errors='replace') + json.dumps(h)).lower()
    hit = [m for m in markersLower if m in text]
    results.append((label, s, len(b)))
    if hit and s < 400: leaks.append((label, s, hit[:3]))
gets = []
for path, ops in spec['paths'].items():
    for method in ops:
        if method.upper() != 'GET': continue
        if '{' in path:
            for i in list(idsB)[:12] + ['gulfsteel', 'GSI']:
                gets.append(re.sub(r'\{[^}]+\}', i, path))
        else:
            gets.append(path)
            for q in ['tenantId=' + sesB['tenant']['id'], 'tenant=gulfsteel', 'q=gulfsteel', 'search=gulfsteel', 'companyId=' + next(iter(idsB))]:
                gets.append(path + '?' + q)
for g in gets:
    for hdr in [{}, {'X-Tenant-Id': sesB['tenant']['id'], 'X-Tenant': 'gulfsteel'}]:
        s, h, b = reqA('GET', g, headers=hdr)
        check('GET ' + g + (' +tenant-headers' if hdr else ''), s, h, b)
# The shell's own write: preferences with B's ids in body, query and headers, every combination
for lang in ['en', 'ar']:
    for num in ['latn', 'arab']:
        body = {'language': lang, 'numerals': num, 'userId': sesB['user']['id'], 'tenantId': sesB['tenant']['id'], 'user': {'id': sesB['user']['id']}}
        s, h, b = reqA('PUT', '/api/identity/me/preferences?userId=' + sesB['user']['id'] + '&tenantId=' + sesB['tenant']['id'], body, {'X-Tenant-Id': sesB['tenant']['id']})
        check(f'PUT preferences {lang}/{num} with B ids', s, h, b)
s, h, b = reqB('GET', '/api/auth/session'); after = json.loads(b)['user']
print('B preferences after A attack:', after['language'], after['numerals'])
# Destructive tries on B's ids
for i in list(idsB)[:12]:
    for p in ['/api/identity/users/', '/api/identity/roles/', '/api/tenancy/companies/', '/api/tenancy/branches/']:
        s, h, b = reqA('DELETE', p + i); check('DELETE ' + p + i, s, h, b)
        if s < 300: leaks.append(('DELETE succeeded ' + p + i, s, []))
reqA('PUT', '/api/identity/me/preferences', {'language': 'en', 'numerals': 'latn'})
print('requests', len(results), 'status counts', {k: sum(1 for r in results if r[1] == k) for k in sorted({r[1] for r in results})})
print('LEAKS', json.dumps(leaks, ensure_ascii=False, indent=1))
