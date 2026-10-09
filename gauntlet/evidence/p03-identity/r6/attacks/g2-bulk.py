# Critic p03 r6: live re-test of r5's R1 and variants on the demo (port 20350).
import sys; sys.path.insert(0,'.')
from lib import *
a = C("admin@alnoor.example"); T = tag(); print("tag", T)
st, comps = a.get("/api/identity/companies"); comps = [ (c["id"], c["code"]) for c in comps ]; print("companies", comps)
st, roles = a.get("/api/identity/roles?take=200"); roles = roles["items"]
cm = [r for r in roles if r["nameEn"] == "Company manager"]; print("Company manager role", [ (r["id"], r["permissions"]) for r in cm])
def role(name, perms):
    st, r = a.post("/api/identity/roles", {"nameEn": f"{name} {T}", "nameAr": f"دور {name} {T}", "permissions": perms}); assert st == 201, (st, r); return r["id"]
def user(local, roleIds, companyRoles=(), pw="Critic-Pass-2026x"):
    email = f"{local}.{T}@alnoor.example"
    st, r = a.post("/api/identity/users", {"email": email, "displayName": f"{local} {T}", "language": "en", "password": pw, "mustChangePassword": False, "roleIds": list(roleIds), "companyRoles": list(companyRoles)})
    assert st == 201, (st, r); return r["id"], email
def access(uid, companies):
    st, cur = a.get(f"/api/tenancy/access/{uid}")
    body = {"companies": [{"companyId": c, "allBranches": True, "branchIds": []} for c in companies]}
    if isinstance(cur, dict) and "version" in cur: body["version"] = cur["version"]
    return a.put(f"/api/tenancy/access/{uid}", body)
C1, C2 = comps[0][0], comps[1][0]
helpdesk = role("Helpdesk clerk", ["identity.users.read", "identity.users.update"])
mgr_role = cm[0]["id"]
mgr, mgr_email = user("dubai.manager", [], [{"roleId": mgr_role, "companyId": C1}]); show("manager access", access(mgr, [C1]))
clerk, clerk_email = user("clerk", [helpdesk])
c = C(clerk_email, "Critic-Pass-2026x"); print("clerk signin", c.signin[0])
m = C(mgr_email, "Critic-Pass-2026x"); print("manager signin", m.signin[0])
st, u = a.get(f"/api/identity/users/{mgr}")
show("1 CONTROL clerk PUT manager isActive=false", c.put(f"/api/identity/users/{mgr}", {"displayName": u["displayName"], "language": "en", "isActive": False, "version": u["version"]}))
show("2 ATTACK (r5 R1) clerk matching/active by search", c.post("/api/identity/users/matching/active", {"active": False, "search": mgr_email, "filter": "", "expectedCount": 1}))
show("3 ATTACK clerk matching/active by filter", c.post("/api/identity/users/matching/active", {"active": False, "search": "", "filter": f"email eq '{mgr_email}'", "expectedCount": 1}))
show("4 manager still active? (admin read)", a.get(f"/api/identity/users/{mgr}"))
show("5 manager live session", m.get("/api/auth/session"))
# Variant: a weak role held in a company the clerk does not work in
weak = role("Weak reader", ["identity.users.read"])
w2, w2_email = user("weak.elsewhere", [], [{"roleId": weak, "companyId": C2}]); access(w2, [C2])
show("6 clerk access to C1 only", access(clerk, [C1]))
c = C(clerk_email, "Critic-Pass-2026x")
show("7 ATTACK clerk (works in C1) matching/active at user with weak role in C2", c.post("/api/identity/users/matching/active", {"active": False, "search": w2_email, "filter": "", "expectedCount": 1}))
# Control: a user without roles is changed
plain, plain_email = user("plain", [])
show("8 CONTROL clerk matching/active at user without roles", c.post("/api/identity/users/matching/active", {"active": False, "search": plain_email, "filter": "", "expectedCount": 1}))
# Variant: caller holds users.update only in company C1 (company role); target holds tenancy.companies.update in C2 only
cm1 = role("Company users clerk", ["identity.users.read", "identity.users.update", "tenancy.companies.read", "tenancy.companies.update"])
cc, cc_email = user("company.clerk", [], [{"roleId": cm1, "companyId": C1}]); show("9 company clerk access C1,C2", access(cc, [C1, C2]))
tgt_role = role("Companies editor", ["identity.users.read", "tenancy.companies.read", "tenancy.companies.update"])
tg, tg_email = user("c2.editor", [], [{"roleId": tgt_role, "companyId": C2}]); access(tg, [C1, C2])
k = C(cc_email, "Critic-Pass-2026x"); print("company clerk signin", k.signin[0])
show("10 company clerk session permissions", k.get("/api/auth/session"))
show("11 company clerk workplace", k.get("/api/tenancy/workplace"))
show("12 ATTACK company clerk (perms in C1) matching/active at C2 editor", k.post("/api/identity/users/matching/active", {"active": False, "search": tg_email, "filter": "", "expectedCount": 1}))
st, u = a.get(f"/api/identity/users/{tg}")
show("13 CONTROL company clerk PUT C2 editor", k.put(f"/api/identity/users/{tg}", {"displayName": u["displayName"], "language": "en", "isActive": False, "version": u["version"]}))
show("14 C2 editor state", a.get(f"/api/identity/users/{tg}"))
# Variant: target same role in C1 (covered there) -> allowed
tg1, tg1_email = user("c1.editor", [], [{"roleId": tgt_role, "companyId": C1}]); access(tg1, [C1])
show("15 company clerk matching/active at C1 editor (covered in C1: allowed)", k.post("/api/identity/users/matching/active", {"active": False, "search": tg1_email, "filter": "", "expectedCount": 1}))
print("IDS", json.dumps({"mgr": mgr, "clerk": clerk, "w2": w2, "plain": plain, "cc": cc, "tg": tg, "tg1": tg1, "T": T}))
