# Critic p04 r13 (from r12 critic) (adapted from r11 critic evidence): tenant A (alnoor) attacks tenant B (gulfsteel) on the demo: every documented GET with B's ids,
# tenant headers and query fields; every documented write (PUT/POST/DELETE) with B's ids in route and body;
# list exports (pdf/csv/xlsx); then B's own view of its data must be unchanged.
import json, re, sys, hashlib
exec(open('isolation_probe_base.py').read().split("# The shell's own write")[0])
def snapshot():
    out = {}
    for p in ['/api/auth/session', '/api/tenancy/companies', '/api/tenancy/branches', '/api/identity/roles', '/api/identity/users?take=200', '/api/tenancy/workspace', '/api/tenancy/workplace']:
        s, h, b = reqB('GET', p)
        t = re.sub(r'"(lastSignInAt|expiresAt|validUntil|serverTime)":"[^"]*"', '', b.decode(errors='replace'))
        out[p] = (s, hashlib.sha256(t.encode()).hexdigest())
    return out
before = snapshot()
writes = 0
bodies = [{}, {'id': None, 'tenantId': sesB['tenant']['id'], 'language': 'ar', 'numerals': 'arab', 'name': 'PWNED', 'nameEn': 'PWNED', 'displayName': 'PWNED', 'code': 'PWN', 'isActive': False, 'companyId': None, 'userId': sesB['user']['id']}]
for path, ops in spec['paths'].items():
    for method in ops:
        M = method.upper()
        if M not in ('PUT', 'POST', 'DELETE', 'PATCH'): continue
        if path.startswith('/api/auth/sign') or 'sign-out' in path or 'password' in path and 'me' in path: continue
        targets = [re.sub(r'\{[^}]+\}', i, path) for i in list(idsB)[:10]] if '{' in path else [path + '?tenantId=' + sesB['tenant']['id']]
        for t in targets:
            for bd in bodies:
                bd = dict(bd); 
                if 'id' in bd: bd['id'] = t.rstrip('/').split('/')[-1] if '{' in path else sesB['user']['id']
                s, h, b = reqA(M, t, None if M == 'DELETE' else bd, {'X-Tenant-Id': sesB['tenant']['id']})
                writes += 1
                check(f'{M} {t}', s, h, b)
                if s < 300 and '{' in path: leaks.append((f'{M} on B id succeeded {t}', s, []))
# exports
for key in ['identity.users', 'identity.roles', 'tenancy.companies', 'tenancy.branches', 'tenancy.access']:
    for fmt in ['pdf', 'csv', 'xlsx']:
        for q in ['', '&tenantId=' + sesB['tenant']['id'], '&tenant=gulfsteel', '&q=gulfsteel']:
            s, h, b = reqA('GET', f'/api/reports/lists/{key}?format={fmt}&language=en{q}', headers={'X-Tenant-Id': sesB['tenant']['id']})
            check(f'EXPORT {key} {fmt}{q}', s, h, b)
after = snapshot()
changed = [p for p in before if before[p] != after[p]]
reqA('PUT', '/api/identity/me/preferences', {'language': 'en', 'numerals': 'latn'})
print('requests', len(results), 'writes', writes, 'status counts', {k: sum(1 for r in results if r[1] == k) for k in sorted({r[1] for r in results})})
print('B views changed by A:', changed)
print('LEAKS', json.dumps(leaks, ensure_ascii=False, indent=1))
