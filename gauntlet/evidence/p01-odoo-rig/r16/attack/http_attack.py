#!/usr/bin/env python3
# Reused by critic p01 r16 from gauntlet/evidence/p01-odoo-rig/r15/attack (the r15 critic wrote it); run against the r16 demo.
"""Critic p01 r15: tenant A (alnoor) attacks tenant B (gulfsteel) over every OpenAPI operation.
Reads B's ids and words as B, then as A sends them in paths, query strings, headers and bodies.
A leak = a 2xx answer to A that carries one of B's ids or names, or a write on B's id that B then sees changed."""
import json, re, sys, time, urllib.request, urllib.error, random

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:20150"
PW = "Demo-Pass-2026"
UUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
stats = {"requests": 0, "429": 0, "status": {}}

def call(method, path, token=None, body=None, headers=None, raw=False):
    for attempt in range(8):
        h = {"Accept": "application/json", "X-Erp-Request": "1"}
        if token: h["Authorization"] = "Bearer " + token
        if headers: h.update(headers)
        data = None
        if body is not None:
            data = json.dumps(body).encode(); h["Content-Type"] = "application/json"
        req = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                code, txt = r.status, r.read()
        except urllib.error.HTTPError as e:
            code, txt = e.code, e.read()
        except Exception as e:
            code, txt = 0, str(e).encode()
        stats["requests"] += 1
        if code == 429:
            stats["429"] += 1; time.sleep(min(30, 2 + attempt * 3)); continue
        stats["status"][code] = stats["status"].get(code, 0) + 1
        return code, txt.decode("utf-8", "replace") if not raw else txt
    return 429, ""

def sign_in(email, ws):
    c, t = call("POST", "/api/auth/sign-in", body={"email": email, "password": PW, "workspace": ws, "issueToken": True})
    assert c == 200, (email, c, t[:300])
    return json.loads(t)["token"]

spec = json.load(open(sys.argv[2]))
A = sign_in("admin@alnoor.example", "alnoor.example")
B = sign_in("admin@gulfsteel.example", "gulfsteel.example")

collections = [p for p, v in spec["paths"].items() if "get" in v and "{" not in p]
ids = {"A": set(), "B": set()}; by_prefix = {}
textB = {"A": "", "B": ""}
for who, tok in (("A", A), ("B", B)):
    for p in collections:
        if "reports" in p or "template" in p: continue
        sep = "&" if "?" in p else "?"
        c, t = call("GET", p + sep + "take=50", tok)
        if c == 200:
            found = set(UUID.findall(t))
            ids[who] |= found
            textB[who] += t
            if who == "B": by_prefix[p] = found
onlyB = ids["B"] - ids["A"]
# B's distinctive words: names in B's companies/branches/users that never occur in A's answers.
wordsB = set()
for p in ("/api/tenancy/companies", "/api/tenancy/branches", "/api/identity/users", "/api/contacts/contacts", "/api/identity/roles"):
    c, t = call("GET", p + "?take=50", B)
    for m in re.finditer(r'"(?:name|nameEn|displayName|code|email|legalName)"\s*:\s*"([^"]{5,})"', t):
        w = m.group(1)
        if w not in textB["A"]: wordsB.add(w)
wordsB = {w.strip() for w in open("b-only-words.txt") if len(w.strip()) >= 8}  # exact B-only names and e-mails from the database
print(f"B ids not in A: {len(onlyB)}; B words: {len(wordsB)}: {sorted(wordsB)[:12]}")

def leaks(t):
    toks = {x.strip() for x in re.split(r'[",\r\n\t<>;]', t)}
    hit = [i for i in onlyB if i in t] + sorted(toks & wordsB)
    return hit[:5]

findings = []
def judge(kind, method, path, code, t):
    if 200 <= code < 300:
        h = leaks(t)
        if h: findings.append({"kind": kind, "method": method, "path": path, "code": code, "hits": h})
    if code == 0 or code >= 500: findings.append({"kind": kind + "-5xx", "method": method, "path": path, "code": code, "body": t[:200]})

snapshotB = {p: call("GET", p + "?take=50", B)[1] for p in ("/api/tenancy/companies", "/api/tenancy/branches", "/api/identity/users", "/api/identity/roles", "/api/contacts/contacts", "/api/numbering/sequences", "/api/customfields/fields")}

sample = random.Random(15).sample(sorted(onlyB), min(12, len(onlyB)))
SKIP_WRITE = ("/api/auth/", "sign-out", "/me/")
for path, item in spec["paths"].items():
    if "{" not in path: continue
    pref = next((p for p in sorted(by_prefix, key=len, reverse=True) if path.startswith(p + "/")), None)
    cands = sorted(by_prefix.get(pref, set()) - ids["A"])[:4] if pref else []
    cands = cands or sample[:4]
    for method in item:
        if method not in ("get", "put", "post", "delete", "patch"): continue
        if method != "get" and any(s in path for s in SKIP_WRITE): continue
        for bid in cands:
            p = re.sub(r"\{(id|userId)\}", bid, path)
            p = re.sub(r"\{[^}]+\}", "contacts.contacts", p)
            body = None
            if method in ("put", "post", "patch"):
                body = {"name": "R15 attack", "nameEn": "R15 attack", "id": bid, "tenantId": bid, "companyId": bid, "version": 1, "rowVersion": 1}
            c, t = call(method.upper(), p, A, body=body)
            judge("path", method.upper(), p, c, t)
            if method != "get" and 200 <= c < 300:
                findings.append({"kind": "write-accepted-on-B-id", "method": method.upper(), "path": p, "code": c, "body": t[:200]})

# Query strings, filters and exports with B ids; tenant headers.
for p in collections:
    if p in ("/api/health",): continue
    for q in ("companyId={b}", "branchId={b}", "tenantId={b}", "id={b}", "filter=id eq {b}", "q={b}", "search={b}", "format=csv&companyId={b}", "format=xlsx&tenantId={b}", "format=pdf&companyId={b}"):
        b = random.Random(p + q).choice(sorted(onlyB))
        sep = "&" if "?" in p else "?"
        import urllib.parse
        qq = "&".join(k + "=" + urllib.parse.quote(v) for k, v in (x.split("=", 1) for x in q.format(b=b).split("&")))
        c, t = call("GET", p + sep + qq, A, raw=True)
        tt = t.decode("utf-8", "replace") if isinstance(t, bytes) else t
        judge("query", "GET", p + sep + q.format(b=b), c, tt)
    for hdr in ({"X-Tenant": "gulfsteel"}, {"X-Tenant-Id": sample[0]}, {"X-Workspace": "gulfsteel.example"}, {"Host": "gulfsteel.example"}, {"X-Company-Id": sample[0]}):
        c, t = call("GET", p, A, headers=hdr)
        judge("header", "GET", p + " " + json.dumps(hdr), c, t)

changed = [p for p, before in snapshotB.items() if call("GET", p + "?take=50", B)[1] != before]
out = {"stats": stats, "onlyB_ids": len(onlyB), "wordsB": sorted(wordsB), "findings": findings, "B_lists_changed_after_attack": changed}
json.dump(out, open(sys.argv[3], "w"), indent=1)
print(json.dumps({"stats": stats, "findings": len(findings), "B_changed": changed}, indent=1))
for f in findings[:40]: print(f)
