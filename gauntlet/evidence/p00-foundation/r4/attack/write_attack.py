import json, re, sys
sys.argv=[sys.argv[0], "http://localhost:20050"]
exec(open("http_attack.py").read().split("switch_headers =")[0])  # reuse sign-in, harvest, helpers
out=[]
def show(desc,s,b): out.append(f"{s} {desc} :: {b[:160]}"); note(desc,s,b)
# A's own user and role as templates
usersA=json.loads(req(opA,"GET","/api/identity/users?take=5")[1])
usersB=json.loads(req(opB,"GET","/api/identity/users?take=5")[1])
rolesA=json.loads(req(opA,"GET","/api/identity/roles")[1]); rolesB=json.loads(req(opB,"GET","/api/identity/roles")[1])
def items(x): return x.get("items") or x.get("rows") or x.get("data") or x
ua=items(usersA)[1]; ub=items(usersB)[1]
ra=items(rolesA)[-1]; rb=items(rolesB)[-1]
s,b,_=req(opA,"GET",f"/api/identity/users/{ua['id']}"); full_ua=json.loads(b)
s,b,_=req(opB,"GET",f"/api/identity/users/{ub['id']}"); full_ub=json.loads(b)
s,b,_=req(opA,"GET",f"/api/identity/roles/{ra['id']}"); full_ra=json.loads(b)
s,b,_=req(opB,"GET",f"/api/identity/roles/{rb['id']}"); full_rb=json.loads(b)
body_u=dict(full_ua); body_u["displayName"]="PWNED by A"
for k in ("version",):
    if k in full_ub: body_u[k]=full_ub[k]
show("PUT B user with valid body", *req(opA,"PUT",f"/api/identity/users/{ub['id']}", body_u)[:2])
body_r=dict(full_ra); body_r["nameEn"]="PWNED"; body_r["version"]=full_rb.get("version")
show("PUT B role with valid body", *req(opA,"PUT",f"/api/identity/roles/{rb['id']}", body_r)[:2])
show("POST B role copy", *req(opA,"POST",f"/api/identity/roles/{rb['id']}/copy", {"nameEn":"x","nameAr":"س"})[:2])
show("POST B user password", *req(opA,"POST",f"/api/identity/users/{ub['id']}/password", {"password":"Pwned-Pass-2026!x"})[:2])
show("POST B user unblock", *req(opA,"POST",f"/api/identity/users/{ub['id']}/unblock", {})[:2])
show("POST B user revoke sessions", *req(opA,"POST",f"/api/identity/users/{ub['id']}/sessions/revoke", {})[:2])
show("GET B user access", *req(opA,"GET",f"/api/identity/users/{ub['id']}/access")[:2])
show("GET B user sign-ins", *req(opA,"GET",f"/api/identity/users/{ub['id']}/sign-ins")[:2])
# create user in A with B's role id
show("POST user with B role", *req(opA,"POST","/api/identity/users", {"email":"x1@alnoor.example","displayName":"X","language":"en","password":"Some-Pass-2026!x","roleIds":[rb['id']]})[:2])
# saved views: B creates a shared view; A tries to read/update/delete it
s,b,_=req(opB,"POST","/api/lists/identity.users/shared-views", {"name":"B secret view gulfsteel","columns":["displayName"],"sort":"displayName","filter":"","search":"gulfsteel-canary"})
out.append(f"B created shared view: {s} {b[:200]}")
vb=json.loads(b).get("id") if s<300 else None
if vb:
    markers.add(vb); markers.add("gulfsteel-canary")
    show("GET B shared view", *req(opA,"GET",f"/api/lists/identity.users/shared-views/{vb}")[:2])
    show("PUT B shared view", *req(opA,"PUT",f"/api/lists/identity.users/shared-views/{vb}", {"name":"pwn","columns":["displayName"],"version":0})[:2])
    show("DELETE B shared view", *req(opA,"DELETE",f"/api/lists/identity.users/shared-views/{vb}")[:2])
    show("GET A views list", *req(opA,"GET","/api/lists/identity.users/views")[:2])
    show("A users search for B canary", *req(opA,"GET","/api/identity/users?q=gulfsteel")[:2])
show("DELETE B user", *req(opA,"DELETE",f"/api/identity/users/{ub['id']}")[:2])
show("DELETE B role", *req(opA,"DELETE",f"/api/identity/roles/{rb['id']}")[:2])
s,b,_=req(opB,"GET",f"/api/identity/users/{ub['id']}"); out.append(f"B user after: {s} {json.loads(b).get('displayName')}")
s,b,_=req(opB,"GET",f"/api/identity/roles/{rb['id']}"); out.append(f"B role after: {s} {json.loads(b).get('nameEn')}")
if vb: out.append("B view after: "+str(req(opB,"GET",f"/api/lists/identity.users/shared-views/{vb}")[0]))
print("\n".join(out)); print("leaks", results["leaks"])
