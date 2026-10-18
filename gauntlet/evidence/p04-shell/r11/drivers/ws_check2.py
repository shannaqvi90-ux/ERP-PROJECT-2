exec(open('isolation_probe_base.py').read().split("spec = json.loads")[0])
for name, r in [('B', reqB), ('A', reqA)]:
    for p in ['/api/tenancy/tenant', '/api/auth/session', '/api/tenancy/workplace']:
        s, h, b = r('GET', p); print(name, p, s, b.decode()[:500])
    s, h, b = r('GET', '/api/tenancy/companies'); print(name, 'companies', s, b.decode()[:300])
    s, h, b = r('GET', '/api/identity/roles'); import re; print(name, 'roles', re.findall(r'"name(?:En)?":"([^"]+)"', b.decode())[:12])
