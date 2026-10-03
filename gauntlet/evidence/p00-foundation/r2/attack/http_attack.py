# Critic's own tenant-isolation attack against the running demo (tenant A = alnoor attacks tenant B = gulfsteel).
import urllib.parse, json, subprocess, urllib.request, urllib.error, http.cookiejar, hashlib, itertools, sys
BASE = "http://localhost:20050"
DB = "c-p00-foundation-r2-db-1"
def sql(q):
    return subprocess.run(["docker","exec",DB,"psql","-U","postgres","-d","erp","-Atc",q],capture_output=True,text=True).stdout.strip().splitlines()
B = sql("select id from tenancy.tenants where code='gulfsteel'")[0]
A = sql("select id from tenancy.tenants where code='alnoor'")[0]
def snapshot():
    out = {}
    for t in ["identity.users","identity.roles","identity.user_roles","identity.sessions","tenancy.tenants","audit.entries"]:
        col = "id" if t=="tenancy.tenants" else "tenant_id"
        cols = "*" if t!="identity.users" else "id,email,display_name,language,password_hash,is_active,updated_at"
        out[t] = sql(f"select count(*)||':'||md5(coalesce(string_agg(r::text,'|' order by r::text),'')) from (select {cols} from {t} where {col}='{B}') r")[0]
    return out

b_ids = sql(f"select id from identity.users where tenant_id='{B}' order by id limit 20") + sql(f"select id from identity.roles where tenant_id='{B}'") + sql(f"select id from identity.user_roles where tenant_id='{B}' limit 5") + sql(f"select id from identity.sessions where tenant_id='{B}' limit 5") + [B]
b_strings = sql(f"select email from identity.users where tenant_id='{B}' order by email limit 15") + sql(f"select display_name from identity.users where tenant_id='{B}' and display_name not in (select display_name from identity.users where tenant_id='{A}') limit 10") + sql(f"select name_en from identity.roles where tenant_id='{B}' and name_en not in (select name_en from identity.roles where tenant_id='{A}')") + ["gulfsteel"] + sql(f"select name_en from tenancy.tenants where id='{B}'")
markers = set(b_ids) | set(s for s in b_strings if len(s) >= 8) | set(sql(f"select left(password_hash,40) from identity.users where tenant_id='{B}' limit 5"))
markers.discard("gulfsteel")  # the code itself is echoed by sign-in field validation; checked separately
print(f"B={B}; {len(b_ids)} B ids, {len(b_strings)} B strings, {len(markers)} markers")

class Client:
    def __init__(self, bearer=None):
        self.cj = http.cookiejar.CookieJar(); self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cj)); self.bearer = bearer
    def req(self, method, path, body=None, headers=None):
        h = {"X-Erp-Request":"1","Content-Type":"application/json"}
        if self.bearer: h["Authorization"]="Bearer "+self.bearer
        h.update(headers or {})
        data = json.dumps(body).encode() if body is not None else (b"{}" if method in ("POST","PUT","PATCH") else None)
        r = urllib.request.Request(BASE+path, data=data, method=method, headers=h)
        try:
            with self.op.open(r) as resp: return resp.status, resp.read().decode("utf-8","replace"), dict(resp.headers)
        except urllib.error.HTTPError as e: return e.code, e.read().decode("utf-8","replace"), dict(e.headers)
def signin(email, workspace=None):
    c = Client(); body={"email":email,"password":"Demo-Pass-2026"}
    if workspace: body["workspace"]=workspace
    s,t,h = c.req("POST","/api/auth/sign-in",body); return c, s, t
admin, s, t = signin("admin@alnoor.example"); assert s==200, (s,t)
token = json.loads(t).get("token")
bearer = Client(bearer=token) if token else None
noacc, s, _ = signin("noaccess@alnoor.example")
viewer, s, _ = signin("viewer@alnoor.example")
attackers = {"admin-cookie":admin, "viewer":viewer, "noaccess":noacc, "anon":Client()}
if bearer: attackers["admin-bearer"]=bearer
c,s,t = signin("admin@gulfsteel.example"); print("B admin with demo password (demo shares password):", s)
# existence oracle on sign-in: B e-mail vs unknown e-mail
r1 = Client().req("POST","/api/auth/sign-in",{"email":"viewer@gulfsteel.example","password":"wrong-pass-x"})
r2 = Client().req("POST","/api/auth/sign-in",{"email":"nobody@nowhere.example","password":"wrong-pass-x"})
same = (r1[0]==r2[0]) and (json.loads(r1[1]).get("code")==json.loads(r2[1]).get("code"))
print("sign-in oracle B vs unknown:", r1[0], r2[0], "same" if same else "DIFFERENT")
before = snapshot()
leaks=[]; n=0; errors=[]
def judge(who, label, status, text, sent=()):
    global n; n+=1
    scrub = text
    for v in sent: scrub = scrub.replace(v, "<sent>")
    for m in markers:
        if m.lower() in scrub.lower(): leaks.append(f"{who} {label} -> {status}: marker {m}"); break
    if status>=500: errors.append(f"{who} {label} -> {status}")
hdrs = [{}, {"X-Tenant-Id":B}, {"X-Erp-Workspace":B}, {"X-Workspace":"gulfsteel"}, {"X-Company-Id":B}, {"Cookie":f"erp_tenant={B}"}, {"X-Forwarded-Host":"gulfsteel.example"}, {"Host":"gulfsteel.localhost"}]
eps = [("GET","/api/tenancy/tenant"),("PUT","/api/tenancy/tenant"),("GET","/api/auth/session"),("GET","/api/identity/users"),("POST","/api/identity/users"),("GET","/api/identity/roles"),("POST","/api/identity/roles"),("GET","/api/identity/permissions"),("PUT","/api/identity/me/preferences")]
idEps = [("GET","/api/identity/users/{}"),("PUT","/api/identity/users/{}"),("GET","/api/identity/roles/{}"),("PUT","/api/identity/roles/{}"),("DELETE","/api/identity/roles/{}")]
tq = f"?tenantId={B}&tenant=gulfsteel&workspace=gulfsteel&companyId={B}"
for who,c in attackers.items():
    for (m,p) in eps:
        for h in hdrs:
            for q in ["", tq]:
                body = {"tenantId":B,"workspaceId":B,"nameEn":"x","nameAr":"x","version":1,"email":f"x{n}@alnoor.example","displayName":"x","language":"en","password":"Abcdefgh-1234","roleIds":[b_ids[-2]],"permissions":["identity.users.read"]} if m in ("POST","PUT") else None
                s,t,_ = c.req(m,p+q,body,h); judge(who,f"{m} {p+q} {h}",s,t,[B,"gulfsteel"])
    for (m,p) in idEps:
        for i in b_ids:
            body = {"displayName":"pwn","language":"en","isActive":False,"roleIds":[],"version":1,"nameEn":"pwn","nameAr":"pwn","permissions":[]} if m=="PUT" else None
            s,t,_ = c.req(m,p.format(i),body); judge(who,f"{m} {p.format(i)}",s,t,[i])
            if m=="GET" and s not in (401,403,404): leaks.append(f"{who} {m} {p.format(i)} -> {s} (expected 404)")
    for v in b_strings + b_ids:
        for qn in ["search","q","email","code","name","filter","id","userId"]:
            path = f"/api/identity/users?{qn}="+urllib.parse.quote(v)
            s,t,_ = c.req("GET",path); judge(who,f"GET {path}",s,t,[v])
            if qn=="search" and s==200 and json.loads(t)["total"]!=0: leaks.append(f"{who} GET {path} -> total {json.loads(t)['total']}")
# sign-in: A's credentials with workspace B; B's email with A's password; same email different workspace
for ws in ["gulfsteel", B]:
    c,s,t = signin("admin@alnoor.example", ws)
    st,body,_ = c.req("GET","/api/tenancy/tenant"); judge("signin-ws",f"admin@alnoor + workspace {ws}",st,body)
    if st==200 and json.loads(body)["code"]!="alnoor": leaks.append(f"workspace field switched tenant: {body}")
after = snapshot()
changed = [k for k in before if before[k]!=after[k]]
print(f"requests={n} leaks={len(leaks)} server_errors={len(errors)} B tables changed={changed}")
for l in leaks[:30]: print("LEAK", l)
for e in errors[:10]: print("5xx", e)
