#!/usr/bin/env python3
"""Critic's own API attack on p10 numbering (tenant A = Al Noor, tenant B = Gulf Steel)."""
import json, sys, urllib.request, urllib.error, http.cookiejar, time

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:21050"
PW = "Demo-Pass-2026"
out = []

def log(*a):
    s = " ".join(str(x) for x in a)
    print(s); out.append(s)

class Client:
    def __init__(self, email, workspace):
        self.token = None
        r = self.req("POST", "/api/auth/sign-in", {"email": email, "password": PW, "workspace": workspace, "issueToken": True})
        if r[0] == 429:
            time.sleep(61); r = self.req("POST", "/api/auth/sign-in", {"email": email, "password": PW, "workspace": workspace, "issueToken": True})
        body = json.loads(r[1] or b"{}")
        self.token = body.get("token") or body.get("accessToken")
        log("sign-in", email, r[0], "token" if self.token else body)

    def req(self, method, path, body=None, headers=None, raw=False):
        h = {"Content-Type": "application/json", "Accept": "application/json", "X-Erp-Request": "1"}
        if self.token: h["Authorization"] = "Bearer " + self.token
        if headers: h.update(headers)
        data = json.dumps(body).encode() if body is not None else None
        rq = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
        try:
            with urllib.request.urlopen(rq, timeout=300) as resp:
                return resp.status, resp.read()
        except urllib.error.HTTPError as e:
            return e.code, e.read()

def items(c, q=""):
    s, b = c.req("GET", "/api/numbering/sequences?take=200" + q)
    return s, (json.loads(b)["items"] if s == 200 else b[:300])

A = Client("admin@alnoor.example", "alnoor.example")
B = Client("admin@gulfsteel.example", "gulfsteel.example")
V = Client("viewer@alnoor.example", "alnoor.example")
N = Client("noaccess@alnoor.example", "alnoor.example")
anon = Client.__new__(Client); anon.token = None

sa, ia = items(A); sb, ib = items(B)
log("A list", sa, len(ia), "B list", sb, len(ib))
ida = {r["id"] for r in ia}; idb = {r["id"] for r in ib}
log("ids in both:", len(ida & idb))
bcompanies = {r["companyId"] for r in ib}; bcodes = {r["companyCode"] for r in ib}
log("B companies", bcompanies, bcodes)
log("B types", sorted({r["documentType"] for r in ib}))
btax = next((r for r in ib if "invoice" in r["documentType"].lower()), ib[0])
log("B target", btax["id"], btax["documentType"], btax["prefix"])

# Direct reads/writes on B's row as A
s, b = A.req("GET", f"/api/numbering/sequences/{btax['id']}"); log("A GET B row", s)
s, bdto = B.req("GET", f"/api/numbering/sequences/{btax['id']}"); bdto = json.loads(bdto)
body = {"prefix": "HACK-", "suffix": "", "padding": 7, "reset": "never", "gapless": True, "perBranch": False, "nextValue": None, "version": bdto["version"]}
s, b = A.req("PUT", f"/api/numbering/sequences/{btax['id']}", body); log("A PUT B row", s, b[:200])
s, after = B.req("GET", f"/api/numbering/sequences/{btax['id']}"); after = json.loads(after)
log("B row unchanged:", after["prefix"] == bdto["prefix"] and after["version"] == bdto["version"], after["prefix"], after["version"])

# Filters / search with B identifiers
for q in [f"&filter=companyId eq '{c}'" for c in bcompanies] + [f"&search={c}" for c in bcodes] + [f"&filter=companyCode eq '{c}'" for c in bcodes]:
    s, r = items(A, urllib.parse.quote(q, safe="&=") if False else q.replace(" ", "%20").replace("'", "%27"))
    leaked = [x for x in r if isinstance(r, list) and x["companyId"] in bcompanies] if isinstance(r, list) else r
    log("A list", q[:60], s, "B rows:", len(leaked) if isinstance(leaked, list) else leaked)

# Spoofed headers
for h in ["X-Tenant", "X-Tenant-Id", "X-Company", "X-Company-Id", "X-Workspace", "X-Erp-Company"]:
    for v in list(bcompanies)[:1] + ["gulfsteel.example"]:
        s, b = A.req("GET", f"/api/numbering/sequences/{btax['id']}", headers={h: v}); log("A GET B row with", h, "=", v, "->", s)

# Exports
for fmt in ["csv", "xlsx", "pdf", "json"]:
    for q in ["", f"&filter=companyId%20eq%20%27{list(bcompanies)[0]}%27"]:
        s, b = A.req("GET", f"/api/reports/lists/numbering.sequences?format={fmt}{q}", headers={"Accept": "*/*"})
        hit = any(code.encode() in b for code in bcodes) or any(c.encode() in b for c in bcompanies) or btax["id"].encode() in b
        log("A export", fmt, "Bfilter" if q else "plain", s, len(b), "B marker in body:", hit)

# Saved views of the numbering list: B's views not reachable
s, b = B.req("POST", "/api/lists/numbering.sequences/views", {"name": "B private view", "filter": "gapless eq true", "sort": None, "columns": None})
log("B create view", s, b[:200])
try:
    vid = json.loads(b).get("id")
    s, b2 = A.req("GET", f"/api/lists/numbering.sequences/views/{vid}"); log("A GET B view", s)
    s, b2 = A.req("DELETE", f"/api/lists/numbering.sequences/views/{vid}"); log("A DELETE B view", s)
    s, b2 = B.req("DELETE", f"/api/lists/numbering.sequences/views/{vid}"); log("B DELETE own view", s)
except Exception as e:
    log("view test skipped", e)

# Audit history of B's sequence
s, b = A.req("GET", f"/api/audit/records/{btax['id']}?type=numbering.sequences"); log("A audit of B sequence", s, b[:150])

# Permissions
aseq = next(r for r in ia if r["workingCompany"]) if any(r.get("workingCompany") for r in ia) else ia[0]
s, adto = A.req("GET", f"/api/numbering/sequences/{aseq['id']}"); adto = json.loads(adto)
pbody = {"prefix": adto["prefix"], "suffix": adto["suffix"], "padding": adto["padding"], "reset": adto["reset"], "gapless": adto["gapless"], "perBranch": adto["perBranch"], "nextValue": None, "version": adto["version"]}
for name, c in [("viewer", V), ("noaccess", N), ("anonymous", anon)]:
    r1 = c.req("GET", "/api/numbering/sequences")[0]
    r2 = c.req("GET", f"/api/numbering/sequences/{aseq['id']}")[0]
    r3 = c.req("PUT", f"/api/numbering/sequences/{aseq['id']}", pbody)[0]
    r4 = c.req("GET", "/api/reports/lists/numbering.sequences?format=csv", headers={"Accept": "*/*"})[0]
    log(f"{name}: list {r1} get {r2} put {r3} export {r4}")

open(sys.argv[2] if len(sys.argv) > 2 else "/dev/null", "w").write("\n".join(out) + "\n")

# follow-ups: xlsx unzipped, views with columns, own audit control
import zipfile, io
for q in ["", f"&filter=companyId%20eq%20%27{list(bcompanies)[0]}%27"]:
    s, b = A.req("GET", f"/api/reports/lists/numbering.sequences?format=xlsx{q}", headers={"Accept": "*/*"})
    z = zipfile.ZipFile(io.BytesIO(b)); txt = b"".join(z.read(n) for n in z.namelist())
    log("A xlsx unzipped", "Bfilter" if q else "plain", s, "B marker:", any(c.encode() in txt for c in bcodes), "A marker:", b"ANT" in txt or b"AN-" in txt or ia[0]["companyCode"].encode() in txt)
s, b = B.req("POST", "/api/lists/numbering.sequences/views", {"name": "B private view", "filter": "gapless eq true", "sort": None, "columns": ["nameEn", "companyId", "nextNumber"]})
log("B create view(2)", s, b[:160])
if s in (200, 201):
    vid = json.loads(b).get("id")
    log("A GET B view", A.req("GET", f"/api/lists/numbering.sequences/views/{vid}")[0])
    log("A PUT B view", A.req("PUT", f"/api/lists/numbering.sequences/views/{vid}", {"name": "pwn", "filter": None, "sort": None, "columns": ["nameEn"]})[0])
    log("A DELETE B view", A.req("DELETE", f"/api/lists/numbering.sequences/views/{vid}")[0])
    s2, lv = A.req("GET", "/api/lists/numbering.sequences/views"); log("A views list has B view:", vid.encode() in lv)
    log("B DELETE own view", B.req("DELETE", f"/api/lists/numbering.sequences/views/{vid}")[0])
s, b = A.req("GET", f"/api/audit/records/{aseq['id']}?type=numbering.sequences"); log("A audit of own sequence (control)", s, len(json.loads(b).get("items", [])) if s == 200 else b[:150])
open(sys.argv[2], "w").write("\n".join(out) + "\n")
