# Critic p03 r6: tenant A (alnoor admin) against tenant B (gulfsteel) on the identity surface.
import sys, re; sys.path.insert(0,'.')
from lib import *
b = C("admin@gulfsteel.example"); print("B signin", b.signin[0])
a = C("admin@alnoor.example"); print("A signin", a.signin[0])
st, bs = b.get("/api/auth/session"); bid = bs["user"]["id"]
st, bu = b.get("/api/identity/users?take=50"); busers = bu["items"]; print("B users", len(busers))
st, br = b.get("/api/identity/roles?take=50"); broles = br["items"]
st, bc = b.get("/api/identity/companies"); bcomp = [c["id"] for c in bc]
st, ar = a.get("/api/identity/roles?take=200"); arole = [r for r in ar["items"] if not r["isSystem"]][0]["id"]
markers = [u["email"] for u in busers] + [r["nameEn"] for r in broles] + bcomp + [u["id"] for u in busers]
leaks = []
def check(label, r):
    st, body = r; t = json.dumps(body, ensure_ascii=False) if not isinstance(body, str) else body
    hit = [m for m in markers if m in t and m not in label]
    print(f"{label}: [{st}] {t[:160]}" + (f"  LEAK? {hit[:3]}" if hit else "")); 
    if hit: leaks.append(label)
for u in busers[:3]:
    i = u["id"]
    for p in [f"/api/identity/users/{i}", f"/api/identity/users/{i}/access", f"/api/identity/users/{i}/sign-ins", f"/api/identity/users/{i}/default-company", f"/api/tenancy/access/{i}"]:
        check(f"GET {p}", a.get(p))
    check(f"PUT users/{i}", a.put(f"/api/identity/users/{i}", {"displayName": "pwned", "language": "en", "isActive": False, "version": 1}))
    check(f"POST users/{i}/password", a.post(f"/api/identity/users/{i}/password", {}))
    check(f"POST users/{i}/sessions/revoke", a.post(f"/api/identity/users/{i}/sessions/revoke"))
    check(f"POST users/{i}/unblock", a.post(f"/api/identity/users/{i}/unblock"))
    check(f"PUT users/{i}/default-company", a.put(f"/api/identity/users/{i}/default-company", {"companyId": None, "version": 1}))
    check(f"DELETE users/{i}", a.delete(f"/api/identity/users/{i}"))
for r in broles[:3]:
    i = r["id"]
    check(f"GET roles/{i}", a.get(f"/api/identity/roles/{i}"))
    check(f"POST roles/{i}/copy", a.post(f"/api/identity/roles/{i}/copy", {"nameEn": "x"+tag(), "nameAr": "س"+tag()}))
    check(f"PUT roles/{i}", a.put(f"/api/identity/roles/{i}", {"nameEn": "pwned", "nameAr": "س", "permissions": [], "version": 1}))
    check(f"DELETE roles/{i}", a.delete(f"/api/identity/roles/{i}"))
# Set-based endpoint with B's e-mails
for u in busers[:3]:
    check(f"matching/active search B email", a.post("/api/identity/users/matching/active", {"active": False, "search": u["email"], "filter": "", "expectedCount": 0}))
    check(f"list search B email", a.get(f"/api/identity/users?search={urllib.parse.quote(u['email'])}"))
import urllib.parse
check("matching/active empty selection expectedCount 0 probe", a.post("/api/identity/users/matching/active", {"active": True, "search": "gulfsteel", "filter": "", "expectedCount": 0}))
# Differential: B's role/company ids vs random in bodies (nested arrays included)
def diff(label, make):
    outs = []
    for v in [("B", None), ("random", str(uuid.uuid4()))]:
        st, body = make(v)
        t = re.sub(r'"traceId":"[^"]*"', '', json.dumps(body, separators=(",", ":")) if not isinstance(body, str) else body)
        t = re.sub(r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', 'ID', t)
        t = re.sub(r'"email":"[^"]*"|"displayName":"[^"]*"|"createdAt":"[^"]*"|"version":\d+', '', t)
        outs.append(f"{st} {t[:200]}")
    same = outs[0] == outs[1]; print(f"DIFF {label}: {'same' if same else 'DIFFERENT'}\n   B: {outs[0]}\n   random: {outs[1]}")
    if not same: leaks.append("diff " + label)
diff("POST users companyRoles[].companyId", lambda v: a.post("/api/identity/users", {"email": f"d.{tag()}@alnoor.example", "displayName": "d", "language": "en", "roleIds": [], "companyRoles": [{"roleId": arole, "companyId": v[1] or bcomp[0]}]}))
diff("POST users companyRoles[].roleId", lambda v: a.post("/api/identity/users", {"email": f"d.{tag()}@alnoor.example", "displayName": "d", "language": "en", "roleIds": [], "companyRoles": [{"roleId": v[1] or broles[0]["id"], "companyId": bcomp[0]}]}))
diff("POST users roleIds[]", lambda v: a.post("/api/identity/users", {"email": f"d.{tag()}@alnoor.example", "displayName": "d", "language": "en", "roleIds": [v[1] or broles[0]["id"]]}))
diff("PUT tenancy access companies[].companyId", lambda v: a.put(f"/api/tenancy/access/{bs['user']['id']}", {"companies": [{"companyId": v[1] or bcomp[0], "allBranches": True, "branchIds": []}]}))
diff("users filter by role id", lambda v: a.get(f"/api/identity/users?filter=" + urllib.parse.quote(f"roles eq '{v[1] or broles[0]['id']}'")))
st, aemails = None, None
diff("POST users with B's email", lambda v: a.post("/api/identity/users", {"email": (v[1][:8] + "@gulfsteel.example") if v[1] else busers[1]["email"], "displayName": "d", "language": "en", "roleIds": []}))
diff("POST roles with B's role name", lambda v: a.post("/api/identity/roles", {"nameEn": (v[1][:10]) if v[1] else broles[-1]["nameEn"], "nameAr": "س"+tag(), "permissions": []}))
# B unchanged?
st, bu2 = b.get("/api/identity/users?take=50"); st, br2 = b.get("/api/identity/roles?take=50")
print("B users unchanged:", [ (u["id"], u.get("isActive"), u.get("displayName")) for u in bu2["items"]] == [ (u["id"], u.get("isActive"), u.get("displayName")) for u in busers])
print("B roles unchanged:", [ (r["id"], r["nameEn"]) for r in br2["items"]] == [ (r["id"], r["nameEn"]) for r in broles])
print("B admin session still valid:", b.get("/api/auth/session")[0])
print("LEAKS/DIFFERENCES:", leaks)
