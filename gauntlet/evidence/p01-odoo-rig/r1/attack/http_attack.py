# Tenant A (alnoor admin) attacks tenant B (gulfsteel) through every documented route.
import json, urllib.request, http.cookiejar, sys
BASE = "http://localhost:20150"
B_TENANT = "0190a000-0000-7000-8000-000000000002"
B_USERS = "018cc5fc-5e60-7221-bba3-7b3ee26c1908 018cd936-3e60-7c72-a148-c5cce2bfd975 018cd247-0940-7472-aaab-f45b2a2e6c65".split()
B_ROLES = "01a0ff18-6c99-7086-9ab2-b937237e82bf 01a0ff18-6c99-74a0-93ae-6e06ab63027f".split()
B_MARKERS = ["gulfsteel", B_TENANT] + B_USERS + B_ROLES
cj = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
def call(method, path, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("Content-Type", "application/json"); req.add_header("X-Erp-Request", "1")
    for c in cj:
        if "csrf" in c.name.lower() or "xsrf" in c.name.lower(): req.add_header("X-CSRF-Token", c.value); req.add_header("X-XSRF-TOKEN", c.value)
    for k, v in (headers or {}).items(): req.add_header(k, v)
    try:
        r = op.open(req); return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()
st, body = call("POST", "/api/auth/sign-in", {"email": "admin@alnoor.example", "password": "Demo-Pass-2026"})
print("sign-in", st, body[:200])
hdrs = [{}, {"X-Tenant-Id": B_TENANT}, {"X-Tenant": "gulfsteel"}, {"Tenant": B_TENANT}]
qs = ["", f"?tenantId={B_TENANT}", "?tenant=gulfsteel", "?search=gulfsteel", "?search=abdullah.cruz.587@gulfsteel.example"]
reqs = leaks = 0
paths = ["/api/tenancy/tenant", "/api/identity/users", "/api/identity/roles", "/api/identity/permissions", "/api/auth/session"] + \
        [f"/api/identity/users/{u}" for u in B_USERS] + [f"/api/identity/roles/{r}" for r in B_ROLES]
for p in paths:
    for q in qs:
        for h in hdrs:
            st, body = call("GET", p + q, headers=h); reqs += 1
            hit = [m for m in B_MARKERS if m.lower() in body.lower()]
            if st < 300 and hit: leaks += 1; print("LEAK", st, p + q, h, hit)
for u in B_USERS:
    st, body = call("PUT", f"/api/identity/users/{u}", {"displayName": "pwned", "language": "en", "isActive": False, "roleIds": [], "version": 1}); reqs += 1
    print("PUT user B", st, body[:120])
for r in B_ROLES:
    st, body = call("PUT", f"/api/identity/roles/{r}", {"nameEn": "pwned", "nameAr": "x", "permissions": [], "version": 1}); reqs += 1
    print("PUT role B", st, body[:120])
    st, body = call("DELETE", f"/api/identity/roles/{r}"); reqs += 1
    print("DELETE role B", st, body[:120])
st, body = call("POST", "/api/identity/users", {"email": "x1@alnoor.example", "displayName": "x", "password": "Demo-Pass-2026x", "language": "en", "roleIds": B_ROLES}); reqs += 1
print("POST user with B roles", st, body[:160])
st, body = call("PUT", "/api/tenancy/tenant", {"nameEn": "x", "nameAr": "x", "version": 1}, {"X-Tenant-Id": B_TENANT}); reqs += 1
print("PUT tenant with B header", st, body[:160])
print(f"requests={reqs} leaks={leaks}")
