# Critic p03 r7 (from r6 critic script): escalation probes (self-assignment, company roles on self, access changes on stronger users).
import sys; sys.path.insert(0,'.')
from lib import *
a = C("admin@alnoor.example"); T = tag(); print("tag", T)
st, comps = a.get("/api/identity/companies"); C1, C2 = comps[0]["id"], comps[1]["id"]
st, roles = a.get("/api/identity/roles?take=200"); roles = roles["items"]
cm = [r for r in roles if r["nameEn"] == "Company manager"][0]["id"]
adminrole = [r for r in roles if r["isSystem"]][0]["id"]
st, sess = a.get("/api/auth/session"); admin_id = sess["user"]["id"]
def role(name, perms):
    st, r = a.post("/api/identity/roles", {"nameEn": f"{name} {T}", "nameAr": f"دور {name} {T}", "permissions": perms}); assert st == 201, (st, r); return r["id"]
def user(local, roleIds, companyRoles=(), pw="Critic-Pass-2026x"):
    email = f"{local}.{T}@alnoor.example"
    st, r = a.post("/api/identity/users", {"email": email, "displayName": f"{local} {T}", "language": "en", "password": pw, "mustChangePassword": False, "roleIds": list(roleIds), "companyRoles": list(companyRoles)})
    assert st == 201, (st, r); return r["id"], email
def access(cl, uid, companies):
    st, cur = cl.get(f"/api/tenancy/access/{uid}")
    body = {"companies": [{"companyId": c, "allBranches": True, "branchIds": []} for c in companies]}
    if isinstance(cur, dict) and "version" in cur: body["version"] = cur["version"]
    return cl.put(f"/api/tenancy/access/{uid}", body)
# A user manager with roles read and users update everywhere, company manager in C1
um = role("User manager", ["identity.users.read", "identity.users.update", "identity.roles.read", "identity.users.create"])
u, ue = user("usermgr", [um], [{"roleId": cm, "companyId": C1}]); access(a, u, [C1])
c = C(ue, "Critic-Pass-2026x")
st, me = a.get(f"/api/identity/users/{u}")
show("1 self: add company role Company manager in C2", c.put(f"/api/identity/users/{u}", {"displayName": me["displayName"], "language": "en", "isActive": True, "version": me["version"], "roleIds": me["roleIds"], "companyRoles": me["companyRoles"] + [{"roleId": cm, "companyId": C2}]}))
show("2 self: tenancy access add C2", access(c, u, [C1, C2]))
show("3 company manager (C1) changes Administrator's access to C1 only", access(c, admin_id, [C1]))
show("4 admin access now", a.get(f"/api/tenancy/access/{admin_id}"))
# 5 create a user holding Company manager in C1 (caller holds it in C1) and in C2 (not held)
show("5a create user with Company manager in C1 (held there)", a.post("/api/identity/users", {}) if False else c.post("/api/identity/users", {"email": f"n1.{T}@alnoor.example", "displayName": "n1", "language": "en", "roleIds": [], "companyRoles": [{"roleId": cm, "companyId": C1}]}))
show("5b create user with Company manager in C2 (not held there)", c.post("/api/identity/users", {"email": f"n2.{T}@alnoor.example", "displayName": "n2", "language": "en", "roleIds": [], "companyRoles": [{"roleId": cm, "companyId": C2}]}))
show("5c create user with Company manager everywhere", c.post("/api/identity/users", {"email": f"n3.{T}@alnoor.example", "displayName": "n3", "language": "en", "roleIds": [cm], "companyRoles": []}))
# 6 role edit by roles.update holder who holds permission only in a company
re_ = role("Role editor", ["identity.roles.read", "identity.roles.update", "identity.users.read"])
r2, r2e = user("roleeditor", [re_], [{"roleId": cm, "companyId": C1}]); access(a, r2, [C1])
k = C(r2e, "Critic-Pass-2026x")
st, cmr = a.get(f"/api/identity/roles/{cm}")
body = {k_: cmr[k_] for k_ in ("nameEn", "nameAr", "permissions", "version") if k_ in cmr}
body["permissions"] = cmr["permissions"] + ["identity.users.update"]
show("6 role editor (Company manager in C1 only) adds users.update to Company manager role", k.put(f"/api/identity/roles/{cm}", body))
victim = role("Victim role", ["identity.users.read", "tenancy.companies.update", "tenancy.companies.read"])
st, vr = a.get(f"/api/identity/roles/{victim}")
body = {k_: vr[k_] for k_ in ("nameEn", "nameAr", "permissions", "version") if k_ in vr}; body["permissions"] = ["identity.users.read"]
show("7 role editor (tenancy.companies.update only in C1) strips it from a workspace role", k.put(f"/api/identity/roles/{victim}", body))
