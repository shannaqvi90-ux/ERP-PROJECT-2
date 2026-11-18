#!/usr/bin/env python3
"""Critic r4 (re-judge at bb33a04e): tenant A (alnoor) attacks tenant B (gulfsteel) on p10's surface,
plus permission checks with viewer / noaccess / anonymous. Uses only the public API."""
import json, sys, time, urllib.parse, urllib.request, urllib.error, http.cookiejar, uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:21050"
PW = "Demo-Pass-2026"
out = []
def log(*a):
    s = " ".join(str(x) for x in a); print(s); out.append(s)

class Client:
    def __init__(self, email=None):
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        self.csrf = None
        if email:
            for attempt in range(6):
                st, body, _ = self.req("POST", "/api/auth/sign-in", {"email": email, "password": PW})
                if st == 429:
                    time.sleep(15); continue
                break
            assert st == 200, (email, st, body[:300])
            for c in self.jar:
                if "csrf" in c.name.lower() or "xsrf" in c.name.lower():
                    self.csrf = c.value
    def req(self, method, path, body=None, headers=None):
        h = {"Accept": "application/json", "Content-Type": "application/json", "X-Erp-Request": "1"}
        if self.csrf: h["X-CSRF-Token"] = self.csrf; h["X-XSRF-TOKEN"] = self.csrf
        for c in self.jar:
            if "csrf" in c.name.lower() or "xsrf" in c.name.lower():
                h["X-CSRF-Token"] = c.value; h["X-XSRF-TOKEN"] = c.value
        if headers: h.update(headers)
        data = json.dumps(body).encode() if body is not None else None
        r = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
        try:
            with self.op.open(r, timeout=300) as resp:
                return resp.status, resp.read().decode("utf-8", "replace"), dict(resp.headers)
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode("utf-8", "replace"), dict(e.headers)

A = Client("admin@alnoor.example")
B = Client("admin@gulfsteel.example")

st, body, _ = A.req("GET", "/api/numbering/sequences?take=500")
a_items = json.loads(body)["items"]
st, body, _ = B.req("GET", "/api/numbering/sequences?take=500")
b_items = json.loads(body)["items"]
log("A sees", len(a_items), "sequences; B sees", len(b_items))
a_ids = {i["id"] for i in a_items}; b_ids = {i["id"] for i in b_items}
log("ids in both:", len(a_ids & b_ids))
b_companies = {i["companyId"] for i in b_items}; b_codes = {i["companyCode"] for i in b_items}
log("B company codes:", sorted(b_codes))
b_seq = b_items[0]
markers = set(b_ids) | b_companies
def leak(text):
    return [m for m in markers if m in text]

# Direct reads and writes of B's sequence
for it in b_items[:5]:
    st, body, _ = A.req("GET", f"/api/numbering/sequences/{it['id']}")
    log("A GET B seq", it["documentType"], st, "leak" if leak(body) else "clean")
    st, body, _ = A.req("PUT", f"/api/numbering/sequences/{it['id']}", {"prefix": "HACK-", "suffix": "", "padding": 9, "reset": "never", "gapless": True, "perBranch": False, "version": it["version"]})
    log("A PUT B seq", it["documentType"], st, "leak" if leak(body) else "clean")
st, body, _ = B.req("GET", f"/api/numbering/sequences/{b_seq['id']}")
bs = json.loads(body); log("B's seq after A's PUTs: prefix", bs["prefix"], "padding", bs["padding"], "version", bs["version"], "(was", b_seq["prefix"], b_seq["padding"], b_seq["version"], ")")

# List with B's ids in query params / filters / search
qs = [f"companyId={c}" for c in b_companies] + [f"tenantId={uuid.uuid4()}"] + \
     [f"filter=companyId eq '{c}'" for c in b_companies] + [f"search={c}" for c in b_codes] + \
     [f"filter=companyCode eq '{c}'" for c in b_codes] + [f"filter=id eq '{b_seq['id']}'"]
for q in qs:
    st, body, _ = A.req("GET", "/api/numbering/sequences?" + urllib.parse.quote(q, safe="=&"))
    try: n = len(json.loads(body).get("items", []))
    except Exception: n = "?"
    log("A list", q[:70], st, "rows", n, "LEAK" if leak(body) else "clean")

# Tenant / company switch headers
for hname in ["X-Tenant", "X-Tenant-Id", "X-Company", "X-Company-Id", "X-Erp-Company", "X-Erp-Tenant", "X-Active-Company"]:
    for val in list(b_companies)[:1]:
        st, body, _ = A.req("GET", f"/api/numbering/sequences/{b_seq['id']}", headers={hname: val})
        log("A GET B seq with", hname, st, "LEAK" if leak(body) else "clean")
        st, body, _ = A.req("GET", "/api/numbering/sequences?take=500", headers={hname: val})
        log("A list with", hname, st, "LEAK" if leak(body) else "clean")

# Exports and print
for fmt in ["json", "csv", "xlsx", "pdf"]:
    for q in ["", f"&filter=companyId eq '{list(b_companies)[0]}'", f"&search={list(b_codes)[0]}"]:
        st, body, hdr = A.req("GET", f"/api/reports/lists/numbering.sequences?format={fmt}" + urllib.parse.quote(q, safe="=&"))
        log("A export", fmt, q[:50], st, len(body), "LEAK" if leak(body) or any(c in body for c in b_codes if len(c) > 3) else "clean")

# Saved views of B? try reading list views with B's ids
st, body, _ = A.req("GET", f"/api/lists/numbering.sequences/views/{b_seq['id']}")
log("A GET view with B seq id", st)

# Permissions
V = Client("viewer@alnoor.example"); N = Client("noaccess@alnoor.example"); X = Client()
a0 = a_items[0]
for name, c in [("viewer", V), ("noaccess", N), ("anonymous", X)]:
    r1 = c.req("GET", "/api/numbering/sequences")[0]
    r2 = c.req("GET", f"/api/numbering/sequences/{a0['id']}")[0]
    r3 = c.req("PUT", f"/api/numbering/sequences/{a0['id']}", {"prefix": a0["prefix"], "suffix": a0["suffix"], "padding": a0["padding"], "reset": a0["reset"], "gapless": a0["gapless"], "perBranch": a0["perBranch"], "version": a0["version"]})[0]
    r4 = c.req("GET", "/api/reports/lists/numbering.sequences?format=csv")[0]
    log(name, "list", r1, "get", r2, "put", r3, "export", r4)

open(sys.argv[2] if len(sys.argv) > 2 else "c-api-attack.txt", "w").write("\n".join(out) + "\n")
